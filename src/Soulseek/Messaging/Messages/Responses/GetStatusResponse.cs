﻿// <copyright file="GetStatusResponse.cs" company="JP Dillingham">
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

namespace Soulseek.Messaging.Messages
{
    using Soulseek.Exceptions;

    /// <summary>
    ///     The response to a peer info request.
    /// </summary>
    public sealed class GetStatusResponse
    {
        /// <summary>
        ///     Initializes a new instance of the <see cref="GetStatusResponse"/> class.
        /// </summary>
        /// <param name="username">The username of the peer.</param>
        /// <param name="status">The status of the peer (0 = offline, 1 = away, 2 = online).</param>
        /// <param name="privileged">A value indicating whether the peer is privileged.</param>
        internal GetStatusResponse(string username, int status, bool privileged)
        {
            Username = username;
            Status = status;
            Privileged = privileged;
        }

        /// <summary>
        ///     Gets a value indicating whether the peer is privileged.
        /// </summary>
        public bool Privileged { get; }

        /// <summary>
        ///     Gets the status of the peer (0 = offline, 1 = away, 2 = online).
        /// </summary>
        public int Status { get; }

        /// <summary>
        ///     Gets the username of the peer.
        /// </summary>
        public string Username { get; }

        /// <summary>
        ///     Parses a new instance of <see cref="AddUserResponse"/> from the specified <paramref name="message"/>.
        /// </summary>
        /// <param name="message">The message from which to parse.</param>
        /// <returns>The parsed instance.</returns>
        public static GetStatusResponse Parse(Message message)
        {
            var reader = new MessageReader(message);

            if (reader.Code != MessageCode.ServerGetStatus)
            {
                throw new MessageException($"Message Code mismatch creating Get Status Response (expected: {(int)MessageCode.ServerGetStatus}, received: {(int)reader.Code}.");
            }

            var username = reader.ReadString();
            int status = reader.ReadInteger();
            var privileged = reader.ReadByte() > 0;

            return new GetStatusResponse(username, status, privileged);
        }
    }
}