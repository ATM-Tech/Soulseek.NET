﻿// <copyright file="HaveNoParentsRequest.cs" company="JP Dillingham">
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
    /// <summary>
    ///     Adds a peer to the server-side watch list.
    /// </summary>
    public class HaveNoParentsRequest
    {
        /// <summary>
        ///     Initializes a new instance of the <see cref="HaveNoParentsRequest"/> class.
        /// </summary>
        /// <param name="haveNoParents">A value indicating whether distributed parent connections have been established.</param>
        public HaveNoParentsRequest(bool haveNoParents)
        {
            HaveNoParents = haveNoParents;
        }

        /// <summary>
        ///     Gets a value indicating whether distributed parent connections have been established.
        /// </summary>
        public bool HaveNoParents { get; }

        /// <summary>
        ///     Constructs a <see cref="byte"/> array from this message.
        /// </summary>
        /// <returns>The constructed byte array.</returns>
        public byte[] ToByteArray()
        {
            return new MessageBuilder()
                .WriteCode(MessageCode.Server.HaveNoParents)
                .WriteByte((byte)(HaveNoParents ? 1 : 0))
                .Build();
        }
    }
}