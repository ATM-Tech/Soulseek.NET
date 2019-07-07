﻿namespace Soulseek.Messaging.Handlers
{
    using System;
    using System.Linq;
    using System.Threading;
    using Soulseek.Exceptions;
    using Soulseek.Messaging;
    using Soulseek.Messaging.Messages;

    internal sealed class ServerMessageHandler : IServerMessageHandler
    {
        public ServerMessageHandler(
            ISoulseekClient soulseekClient,
            IDiagnosticFactory diagnosticFactory = null)
        {
            SoulseekClient = (SoulseekClient)soulseekClient;
            Diagnostic = diagnosticFactory ??
                new DiagnosticFactory(this, SoulseekClient.Options.MinimumDiagnosticLevel, (e) => DiagnosticGenerated?.Invoke(this, e));
        }

        /// <summary>
        ///     Occurs when an internal diagnostic message is generated.
        /// </summary>
        public event EventHandler<DiagnosticGeneratedEventArgs> DiagnosticGenerated;

        /// <summary>
        ///     Occurs when a watched user's status changes.
        /// </summary>
        public event EventHandler<UserStatusChangedEventArgs> UserStatusChanged;

        /// <summary>
        ///     Occurs when a private message is received.
        /// </summary>
        public event EventHandler<PrivateMessage> PrivateMessageReceived;

        private IDiagnosticFactory Diagnostic { get; }
        private SoulseekClient SoulseekClient { get; }

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
                        SoulseekClient.Waiter.Complete(new WaitKey(code), IntegerResponse.Parse<MessageCode.Server>(message));
                        break;

                    case MessageCode.Server.Login:
                        SoulseekClient.Waiter.Complete(new WaitKey(code), LoginResponse.Parse(message));
                        break;

                    case MessageCode.Server.RoomList:
                        SoulseekClient.Waiter.Complete(new WaitKey(code), RoomList.Parse(message));
                        break;

                    case MessageCode.Server.PrivilegedUsers:
                        SoulseekClient.Waiter.Complete(new WaitKey(code), PrivilegedUserList.Parse(message));
                        break;

                    case MessageCode.Server.NetInfo:
                        var netInfo = NetInfo.Parse(message);
                        foreach (var peer in netInfo.Parents)
                        {
                            Console.WriteLine($"{peer.Username} {peer.IPAddress} {peer.Port}");
                        }

                        break;

                    case MessageCode.Server.ConnectToPeer:
                        var connectToPeerResponse = ConnectToPeerResponse.Parse(message);

                        if (connectToPeerResponse.Type == Constants.ConnectionType.Tranfer)
                        {
                            // ensure that we are expecting at least one file from this user before we connect. the response
                            // doesn't contain any other identifying information about the file.
                            if (!SoulseekClient.Downloads.IsEmpty && SoulseekClient.Downloads.Values.Any(d => d.Username == connectToPeerResponse.Username))
                            {
                                var (connection, remoteToken) = await SoulseekClient.PeerConnectionManager.GetTransferConnectionAsync(connectToPeerResponse).ConfigureAwait(false);
                                var download = SoulseekClient.Downloads.Values.FirstOrDefault(v => v.RemoteToken == remoteToken && v.Username == connectToPeerResponse.Username);

                                if (download != default(Transfer))
                                {
                                    SoulseekClient.Waiter.Complete(new WaitKey(Constants.WaitKey.IndirectTransfer, download.Username, download.Filename, download.RemoteToken), connection);
                                }
                            }
                            else
                            {
                                throw new SoulseekClientException($"Unexpected transfer request from {connectToPeerResponse.Username} ({connectToPeerResponse.IPAddress}:{connectToPeerResponse.Port}); Ignored.");
                            }
                        }
                        else
                        {
                            await SoulseekClient.PeerConnectionManager.GetOrAddMessageConnectionAsync(connectToPeerResponse).ConfigureAwait(false);
                        }

                        break;

                    case MessageCode.Server.AddUser:
                        var addUserResponse = AddUserResponse.Parse(message);
                        SoulseekClient.Waiter.Complete(new WaitKey(code, addUserResponse.Username), addUserResponse);
                        break;

                    case MessageCode.Server.GetStatus:
                        var statsResponse = GetStatusResponse.Parse(message);
                        SoulseekClient.Waiter.Complete(new WaitKey(code, statsResponse.Username), statsResponse);
                        UserStatusChanged?.Invoke(this, new UserStatusChangedEventArgs(statsResponse));
                        break;

                    case MessageCode.Server.PrivateMessage:
                        var pm = PrivateMessage.Parse(message);
                        PrivateMessageReceived?.Invoke(this, pm);

                        if (SoulseekClient.Options.AutoAcknowledgePrivateMessages)
                        {
                            await SoulseekClient.AcknowledgePrivateMessageAsync(pm.Id, CancellationToken.None).ConfigureAwait(false);
                        }

                        break;

                    case MessageCode.Server.GetPeerAddress:
                        var peerAddressResponse = GetPeerAddressResponse.Parse(message);
                        SoulseekClient.Waiter.Complete(new WaitKey(code, peerAddressResponse.Username), peerAddressResponse);
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