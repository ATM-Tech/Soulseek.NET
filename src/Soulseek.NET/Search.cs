﻿// <copyright file="Search.cs" company="JP Dillingham">
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
    using System.Threading;
    using System.Threading.Tasks;
    using Soulseek.NET.Messaging;
    using Soulseek.NET.Messaging.Requests;
    using Soulseek.NET.Messaging.Responses;
    using Soulseek.NET.Tcp;
    using SystemTimer = System.Timers.Timer;

    /// <summary>
    ///     A single file search.
    /// </summary>
    public sealed class Search : IDisposable
    {
        private int resultCount = 0;

        /// <summary>
        ///     Initializes a new instance of the <see cref="Search"/> class with the specified <paramref name="searchText"/>,
        ///     <paramref name="options"/>, and <paramref name="serverConnection"/>.
        /// </summary>
        /// <param name="searchText">The text for which to search.</param>
        /// <param name="options">The options for the search.</param>
        /// <param name="serverConnection">The connection to use when searching.</param>
        internal Search(string searchText, SearchOptions options, Connection serverConnection)
        {
            SearchText = searchText;
            Options = options;
            ServerConnection = serverConnection;

            Ticket = new Random().Next(1, 2147483647);

            SearchTimeoutTimer = new SystemTimer()
            {
                Interval = Options.SearchTimeout * 1000,
                Enabled = false,
                AutoReset = false,
            };
        }

        /// <summary>
        ///     Occurs when the search has ended.
        /// </summary>
        internal event EventHandler<SearchCompletedEventArgs> SearchEnded;

        /// <summary>
        ///     Occurs when a search response is received from a peer.
        /// </summary>
        internal event EventHandler<SearchResponseReceivedEventArgs> SearchResponseReceived;

        /// <summary>
        ///     Gets the options for the search.
        /// </summary>
        public SearchOptions Options { get; private set; }

        /// <summary>
        ///     Gets the collection of responses received from peers.
        /// </summary>
        public IEnumerable<SearchResponse> Responses => ResponseList.AsReadOnly();

        /// <summary>
        ///     Gets the text for which to search.
        /// </summary>
        public string SearchText { get; private set; }

        /// <summary>
        ///     Gets the current state of the search.
        /// </summary>
        public SearchState State { get; private set; } = SearchState.Pending;

        /// <summary>
        ///     Gets the unique identifier for the search.
        /// </summary>
        public int Ticket { get; private set; }

        private ConcurrentDictionary<ConnectToPeerResponse, Connection> PeerConnectionsActive { get; set; } = new ConcurrentDictionary<ConnectToPeerResponse, Connection>();
        private ConcurrentQueue<KeyValuePair<ConnectToPeerResponse, Connection>> PeerConnectionsQueued { get; set; } = new ConcurrentQueue<KeyValuePair<ConnectToPeerResponse, Connection>>();
        private bool Disposed { get; set; } = false;
        private List<SearchResponse> ResponseList { get; set; } = new List<SearchResponse>();
        private SystemTimer SearchTimeoutTimer { get; set; }
        private Connection ServerConnection { get; set; }

        /// <summary>
        ///     Disposes this instance.
        /// </summary>
        public void Dispose()
        {
            Dispose(true);
        }

        /// <summary>
        ///     If the specified response meets the filter criteria set in <see cref="Options"/>, adds the specified
        ///     <paramref name="response"/> to the collection of peer responses, then fires the
        ///     <see cref="SearchResponseReceived"/> event.
        /// </summary>
        /// <param name="response">The response to add.</param>
        /// <param name="e">The network context of the response.</param>
        internal void AddResponse(SearchResponse response, NetworkEventArgs e)
        {
            if (State == SearchState.InProgress && ResponseMeetsOptionCriteria(response))
            {
                response.ParseFiles();

                if (Options.FilterFiles || true)
                {
                    response.Files = response.Files.Where(f => FileMeetsOptionCriteria(f));
                }

                Interlocked.Add(ref resultCount, response.Files.Count());

                if (resultCount >= Options.FileLimit)
                {
                    End(SearchState.Completed);
                    return;
                }

                ResponseList.Add(response);
                Task.Run(() => SearchResponseReceived?.Invoke(this, new SearchResponseReceivedEventArgs(e) { Response = response })).Forget();

                SearchTimeoutTimer.Reset();
            }
        }

        internal async Task AddPeer(ConnectToPeerResponse connectToPeerResponse, NetworkEventArgs e)
        {
            Console.WriteLine($"[CONNECT TO PEER]: {connectToPeerResponse.Username}");

            var connection = new Connection(ConnectionType.Peer, connectToPeerResponse.IPAddress.ToString(), connectToPeerResponse.Port, 15, 15, Options.BufferSize)
            {
                Context = connectToPeerResponse
            };

            connection.DataReceived += OnPeerConnectionDataReceived;
            connection.StateChanged += OnPeerConnectionStateChanged;

            if (PeerConnectionsActive.Count() < Options.ConcurrentPeerConnections)
            {
                if (PeerConnectionsActive.TryAdd(connectToPeerResponse, connection))
                {
                    await TryConnectPeerConnection(connectToPeerResponse, connection);
                }
            }
            else
            {
                PeerConnectionsQueued.Enqueue(new KeyValuePair<ConnectToPeerResponse, Connection>(connectToPeerResponse, connection));
            }
        }

        /// <summary>
        ///     Ends the search with the specified <paramref name="state"/>.
        /// </summary>
        /// <remarks>
        ///     A state of <see cref="SearchState.Completed"/> indicates that the search completed normally by timeout or after
        ///     having reached the result limit, while <see cref="SearchState.Stopped"/> indicates that the search was stopped
        ///     prematurely, e.g., by error or user request.
        /// </remarks>
        /// <param name="state">The desired state of the search.</param>
        internal void End(SearchState state)
        {
            if (State != SearchState.Completed && State != SearchState.Stopped)
            {
                State = state;
                SearchTimeoutTimer.Stop();

                ClearPeerConnectionsQueued();
                ClearPeerConnectionsActive("Search completed.");

                Task.Run(() => SearchEnded?.Invoke(this, new SearchCompletedEventArgs() { Search = this })).Forget();
            }
        }

        /// <summary>
        ///     Asynchronously starts the search.
        /// </summary>
        /// <returns>This search.</returns>
        internal async Task<Search> StartAsync()
        {
            if (State != SearchState.Pending)
            {
                throw new SearchException($"The Search is already in progress or has completed.");
            }

            State = SearchState.InProgress;

            var request = new SearchRequest(SearchText, Ticket);

            Console.WriteLine($"Searching for {SearchText}...");
            await ServerConnection.SendAsync(request.ToMessage().ToByteArray());

            SearchTimeoutTimer.Reset();
            SearchTimeoutTimer.Elapsed += (sender, e) => End(SearchState.Completed);

            return this;
        }

        private void Dispose(bool disposing)
        {
            if (!Disposed)
            {
                if (disposing)
                {
                    SearchTimeoutTimer.Dispose();
                    ResponseList = default(List<SearchResponse>);
                }

                Disposed = true;
            }
        }

        private bool FileMeetsOptionCriteria(File file)
        {
            if (!Options.FilterFiles)
            {
                return true;
            }

            bool fileHasIgnoredExtension(File f)
            {
                return Options.IgnoredFileExtensions == null ? false :
                    Options.IgnoredFileExtensions.Any(e => e == System.IO.Path.GetExtension(f.Filename));
            }

            if (file.Size < Options.MinimumFileSize || fileHasIgnoredExtension(file))
            {
                return false;
            }

            var bitRate = file.GetAttributeValue(FileAttributeType.BitRate);
            var length = file.GetAttributeValue(FileAttributeType.Length);
            var bitDepth = file.GetAttributeValue(FileAttributeType.BitDepth);
            var sampleRate = file.GetAttributeValue(FileAttributeType.SampleRate);

            if ((bitRate != null && bitRate < Options.MinimumFileBitRate) ||
                (length != null && length < Options.MinimumFileLength) ||
                (bitDepth != null && bitDepth < Options.MinimumFileBitDepth) ||
                (sampleRate != null && sampleRate < Options.MinimumFileSampleRate))
            {
                return false;
            }

            var constantBitRates = new[] { 32, 64, 128, 192, 256, 320 };
            var isConstant = constantBitRates.Any(b => b == bitRate);

            if (bitRate != null && ((!Options.IncludeConstantBitRate && isConstant) || (!Options.IncludeVariableBitRate && !isConstant)))
            {
                return false;
            }

            return true;
        }

        private bool ResponseMeetsOptionCriteria(SearchResponse response)
        {
            if (Options.FilterResponses && (
                    response.FileCount < Options.MinimumResponseFileCount ||
                    response.FreeUploadSlots < Options.MinimumPeerFreeUploadSlots ||
                    response.UploadSpeed < Options.MinimumPeerUploadSpeed ||
                    response.QueueLength > Options.MaximumPeerQueueLength))
            {
                return false;
            }

            return true;
        }

        private void OnPeerConnectionDataReceived(object sender, DataReceivedEventArgs e)
        {
            var message = new Message(e.Data);

            Console.WriteLine($"[PEER MESSAGE]: {message.Code}");

            switch (message.Code)
            {
                case MessageCode.PeerSearchResponse:
                    AddResponse(SearchResponse.Parse(message), e);
                    break;

                default:
                    if (sender is Connection peerConnection)
                    {
                        peerConnection.Disconnect($"Unknown response from peer: {message.Code}");
                    }

                    break;
            }
        }

        private async void OnPeerConnectionStateChanged(object sender, ConnectionStateChangedEventArgs e)
        {
            Console.WriteLine($"Peer connection state changed: {e.Address} {e.State}");
            if (e.State == ConnectionState.Disconnected &&
                sender is Connection connection &&
                connection.Context is ConnectToPeerResponse connectToPeerResponse)
            {
                connection.Dispose();
                PeerConnectionsActive.TryRemove(connectToPeerResponse, out var _);

                if (PeerConnectionsActive.Count() < Options.ConcurrentPeerConnections &&
                    PeerConnectionsQueued.TryDequeue(out var nextConnection))
                {
                    if (PeerConnectionsActive.TryAdd(nextConnection.Key, nextConnection.Value))
                    {
                        await TryConnectPeerConnection(nextConnection.Key, nextConnection.Value);
                    }
                }
            }
        }

        private void ClearPeerConnectionsActive(string disconnectMessage)
        {
            while (!PeerConnectionsActive.IsEmpty)
            {
                var key = PeerConnectionsActive.Keys.First();

                if (PeerConnectionsActive.TryRemove(key, out var connection))
                {
                    try
                    {
                        connection?.Disconnect(disconnectMessage);
                        connection?.Dispose();
                    }
                    catch (Exception)
                    {
                    }
                }
            }
        }

        private void ClearPeerConnectionsQueued()
        {
            while (!PeerConnectionsQueued.IsEmpty)
            {
                if (PeerConnectionsQueued.TryDequeue(out var queuedConnection))
                {
                    try
                    {
                        queuedConnection.Value?.Dispose();
                    }
                    catch (Exception)
                    {
                    }
                }
            }
        }

        private async Task TryConnectPeerConnection(ConnectToPeerResponse response, Connection connection)
        {
            try
            {
                await connection.ConnectAsync();

                Console.WriteLine($"[PIERCE FIREWALL]: {response.Username}/{response.IPAddress}:{response.Port} Token: {response.Token}");

                var request = new PierceFirewallRequest(response.Token);
                await connection.SendAsync(request.ToByteArray(), suppressCodeNormalization: true);
            }
            catch (ConnectionException ex)
            {
                Console.WriteLine(ex);
                connection.Disconnect($"Failed to connect to peer {response.Username}@{response.IPAddress}:{response.Port}: {ex.Message}");
            }
        }
    }
}