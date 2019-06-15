﻿// <copyright file="IConnectionManager.cs" company="JP Dillingham">
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

namespace Soulseek
{
    using System;
    using System.Net;
    using System.Threading;
    using System.Threading.Tasks;
    using Soulseek.Messaging;
    using Soulseek.Messaging.Messages;
    using Soulseek.Messaging.Tcp;
    using Soulseek.Tcp;

    /// <summary>
    ///     Manages peer <see cref="IConnection"/> instances for the application.
    /// </summary>
    internal interface IConnectionManager : IDisposable
    {
        /// <summary>
        ///     Gets the number of active peer message connections.
        /// </summary>
        int ActivePeerConnections { get; }

        /// <summary>
        ///     Gets the number of active transfer connections.
        /// </summary>
        int ActiveTransferConnections { get; }

        /// <summary>
        ///     Gets the number of allowed concurrent peer message connections.
        /// </summary>
        int ConcurrentPeerConnections { get; }

        /// <summary>
        ///     Gets the number of waiting peer message connections.
        /// </summary>
        int WaitingPeerConnections { get; }

        /// <summary>
        ///     Adds a new peer <see cref="IMessageConnection"/> from an incoming direct connection.
        /// </summary>
        /// <param name="username">The username of the connection.</param>
        /// <param name="ipAddress">The IP address of the connection.</param>
        /// <param name="port">The port of the connection.</param>
        /// <param name="tcpClient">The TCP client for the established connection.</param>
        /// <param name="messageHandler">
        ///     The message handler to subscribe to the connection's <see cref="IMessageConnection.MessageRead"/> event.
        /// </param>
        /// <param name="options">The optional options for the connection.</param>
        /// <param name="cancellationToken">The token to monitor for cancellation requests while the connection is connecting.</param>
        /// <returns>The new connection.</returns>
        Task<IMessageConnection> AddDirectPeerConnectionAsync(string username, IPAddress ipAddress, int port, ITcpClient tcpClient, ConnectionOptions options, CancellationToken cancellationToken);

        /// <summary>
        ///     Adds a new transfer <see cref="IConnection"/> from an incoming direct connection.
        /// </summary>
        /// <param name="ipAddress">The IP address of the connection.</param>
        /// <param name="port">The port of the connection.</param>
        /// <param name="token">The transfer token.</param>
        /// <param name="tcpClient">The TCP client for the established connection.</param>
        /// <param name="options">The optional options for the connection.</param>
        /// <returns>The new connection.</returns>
        IConnection AddDirectTransferConnection(IPAddress ipAddress, int port, int token, ITcpClient tcpClient, ConnectionOptions options);

        /// <summary>
        ///     Adds a new transfer <see cref="IConnection"/> and pierces the firewall.
        /// </summary>
        /// <param name="connectToPeerResponse">The response that solicited the connection.</param>
        /// <param name="options">The optional options for the connection.</param>
        /// <param name="cancellationToken">The token to monitor for cancellation requests while the connection is connecting.</param>
        /// <returns>The new connection.</returns>
        Task<IConnection> AddSolicitedTransferConnectionAsync(ConnectToPeerResponse connectToPeerResponse, ConnectionOptions options, CancellationToken cancellationToken);

        /// <summary>
        ///     Adds a new transfer <see cref="IConnection"/> and sends a peer init request.
        /// </summary>
        /// <param name="connectionKey">The connection key, comprised of the remote IP address and port.</param>
        /// <param name="token">The transfer token.</param>
        /// <param name="localUsername">The username of the local user, required to initiate the connection.</param>
        /// <param name="options">The optional options for the connection.</param>
        /// <param name="cancellationToken">The token to monitor for cancellation requests while the connection is connecting.</param>
        /// <returns>The new connection.</returns>
        Task<IConnection> AddUnsolicitedTransferConnectionAsync(ConnectionKey connectionKey, int token, ConnectionOptions options, CancellationToken cancellationToken);

        Task<IMessageConnection> GetPeerConnectionAsync(string username, ConnectionOptions options, CancellationToken cancellationToken);

        Task<IMessageConnection> GetOrAddSolicitedPeerConnectionAsync(ConnectToPeerResponse connectToPeerResponse);

        /// <summary>
        ///     Gets an existing peer <see cref="IMessageConnection"/>, or adds and initializes new instance if one does not exist.
        /// </summary>
        /// <remarks>
        ///     <para>
        ///         An unsolicited connection is one which is initiated locally for purposes of retrieving user info, browsing
        ///         shares, etc.
        ///     </para>
        ///     <para>Solicited connections (such as one used to retrieve search results) will be reused if possible.</para>
        /// </remarks>
        /// <param name="connectionKey">The connection key, comprised of the remote IP address and port.</param>
        /// <param name="localUsername">The username of the local user, required to initiate the connection.</param>
        /// <param name="messageHandler">
        ///     The message handler to substribe to the conection's <see cref="IMessageConnection.MessageRead"/> event.
        /// </param>
        /// <param name="options">The optional options for the connection.</param>
        /// <param name="cancellationToken">The token to monitor for cancellation requests while the connection is connecting.</param>
        /// <returns>The existing or new connection.</returns>
        //Task<IMessageConnection> GetOrAddUnsolicitedPeerConnectionAsync(ConnectionKey connectionKey, string localUsername, EventHandler<Message> messageHandler, ConnectionOptions options, CancellationToken cancellationToken);

        /// <summary>
        ///     Disposes and removes all active and queued connections.
        /// </summary>
        void RemoveAndDisposeAll();
    }
}