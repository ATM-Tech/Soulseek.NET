﻿// <copyright file="ServerConnection_MessageReadTests.cs" company="JP Dillingham">
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

namespace Soulseek.NET.Tests.Unit.Client
{
    using System;
    using System.Net;
    using System.Threading.Tasks;
    using AutoFixture.Xunit2;
    using Moq;
    using Soulseek.NET.Messaging;
    using Soulseek.NET.Messaging.Messages;
    using Soulseek.NET.Messaging.Tcp;
    using Xunit;

    public class ServerConnection_MessageReadTests
    {
        [Trait("Category", "Diagnostic")]
        [Fact(DisplayName = "Creates diagnostic on message")]
        public void Creates_Diagnostic_On_Message()
        {
            var diagnostic = new Mock<IDiagnosticFactory>();
            diagnostic.Setup(m => m.Debug(It.IsAny<string>()));

            var message = new MessageBuilder()
                .Code(MessageCode.ServerParentMinSpeed)
                .WriteInteger(1)
                .Build();

            var s = new SoulseekClient("127.0.0.1", 1, diagnosticFactory: diagnostic.Object);

            s.InvokeMethod("ServerConnection_MessageRead", null, message);

            diagnostic.Verify(m => m.Debug(It.IsAny<string>()), Times.Once);
        }

        [Trait("Category", "Diagnostic")]
        [Fact(DisplayName = "Creates unhandled diagnostic on unhandled message")]
        public void Creates_Unhandled_Diagnostic_On_Unhandled_Message()
        {
            string msg = null;

            var diagnostic = new Mock<IDiagnosticFactory>();
            diagnostic.Setup(m => m.Debug(It.IsAny<string>()))
                .Callback<string>(m => msg = m);

            var message = new MessageBuilder().Code(MessageCode.ServerPrivateRoomOwned).Build();

            var s = new SoulseekClient("127.0.0.1", 1, diagnosticFactory: diagnostic.Object);

            s.InvokeMethod("ServerConnection_MessageRead", null, message);

            diagnostic.Verify(m => m.Debug(It.IsAny<string>()), Times.Exactly(2));

            Assert.Contains("Unhandled", msg, StringComparison.InvariantCultureIgnoreCase);
        }

        [Trait("Category", "Message")]
        [Theory(DisplayName = "Handles ServerGetPeerAddress"), AutoData]
        public void Handles_ServerGetPeerAddress(string username, IPAddress ip, int port)
        {
            GetPeerAddressResponse result = null;

            var waiter = new Mock<IWaiter>();
            waiter.Setup(m => m.Complete(It.IsAny<WaitKey>(), It.IsAny<GetPeerAddressResponse>()))
                .Callback<WaitKey, GetPeerAddressResponse>((key, response) => result = response);

            var ipBytes = ip.GetAddressBytes();
            Array.Reverse(ipBytes);

            var message = new MessageBuilder()
                .Code(MessageCode.ServerGetPeerAddress)
                .WriteString(username)
                .WriteBytes(ipBytes)
                .WriteInteger(port)
                .Build();

            var s = new SoulseekClient("127.0.0.1", 1, messageWaiter: waiter.Object);

            s.InvokeMethod("ServerConnection_MessageRead", null, message);

            Assert.Equal(username, result.Username);
            Assert.Equal(ip, result.IPAddress);
            Assert.Equal(port, result.Port);
        }

        [Trait("Category", "Message")]
        [Theory(DisplayName = "Raises PrivateMessageReceived event on ServerPrivateMessage"), AutoData]
        public void Raises_PrivateMessageRecieved_Event_On_ServerPrivateMessage(int id, int timeOffset, string username, string message, bool isAdmin)
        {
            var options = new SoulseekClientOptions(autoAcknowledgePrivateMessages: false);

            var conn = new Mock<IMessageConnection>();
            conn.Setup(m => m.WriteMessageAsync(It.IsAny<Message>()))
                .Returns(Task.CompletedTask);

            var epoch = new DateTime(1970, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc);
            var timestamp = epoch.AddSeconds(timeOffset).ToLocalTime();

            var msg = new MessageBuilder()
                .Code(MessageCode.ServerPrivateMessage)
                .WriteInteger(id)
                .WriteInteger(timeOffset)
                .WriteString(username)
                .WriteString(message)
                .WriteByte((byte)(isAdmin ? 1 : 0))
                .Build();

            var s = new SoulseekClient("127.0.0.1", 1, options: options, serverConnection: conn.Object);

            PrivateMessage response = null;
            s.PrivateMessageReceived += (_, privateMessage) => response = privateMessage;

            s.InvokeMethod("ServerConnection_MessageRead", null, msg);

            Assert.NotNull(response);
            Assert.Equal(id, response.Id);
            Assert.Equal(timestamp, response.Timestamp);
            Assert.Equal(username, response.Username);
            Assert.Equal(message, response.Message);
            Assert.Equal(isAdmin, response.IsAdmin);
        }

        [Trait("Category", "Message")]
        [Theory(DisplayName = "Acknowledges ServerPrivateMessage"), AutoData]
        public void Acknowledges_ServerPrivateMessage(int id, int timeOffset, string username, string message, bool isAdmin)
        {
            var options = new SoulseekClientOptions(autoAcknowledgePrivateMessages: true);

            var conn = new Mock<IMessageConnection>();
            conn.Setup(m => m.WriteMessageAsync(It.Is<Message>(a => new MessageReader(a).ReadInteger() == id)))
                .Returns(Task.CompletedTask);

            var msg = new MessageBuilder()
                .Code(MessageCode.ServerPrivateMessage)
                .WriteInteger(id)
                .WriteInteger(timeOffset)
                .WriteString(username)
                .WriteString(message)
                .WriteByte((byte)(isAdmin ? 1 : 0))
                .Build();

            var s = new SoulseekClient("127.0.0.1", 1, options: options, serverConnection: conn.Object);

            s.InvokeMethod("ServerConnection_MessageRead", null, msg);

            conn.Verify(m => m.WriteMessageAsync(It.Is<Message>(a => new MessageReader(a).ReadInteger() == id)), Times.Once);
        }

        [Trait("Category", "Message")]
        [Theory(DisplayName = "Handles IntegerResponse messages")]
        [InlineData(MessageCode.ServerParentMinSpeed)]
        [InlineData(MessageCode.ServerParentSpeedRatio)]
        [InlineData(MessageCode.ServerWishlistInterval)]
        public void Handles_IntegerResponse_Messages(MessageCode code)
        {
            int value = new Random().Next();
            int? result = null;

            var waiter = new Mock<IWaiter>();
            waiter.Setup(m => m.Complete(It.IsAny<WaitKey>(), It.IsAny<int>()))
                .Callback<WaitKey, int>((key, response) => result = response);

            var msg = new MessageBuilder()
                .Code(code)
                .WriteInteger(value)
                .Build();

            var s = new SoulseekClient("127.0.0.1", 1, messageWaiter: waiter.Object);

            s.InvokeMethod("ServerConnection_MessageRead", null, msg);

            Assert.Equal(value, result);
        }

        [Trait("Category", "Message")]
        [Theory(DisplayName = "Handles ServerLogin"), AutoData]
        public void Handles_ServerLogin(bool success, string message, IPAddress ip)
        {
            LoginResponse result = null;

            var waiter = new Mock<IWaiter>();
            waiter.Setup(m => m.Complete(It.IsAny<WaitKey>(), It.IsAny<LoginResponse>()))
                .Callback<WaitKey, LoginResponse>((key, response) => result = response);

            var ipBytes = ip.GetAddressBytes();
            Array.Reverse(ipBytes);

            var msg = new MessageBuilder()
                .Code(MessageCode.ServerLogin)
                .WriteByte((byte)(success ? 1 : 0))
                .WriteString(message)
                .WriteBytes(ipBytes)
                .Build();

            var s = new SoulseekClient("127.0.0.1", 1, messageWaiter: waiter.Object);

            s.InvokeMethod("ServerConnection_MessageRead", null, msg);

            Assert.Equal(success, result.Succeeded);
            Assert.Equal(message, result.Message);
            Assert.Equal(ip, result.IPAddress);
        }
    }
}
