﻿// <copyright file="MessageConnection.cs" company="JP Dillingham">
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

namespace Soulseek.Messaging.Tcp
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Net;
    using System.Threading;
    using System.Threading.Tasks;
    using Soulseek.Messaging;
    using Soulseek.Tcp;

    /// <summary>
    ///     Provides client connections to the Soulseek network.
    /// </summary>
    internal sealed class MessageConnection : Connection, IMessageConnection
    {
        /// <summary>
        ///     Initializes a new instance of the <see cref="MessageConnection"/> class.
        /// </summary>
        /// <param name="type">The connection type (Peer, Server).</param>
        /// <param name="username">The username of the peer associated with the connection, if applicable.</param>
        /// <param name="ipAddress">The remote IP address of the connection.</param>
        /// <param name="port">The remote port of the connection.</param>
        /// <param name="options">The optional options for the connection.</param>
        /// <param name="tcpClient">The optional TcpClient instance to use.</param>
        internal MessageConnection(MessageConnectionType type, string username, IPAddress ipAddress, int port, ConnectionOptions options = null, ITcpClient tcpClient = null)
            : this(type, ipAddress, port, options, tcpClient)
        {
            Username = username;
        }

        /// <summary>
        ///     Initializes a new instance of the <see cref="MessageConnection"/> class.
        /// </summary>
        /// <param name="type">The connection type (Peer, Server).</param>
        /// <param name="ipAddress">The remote IP address of the connection.</param>
        /// <param name="port">The remote port of the connection.</param>
        /// <param name="options">The optional options for the connection.</param>
        /// <param name="tcpClient">The optional TcpClient instance to use.</param>
        internal MessageConnection(MessageConnectionType type, IPAddress ipAddress, int port, ConnectionOptions options = null, ITcpClient tcpClient = null)
            : base(ipAddress, port, options, tcpClient)
        {
            Type = type;

            // if the supplied ITcpClient instance is not null and is Connected, disallow StartReadingContinuously() to prevent
            // duplicate running loops.
            CanStartReadingContinuously = tcpClient?.Connected ?? false;

            // circumvent the inactivity timer for server connections; this connection is expected to idle.
            if (Type == MessageConnectionType.Server)
            {
                InactivityTimer = null;

                Connected += (sender, e) =>
                {
                    Task.Run(() => ReadContinuouslyAsync()).ForgetButThrowWhenFaulted<ConnectionException>();
                };
            }
            else
            {
                Connected += (sender, e) =>
                {
                    Task.Run(() => ReadContinuouslyAsync()).Forget();
                };
            }
        }

        /// <summary>
        ///     Occurs when a new message is received.
        /// </summary>
        public event EventHandler<byte[]> MessageRead;

        /// <summary>
        ///     Gets the unique identifier for the connection.
        /// </summary>
        public override ConnectionKey Key => new ConnectionKey(Username, IPAddress, Port, Type);

        /// <summary>
        ///     Gets a value indicating whether the internal continuous read loop is running.
        /// </summary>
        public bool ReadingContinuously { get; private set; }

        /// <summary>
        ///     Gets the connection type (Peer, Server).
        /// </summary>
        public MessageConnectionType Type { get; private set; }

        /// <summary>
        ///     Gets the username of the peer associated with the connection, if applicable.
        /// </summary>
        public string Username { get; private set; } = string.Empty;

        private bool CanStartReadingContinuously { get; set; }

        /// <summary>
        ///     Begins the internal continuous read loop, if it has not yet started.
        /// </summary>
        public void StartReadingContinuously()
        {
            if (CanStartReadingContinuously)
            {
                CanStartReadingContinuously = false;
                Task.Run(() => ReadContinuouslyAsync()).Forget();
            }
        }

        public async Task WriteMessagesAsync(IEnumerable<byte[]> messages, CancellationToken? cancellationToken = null)
        {
            if (messages == null || !messages.Any() || messages.Any(m => m == null || m.Length == 0))
            {
                throw new ArgumentException($"The specified list of Messages is null, empty, or contains at least one Message which is null or empty.", nameof(messages));
            }

            if (State != ConnectionState.Connected)
            {
                throw new InvalidOperationException($"Invalid attempt to send to a disconnected or disconnecting connection (current state: {State})");
            }

            var bytes = new List<byte>();

            foreach (var message in messages)
            {
                cancellationToken?.ThrowIfCancellationRequested();

                // todo: fix this
                var messageBytes = message;
                NormalizeMessageCode(messageBytes, 0 - (int)Type);

                bytes.AddRange(messageBytes);
            }

            Console.WriteLine(BitConverter.ToString(bytes.ToArray()).Replace("-", string.Empty));
            await WriteAsync(bytes.ToArray(), cancellationToken ?? CancellationToken.None).ConfigureAwait(false);
        }

        /// <summary>
        ///     Asynchronously writes the specified message to the connection.
        /// </summary>
        /// <remarks>
        ///     Only to be used for messages with a code length of 4 bytes.  For messages with a single byte code, write the data directly with <see cref="IConnection.WriteAsync"/>.
        /// </remarks>
        /// <param name="message">The message to write.</param>
        /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
        /// <returns>A Task representing the asynchronous operation.</returns>
        public async Task WriteMessageAsync(byte[] message, CancellationToken? cancellationToken = null)
        {
            if (message == null || message.Length == 0)
            {
                throw new ArgumentException($"The specified Message is null or contains no data.", nameof(message));
            }

            if (State != ConnectionState.Connected)
            {
                throw new InvalidOperationException($"Invalid attempt to send to a disconnected or disconnecting connection (current state: {State})");
            }
            // todo: remove this
            var bytes = message;

            NormalizeMessageCode(bytes, 0 - (int)Type);

            Console.WriteLine(BitConverter.ToString(bytes).Replace("-", string.Empty));
            await WriteAsync(bytes, cancellationToken ?? CancellationToken.None).ConfigureAwait(false);
        }

        private void NormalizeMessageCode(byte[] messageBytes, int newCode)
        {
            var code = BitConverter.ToInt32(messageBytes, 4);
            var adjustedCode = BitConverter.GetBytes(code + newCode);

            Array.Copy(adjustedCode, 0, messageBytes, 4, 4);
        }

        private async Task ReadContinuouslyAsync()
        {
            ReadingContinuously = true;

            try
            {
                while (true)
                {
                    var message = new List<byte>();

                    var lengthBytes = await ReadAsync(4, CancellationToken.None).ConfigureAwait(false);
                    var length = BitConverter.ToInt32(lengthBytes, 0);
                    message.AddRange(lengthBytes);

                    var codeBytes = await ReadAsync(4, CancellationToken.None).ConfigureAwait(false);
                    message.AddRange(codeBytes);

                    var payloadBytes = await ReadAsync(length - 4, CancellationToken.None).ConfigureAwait(false);
                    message.AddRange(payloadBytes);

                    var messageBytes = message.ToArray();

                    NormalizeMessageCode(messageBytes, (int)Type);

                    MessageRead?.Invoke(this, messageBytes);
                }
            }
            finally
            {
                ReadingContinuously = false;
            }
        }
    }
}