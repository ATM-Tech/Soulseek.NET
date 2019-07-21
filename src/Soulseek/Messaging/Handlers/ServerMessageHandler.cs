﻿// <copyright file="ServerMessageHandler.cs" company="JP Dillingham">
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

namespace Soulseek.Messaging.Handlers
{
    using System;
    using System.Collections.Concurrent;
    using System.Linq;
    using System.Threading;
    using Soulseek.Exceptions;
    using Soulseek.Messaging;
    using Soulseek.Messaging.Messages;
    using Soulseek.Network;

    /// <summary>
    ///     Handles incoming messages from the server connection.
    /// </summary>
    internal sealed class ServerMessageHandler : IServerMessageHandler
    {
        /// <summary>
        ///     Initializes a new instance of the <see cref="ServerMessageHandler"/> class.
        /// </summary>
        /// <param name="soulseekClient">The ISoulseekClient instance to use.</param>
        /// <param name="peerConnectionManager">The IPeerConnectionManager instance to use.</param>
        /// <param name="waiter">The IWaiter instance to use.</param>
        /// <param name="downloads">The collection of download transfers.</param>
        /// <param name="diagnosticFactory">The IDiagnosticFactory instance to use.</param>
        public ServerMessageHandler(
            ISoulseekClient soulseekClient,
            IPeerConnectionManager peerConnectionManager,
            IDistributedConnectionManager distributedConnectionManager,
            IWaiter waiter,
            ConcurrentDictionary<int, Transfer> downloads,
            IDiagnosticFactory diagnosticFactory = null)
        {
            SoulseekClient = soulseekClient;
            PeerConnectionManager = peerConnectionManager;
            DistributedConnectionManager = distributedConnectionManager;
            Waiter = waiter;
            Downloads = downloads;
            Diagnostic = diagnosticFactory ??
                new DiagnosticFactory(this, SoulseekClient?.Options?.MinimumDiagnosticLevel ?? new ClientOptions().MinimumDiagnosticLevel, (e) => DiagnosticGenerated?.Invoke(this, e));
        }

        /// <summary>
        ///     Occurs when an internal diagnostic message is generated.
        /// </summary>
        public event EventHandler<DiagnosticGeneratedEventArgs> DiagnosticGenerated;

        /// <summary>
        ///     Occurs when a private message is received.
        /// </summary>
        public event EventHandler<PrivateMessage> PrivateMessageReceived;

        /// <summary>
        ///     Occurs when a watched user's status changes.
        /// </summary>
        public event EventHandler<UserStatusChangedEventArgs> UserStatusChanged;

        private IDiagnosticFactory Diagnostic { get; }
        private ConcurrentDictionary<int, Transfer> Downloads { get; }
        private IPeerConnectionManager PeerConnectionManager { get; }
        private IDistributedConnectionManager DistributedConnectionManager { get; }
        private ISoulseekClient SoulseekClient { get; }
        private IWaiter Waiter { get; }

        /// <summary>
        ///     Handles incoming messages.
        /// </summary>
        /// <param name="sender">The <see cref="IMessageConnection"/> instance from which the message originated.</param>
        /// <param name="message">The message.</param>
        public async void HandleMessage(object sender, byte[] message)
        {
            var code = new MessageReader<MessageCode.Server>(message).ReadCode();
            Diagnostic.Debug($"Server message received: {code}");

            try
            {
                switch (code)
                {
                    case MessageCode.Server.ParentMinSpeed:
                    case MessageCode.Server.ParentSpeedRatio:
                    case MessageCode.Server.WishlistInterval:
                        Waiter.Complete(new WaitKey(code), IntegerResponse.FromByteArray<MessageCode.Server>(message));
                        break;

                    case MessageCode.Server.Login:
                        Waiter.Complete(new WaitKey(code), LoginResponse.FromByteArray(message));
                        break;

                    case MessageCode.Server.RoomList:
                        Waiter.Complete(new WaitKey(code), RoomList.FromByteArray(message));
                        break;

                    case MessageCode.Server.PrivilegedUsers:
                        Waiter.Complete(new WaitKey(code), PrivilegedUserList.FromByteArray(message));
                        break;

                    case MessageCode.Server.NetInfo:
                        var netInfo = NetInfo.FromByteArray(message);
                        foreach (var peer in netInfo.Parents)
                        {
                            Console.WriteLine($"{peer.Username} {peer.IPAddress} {peer.Port}");
                        }

                        await DistributedConnectionManager.UpdateParentPool(netInfo.Parents).ConfigureAwait(false);

                        break;

                    case MessageCode.Server.ConnectToPeer:
                        var connectToPeerResponse = ConnectToPeerResponse.FromByteArray(message);

                        if (connectToPeerResponse.Type == Constants.ConnectionType.Tranfer)
                        {
                            // ensure that we are expecting at least one file from this user before we connect. the response
                            // doesn't contain any other identifying information about the file.
                            if (!Downloads.IsEmpty && Downloads.Values.Any(d => d.Username == connectToPeerResponse.Username))
                            {
                                var (connection, remoteToken) = await PeerConnectionManager.GetTransferConnectionAsync(connectToPeerResponse).ConfigureAwait(false);
                                var download = Downloads.Values.FirstOrDefault(v => v.RemoteToken == remoteToken && v.Username == connectToPeerResponse.Username);

                                if (download != default(Transfer))
                                {
                                    Waiter.Complete(new WaitKey(Constants.WaitKey.IndirectTransfer, download.Username, download.Filename, download.RemoteToken), connection);
                                }
                            }
                            else
                            {
                                throw new SoulseekClientException($"Unexpected transfer request from {connectToPeerResponse.Username} ({connectToPeerResponse.IPAddress}:{connectToPeerResponse.Port}); Ignored.");
                            }
                        }
                        else
                        {
                            await PeerConnectionManager.GetOrAddMessageConnectionAsync(connectToPeerResponse).ConfigureAwait(false);
                        }

                        break;

                    case MessageCode.Server.AddUser:
                        var addUserResponse = AddUserResponse.FromByteArray(message);
                        Waiter.Complete(new WaitKey(code, addUserResponse.Username), addUserResponse);
                        break;

                    case MessageCode.Server.GetStatus:
                        var statsResponse = GetStatusResponse.FromByteArray(message);
                        Waiter.Complete(new WaitKey(code, statsResponse.Username), statsResponse);
                        UserStatusChanged?.Invoke(this, new UserStatusChangedEventArgs(statsResponse));
                        break;

                    case MessageCode.Server.PrivateMessage:
                        var pm = PrivateMessage.FromByteArray(message);
                        PrivateMessageReceived?.Invoke(this, pm);

                        if (SoulseekClient.Options.AutoAcknowledgePrivateMessages)
                        {
                            await SoulseekClient.AcknowledgePrivateMessageAsync(pm.Id, CancellationToken.None).ConfigureAwait(false);
                        }

                        break;

                    case MessageCode.Server.GetPeerAddress:
                        var peerAddressResponse = GetPeerAddressResponse.FromByteArray(message);
                        Waiter.Complete(new WaitKey(code, peerAddressResponse.Username), peerAddressResponse);
                        break;

                    default:
                        Diagnostic.Debug($"Unhandled server message: {code}; {message.Length} bytes");
                        break;
                }
            }
            catch (Exception ex)
            {
                Diagnostic.Warning($"Error handling server message: {code}; {ex.Message}", ex);
            }
        }
    }
}