﻿using Soulseek.NET.Messaging;
// <copyright file="RequestsTests.cs" company="JP Dillingham">
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

namespace Soulseek.NET.Tests.Unit.Messaging
{
    using Soulseek.NET.Messaging.Requests;
    using System;
    using Xunit;

    public class RequestsTests
    {
        [Trait("Category", "Instantiation")]
        [Trait("Request", "AcknowledgePrivateMessageRequest")]
        [Fact(DisplayName = "AcknowledgePrivateMessageRequest instantiates properly")]
        public void AcknowledgePrivateMessageRequest_Instantiates_Properly()
        {
            var num = new Random().Next();
            var a = new AcknowledgePrivateMessageRequest(num);

            Assert.Equal(num, a.Id);
        }

        [Trait("Category", "ToMessage")]
        [Trait("Request", "AcknowledgePrivateMessageRequest")]
        [Fact(DisplayName = "AcknowledgePrivateMessageRequest constructs the correct Message")]
        public void AcknowledgePrivateMessageRequest_Constructs_The_Correct_Message()
        {
            var num = new Random().Next();
            var msg = new AcknowledgePrivateMessageRequest(num).ToMessage();

            Assert.Equal(MessageCode.ServerAcknowledgePrivateMessage, msg.Code);
            Assert.Equal(8, msg.Length);

            var reader = new MessageReader(msg);

            Assert.Equal(num, reader.ReadInteger());
        }

        [Trait("Category", "Instantiation")]
        [Trait("Request", "GetPeerAddressRequest")]
        [Fact(DisplayName = "GetPeerAddressRequest instantiates properly")]
        public void GetPeerAddressRequest_Instantiates_Properly()
        {
            var name = Guid.NewGuid().ToString();
            var a = new GetPeerAddressRequest(name);

            Assert.Equal(name, a.Username);
        }

        [Trait("Category", "ToMessage")]
        [Trait("Request", "GetPeerAddressRequest")]
        [Fact(DisplayName = "GetPeerAddressRequest constructs the correct Message")]
        public void GetPeerAddressRequest_Constructs_The_Correct_Message()
        {
            var name = Guid.NewGuid().ToString();
            var msg = new GetPeerAddressRequest(name).ToMessage();

            Assert.Equal(MessageCode.ServerGetPeerAddress, msg.Code);
            Assert.Equal(name.Length + 8, msg.Length);

            var reader = new MessageReader(msg);

            Assert.Equal(name, reader.ReadString());
        }

        [Trait("Category", "Instantiation")]
        [Trait("Request", "LoginRequest")]
        [Fact(DisplayName = "LoginRequest instantiates properly")]
        public void LoginRequest_Instantiates_Properly()
        {
            var name = Guid.NewGuid().ToString();
            var password = Guid.NewGuid().ToString();
            var a = new LoginRequest(name, password);

            Assert.Equal(name, a.Username);
            Assert.Equal(password, a.Password);
            Assert.NotEmpty(a.Hash);
            Assert.NotEqual(0, a.Version);
            Assert.NotEqual(0, a.MinorVersion);
        }

        [Trait("Category", "ToMessage")]
        [Trait("Request", "LoginRequest")]
        [Fact(DisplayName = "LoginRequest constructs the correct Message")]
        public void LoginRequest_Constructs_The_Correct_Message()
        {
            var name = Guid.NewGuid().ToString();
            var password = Guid.NewGuid().ToString();
            var a = new LoginRequest(name, password);
            var msg = a.ToMessage();

            Assert.Equal(MessageCode.ServerLogin, msg.Code);
            Assert.Equal(name.Length + password.Length + a.Hash.Length + 24, msg.Length);

            var reader = new MessageReader(msg);

            Assert.Equal(name, reader.ReadString());
            Assert.Equal(password, reader.ReadString());
            Assert.Equal(a.Version, reader.ReadInteger());
            Assert.Equal(a.Hash, reader.ReadString());
            Assert.Equal(a.MinorVersion, reader.ReadInteger());
        }
    }
}
