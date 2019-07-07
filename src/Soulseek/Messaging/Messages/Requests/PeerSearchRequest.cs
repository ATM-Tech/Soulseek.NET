﻿// <copyright file="PeerSearchRequest.cs" company="JP Dillingham">
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
    ///     Requests a search from a peer.
    /// </summary>
    public class PeerSearchRequest
    {
        /// <summary>
        ///     Initializes a new instance of the <see cref="PeerSearchRequest"/> class.
        /// </summary>
        /// <param name="searchText">The text for which to search.</param>
        /// <param name="token">The unique token for the search.</param>
        public PeerSearchRequest(string searchText, int token)
        {
            Token = token;
            SearchText = searchText;
        }

        /// <summary>
        ///     Gets the text for which to search.
        /// </summary>
        public string SearchText { get; }

        /// <summary>
        ///     Gets the unique token for the search.
        /// </summary>
        public int Token { get; }

        /// <summary>
        ///     Implicitly converts an instance to a <see cref="Message"/> via <see cref="ToMessage()"/>.
        /// </summary>
        /// <param name="instance">The instance to convert.</param>
        public static implicit operator byte[](PeerSearchRequest instance)
        {
            return instance.ToMessage();
        }

        /// <summary>
        ///     Constructs a <see cref="Message"/> from this request.
        /// </summary>
        /// <returns>The constructed message.</returns>
        public byte[] ToMessage()
        {
            return new MessageBuilder()
                .WriteCode(MessageCode.Peer.SearchRequest)
                .WriteInteger(Token)
                .WriteString(SearchText)
                .Build();
        }
    }
}