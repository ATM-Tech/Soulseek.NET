﻿// <copyright file="SoulseekClient.cs" company="JP Dillingham">
//     Copyright(C) 2018 JP Dillingham
//     
//     This program is free software: you can redistribute it and/or modify
//     it under the terms of the GNU General Public License as published by
//     the Free Software Foundation, either version 3 of the License, or
//     (at your option) any later version.
//     
//     This program is distributed in the hope that it will be useful,
//     but WITHOUT ANY WARRANTY; without even the implied warranty of
//     MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.See the
//     GNU General Public License for more details.
//     
//     You should have received a copy of the GNU General Public License
//     along with this program.If not, see<https://www.gnu.org/licenses/>.
// </copyright>

namespace Soulseek.NET
{
    using Soulseek.NET.Messaging;
    using Soulseek.NET.Messaging.Requests;
    using Soulseek.NET.Messaging.Responses;
    using Soulseek.NET.Tcp;
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using System.Timers;
    using SystemTimer = System.Timers.Timer;

    /// <summary>
    ///     A client for the Soulseek file sharing network.
    /// </summary>
    public class SoulseekClient : IDisposable
    {
        /// <summary>
        ///     Initializes a new instance of the <see cref="SoulseekClient"/> class with the specified <paramref name="address"/> and <paramref name="port"/>.
        /// </summary>
        /// <param name="address">The address of the server to which to connect.</param>
        /// <param name="port">The port to which to connect.</param>
        public SoulseekClient(string address = "server.slsknet.org", int port = 2242)
        {
            Address = address;
            Port = port;

            Connection = new Connection(ConnectionType.Server, Address, Port);
            Connection.StateChanged += OnServerConnectionStateChanged;
            Connection.DataReceived += OnConnectionDataReceived;

            PeerConnectionMonitorTimer = new SystemTimer(5000);
            PeerConnectionMonitorTimer.Elapsed += PeerConnectionMonitor_Elapsed;
        }

        /// <summary>
        ///     Occurs when the underlying TCP connection to the server changes state.
        /// </summary>
        public event EventHandler<ConnectionStateChangedEventArgs> ConnectionStateChanged;

        /// <summary>
        ///     Occurs when raw data is recieved by the underlying TCP connection.
        /// </summary>
        public event EventHandler<DataReceivedEventArgs> DataReceived;

        /// <summary>
        ///     Occurs when a new message is recieved.
        /// </summary>
        public event EventHandler<MessageReceivedEventArgs> MessageReceived;

        /// <summary>
        ///     Gets or sets the address of the server to which to connect.
        /// </summary>
        public string Address { get; set; }

        /// <summary>
        ///     Gets the current state of the underlying TCP connection.
        /// </summary>
        public ConnectionState ConnectionState => Connection.State;

        /// <summary>
        ///     Gets the ParentMinSpeed value from the server.
        /// </summary>
        public int ParentMinSpeed { get; private set; }

        /// <summary>
        ///     Gets the ParentSpeedRatio value from the server.
        /// </summary>
        public int ParentSpeedRatio { get; private set; }

        /// <summary>
        ///     Gets or sets the port to which to connect.
        /// </summary>
        public int Port { get; set; }

        /// <summary>
        ///     Gets the list of privileged users from the server.
        /// </summary>
        public IEnumerable<string> PrivilegedUsers { get; private set; }

        /// <summary>
        ///     Gets the list of rooms from the server.
        /// </summary>
        public IEnumerable<Room> Rooms { get; private set; }

        /// <summary>
        ///     Gets the WishlistInterval value from the server.
        /// </summary>
        public int WishlistInterval { get; private set; }

        private List<Search> ActiveSearches { get; set; } = new List<Search>();
        private ReaderWriterLockSlim ActiveSearchesLock { get; set; } = new ReaderWriterLockSlim();
        private Connection Connection { get; set; }
        private bool Disposed { get; set; } = false;
        private MessageWaiter MessageWaiter { get; set; } = new MessageWaiter();
        private SystemTimer PeerConnectionMonitorTimer { get; set; }
        private List<Connection> PeerConnections { get; set; } = new List<Connection>();
        private ReaderWriterLockSlim PeerConnectionsLock { get; set; } = new ReaderWriterLockSlim();
        private Random Random { get; set; } = new Random();

        /// <summary>
        ///     Connects the client to the server specified in the <see cref="Address"/> and <see cref="Port"/> properties.
        /// </summary>
        public void Connect()
        {
            Task.Run(() => ConnectAsync()).GetAwaiter().GetResult();
        }

        /// <summary>
        ///     Asynchronously connects the client to the server specified in the <see cref="Address"/> and <see cref="Port"/> properties.
        /// </summary>
        public async Task ConnectAsync()
        {
            await Connection.ConnectAsync();
        }

        /// <summary>
        ///     Creates a Pending <see cref="Soulseek.Search"/> with the specified <paramref name="searchText"/>.
        /// </summary>
        /// <param name="searchText">The text for which to search.</param>
        /// <returns>A <see cref="SearchState.Pending"/> Search.</returns>
        public Search CreateSearch(string searchText)
        {
            var search = new Search(Connection, searchText);
            search.SearchCompleted += OnSearchCompleted;

            ActiveSearchesLock.EnterWriteLock();

            try
            {
                ActiveSearches.Add(search);
            }
            finally
            {
                ActiveSearchesLock.ExitWriteLock();
            }

            return search;
        }

        /// <summary>
        ///     Disconnects the client from the server with an optionally supplied <paramref name="message"/>.
        /// </summary>
        /// <param name="message">An optional disconnect message.</param>
        public void Disconnect(string message)
        {
            if (string.IsNullOrEmpty(message))
            {
                message = "User initiated shutdown";
            }

            Connection.Disconnect(message);

            PeerConnectionsLock.EnterUpgradeableReadLock();

            try
            {
                var connections = new List<Connection>(PeerConnections);

                PeerConnectionsLock.EnterWriteLock();

                try
                {
                    foreach (var connection in PeerConnections)
                    {
                        connection.Disconnect(message);
                        connection.Dispose();
                        PeerConnections.Remove(connection);
                    }
                }
                finally
                {
                    PeerConnectionsLock.ExitWriteLock();
                }
            }
            finally
            {
                PeerConnectionsLock.ExitUpgradeableReadLock();
            }

            ActiveSearchesLock.EnterUpgradeableReadLock();

            try
            {
                var searches = new List<Search>(ActiveSearches);

                ActiveSearchesLock.EnterWriteLock();

                try
                {
                    foreach (var search in ActiveSearches)
                    {
                        search.Cancel();
                        search.Dispose();
                        ActiveSearches.Remove(search);
                    }
                }
                finally
                {
                    ActiveSearchesLock.ExitWriteLock();
                }
            }
            finally
            {
                ActiveSearchesLock.ExitUpgradeableReadLock();
            }
        }

        /// <summary>
        ///     Disposes this instance.
        /// </summary>
        public void Dispose()
        {
            Dispose(true);
        }

        /// <summary>
        ///     Logs in to the server with the specified <paramref name="username"/> and <paramref name="password"/>.
        /// </summary>
        /// <param name="username">The username with which to log in.</param>
        /// <param name="password">The password with which to log in.</param>
        /// <returns>The server response.</returns>
        public LoginResponse Login(string username, string password)
        {
            return Task.Run(() => LoginAsync(username, password)).GetAwaiter().GetResult();
        }

        /// <summary>
        ///     Asynchronously logs in to the server with the specified <paramref name="username"/> and <paramref name="password"/>.
        /// </summary>
        /// <param name="username">The username with which to log in.</param>
        /// <param name="password">The password with which to log in.</param>
        /// <returns>The server response.</returns>
        public async Task<LoginResponse> LoginAsync(string username, string password)
        {
            var request = new LoginRequest(username, password);

            var login = MessageWaiter.Wait(MessageCode.ServerLogin).Task;
            var roomList = MessageWaiter.Wait(MessageCode.ServerRoomList).Task;
            var parentMinSpeed = MessageWaiter.Wait(MessageCode.ServerParentMinSpeed).Task;
            var parentSpeedRatio = MessageWaiter.Wait(MessageCode.ServerParentSpeedRatio).Task;
            var wishlistInterval = MessageWaiter.Wait(MessageCode.ServerWishlistInterval).Task;
            var privilegedUsers = MessageWaiter.Wait(MessageCode.ServerPrivilegedUsers).Task;

            await Connection.SendAsync(request.ToMessage().ToByteArray());

            Task.WaitAll(login, roomList, parentMinSpeed, parentSpeedRatio, wishlistInterval, privilegedUsers);

            Rooms = (IEnumerable<Room>)roomList.Result;
            ParentMinSpeed = ((int)parentMinSpeed.Result);
            ParentSpeedRatio = ((int)parentSpeedRatio.Result);
            WishlistInterval = ((int)wishlistInterval.Result);
            PrivilegedUsers = (IEnumerable<string>)privilegedUsers.Result;

            return (LoginResponse)login.Result;
        }

        /// <summary>
        ///     Performs a search for the specified <paramref name="searchText"/>.
        /// </summary>
        /// <param name="searchText">The text for which to search.</param>
        /// <returns>The completed search result.</returns>
        public Search Search(string searchText)
        {
            return Task.Run(() => SearchAsync(searchText)).GetAwaiter().GetResult();
        }

        /// <summary>
        ///     Asynchronously performs a search for the specified <paramref name="searchText"/>.
        /// </summary>
        /// <param name="searchText">The text for which to search.</param>
        /// <returns>The completed search result.</returns>
        public async Task<Search> SearchAsync(string searchText)
        {
            var search = CreateSearch(searchText);

            await search.StartAsync();

            var result = await MessageWaiter.Wait(MessageCode.ServerFileSearch, search.Ticket).Task;

            return (Search)result;
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!Disposed)
            {
                if (disposing)
                {
                    Connection?.Dispose();
                    ActiveSearches?.ForEach(s => s.Dispose());
                    PeerConnections?.ForEach(c => c.Dispose());
                    PeerConnectionMonitorTimer?.Dispose();
                }

                Disposed = true;
            }
        }

        private void HandlePeerSearchResponse(SearchResponse response, NetworkEventArgs e)
        {
            if (response.FileCount > 0)
            {
                var search = default(Search);

                ActiveSearchesLock.EnterReadLock();

                try
                {
                    search = ActiveSearches
                        .Where(s => s.State == SearchState.InProgress)
                        .Where(s => s.Ticket == response.Ticket)
                        .SingleOrDefault();
                }
                finally
                {
                    ActiveSearchesLock.ExitReadLock();
                }

                if (search != default(Search))
                {
                    search.AddResponse(response, e);
                }
            }
        }

        private async Task HandlePrivateMessage(PrivateMessage message, NetworkEventArgs e)
        {
            Console.WriteLine($"[{message.Timestamp}][{message.Username}]: {message.Message}");
            await Connection.SendAsync(new AcknowledgePrivateMessageRequest(message.Id).ToByteArray());
        }

        private async Task HandleServerConnectToPeer(ConnectToPeerResponse response, NetworkEventArgs e)
        {
            var connection = new Connection(ConnectionType.Peer, response.IPAddress.ToString(), response.Port);
            connection.DataReceived += OnConnectionDataReceived;
            connection.StateChanged += OnPeerConnectionStateChanged;

            PeerConnectionsLock.EnterWriteLock();

            try
            {
                PeerConnections.Add(connection);
            }
            finally
            {
                PeerConnectionsLock.ExitWriteLock();
            }

            try
            {
                await connection.ConnectAsync();

                var request = new PierceFirewallRequest(response.Token);
                await connection.SendAsync(request.ToByteArray(), suppressCodeNormalization: true);
            }
            catch (ConnectionException ex)
            {
                connection.Disconnect($"Failed to connect to peer {response.Username}@{response.IPAddress}:{response.Port}: {ex.Message}");
            }
        }

        private async void OnConnectionDataReceived(object sender, DataReceivedEventArgs e)
        {
            Task.Run(() => DataReceived?.Invoke(this, e)).Forget();

            var message = new Message(e.Data);
            var messageEventArgs = new MessageReceivedEventArgs(e) { Message = message };

            Task.Run(() => MessageReceived?.Invoke(this, messageEventArgs)).Forget();

            switch (message.Code)
            {
                case MessageCode.ServerParentMinSpeed:
                case MessageCode.ServerParentSpeedRatio:
                case MessageCode.ServerWishlistInterval:
                    MessageWaiter.Complete(message.Code, Integer.Parse(message));
                    break;

                case MessageCode.ServerLogin:
                    MessageWaiter.Complete(message.Code, LoginResponse.Parse(message));
                    break;

                case MessageCode.ServerRoomList:
                    MessageWaiter.Complete(message.Code, RoomList.Parse(message));
                    break;

                case MessageCode.ServerPrivilegedUsers:
                    MessageWaiter.Complete(message.Code, PrivilegedUserList.Parse(message));
                    break;

                case MessageCode.PeerSearchResponse:
                    HandlePeerSearchResponse(SearchResponse.Parse(message), e);
                    break;

                case MessageCode.ServerConnectToPeer:
                    await HandleServerConnectToPeer(ConnectToPeerResponse.Parse(message), e);
                    break;

                case MessageCode.ServerPrivateMessages:
                    await HandlePrivateMessage(PrivateMessage.Parse(message), e);
                    break;

                default:
                    Console.WriteLine($"Unknown message: [{e.IPAddress}] {message.Code}: {message.Payload.Length} bytes");
                    break;
            }
        }

        private void OnPeerConnectionStateChanged(object sender, ConnectionStateChangedEventArgs e)
        {
            if (e.State == ConnectionState.Disconnected && sender is Connection connection)
            {
                PeerConnectionsLock.EnterWriteLock();

                try
                {
                    connection.Dispose();
                    PeerConnections.Remove(connection);
                }
                finally
                {
                    PeerConnectionsLock.ExitWriteLock();
                }
            }
        }

        private void OnSearchCompleted(object sender, SearchCompletedEventArgs e)
        {
            ActiveSearchesLock.EnterWriteLock();

            try
            {
                ActiveSearches.Remove(e.Search);
            }
            finally
            {
                ActiveSearchesLock.ExitWriteLock();
            }

            MessageWaiter.Complete(MessageCode.ServerFileSearch, e.Search.Ticket, e.Search);
        }

        private async void OnServerConnectionStateChanged(object sender, ConnectionStateChangedEventArgs e)
        {
            if (e.State == ConnectionState.Connected)
            {
                PeerConnectionMonitorTimer.Start();
            }

            await Task.Run(() => ConnectionStateChanged?.Invoke(this, e));
        }

        private void PeerConnectionMonitor_Elapsed(object sender, ElapsedEventArgs e)
        {
            try
            {
                var total = 0;
                var connecting = 0;
                var connected = 0;

                PeerConnectionsLock.EnterUpgradeableReadLock();

                try
                {
                    total = PeerConnections.Count();
                    connecting = PeerConnections.Where(c => c?.State == ConnectionState.Connecting).Count();
                    connected = PeerConnections.Where(c => c?.State == ConnectionState.Connected).Count();
                    var disconnectedPeers = new List<Connection>(PeerConnections.Where(c => c == null || c.State == ConnectionState.Disconnected));

                    Console.WriteLine($"████████████████████ Peers: Total: {total}, Connecting: {connecting}, Connected: {connected}, Disconnected: {disconnectedPeers.Count()}");

                    PeerConnectionsLock.EnterWriteLock();

                    try
                    {
                        foreach (var connection in disconnectedPeers)
                        {
                            connection?.Dispose();
                            PeerConnections.Remove(connection);
                        }
                    }
                    finally
                    {
                        PeerConnectionsLock.ExitWriteLock();
                    }
                }
                finally
                {
                    PeerConnectionsLock.ExitUpgradeableReadLock();
                }

                PeerConnectionMonitorTimer.Reset();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in peer connection monitor: {ex}");
            }
        }
    }
}