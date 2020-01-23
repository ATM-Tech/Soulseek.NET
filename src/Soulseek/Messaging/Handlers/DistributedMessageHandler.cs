﻿// <copyright file="DistributedMessageHandler.cs" company="JP Dillingham">
//     Copyright (c) JP Dillingham. All rights reserved.
//
//     This program is free software: you can redistribute it and/or modify it under the terms of the GNU General Public License
//     as published by the Free Software Foundation, either version 3 of the License, or (at your option) any later version.
//
//     This program is distributed in the hope that it will be useful, but WITHOUT ANY WARRANTY; without even the implied warranty
//     of MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.See the GNU General Public License for more details.
//
//     You should have received a copy of the GNU General Public License along with this program. If not, see https://www.gnu.org/licenses/.
// </copyright>

namespace Soulseek.Messaging.Handlers
{
    using System;
    using System.Threading;
    using Soulseek.Diagnostics;
    using Soulseek.Messaging.Messages;
    using Soulseek.Network;

    /// <summary>
    ///     Handles incoming messages from distributed connections.
    /// </summary>
    internal sealed class DistributedMessageHandler : IDistributedMessageHandler
    {
        /// <summary>
        ///     Initializes a new instance of the <see cref="DistributedMessageHandler"/> class.
        /// </summary>
        /// <param name="soulseekClient">The ISoulseekClient instance to use.</param>
        /// <param name="diagnosticFactory">The IDiagnosticFactory instance to use.</param>
        public DistributedMessageHandler(
            SoulseekClient soulseekClient,
            IDiagnosticFactory diagnosticFactory = null)
        {
            SoulseekClient = soulseekClient ?? throw new ArgumentNullException(nameof(soulseekClient));
            Diagnostic = diagnosticFactory ??
                new DiagnosticFactory(this, SoulseekClient.Options.MinimumDiagnosticLevel, (e) => DiagnosticGenerated?.Invoke(this, e));
        }

        /// <summary>
        ///     Occurs when an internal diagnostic message is generated.
        /// </summary>
        public event EventHandler<DiagnosticEventArgs> DiagnosticGenerated;

        private IDiagnosticFactory Diagnostic { get; }
        private SoulseekClient SoulseekClient { get; }

        /// <summary>
        ///     Handles incoming messages.
        /// </summary>
        /// <param name="sender">The <see cref="IMessageConnection"/> instance from which the message originated.</param>
        /// <param name="args">The message event args.</param>
        public void HandleMessageRead(object sender, MessageReadEventArgs args)
        {
            HandleMessageRead(sender, args.Message);
        }

        /// <summary>
        ///     Handles incoming messages.
        /// </summary>
        /// <param name="sender">The <see cref="IMessageConnection"/> instance from which the message originated.</param>
        /// <param name="message">The message.</param>
        public async void HandleMessageRead(object sender, byte[] message)
        {
            var connection = (IMessageConnection)sender;
            var code = new MessageReader<MessageCode.Distributed>(message).ReadCode();

            if (code != MessageCode.Distributed.SearchRequest)
            {
                Diagnostic.Debug($"Distributed message received: {code} from {connection.Username} ({connection.IPEndPoint})");
            }

            try
            {
                switch (code)
                {
                    // some clients erroneously send code 93, which is a server code, in place of 3.
                    case MessageCode.Distributed.ServerSearchRequest:
                    case MessageCode.Distributed.SearchRequest:
                        var searchRequest = DistributedSearchRequest.FromByteArray(message);
                        SearchResponse searchResponse;

                        SoulseekClient.Waiter.Complete(new WaitKey(Constants.WaitKey.SearchRequestMessage, connection.Context, connection.Key));
                        SoulseekClient.DistributedConnectionManager.BroadcastMessageAsync(message).Forget();

                        if (SoulseekClient.Options.SearchResponseResolver == default)
                        {
                            break;
                        }

                        try
                        {
                            searchResponse = await SoulseekClient.Options.SearchResponseResolver(searchRequest.Username, searchRequest.Token, SearchQuery.FromText(searchRequest.Query)).ConfigureAwait(false);

                            if (searchResponse != null && searchResponse.FileCount > 0)
                            {
                                Diagnostic.Debug($"Resolved {searchResponse.FileCount} files for query '{searchRequest.Query}'");

                                var endpoint = await SoulseekClient.GetUserEndPointAsync(searchRequest.Username).ConfigureAwait(false);

                                var peerConnection = await SoulseekClient.PeerConnectionManager.GetOrAddMessageConnectionAsync(searchRequest.Username, endpoint, CancellationToken.None).ConfigureAwait(false);
                                await peerConnection.WriteAsync(searchResponse.ToByteArray()).ConfigureAwait(false);

                                Diagnostic.Debug($"Sent response containing {searchResponse.FileCount} files to {searchRequest.Username} for query '{searchRequest.Query}' with token {searchRequest.Token}");
                            }
                        }
                        catch (Exception ex)
                        {
                            Diagnostic.Warning($"Error resolving search response for query '{searchRequest.Query}' requested by {searchRequest.Username} with token {searchRequest.Token}: {ex.Message}", ex);
                        }

                        break;

                    case MessageCode.Distributed.Ping:
                        Diagnostic.Debug($"PING?");
                        var pingResponse = new PingResponse(SoulseekClient.GetNextToken());
                        await connection.WriteAsync(pingResponse.ToByteArray()).ConfigureAwait(false);
                        Diagnostic.Debug($"PONG!");
                        break;

                    case MessageCode.Distributed.BranchLevel:
                        var branchLevel = DistributedBranchLevel.FromByteArray(message);

                        SoulseekClient.Waiter.Complete(new WaitKey(Constants.WaitKey.BranchLevelMessage, connection.Context, connection.Key), branchLevel.Level);

                        if ((connection.Username, connection.IPEndPoint) == SoulseekClient.DistributedConnectionManager.Parent)
                        {
                            SoulseekClient.DistributedConnectionManager.SetBranchLevel(branchLevel.Level);
                        }

                        break;

                    case MessageCode.Distributed.BranchRoot:
                        var branchRoot = DistributedBranchRoot.FromByteArray(message);

                        SoulseekClient.Waiter.Complete(new WaitKey(Constants.WaitKey.BranchRootMessage, connection.Context, connection.Key), branchRoot.Username);

                        if ((connection.Username, connection.IPEndPoint) == SoulseekClient.DistributedConnectionManager.Parent)
                        {
                            SoulseekClient.DistributedConnectionManager.SetBranchRoot(branchRoot.Username);
                        }

                        break;

                    case MessageCode.Distributed.ChildDepth:
                        var childDepth = DistributedChildDepth.FromByteArray(message);
                        SoulseekClient.Waiter.Complete(new WaitKey(Constants.WaitKey.ChildDepthMessage, connection.Key), childDepth.Depth);
                        break;

                    default:
                        Diagnostic.Debug($"Unhandled distributed message: {code} from {connection.Username} ({connection.IPEndPoint}); {message.Length} bytes");
                        break;
                }
            }
            catch (Exception ex)
            {
                Diagnostic.Warning($"Error handling distributed message: {code} from {connection.Username} ({connection.IPEndPoint}); {ex.Message}", ex);
            }
        }
    }
}