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
    using Soulseek.NET.Exceptions;
    using Soulseek.NET.Messaging;
    using Soulseek.NET.Messaging.Messages;
    using Soulseek.NET.Messaging.Tcp;
    using Soulseek.NET.Tcp;

    /// <summary>
    ///     A client for the Soulseek file sharing network.
    /// </summary>
    public class SoulseekClient : ISoulseekClient
    {
        private const string DefaultAddress = "vps.slsknet.org";
        private const int DefaultPort = 2271;

        /// <summary>
        ///     Initializes a new instance of the <see cref="SoulseekClient"/> class.
        /// </summary>
        /// <param name="options">The client <see cref="SoulseekClientOptions"/>.</param>
        public SoulseekClient(SoulseekClientOptions options)
            : this(DefaultAddress, DefaultPort, options)
        {
        }

        /// <summary>
        ///     Initializes a new instance of the <see cref="SoulseekClient"/> class.
        /// </summary>
        /// <param name="address">The address of the server to which to connect.</param>
        /// <param name="port">The port to which to connect.</param>
        /// <param name="options">The client <see cref="SoulseekClientOptions"/>.</param>
        public SoulseekClient(string address = DefaultAddress, int port = DefaultPort, SoulseekClientOptions options = null)
            : this(address, port, options, null)
        {
        }

        /// <summary>
        ///     Initializes a new instance of the <see cref="SoulseekClient"/> class.
        /// </summary>
        /// <param name="address">The address of the server to which to connect.</param>
        /// <param name="port">The port to which to connect.</param>
        /// <param name="options">The client <see cref="SoulseekClientOptions"/>.</param>
        /// <param name="serverConnection">The IMessageConnection instance to use.</param>
        /// <param name="messageConnectionFactory">The IMessageConnectionFactory instance to use.</param>
        /// <param name="connectionFactory">The IConnectionFactory instance to use.</param>
        /// <param name="peerConnectionManager">The IConnectionManager instance to use.</param>
        /// <param name="messageWaiter">The IWaiter instance to use.</param>
        /// <param name="tokenFactory">The ITokenFactory to use.</param>
        /// <param name="diagnosticFactory">The IDiagnosticFactory to use.</param>
        internal SoulseekClient(
            string address,
            int port,
            SoulseekClientOptions options = null,
            IMessageConnection serverConnection = null,
            IMessageConnectionFactory messageConnectionFactory = null,
            IConnectionFactory connectionFactory = null,
            IConnectionManager<IMessageConnection> peerConnectionManager = null,
            IWaiter messageWaiter = null,
            ITokenFactory tokenFactory = null,
            IDiagnosticFactory diagnosticFactory = null)
        {
            Address = address;
            Port = port;

            Options = options ?? new SoulseekClientOptions();

            ServerConnection = serverConnection ?? GetServerMessageConnection(Address, Port, Options.ServerConnectionOptions);
            MessageConnectionFactory = messageConnectionFactory ?? new MessageConnectionFactory();
            ConnectionFactory = connectionFactory ?? new ConnectionFactory();
            PeerConnectionManager = peerConnectionManager ?? new ConnectionManager<IMessageConnection>(Options.ConcurrentPeerConnections);
            MessageWaiter = messageWaiter ?? new Waiter(Options.MessageTimeout);
            TokenFactory = tokenFactory ?? new TokenFactory();
            Diagnostic = diagnosticFactory ?? new DiagnosticFactory(this, Options.MinimumDiagnosticLevel, (e) => DiagnosticGenerated?.Invoke(this, e));
        }

        /// <summary>
        ///     Occurs when an internal diagnostic message is generated.
        /// </summary>
        public event EventHandler<DiagnosticGeneratedEventArgs> DiagnosticGenerated;

        /// <summary>
        ///     Occurs when an active download receives data.
        /// </summary>
        public event EventHandler<DownloadProgressUpdatedEventArgs> DownloadProgressUpdated;

        /// <summary>
        ///     Occurs when a download changes state.
        /// </summary>
        public event EventHandler<DownloadStateChangedEventArgs> DownloadStateChanged;

        /// <summary>
        ///     Occurs when a private message is received.
        /// </summary>
        public event EventHandler<PrivateMessage> PrivateMessageReceived;

        /// <summary>
        ///     Occurs when a new search result is received.
        /// </summary>
        public event EventHandler<SearchResponseReceivedEventArgs> SearchResponseReceived;

        /// <summary>
        ///     Occurs when a search changes state.
        /// </summary>
        public event EventHandler<SearchStateChangedEventArgs> SearchStateChanged;

        /// <summary>
        ///     Occurs when the client changes state.
        /// </summary>
        public event EventHandler<SoulseekClientStateChangedEventArgs> StateChanged;

        /// <summary>
        ///     Gets or sets the address of the server to which to connect.
        /// </summary>
        public string Address { get; set; }

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
        public SoulseekClientStates State { get; private set; } = SoulseekClientStates.Disconnected;

        /// <summary>
        ///     Gets the name of the currently signed in user.
        /// </summary>
        public string Username { get; private set; }

        private ConcurrentDictionary<int, Download> ActiveDownloads { get; set; } = new ConcurrentDictionary<int, Download>();
        private ConcurrentDictionary<int, Search> ActiveSearches { get; set; } = new ConcurrentDictionary<int, Search>();
        private IDiagnosticFactory Diagnostic { get; }
        private bool Disposed { get; set; } = false;
        private IMessageConnectionFactory MessageConnectionFactory { get; set; }
        private IWaiter MessageWaiter { get; set; }
        private IConnectionFactory ConnectionFactory { get; set; }
        private IConnectionManager<IMessageConnection> PeerConnectionManager { get; set; }
        private ConcurrentDictionary<int, Download> QueuedDownloads { get; set; } = new ConcurrentDictionary<int, Download>();
        private IMessageConnection ServerConnection { get; set; }
        private ITokenFactory TokenFactory { get; set; }

        /// <summary>
        ///     Asynchronously sends a private message acknowledgement for the specified <paramref name="privateMessageId"/>.
        /// </summary>
        /// <param name="privateMessageId">The unique id of the private message to acknowledge.</param>
        /// <returns>A Task representing the operation.</returns>
        /// <exception cref="InvalidOperationException">Thrown when the client is not connected or logged in.</exception>
        /// <exception cref="PrivateMessageException">Thrown when an exception is encountered during the operation.</exception>
        public Task AcknowledgePrivateMessageAsync(int privateMessageId)
        {
            if (!State.HasFlag(SoulseekClientStates.Connected))
            {
                throw new InvalidOperationException($"The server connection must be Connected to browse (currently: {State})");
            }

            if (!State.HasFlag(SoulseekClientStates.LoggedIn))
            {
                throw new InvalidOperationException($"A user must be logged in to browse.");
            }

            return AcknowledgePrivateMessageInternalAsync(privateMessageId);
        }

        /// <summary>
        ///     Asynchronously fetches the list of files shared by the specified <paramref name="username"/> with the optionally
        ///     specified <paramref name="cancellationToken"/>.
        /// </summary>
        /// <param name="username">The user to browse.</param>
        /// <param name="cancellationToken">A cancellation token.</param>
        /// <returns>The operation response.</returns>
        /// <exception cref="ArgumentException">Thrown when the <paramref name="username"/> is null, empty, or consists only of whitespace.</exception>
        /// <exception cref="InvalidOperationException">Thrown when the client is not connected or logged in.</exception>
        /// <exception cref="BrowseException">Thrown when an exception is encountered during the operation.</exception>
        public Task<BrowseResponse> BrowseAsync(string username, CancellationToken? cancellationToken = null)
        {
            if (string.IsNullOrWhiteSpace(username))
            {
                throw new ArgumentException($"The username must not be a null or empty string, or one consisting only of whitespace.", nameof(username));
            }

            if (!State.HasFlag(SoulseekClientStates.Connected))
            {
                throw new InvalidOperationException($"The server connection must be Connected to browse (currently: {State})");
            }

            if (!State.HasFlag(SoulseekClientStates.LoggedIn))
            {
                throw new InvalidOperationException($"A user must be logged in to browse.");
            }

            return BrowseInternalAsync(username, cancellationToken, null);
        }

        /// <summary>
        ///     Asynchronously connects the client to the server specified in the <see cref="Address"/> and <see cref="Port"/> properties.
        /// </summary>
        /// <returns>A task representing the asynchronous operation.</returns>
        /// <exception cref="InvalidOperationException">Thrown when the client is already connected.</exception>
        /// <exception cref="ConnectionException">Thrown when an exception is encountered during the operation.</exception>
        public async Task ConnectAsync()
        {
            if (State.HasFlag(SoulseekClientStates.Connected))
            {
                throw new InvalidOperationException($"Failed to connect; the client is already connected.");
            }

            try
            {
                await ServerConnection.ConnectAsync().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                throw new ConnectionException($"Failed to connect: {ex.Message}.", ex);
            }
        }

        /// <summary>
        ///     Disconnects the client from the server.
        /// </summary>
        /// <param name="message">An optional message describing the reason the client is being disconnected.</param>
        public void Disconnect(string message = null)
        {
            ServerConnection?.Disconnect(message ?? "Client disconnected.");

            PeerConnectionManager?.RemoveAll();

            ActiveSearches?.RemoveAndDisposeAll();

            QueuedDownloads?.RemoveAll();
            ActiveDownloads?.RemoveAll();

            MessageWaiter?.CancelAll();

            Username = null;

            ChangeState(SoulseekClientStates.Disconnected);
        }

        /// <summary>
        ///     Disposes this instance.
        /// </summary>
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        /// <summary>
        ///     Asynchronously downloads the specified <paramref name="filename"/> from the specified <paramref name="username"/>
        ///     and with the optionally specified <paramref name="token"/> and <paramref name="cancellationToken"/>.
        /// </summary>
        /// <remarks>If no <paramref name="token"/> is specified, one will be randomly generated internally.</remarks>
        /// <param name="username">The user from which to download the file.</param>
        /// <param name="filename">The file to download.</param>
        /// <param name="token">The unique download token.</param>
        /// <param name="cancellationToken">A cancellation token.</param>
        /// <returns>The operation context, including a byte array containing the file contents.</returns>
        /// <exception cref="ArgumentException">Thrown when the <paramref name="username"/> or <paramref name="filename"/> is null, empty, or consists only of whitespace.</exception>
        /// <exception cref="InvalidOperationException">Thrown when the client is not connected or logged in.</exception>
        /// <exception cref="DownloadException">Thrown when an exception is encountered during the operation.</exception>
        public Task<byte[]> DownloadAsync(string username, string filename, int? token = null, CancellationToken? cancellationToken = null)
        {
            if (string.IsNullOrWhiteSpace(username))
            {
                throw new ArgumentException($"The username must not be a null or empty string, or one consisting only of whitespace.", nameof(username));
            }

            if (string.IsNullOrWhiteSpace(filename))
            {
                throw new ArgumentException($"The filename must not be a null or empty string, or one consisting only of whitespace.", nameof(filename));
            }

            if (!State.HasFlag(SoulseekClientStates.Connected))
            {
                throw new InvalidOperationException($"The server connection must be Connected to browse (currently: {State})");
            }

            if (!State.HasFlag(SoulseekClientStates.LoggedIn))
            {
                throw new InvalidOperationException($"A user must be logged in to browse.");
            }

            bool tokenExists(int t) => QueuedDownloads.ContainsKey(t) || ActiveDownloads.Any(d => d.Value.Token == t);
            int tokenInternal;

            if (token != null)
            {
                if (tokenExists((int)token))
                {
                    throw new ArgumentException($"An active or queued download with token {token} is already in progress.", nameof(token));
                }

                tokenInternal = (int)token;
            }
            else
            {
                if (!TokenFactory.TryGetToken(tokenExists, out var generatedToken))
                {
                    throw new DownloadException($"Unable to generate a unique token for the download.");
                }

                tokenInternal = (int)generatedToken;
            }

            return DownloadInternalAsync(username, filename, tokenInternal, cancellationToken);
        }

        /// <summary>
        ///     Asynchronously logs in to the server with the specified <paramref name="username"/> and <paramref name="password"/>.
        /// </summary>
        /// <param name="username">The username with which to log in.</param>
        /// <param name="password">The password with which to log in.</param>
        /// <returns>A Task representing the operation.</returns>
        /// <exception cref="ArgumentException">Thrown when the <paramref name="username"/> or <paramref name="password"/> is null, empty, or consists only of whitespace.</exception>
        /// <exception cref="InvalidOperationException">Thrown when the client is not connected or logged in.</exception>
        /// <exception cref="LoginException">Thrown when the login fails, or when an exception is encountered during the operation.</exception>
        public Task LoginAsync(string username, string password)
        {
            if (string.IsNullOrEmpty(username))
            {
                throw new ArgumentException("Username may not be null or an empty string.", nameof(username));
            }

            if (string.IsNullOrEmpty(password))
            {
                throw new ArgumentException("Password may not be null or an empty string.", nameof(password));
            }

            if (!State.HasFlag(SoulseekClientStates.Connected))
            {
                throw new InvalidOperationException($"The client must be connected to log in.");
            }

            if (State.HasFlag(SoulseekClientStates.LoggedIn))
            {
                throw new InvalidOperationException($"Already logged in as {Username}.  Disconnect before logging in again.");
            }

            return LoginInternalAsync(username, password);
        }

        /// <summary>
        ///     Asynchronously searches for the specified <paramref name="searchText"/> and unique <paramref name="token"/> and
        ///     with the optionally specified <paramref name="options"/> and <paramref name="cancellationToken"/>.
        /// </summary>
        /// <param name="searchText">The text for which to search.</param>
        /// <param name="token">The unique search token.</param>
        /// <param name="options">The operation <see cref="SearchOptions"/>.</param>
        /// <param name="cancellationToken">A cancellation token.</param>
        /// <param name="waitForCompletion">A value indicating whether the search should wait completion before returning.</param>
        /// <returns>The operation context, including the search results.</returns>
        /// <exception cref="ArgumentException">
        ///     Thrown when the specified <paramref name="searchText"/> is null, empty, or consists of only whitespace.
        /// </exception>
        /// <exception cref="ArgumentException">
        ///     Thrown when a search with the specified <paramref name="token"/> is already in progress.
        /// </exception>
        /// <exception cref="InvalidOperationException">
        ///     Thrown when the client is not connected to the server, or no user is logged in.
        /// </exception>
        /// <exception cref="SearchException">Thrown when an exception is encountered during the operation.</exception>
        public Task<IReadOnlyCollection<SearchResponse>> SearchAsync(string searchText, int token, SearchOptions options = null, CancellationToken? cancellationToken = null, bool waitForCompletion = true)
        {
            if (string.IsNullOrWhiteSpace(searchText))
            {
                throw new ArgumentException($"Search text must not be a null or empty string, or one consisting only of whitespace.", nameof(searchText));
            }

            if (ActiveSearches.ContainsKey(token))
            {
                throw new ArgumentException($"An active search with token {token} is already in progress.", nameof(token));
            }

            if (!State.HasFlag(SoulseekClientStates.Connected))
            {
                throw new InvalidOperationException($"The server connection must be Connected to search (currently: {State})");
            }

            if (!State.HasFlag(SoulseekClientStates.LoggedIn))
            {
                throw new InvalidOperationException($"A user must be logged in to search.");
            }

            options = options ?? new SearchOptions();

            return SearchInternalAsync(searchText, token, options, cancellationToken, waitForCompletion);
        }

        /// <summary>
        ///     Asynchronously sends the specified private <paramref name="message"/> to the specified <paramref name="username"/>.
        /// </summary>
        /// <param name="username">The user to which the message is to be sent.</param>
        /// <param name="message">The message to send.</param>
        /// <returns>A Task representing the operation.</returns>
        /// <exception cref="ArgumentException">Thrown when the <paramref name="username"/> or <paramref name="message"/> is null, empty, or consists only of whitespace.</exception>
        /// <exception cref="InvalidOperationException">Thrown when the client is not connected or logged in.</exception>
        /// <exception cref="PrivateMessageException">Thrown when an exception is encountered during the operation.</exception>
        public Task SendPrivateMessageAsync(string username, string message)
        {
            if (string.IsNullOrWhiteSpace(username))
            {
                throw new ArgumentException($"The username must not be a null or empty string, or one consisting only of whitespace.", nameof(username));
            }

            if (string.IsNullOrWhiteSpace(message))
            {
                throw new ArgumentException($"The message must not be a null or empty string, or one consisting only of whitespace.", nameof(message));
            }

            if (!State.HasFlag(SoulseekClientStates.Connected))
            {
                throw new InvalidOperationException($"The server connection must be Connected to browse (currently: {State})");
            }

            if (!State.HasFlag(SoulseekClientStates.LoggedIn))
            {
                throw new InvalidOperationException($"A user must be logged in to browse.");
            }

            return SendPrivateMessageInternalAsync(username, message);
        }

        /// <summary>
        ///     Disposes this instance.
        /// </summary>
        /// <param name="disposing">A value indicating whether disposal is in progress.</param>
        protected virtual void Dispose(bool disposing)
        {
            if (!Disposed)
            {
                Disconnect("Client is being disposed.");

                if (disposing)
                {
                    PeerConnectionManager?.Dispose();
                    MessageWaiter?.Dispose();
                    ServerConnection?.Dispose();
                }

                Disposed = true;
            }
        }

        private async Task AcknowledgePrivateMessageInternalAsync(int privateMessageId)
        {
            try
            {
                await ServerConnection.WriteMessageAsync(new AcknowledgePrivateMessageRequest(privateMessageId).ToMessage()).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                throw new PrivateMessageException($"Failed to send an acknowledgement for private message id {privateMessageId}: {ex.Message}", ex);
            }
        }

        private async Task<BrowseResponse> BrowseInternalAsync(string username, CancellationToken? cancellationToken = null, IMessageConnection connection = null)
        {
            try
            {
                var browseWait = MessageWaiter.WaitIndefinitely<BrowseResponse>(new WaitKey(MessageCode.PeerBrowseResponse, username), cancellationToken);

                connection = connection ?? await GetUnsolicitedPeerConnectionAsync(username, Options.PeerConnectionOptions).ConfigureAwait(false);
                connection.Disconnected += (sender, message) =>
                {
                    MessageWaiter.Throw(new WaitKey(MessageCode.PeerBrowseResponse, username), new ConnectionException($"Peer connection disconnected unexpectedly: {message}"));
                };

                await connection.WriteMessageAsync(new PeerBrowseRequest().ToMessage()).ConfigureAwait(false);

                var response = await browseWait.ConfigureAwait(false);
                return response;
            }
            catch (Exception ex)
            {
                throw new BrowseException($"Failed to browse user {Username}: {ex.Message}", ex);
            }
        }

        private void ChangeState(SoulseekClientStates state, string message = null)
        {
            var previousState = State;
            State = state;
            Task.Run(() => StateChanged?.Invoke(this, new SoulseekClientStateChangedEventArgs(previousState, State, message)));
        }

        private async Task<byte[]> DownloadInternalAsync(string username, string filename, int token, CancellationToken? cancellationToken = null)
        {
            var download = new Download(username, filename, token);

            try
            {
                // establish a message connection to the peer so that we can request the file
                var peerConnection = await GetUnsolicitedPeerConnectionAsync(username, Options.PeerConnectionOptions).ConfigureAwait(false);

                // prepare two waits; one for the transfer response to confirm that our request is acknowledged and another for the eventual transfer request sent when the
                // peer is ready to send the file. the response message should be returned immediately, while the request will be sent only when we've reached the front of the remote queue.
                var transferRequestAcknowledged = MessageWaiter.Wait<PeerTransferResponse>(
                    new WaitKey(MessageCode.PeerTransferResponse, download.Username, download.Token), cancellationToken: cancellationToken);
                var transferStartRequested = MessageWaiter.WaitIndefinitely<PeerTransferRequest>(
                    new WaitKey(MessageCode.PeerTransferRequest, download.Username, download.Filename), cancellationToken);

                // request the file
                await peerConnection.WriteMessageAsync(new PeerTransferRequest(TransferDirection.Download, token, filename).ToMessage()).ConfigureAwait(false);

                var transferRequestAcknowledgement = await transferRequestAcknowledged.ConfigureAwait(false);

                if (transferRequestAcknowledgement.Allowed)
                {
                    // in testing peers have, without exception, returned Allowed = false, Message = Queued for this request,
                    // regardless of number of available slots and/or queue depth. this condition is likely only used when
                    // uploading to a peer, which is not currently supported.
                    throw new DownloadException($"A condition believed to be unreachable (PeerTransferResponseIncoming.Allowed = true) was reached.  Please report this in a GitHub issue and provide context.");
                }

                // the download is remotely queued, so put it in the local queue.
                QueuedDownloads.TryAdd(download.Token, download);
                download.State = DownloadStates.Queued;
                DownloadStateChanged?.Invoke(this, new DownloadStateChangedEventArgs(previousState: DownloadStates.None, download: download));

                // wait for the peer to respond that they are ready to start the transfer
                var transferStartRequest = await transferStartRequested.ConfigureAwait(false);

                download.Size = transferStartRequest.FileSize;
                download.RemoteToken = transferStartRequest.Token;

                // move the download from the local queue to active
                QueuedDownloads.TryRemove(download.Token, out var _);
                ActiveDownloads.TryAdd(download.RemoteToken, download);
                download.State = DownloadStates.Initializing;
                DownloadStateChanged?.Invoke(this, new DownloadStateChangedEventArgs(previousState: DownloadStates.Queued, download: download));

                // change the following line to await a ConnectToPeerResponse
                var transferConnectionRequest = MessageWaiter.Wait(download.WaitKey, cancellationToken: cancellationToken);
                var downloadCompleted = MessageWaiter.WaitIndefinitely<byte[]>(download.WaitKey, cancellationToken);

                // respond to the peer that we are ready to accept the file
                await peerConnection.WriteMessageAsync(new PeerTransferResponse(download.RemoteToken, true, download.Size, string.Empty).ToMessage()).ConfigureAwait(false);

                // fetch the CTPR from the wait here
                await transferConnectionRequest.ConfigureAwait(false);

                download.State = DownloadStates.InProgress;
                DownloadStateChanged?.Invoke(this, new DownloadStateChangedEventArgs(previousState: DownloadStates.Initializing, download: download));




                // stick the code from HandleDownloadAsync() here





                // wait for the transfer to complete
                download.Data = await downloadCompleted.ConfigureAwait(false);
                download.State = DownloadStates.Succeeded;
                DownloadStateChanged?.Invoke(this, new DownloadStateChangedEventArgs(previousState: DownloadStates.InProgress, download: download));

                return download.Data;
            }
            catch (OperationCanceledException ex)
            {
                download.State = DownloadStates.Cancelled;
                download.Connection?.Disconnect("Transfer cancelled.");

                throw new DownloadException($"Download of file {filename} from user {username} was cancelled.", ex);
            }
            catch (TimeoutException ex)
            {
                download.State = DownloadStates.TimedOut;
                download.Connection?.Disconnect("Transfer timed out.");

                throw new DownloadException($"Failed to download file {filename} from user {username}: {ex.Message}", ex);
            }
            catch (Exception ex)
            {
                download.State = DownloadStates.Errored;
                download.Connection?.Disconnect("Transfer error.");

                throw new DownloadException($"Failed to download file {filename} from user {username}: {ex.Message}", ex);
            }
            finally
            {
                download.Connection?.Dispose();

                QueuedDownloads.TryRemove(download.Token, out var _);
                ActiveDownloads.TryRemove(download.RemoteToken, out var _);

                var previousState = download.State;
                download.State = download.State | DownloadStates.Completed;
                DownloadStateChanged?.Invoke(this, new DownloadStateChangedEventArgs(previousState: previousState, download: download));
            }
        }

        private async Task<ConnectionKey> GetPeerConnectionKeyAsync(string username)
        {
            var addressWait = MessageWaiter.Wait<GetPeerAddressResponse>(new WaitKey(MessageCode.ServerGetPeerAddress, username));

            var request = new GetPeerAddressRequest(username);
            await ServerConnection.WriteMessageAsync(request.ToMessage()).ConfigureAwait(false);

            var address = await addressWait.ConfigureAwait(false);
            return new ConnectionKey(username, address.IPAddress, address.Port, MessageConnectionType.Peer);
        }

        private IMessageConnection GetServerMessageConnection(string address, int port, ConnectionOptions options)
        {
            var ipAddress = default(IPAddress);

            try
            {
                ipAddress = address.ResolveIPAddress();
            }
            catch (Exception ex)
            {
                throw new SoulseekClientException($"Failed to resolve address '{address}': {ex.Message}", ex);
            }

            var conn = new MessageConnection(MessageConnectionType.Server, ipAddress, port, options);
            conn.Connected += (sender, e) =>
            {
                ChangeState(SoulseekClientStates.Connected);
            };

            conn.Disconnected += (sender, e) =>
            {
                Disconnect();
            };

            conn.MessageRead += ServerConnection_MessageRead;

            return conn;
        }

        private async Task<IMessageConnection> GetSolicitedPeerConnectionAsync(ConnectToPeerResponse connectToPeerResponse, ConnectionOptions options)
        {
            var connection = new MessageConnection(MessageConnectionType.Peer, connectToPeerResponse.Username, connectToPeerResponse.IPAddress, connectToPeerResponse.Port, options)
            {
                Context = connectToPeerResponse,
            };

            connection.Connected += async (sender, e) =>
            {
                var conn = (IMessageConnection)sender;
                var context = (ConnectToPeerResponse)conn.Context;
                var request = new PierceFirewallRequest(context.Token).ToMessage();
                await conn.WriteAsync(request.ToByteArray()).ConfigureAwait(false);
            };

            connection.Disconnected += async (sender, e) =>
            {
                await PeerConnectionManager.RemoveAsync((IMessageConnection)sender).ConfigureAwait(false);
            };

            connection.MessageRead += PeerConnection_MessageRead;

            await PeerConnectionManager.AddAsync(connection).ConfigureAwait(false);
            return connection;
        }

        private async Task<IConnection> GetTransferConnectionAsync(ConnectToPeerResponse connectToPeerResponse, ConnectionOptions options)
        {
            var connection = ConnectionFactory.GetConnection(connectToPeerResponse.IPAddress, connectToPeerResponse.Port, options);
            await connection.ConnectAsync().ConfigureAwait(false);

            var request = new PierceFirewallRequest(connectToPeerResponse.Token);
            await connection.WriteAsync(request.ToMessage().ToByteArray()).ConfigureAwait(false);

            return connection;
        }

        private async Task<IMessageConnection> GetUnsolicitedPeerConnectionAsync(string username, ConnectionOptions options)
        {
            var key = await GetPeerConnectionKeyAsync(username).ConfigureAwait(false);
            var connection = PeerConnectionManager.Get(key);

            if (connection != default(IMessageConnection) && (connection.State == ConnectionState.Disconnecting || connection.State == ConnectionState.Disconnected))
            {
                await PeerConnectionManager.RemoveAsync(connection).ConfigureAwait(false);
                connection = default(IMessageConnection);
            }

            if (connection == default(IMessageConnection))
            {
                connection = MessageConnectionFactory.GetMessageConnection(MessageConnectionType.Peer, key.Username, key.IPAddress, key.Port, options);
                connection.MessageRead += PeerConnection_MessageRead;

                connection.Connected += async (sender, e) =>
                {
                    var token = new Random().Next(1, 2147483647);
                    await connection.WriteAsync(new PeerInitRequest(Username, "P", token).ToMessage().ToByteArray()).ConfigureAwait(false);
                };

                connection.Disconnected += async (sender, e) =>
                {
                    await PeerConnectionManager.RemoveAsync((IMessageConnection)sender).ConfigureAwait(false);
                };

                await PeerConnectionManager.AddAsync(connection).ConfigureAwait(false);
            }

            return connection;
        }

        private async Task HandleDownloadAsync(ConnectToPeerResponse downloadResponse)
        {
            int remoteToken = 0;
            IConnection connection = null;

            try
            {
                connection = await GetTransferConnectionAsync(downloadResponse, Options.TransferConnectionOptions).ConfigureAwait(false);
                var remoteTokenBytes = await connection.ReadAsync(4).ConfigureAwait(false);
                remoteToken = BitConverter.ToInt32(remoteTokenBytes, 0);
            }
            catch (Exception ex)
            {
                Diagnostic.Warning($"Error initializing download connection from {downloadResponse.Username}: {ex.Message}", ex);
                return;
            }

            if (ActiveDownloads.TryGetValue(remoteToken, out var download))
            {
                MessageWaiter.Complete(download.WaitKey); // complete the "download start" wait

                connection.DataRead += (sender, e) =>
                {
                    var eventArgs = new DownloadProgressUpdatedEventArgs(download, e.CurrentLength);
                    DownloadProgressUpdated?.Invoke(this, eventArgs);
                };

                connection.Disconnected += (sender, message) =>
                {
                    if (download.State.HasFlag(DownloadStates.Succeeded))
                    {
                        MessageWaiter.Complete(download.WaitKey, download.Data);
                    }
                    else if (download.State.HasFlag(DownloadStates.TimedOut))
                    {
                        MessageWaiter.Throw(download.WaitKey, new TimeoutException(message));
                    }
                    else
                    {
                        MessageWaiter.Throw(download.WaitKey, new ConnectionException($"Transfer failed: {message}"));
                    }
                };

                download.Connection = connection;

                try
                {
                    // write an empty 8 byte array to initiate the transfer. not sure what this is; it was identified via WireShark.
                    await connection.WriteAsync(new byte[8]).ConfigureAwait(false);

                    var bytes = await connection.ReadAsync(download.Size).ConfigureAwait(false);

                    download.Data = bytes;
                    download.State = DownloadStates.Succeeded;
                    connection.Disconnect("Transfer complete.");
                }
                catch (TimeoutException)
                {
                    download.State = DownloadStates.TimedOut;
                    connection.Disconnect($"Transfer timed out after {Options.TransferConnectionOptions.ReadTimeout} seconds of inactivity.");
                }
                catch (Exception ex)
                {
                    connection.Disconnect(ex.Message);
                }
            }
        }

        private async Task LoginInternalAsync(string username, string password)
        {
            try
            {
                var loginWait = MessageWaiter.Wait<LoginResponse>(new WaitKey(MessageCode.ServerLogin));

                await ServerConnection.WriteMessageAsync(new LoginRequest(username, password).ToMessage()).ConfigureAwait(false);

                var response = await loginWait.ConfigureAwait(false);

                if (response.Succeeded)
                {
                    Username = username;
                    ChangeState(SoulseekClientStates.Connected | SoulseekClientStates.LoggedIn);
                }
                else
                {
                    Disconnect(); // upon login failure the server will refuse to allow any more input, eventually disconnecting.
                    throw new LoginException($"The server rejected login attempt: {response.Message}");
                }
            }
            catch (Exception ex)
            {
                throw new LoginException($"Failed to log in as {username}: {ex.Message}", ex);
            }
        }

        private void PeerConnection_MessageRead(object sender, Message message)
        {
            var connection = (IMessageConnection)sender;
            Diagnostic.Debug($"Peer message received: {message.Code} from {connection.Username} ({connection.IPAddress}:{connection.Port})");

            switch (message.Code)
            {
                case MessageCode.PeerSearchResponse:
                    var searchResponse = SearchResponseSlim.Parse(message);
                    if (ActiveSearches.TryGetValue(searchResponse.Token, out var search))
                    {
                        search.AddResponse(searchResponse);
                    }

                    break;

                case MessageCode.PeerBrowseResponse:
                    var browseWaitKey = new WaitKey(MessageCode.PeerBrowseResponse, connection.Key.Username);
                    try
                    {
                        MessageWaiter.Complete(browseWaitKey, BrowseResponse.Parse(message));
                    }
                    catch (Exception ex)
                    {
                        MessageWaiter.Throw(browseWaitKey, new MessageReadException("The peer returned an invalid browse response.", ex));
                    }

                    break;

                case MessageCode.PeerTransferResponse:
                    var transferResponse = PeerTransferResponse.Parse(message);
                    MessageWaiter.Complete(new WaitKey(MessageCode.PeerTransferResponse, connection.Username, transferResponse.Token), transferResponse);
                    break;

                case MessageCode.PeerTransferRequest:
                    var transferRequest = PeerTransferRequest.Parse(message);
                    MessageWaiter.Complete(new WaitKey(MessageCode.PeerTransferRequest, connection.Username, transferRequest.Filename), transferRequest);

                    break;

                case MessageCode.PeerQueueFailed:
                    var pqfResponse = PeerQueueFailedResponse.Parse(message);
                    Console.WriteLine($"[PEER QUEUE FAILED]: {pqfResponse.Filename}; {pqfResponse.Message}");

                    break;

                default:
                    Diagnostic.Debug($"Unhandled peer message: {message.Code} from {connection.Username} ({connection.IPAddress}:{connection.Port}); {message.Payload.Length} bytes");
                    break;
            }
        }

        private async Task<IReadOnlyCollection<SearchResponse>> SearchInternalAsync(string searchText, int token, SearchOptions options, CancellationToken? cancellationToken = null, bool waitForCompletion = true)
        {
            var search = new Search(searchText, token, options);

            try
            {
                var searchWait = MessageWaiter.WaitIndefinitely<Search>(new WaitKey(MessageCode.ServerFileSearch, token), cancellationToken);

                search.Completed += (_, state) =>
                {
                    MessageWaiter.Complete(new WaitKey(MessageCode.ServerFileSearch, token), search); // searchWait above
                    ActiveSearches.TryRemove(search.Token, out var _);

                    if (!waitForCompletion)
                    {
                        SearchStateChanged?.Invoke(this, new SearchStateChangedEventArgs(previousState: SearchStates.InProgress, search: search));
                        search.Dispose();
                    }
                };

                search.ResponseReceived += (_, response) =>
                {
                    var e = new SearchResponseReceivedEventArgs(search, response);
                    SearchResponseReceived?.Invoke(this, e);
                };

                ActiveSearches.TryAdd(search.Token, search);

                search.State = SearchStates.Requested;
                SearchStateChanged?.Invoke(this, new SearchStateChangedEventArgs(previousState: SearchStates.None, search: search));

                await ServerConnection.WriteMessageAsync(new SearchRequest(search.SearchText, search.Token).ToMessage()).ConfigureAwait(false);

                search.State = SearchStates.InProgress;
                SearchStateChanged?.Invoke(this, new SearchStateChangedEventArgs(previousState: SearchStates.Requested, search: search));

                if (!waitForCompletion)
                {
                    return default(IReadOnlyCollection<SearchResponse>);
                }

                search = await searchWait.ConfigureAwait(false);

                var responses = search.Responses;
                return responses;
            }
            catch (OperationCanceledException ex)
            {
                search.Complete(SearchStates.Cancelled);
                throw new SearchException($"Search for {searchText} ({token}) was cancelled.", ex);
            }
            catch (Exception ex)
            {
                search.Complete(SearchStates.Errored);
                throw new SearchException($"Failed to search for {searchText} ({token}): {ex.Message}", ex);
            }
            finally
            {
                if (waitForCompletion)
                {
                    SearchStateChanged?.Invoke(this, new SearchStateChangedEventArgs(previousState: SearchStates.InProgress, search: search));
                    search.Dispose();
                }
            }
        }

        private async Task SendPrivateMessageInternalAsync(string username, string message)
        {
            try
            {
                await ServerConnection.WriteMessageAsync(new PrivateMessageRequest(username, message).ToMessage()).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                throw new PrivateMessageException($"Failed to send private message to user {username}: {ex.Message}", ex);
            }
        }

        private async void ServerConnection_MessageRead(object sender, Message message)
        {
            Diagnostic.Debug($"Server message received: {message.Code}");

            switch (message.Code)
            {
                case MessageCode.ServerParentMinSpeed:
                case MessageCode.ServerParentSpeedRatio:
                case MessageCode.ServerWishlistInterval:
                    MessageWaiter.Complete(new WaitKey(message.Code), IntegerResponse.Parse(message));
                    break;

                case MessageCode.ServerLogin:
                    MessageWaiter.Complete(new WaitKey(message.Code), LoginResponse.Parse(message));
                    break;

                case MessageCode.ServerRoomList:
                    MessageWaiter.Complete(new WaitKey(message.Code), RoomList.Parse(message));
                    break;

                case MessageCode.ServerPrivilegedUsers:
                    MessageWaiter.Complete(new WaitKey(message.Code), PrivilegedUserList.Parse(message));
                    break;

                case MessageCode.ServerConnectToPeer:
                    var connectToPeerResponse = ConnectToPeerResponse.Parse(message);

                    if (connectToPeerResponse.Type == "F")
                    {
                        // ensure that we are expecting at least one file from this user before we connect. the response doesn't contain
                        // any other identifying information about the file.
                        if (!ActiveDownloads.IsEmpty && ActiveDownloads.Select(kvp => kvp.Value).Any(d => d.Username == connectToPeerResponse.Username))
                        {
                            await HandleDownloadAsync(connectToPeerResponse).ConfigureAwait(false);
                        }
                    }
                    else
                    {
                        await GetSolicitedPeerConnectionAsync(connectToPeerResponse, Options.PeerConnectionOptions).ConfigureAwait(false);
                    }

                    break;

                case MessageCode.ServerPrivateMessage:
                    var pm = PrivateMessage.Parse(message);
                    PrivateMessageReceived?.Invoke(this, pm);

                    if (Options.AutoAcknowledgePrivateMessages)
                    {
                        await AcknowledgePrivateMessageInternalAsync(pm.Id).ConfigureAwait(false);
                    }

                    break;

                case MessageCode.ServerGetPeerAddress:
                    var response = GetPeerAddressResponse.Parse(message);
                    MessageWaiter.Complete(new WaitKey(message.Code, response.Username), response);
                    break;

                default:
                    Diagnostic.Debug($"Unhandled server message: {message.Code}; {message.Payload.Length} bytes");
                    break;
            }
        }
    }
}