﻿// <copyright file="SoulseekClient.cs" company="JP Dillingham">
//     Copyright (c) JP Dillingham. All rights reserved.
//
//     This program is free software: you can redistribute it and/or modify it under the terms of the GNU General Public License as
//     published by the Free Software Foundation, either version 3 of the License, or (at your option) any later version.
//
//     This program is distributed in the hope that it will be useful, but WITHOUT ANY WARRANTY; without even the implied warranty
//     of MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.See the GNU General Public License for more details.
//
//     You should have received a copy of the GNU General Public License along with this program. If not, see https://www.gnu.org/licenses/.
// </copyright>

namespace Soulseek.NET
{
    using System;
    using System.Collections.Concurrent;
    using System.Collections.Generic;
    using System.Linq;
    using System.Net;
    using System.Threading;
    using System.Threading.Tasks;
    using Soulseek.NET.Messaging;
    using Soulseek.NET.Messaging.Requests;
    using Soulseek.NET.Messaging.Responses;
    using Soulseek.NET.Tcp;

    /// <summary>
    ///     A client for the Soulseek file sharing network.
    /// </summary>
    public class SoulseekClient : IDisposable, ISoulseekClient
    {
        #region Public Constructors

        /// <summary>
        ///     Initializes a new instance of the <see cref="SoulseekClient"/> class with the specified <paramref name="options"/>.
        /// </summary>
        /// <param name="options">The client options.</param>
        public SoulseekClient(SoulseekClientOptions options = null)
            : this("vps.slsknet.org", 2271, options)
        {
        }

        /// <summary>
        ///     Initializes a new instance of the <see cref="SoulseekClient"/> class with the specified <paramref name="address"/>
        ///     and <paramref name="port"/>.
        /// </summary>
        /// <param name="address">The address of the server to which to connect.</param>
        /// <param name="port">The port to which to connect.</param>
        /// <param name="options">The client <see cref="SoulseekClientOptions"/>.</param>
        public SoulseekClient(string address, int port, SoulseekClientOptions options)
        {
            Address = address;
            Port = port;
            Options = options ?? new SoulseekClientOptions() { ConnectionOptions = new ConnectionOptions() { ReadTimeout = 0 } };

            ServerConnection = new MessageConnection(ConnectionType.Server, Address, Port, Options.ConnectionOptions)
            {
                ConnectHandler = (conn) =>
                {
                    Task.Run(() => ConnectionStateChanged?.Invoke(this, new ConnectionStateChangedEventArgs(conn))).Forget();
                },
                DisconnectHandler = (conn, message) =>
                {
                    Disconnect();
                    Task.Run(() => ConnectionStateChanged?.Invoke(this, new ConnectionStateChangedEventArgs(conn, message))).Forget();
                },
                MessageHandler = async (conn, message) =>
                {
                    await ServerMessageHandler(message);
                }
            };

            MessageWaiter = new MessageWaiter(Options.MessageTimeout);
            MessageConnectionManager = new ConnectionManager<IMessageConnection>(Options.ConcurrentPeerConnections);
        }

        #endregion Public Constructors

        #region Public Events

        /// <summary>
        ///     Occurs when the underlying TCP connection to the server changes state.
        /// </summary>
        public event EventHandler<ConnectionStateChangedEventArgs> ConnectionStateChanged;

        /// <summary>
        ///     Occurs when raw data is received by the underlying TCP connection.
        /// </summary>
        public event EventHandler<DataReceivedEventArgs> DataReceived;

        /// <summary>
        ///     Occurs when a new message is received.
        /// </summary>
        public event EventHandler<MessageReceivedEventArgs> MessageReceived;

        /// <summary>
        ///     Occurs when a new search result is received.
        /// </summary>
        public event EventHandler<SearchResponseReceivedEventArgs> SearchResponseReceived;

        #endregion Public Events

        #region Public Properties

        /// <summary>
        ///     Gets or sets the address of the server to which to connect.
        /// </summary>
        public string Address { get; set; }

        /// <summary>
        ///     Gets a value indicating whether a user is currently signed in.
        /// </summary>
        public bool LoggedIn { get; private set; } = false;

        /// <summary>
        ///     Gets the client options.
        /// </summary>
        public SoulseekClientOptions Options { get; private set; }

        /// <summary>
        ///     Gets or sets the port to which to connect.
        /// </summary>
        public int Port { get; set; }

        /// <summary>
        ///     Gets the current state of the underlying TCP connection.
        /// </summary>
        public ConnectionState State => ServerConnection.State;

        /// <summary>
        ///     Gets the name of the currently signed in user.
        /// </summary>
        public string Username { get; private set; }

        #endregion Public Properties

        #region Private Properties

        private Download ActiveDownload { get; set; }
        // todo: use a ConcurrentDictionary<string username, ConcurrentBag> for this
        private ConcurrentDictionary<string, List<Download>> ActiveDownloads { get; set; } = new ConcurrentDictionary<string, List<Download>>();

        private ConcurrentDictionary<int, Search> ActiveSearches { get; set; } = new ConcurrentDictionary<int, Search>();
        private bool Disposed { get; set; } = false;
        private ConnectionManager<IMessageConnection> MessageConnectionManager { get; set; }
        private MessageWaiter MessageWaiter { get; set; }
        private Random Random { get; set; } = new Random();
        private IMessageConnection ServerConnection { get; set; }

        #endregion Private Properties

        #region Public Methods

        /// <summary>
        ///     Asynchronously fetches the list of files shared by the specified <paramref name="username"/> with the optionally specified <paramref name="options"/> and <paramref name="cancellationToken"/>.
        /// </summary>
        /// <param name="username">The user to browse.</param>
        /// <param name="timeout">The operation timeout, in seconds.</param>
        /// <param name="cancellationToken">A cancellation token.</param>
        /// <returns>The operation response.</returns>
        public async Task<BrowseResponse> BrowseAsync(string username, int timeout = 60, CancellationToken? cancellationToken = null)
        {
            if (State != ConnectionState.Connected)
            {
                throw new ConnectionStateException($"The server connection must be Connected to browse (currently: {State})");
            }

            if (!LoggedIn)
            {
                throw new BrowseException($"A user must be logged in to browse.");
            }

            try
            {
                var address = await GetPeerAddressAsync(username);
                var key = new ConnectionKey() { Username = username, IPAddress = address.IPAddress, Port = address.Port, Type = ConnectionType.Peer };
                var wait = MessageWaiter.Wait<BrowseResponse>(MessageCode.PeerBrowseResponse, key, timeout, cancellationToken);

                var connection = MessageConnectionManager.Get(key);

                if (connection != default(IMessageConnection))
                {
                    try
                    {
                        await connection.SendAsync(new PeerBrowseRequest().ToMessage());
                    }
                    catch (ConnectionException)
                    {
                        await MessageConnectionManager.Remove(connection);
                        connection = default(IMessageConnection);
                    }
                }

                if (connection == default(IMessageConnection))
                {
                    connection = new MessageConnection(ConnectionType.Peer, username, address.IPAddress.ToString(), address.Port, Options.ConnectionOptions)
                    {
                        MessageHandler = PeerMessageHandler,
                        ConnectHandler = async (conn) =>
                        {
                            var token = new Random().Next();
                            await connection.SendAsync(new PeerInitRequest(Username, "P", token).ToMessage(), suppressCodeNormalization: true);
                            await connection.SendAsync(new PeerBrowseRequest().ToMessage());
                        },
                        DisconnectHandler = (conn, message) => { throw new ConnectionException($"Peer connection disconnected unexpectedly."); },
                    };

                    await MessageConnectionManager.Add(connection);
                }

                return await wait;
            }
            catch (Exception ex)
            {
                throw new BrowseException($"Failed to browse user {Username}: {ex.Message}", ex);
            }
        }

        /// <summary>
        ///     Asynchronously connects the client to the server specified in the <see cref="Address"/> and <see cref="Port"/> properties.
        /// </summary>
        /// <returns>A task representing the asynchronous operation.</returns>
        /// <exception cref="ConnectionStateException">Thrown when the client is already connected, or is transitioning between states.</exception>
        public async Task ConnectAsync()
        {
            if (ServerConnection.State == ConnectionState.Connected)
            {
                throw new ConnectionStateException($"Failed to connect; the client is already connected.");
            }

            if (ServerConnection.State == ConnectionState.Connecting || ServerConnection.State == ConnectionState.Disconnecting)
            {
                throw new ConnectionStateException($"Failed to connect; the client is transitioning between states.");
            }

            Console.WriteLine($"Connecting...");
            await ServerConnection.ConnectAsync();
            Console.WriteLine($"Connected.");
        }

        /// <summary>
        ///     Disconnects the client from the server.
        /// </summary>
        public void Disconnect()
        {
            ServerConnection.Disconnect("Client disconnected.");

            var searches = ActiveSearches;
            ActiveSearches = new ConcurrentDictionary<int, Search>();

            while (!searches.IsEmpty)
            {
                if (searches.TryRemove(searches.Keys.First(), out var search))
                {
                    search.Dispose();
                }
            }

            Username = null;
            LoggedIn = false;
        }

        /// <summary>
        ///     Disposes this instance.
        /// </summary>
        public void Dispose()
        {
            Dispose(true);
        }

        public async Task<Download> DownloadAsync(string username, string filename, DownloadOptions options = null, CancellationToken? cancellationToken = null)
        {
            options = options ?? new DownloadOptions() { ConnectionOptions = new ConnectionOptions() { ReadTimeout = 0 } };

            var address = await GetPeerAddressAsync(username);

            // create a key and try to fetch any existing connection
            var key = new ConnectionKey() { Username = username, IPAddress = address.IPAddress, Port = address.Port, Type = ConnectionType.Peer };
            var connection = MessageConnectionManager.Get(key);

            // if the connection we fetched is null, there wasn't one, so create it.
            if (connection == default(IMessageConnection))
            {
                connection = new MessageConnection(ConnectionType.Peer, address.IPAddress.ToString(), address.Port, options.ConnectionOptions);
            }

            var download = new Download(username, filename, address.IPAddress.ToString(), address.Port, options, cancellationToken, connection);
            //await download.DownloadAsync(cancellationToken);

            await MessageConnectionManager.Add(connection);

            return download;
        }

        /// <summary>
        ///     Asynchronously logs in to the server with the specified <paramref name="username"/> and <paramref name="password"/>.
        /// </summary>
        /// <param name="username">The username with which to log in.</param>
        /// <param name="password">The password with which to log in.</param>
        /// <returns>A Task representing the operation.</returns>
        /// <exception cref="LoginException">Thrown when the login fails.</exception>
        public async Task LoginAsync(string username, string password)
        {
            if (LoggedIn)
            {
                throw new LoginException($"Already logged in as {Username}.  Disconnect before logging in again.");
            }

            var login = MessageWaiter.Wait<LoginResponse>(MessageCode.ServerLogin);

            Console.WriteLine($"Sending login message");
            await ServerConnection.SendAsync(new LoginRequest(username, password).ToMessage());
            Console.WriteLine($"Login message sent");

            await login;

            if (login.Result.Succeeded)
            {
                Username = username;
                LoggedIn = true;
            }
            else
            {
                // upon login failure the server will refuse to allow any more input, eventually disconnecting.
                Disconnect();
                throw new LoginException($"Failed to log in as {username}: {login.Result.Message}");
            }
        }

        /// <summary>
        ///     Asynchronously performs a search for the specified <paramref name="searchText"/> using the specified <paramref name="options"/>.
        /// </summary>
        /// <param name="searchText">The text for which to search.</param>
        /// <param name="options">The options for the search.</param>
        /// <param name="cancellationToken">The optional cancellation token for the task.</param>
        /// <returns>The completed search.</returns>
        public async Task<Search> SearchAsync(string searchText, SearchOptions options = null, CancellationToken? cancellationToken = null)
        {
            if (State != ConnectionState.Connected)
            {
                throw new ConnectionStateException($"The server connection must be Connected to perform a search (currently: {State})");
            }

            if (!LoggedIn)
            {
                throw new SearchException($"A user must be logged in to perform a search.");
            }

            options = options ?? new SearchOptions();

            var search = new Search(searchText, options, ServerConnection)
            {
                ResponseHandler = (s, response) =>
                {
                    var e = new SearchResponseReceivedEventArgs() { Search = s, Response = response };
                    Task.Run(() => SearchResponseReceived?.Invoke(this, e)).Forget();
                }
            };

            ActiveSearches.TryAdd(search.Ticket, search);

            return await search.SearchAsync(cancellationToken);
        }

        #endregion Public Methods

        #region Protected Methods

        /// <summary>
        ///     Disposes this instance.
        /// </summary>
        /// <param name="disposing">A value indicating whether disposal is in progress.</param>
        protected virtual void Dispose(bool disposing)
        {
            if (!Disposed)
            {
                if (disposing)
                {
                    var message = "Client is being disposed.";

                    ServerConnection?.Disconnect(message);
                    ServerConnection?.Dispose();
                }

                Disposed = true;
            }
        }

        #endregion Protected Methods

        #region Private Methods

        private async Task ConnectToPeerHandler(ConnectToPeerResponse response)
        {
            if (response.Type == "F")
            {
                Console.WriteLine($"ConnectToPeerResponse \'F\' received, trying to locate download");

                //var t = new Connection(response.IPAddress.ToString(), response.Port, new ConnectionOptions() { ReadTimeout = 0 });

                //Console.WriteLine($"[CONNECT TO PEER]: {response.Token}");
                //Console.WriteLine($"[OPENING TRANSFER CONNECTION] {t.Address}:{t.Port}");
                //await t.ConnectAsync();
                //var request = new PierceFirewallRequest(response.Token);
                //await t.SendAsync(request.ToMessage().ToByteArray());

                //var tokenBytes = await t.ReadAsync(4);
                //var token = BitConverter.ToInt32(tokenBytes, 0);

                //Console.WriteLine($"Peer: {response.Username}, token: {token}");

                //ActiveDownloads.TryGetValue(response.Username, out var downloads);

                //var download = downloads.Where(d => d.Token == token).FirstOrDefault();

                //if (download != null)
                //{
                //    Console.WriteLine($"Download found, starting...");
                //    await download.StartDownload(t);
                //}

                //await ActiveDownload.ConnectToPeer(response, e);
            }
            else
            {
                var connection = new MessageConnection(ConnectionType.Peer, response.Username, response.IPAddress.ToString(), response.Port, Options.ConnectionOptions)
                {
                    Context = response,
                    ConnectHandler = async (conn) =>
                    {
                        var context = (ConnectToPeerResponse)conn.Context;
                        var request = new PierceFirewallRequest(context.Token).ToMessage();
                        await conn.SendAsync(request, suppressCodeNormalization: true);
                    },
                    DisconnectHandler = async (conn, message) =>
                    {
                        await MessageConnectionManager.Remove(conn);
                    },
                    MessageHandler = (conn, message) =>
                    {
                        PeerMessageHandler(conn, message);
                    }
                };

                await MessageConnectionManager.Add(connection);
            }
        }

        private async Task<GetPeerAddressResponse> GetPeerAddressAsync(string username)
        {
            var request = new GetPeerAddressRequest(username);
            await ServerConnection.SendAsync(request.ToMessage());

            return await MessageWaiter.Wait<GetPeerAddressResponse>(MessageCode.ServerGetPeerAddress, username);
        }

        private async void PeerConnectionStateChangedEventHandler(object sender, ConnectionStateChangedEventArgs e)
        {
            var connection = (IConnection)sender;

            Console.WriteLine($"[PEER CONNECTION]: {e.IPAddress}: {connection.State}");

            if (connection is IConnection transferConnection)
            {
                if (e.State == ConnectionState.Disconnected)
                {

                }
                else if (e.State == ConnectionState.Connected)
                {
                    var context = (ConnectToPeerResponse)transferConnection.Context;
                    var request = new PierceFirewallRequest(context.Token);
                    await transferConnection.SendAsync(request.ToMessage().ToByteArray());
                }
            }
        }

        private void PeerMessageHandler(IMessageConnection connection, Message message)
        {
            Console.WriteLine($"[PEER RESPONSE]: {message.Code}");
            switch (message.Code)
            {
                case MessageCode.PeerSearchResponse:
                    var response = SearchResponse.Parse(message);
                    if (ActiveSearches.TryGetValue(response.Ticket, out var search))
                    {
                        search.AddResponse(connection, response);
                    }

                    break;

                case MessageCode.PeerBrowseResponse:
                    MessageWaiter.Complete(MessageCode.PeerBrowseResponse, connection.Key, BrowseResponse.Parse(message));
                    break;
                default:
                    Console.WriteLine($"Unknown message: [{connection.IPAddress}] {message.Code}: {message.Payload.Length} bytes");
                    break;
            }
        }

        private async Task PrivateMessageHandler(PrivateMessage message)
        {
            Console.WriteLine($"[{message.Timestamp}][{message.Username}]: {message.Message}");
            await ServerConnection.SendAsync(new AcknowledgePrivateMessageRequest(message.Id).ToMessage());
        }
        private async Task ServerMessageHandler(Message message)
        {
            Console.WriteLine($"[MESSAGE]: {message.Code}");

            switch (message.Code)
            {
                case MessageCode.ServerParentMinSpeed:
                case MessageCode.ServerParentSpeedRatio:
                case MessageCode.ServerWishlistInterval:
                    MessageWaiter.Complete(message.Code, Integer.Parse(message));
                    break;

                case MessageCode.ServerLogin:
                    Console.WriteLine($"[SERVER LOGIN]");
                    MessageWaiter.Complete(message.Code, LoginResponse.Parse(message));
                    Console.WriteLine($"[SERVER LOGIN COMPLATE]");
                    break;

                case MessageCode.ServerRoomList:
                    MessageWaiter.Complete(message.Code, RoomList.Parse(message));
                    break;

                case MessageCode.ServerPrivilegedUsers:
                    MessageWaiter.Complete(message.Code, PrivilegedUserList.Parse(message));
                    break;

                case MessageCode.ServerConnectToPeer:
                    await ConnectToPeerHandler(ConnectToPeerResponse.Parse(message));
                    break;

                case MessageCode.ServerPrivateMessages:
                    await PrivateMessageHandler(PrivateMessage.Parse(message));
                    break;

                case MessageCode.ServerGetPeerAddress:
                    var response = GetPeerAddressResponse.Parse(message);
                    MessageWaiter.Complete(message.Code, response.Username, response);
                    break;

                default:
                    Console.WriteLine($"Unknown message: {message.Code}: {message.Payload.Length} bytes");
                    break;
            }

            Console.WriteLine($"[MESSAGE DONE]");
        }

        #endregion Private Methods

        //private IConnection GetPeerConnection(PeerConnectionKey key)
        //{
        //    var options = new ConnectionOptions()
        //    {
        //        ConnectTimeout = 15,
        //        ReadTimeout = 0,
        //        BufferSize = Options.ConnectionOptions.BufferSize,
        //    };

        //    if (key.Type == "P")
        //    {
        //        var conn = new MessageConnection(ConnectionType.Peer, key.IPAddress.ToString(), key.Port, options)
        //        {
        //            Context = key
        //        };

        //        conn.MessageReceived += OnPeerConnectionMessageReceived;
        //        conn.StateChanged += OnPeerConnectionStateChanged;
        //        return conn;
        //    }
        //    else if (key.Type == "F")
        //    {
        //        var conn = new TransferConnection(key.IPAddress.ToString(), key.Port, options)
        //        {
        //            Context = key
        //        };

        //        conn.StateChanged += OnPeerConnectionStateChanged;
        //        return conn;
        //    }
        //    else
        //    {
        //        throw new ConnectionException($"Unrecognized conection type '{key.Type}'; expected 'P' or 'F'");
        //    }
        //}
    }
}