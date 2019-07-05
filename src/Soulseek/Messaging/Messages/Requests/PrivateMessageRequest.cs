﻿// <copyright file="PrivateMessageRequest.cs" company="JP Dillingham">
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
    ///     Sends a private message.
    /// </summary>
    internal class PrivateMessageRequest
    {
        /// <summary>
        ///     Initializes a new instance of the <see cref="PrivateMessageRequest"/> class.
        /// </summary>
        /// <param name="username">The user to which the message is to be sent.</param>
        /// <param name="message">The message to send.</param>
        public PrivateMessageRequest(string username, string message)
        {
            Username = username;
            Message = message;
        }

        /// <summary>
        ///     Gets the message to send.
        /// </summary>
        public string Message { get; }

        /// <summary>
        ///     Gets the user to which the message is to be sent.
        /// </summary>
        public string Username { get; }

        /// <summary>
        ///     Implicitly converts an instance to a <see cref="Message"/> via <see cref="ToMessage()"/>.
        /// </summary>
        /// <param name="instance">The instance to convert.</param>
        public static implicit operator byte[](PrivateMessageRequest instance)
        {
            return instance.ToMessage();
        }

        /// <summary>
        ///     Constructs a <see cref="Message"/> from this request.
        /// </summary>
        /// <returns>The constructed message.</returns>
        internal byte[] ToMessage()
        {
            return new MessageBuilder()
                .WriteCode(MessageCode.ServerPrivateMessage)
                .WriteString(Username)
                .WriteString(Message)
                .Build();
        }
    }
}
