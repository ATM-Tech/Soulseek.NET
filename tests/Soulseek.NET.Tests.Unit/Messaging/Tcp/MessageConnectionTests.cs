﻿// <copyright file="MessageConnectionTests.cs" company="JP Dillingham">
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

namespace Soulseek.NET.Tests.Unit.Messaging.Tcp
{
    using AutoFixture.Xunit2;
    using Moq;
    using Soulseek.NET.Messaging;
    using Soulseek.NET.Messaging.Tcp;
    using Soulseek.NET.Tcp;
    using System;
    using System.Collections.Concurrent;
    using System.IO;
    using System.Net;
    using System.Threading.Tasks;
    using Xunit;
    using Xunit.Abstractions;

    public class MessageConnectionTests
    {
        private ITestOutputHelper Output { get; }

        public MessageConnectionTests(ITestOutputHelper output)
        {
            Output = output;
        }

        [Trait("Category", "Instantiation")]
        [Theory(DisplayName = "Instantiates peer connection with given username and IP"), AutoData]
        public void Instantiates_Peer_Connection_With_Given_Username_And_IP(string username, IPAddress ipAddress, int port, ConnectionOptions options)
        {
            var c = new MessageConnection(MessageConnectionType.Peer, username, ipAddress, port, options);

            Assert.Equal(MessageConnectionType.Peer, c.Type);
            Assert.Equal(username, c.Username);
            Assert.Equal(ipAddress, c.IPAddress);
            Assert.Equal(port, c.Port);
            Assert.Equal(options, c.Options);

            Assert.Equal(new ConnectionKey(username, ipAddress, port, MessageConnectionType.Peer), c.Key);
        }

        [Trait("Category", "Instantiation")]
        [Theory(DisplayName = "Instantiates server connection with given IP"), AutoData]
        public void Instantiates_Peer_Connection_With_Given_IP(IPAddress ipAddress, int port, ConnectionOptions options)
        {
            var c = new MessageConnection(MessageConnectionType.Server, ipAddress, port, options);

            Assert.Equal(MessageConnectionType.Server, c.Type);
            Assert.Equal(ipAddress, c.IPAddress);
            Assert.Equal(port, c.Port);
            Assert.Equal(options, c.Options);

            Assert.Equal(new ConnectionKey(string.Empty, ipAddress, port, MessageConnectionType.Server), c.Key);
        }

        [Trait("Category", "WriteMessageAsync")]
        [Theory(DisplayName = "WriteMessageAsync throws InvalidOperationException when disconnected"), AutoData]
        public async Task WriteMessageAsync_Throws_InvalidOperationException_When_Disconnected(string username, IPAddress ipAddress, int port)
        {
            var msg = new MessageBuilder()
                .Code(MessageCode.PeerBrowseRequest)
                .Build();

            var c = new MessageConnection(MessageConnectionType.Peer, username, ipAddress, port);
            c.SetProperty("State", ConnectionState.Disconnected);

            var ex = await Record.ExceptionAsync(async () => await c.WriteMessageAsync(msg));

            Assert.NotNull(ex);
            Assert.IsType<InvalidOperationException>(ex);
        }

        [Trait("Category", "WriteMessageAsync")]
        [Theory(DisplayName = "WriteMessageAsync throws InvalidOperationException when disconnected"), AutoData]
        public async Task WriteMessageAsync_Throws_InvalidOperationException_When_Disconnecting(string username, IPAddress ipAddress, int port)
        {
            var msg = new MessageBuilder()
                .Code(MessageCode.PeerBrowseRequest)
                .Build();

            var c = new MessageConnection(MessageConnectionType.Peer, username, ipAddress, port);
            c.SetProperty("State", ConnectionState.Disconnecting);

            var ex = await Record.ExceptionAsync(async () => await c.WriteMessageAsync(msg));

            Assert.NotNull(ex);
            Assert.IsType<InvalidOperationException>(ex);
        }

        [Trait("Category", "WriteMessageAsync")]
        [Theory(DisplayName = "WriteMessageAsync defers when pending"), AutoData]
        public async Task WriteMessageAsync_Defers_When_Pending(string username, IPAddress ipAddress, int port)
        {
            var msg = new MessageBuilder()
                .Code(MessageCode.PeerBrowseRequest)
                .Build();

            var c = new MessageConnection(MessageConnectionType.Peer, username, ipAddress, port);

            await c.WriteMessageAsync(msg);

            var deferred = c.GetProperty<ConcurrentQueue<Message>>("DeferredMessages");

            Assert.Single(deferred);
        }

        [Trait("Category", "WriteMessageAsync")]
        [Theory(DisplayName = "WriteMessageAsync defers when connecting"), AutoData]
        public async Task WriteMessageAsync_Defers_When_Connecting(string username, IPAddress ipAddress, int port)
        {
            var msg = new MessageBuilder()
                .Code(MessageCode.PeerBrowseRequest)
                .Build();

            var c = new MessageConnection(MessageConnectionType.Peer, username, ipAddress, port);
            c.SetProperty("State", ConnectionState.Connecting);

            await c.WriteMessageAsync(msg);

            var deferred = c.GetProperty<ConcurrentQueue<Message>>("DeferredMessages");

            Assert.Single(deferred);
        }

        [Trait("Category", "WriteMessageAsync")]
        [Theory(DisplayName = "WriteMessageAsync writes when connected"), AutoData]
        public async Task WriteMessageAsync_Writes_When_Connected(string username, IPAddress ipAddress, int port)
        {
            var streamMock = new Mock<INetworkStream>();
            streamMock.Setup(s => s.ReadAsync(It.IsAny<byte[]>(), It.IsAny<int>(), It.IsAny<int>()))
                .Returns(Task.Run(() => 1));

            var tcpMock = new Mock<ITcpClient>();
            tcpMock.Setup(s => s.Connected).Returns(true);
            tcpMock.Setup(s => s.GetStream()).Returns(streamMock.Object);

            var msg = new MessageBuilder()
                .Code(MessageCode.PeerBrowseRequest)
                .Build();

            var c = new MessageConnection(MessageConnectionType.Peer, username, ipAddress, port, tcpClient: tcpMock.Object);
            await c.ConnectAsync();

            await c.WriteMessageAsync(msg);

            streamMock.Verify(s => s.WriteAsync(It.IsAny<byte[]>(), It.IsAny<int>(), It.IsAny<int>()), Times.Once);
        }

        [Trait("Category", "WriteMessageAsync")]
        [Theory(DisplayName = "WriteMessageAsync throws ConnectionWriteException when Stream.WriteAsync throws"), AutoData]
        public async Task WriteMessageAsync_Throws_ConnectionWriteException_When_Stream_WriteAsync_Throws(IPAddress ipAddress, int port)
        {
            var streamMock = new Mock<INetworkStream>();
            streamMock.Setup(s => s.WriteAsync(It.IsAny<byte[]>(), It.IsAny<int>(), It.IsAny<int>())).Throws(new IOException());
            streamMock.Setup(s => s.ReadAsync(It.IsAny<byte[]>(), It.IsAny<int>(), It.IsAny<int>()))
                .Returns(Task.Run(() => 1));

            var tcpMock = new Mock<ITcpClient>();
            tcpMock.Setup(s => s.Connected).Returns(true);
            tcpMock.Setup(s => s.GetStream()).Returns(streamMock.Object);

            var msg = new MessageBuilder()
                .Code(MessageCode.PeerBrowseRequest)
                .Build();

            var c = new MessageConnection(MessageConnectionType.Server, ipAddress, port, tcpClient: tcpMock.Object);

            await c.ConnectAsync();

            var ex = await Record.ExceptionAsync(async () => await c.WriteMessageAsync(msg));

            Assert.NotNull(ex);
            Assert.IsType<ConnectionWriteException>(ex);
            Assert.IsType<IOException>(ex.InnerException);

            streamMock.Verify(s => s.WriteAsync(It.IsAny<byte[]>(), It.IsAny<int>(), It.IsAny<int>()), Times.Once);
        }

        [Trait("Category", "Deferred Messages")]
        [Theory(DisplayName = "Deferred messages are sent on connected"), AutoData]
        public async Task Deferred_Messages_Are_Sent_On_Connected(IPAddress ipAddress, int port)
        {
            var streamMock = new Mock<INetworkStream>();
            streamMock.Setup(s => s.ReadAsync(It.IsAny<byte[]>(), It.IsAny<int>(), It.IsAny<int>()))
                .Returns(Task.Run(() => 1));

            var tcpMock = new Mock<ITcpClient>();
            tcpMock.Setup(s => s.Connected).Returns(true);
            tcpMock.Setup(s => s.GetStream()).Returns(streamMock.Object);

            var msg = new MessageBuilder()
                .Code(MessageCode.PeerBrowseRequest)
                .Build();

            var c = new MessageConnection(MessageConnectionType.Server, ipAddress, port, tcpClient: tcpMock.Object);

            await c.WriteMessageAsync(msg);
            await c.WriteMessageAsync(msg);

            var deferred1 = c.GetProperty<ConcurrentQueue<Message>>("DeferredMessages").Count;

            await c.ConnectAsync();

            var deferred2 = c.GetProperty<ConcurrentQueue<Message>>("DeferredMessages");

            Assert.Equal(2, deferred1);
            Assert.Empty(deferred2);

            streamMock.Verify(s => s.WriteAsync(It.IsAny<byte[]>(), It.IsAny<int>(), It.IsAny<int>()), Times.Exactly(2));
        }
    }
}
