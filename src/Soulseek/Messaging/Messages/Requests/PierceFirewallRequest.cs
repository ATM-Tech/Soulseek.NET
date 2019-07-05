﻿// <copyright file="PierceFirewallRequest.cs" company="JP Dillingham">
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
    using System;
    using System.Collections.Generic;

    /// <summary>
    ///     Pierces a peer's firewall to initate a connection.
    /// </summary>
    public class PierceFirewallRequest
    {
        /// <summary>
        ///     Initializes a new instance of the <see cref="PierceFirewallRequest"/> class.
        /// </summary>
        /// <param name="token">The unique token for the connection.</param>
        public PierceFirewallRequest(int token)
        {
            Token = token;
        }

        /// <summary>
        ///     Gets the unique token for the connection.
        /// </summary>
        public int Token { get; }

        /// <summary>
        ///     Returns the request as a byte array.
        /// </summary>
        /// <returns>The request as a byte array.</returns>
        public byte[] ToMessage()
        {
            var bytes = new List<byte> { (byte)InitializationCode.PierceFirewall };

            bytes.AddRange(BitConverter.GetBytes(Token));

            bytes.InsertRange(0, BitConverter.GetBytes(bytes.Count));

            return bytes.ToArray();
        }
    }
}