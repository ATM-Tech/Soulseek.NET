﻿// <copyright file="BrowseAsyncTests.cs" company="JP Dillingham">
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

namespace Soulseek.Tests.Unit.Client
{
    using System;
    using System.Collections.Generic;
    using System.Net;
    using System.Threading;
    using System.Threading.Tasks;
    using AutoFixture.Xunit2;
    using Moq;
    using Soulseek.Exceptions;
    using Soulseek.Messaging;
    using Soulseek.Messaging.Messages;
    using Soulseek.Messaging.Tcp;
    using Soulseek.Tcp;
    using Xunit;

    public class BrowseAsyncTests
    {
        [Trait("Category", "BrowseAsync")]
        [Theory(DisplayName = "BrowseAsync throws ArgumentException given bad username")]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        public async Task BrowseAsync_Throws_ArgumentException_Given_Bad_Username(string username)
        {
            var s = new SoulseekClient();

            var ex = await Record.ExceptionAsync(async () => await s.BrowseAsync(username));

            Assert.NotNull(ex);
            Assert.IsType<ArgumentException>(ex);
        }

        [Trait("Category", "BrowseAsync")]
        [Fact(DisplayName = "BrowseAsync throws InvalidOperationException when not connected")]
        public async Task BrowseAsync_Throws_InvalidOperationException_When_Not_Connected()
        {
            var s = new SoulseekClient();

            var ex = await Record.ExceptionAsync(async () => await s.BrowseAsync("foo"));

            Assert.NotNull(ex);
            Assert.IsType<InvalidOperationException>(ex);
            Assert.Contains("Connected", ex.Message, StringComparison.InvariantCultureIgnoreCase);
        }

        [Trait("Category", "BrowseAsync")]
        [Fact(DisplayName = "BrowseAsync throws InvalidOperationException when not logged in")]
        public async Task BrowseAsync_Throws_InvalidOperationException_When_Not_Logged_In()
        {
            var s = new SoulseekClient();
            s.SetProperty("State", SoulseekClientStates.Connected);

            var ex = await Record.ExceptionAsync(async () => await s.BrowseAsync("foo"));

            Assert.NotNull(ex);
            Assert.IsType<InvalidOperationException>(ex);
            Assert.Contains("logged in", ex.Message, StringComparison.InvariantCultureIgnoreCase);
        }

        [Trait("Category", "BrowseInternalAsync")]
        [Theory(DisplayName = "BrowseInternalAsync returns expected response on success"), AutoData]
        public async Task BrowseInternalAsync_Returns_Expected_Response_On_Success(string username, IPAddress ip, int port, string localUsername, List<Directory> directories)
        {
            var response = new BrowseResponse(directories.Count, directories);

            var waiter = new Mock<IWaiter>();
            waiter.Setup(m => m.WaitIndefinitely<BrowseResponse>(It.IsAny<WaitKey>(), It.IsAny<CancellationToken>()))
                .Returns(Task.FromResult(response));
            waiter.Setup(m => m.Wait<GetPeerAddressResponse>(It.IsAny<WaitKey>(), null, It.IsAny<CancellationToken>()))
                .Returns(Task.FromResult(new GetPeerAddressResponse(username, ip, port)));

            var conn = new Mock<IMessageConnection>();
            conn.Setup(m => m.State)
                .Returns(ConnectionState.Connected);
            conn.Setup(m => m.WriteMessageAsync(It.IsAny<Message>(), It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);

            var connManager = new Mock<IPeerConnectionManager>();
            connManager.Setup(m => m.GetOrAddMessageConnectionAsync(username, ip, port, It.IsAny<CancellationToken>()))
                .Returns(Task.FromResult(conn.Object));

            var s = new SoulseekClient("127.0.0.1", 1, waiter: waiter.Object, serverConnection: conn.Object, peerConnectionManager: connManager.Object);
            s.SetProperty("Username", localUsername);
            s.SetProperty("State", SoulseekClientStates.Connected | SoulseekClientStates.LoggedIn);

            var result = await s.BrowseAsync(username);

            Assert.Equal(response, result);
        }

        [Trait("Category", "BrowseInternalAsync")]
        [Theory(DisplayName = "BrowseInternalAsync throws BrowseException on cancellation"), AutoData]
        public async Task BrowseInternalAsync_Throws_BrowseException_On_Cancellation(string username, IPAddress ip, int port, string localUsername)
        {
            var waiter = new Mock<IWaiter>();
            waiter.Setup(m => m.WaitIndefinitely<BrowseResponse>(It.IsAny<WaitKey>(), It.IsAny<CancellationToken>()))
                .Returns(Task.FromException<BrowseResponse>(new OperationCanceledException()));
            waiter.Setup(m => m.Wait<GetPeerAddressResponse>(It.IsAny<WaitKey>(), null, It.IsAny<CancellationToken>()))
                .Returns(Task.FromResult(new GetPeerAddressResponse(username, ip, port)));

            var conn = new Mock<IMessageConnection>();
            conn.Setup(m => m.State)
                .Returns(ConnectionState.Connected);
            conn.Setup(m => m.WriteMessageAsync(It.IsAny<Message>(), It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);

            var connManager = new Mock<IPeerConnectionManager>();
            connManager.Setup(m => m.GetOrAddMessageConnectionAsync(username, ip, port, It.IsAny<CancellationToken>()))
                .Returns(Task.FromResult(conn.Object));

            var s = new SoulseekClient("127.0.0.1", 1, waiter: waiter.Object, serverConnection: conn.Object, peerConnectionManager: connManager.Object);
            s.SetProperty("Username", localUsername);
            s.SetProperty("State", SoulseekClientStates.Connected | SoulseekClientStates.LoggedIn);

            BrowseResponse result = null;
            var ex = await Record.ExceptionAsync(async () => result = await s.BrowseAsync(username));

            Assert.NotNull(ex);
            Assert.IsType<BrowseException>(ex);
            Assert.IsType<OperationCanceledException>(ex.InnerException);
        }

        [Trait("Category", "BrowseInternalAsync")]
        [Theory(DisplayName = "BrowseInternalAsync throws BrowseException on write exception"), AutoData]
        public async Task BrowseInternalAsync_Throws_BrowseException_On_Write_Exception(string username, IPAddress ip, int port, string localUsername)
        {
            var waiter = new Mock<IWaiter>();
            waiter.Setup(m => m.Wait<GetPeerAddressResponse>(It.IsAny<WaitKey>(), null, It.IsAny<CancellationToken>()))
                .Returns(Task.FromResult(new GetPeerAddressResponse(username, ip, port)));

            var conn = new Mock<IMessageConnection>();
            conn.Setup(m => m.WriteMessageAsync(It.Is<Message>(n => n.Code == MessageCode.PeerBrowseRequest), It.IsAny<CancellationToken>()))
                .Throws(new ConnectionWriteException());

            var connManager = new Mock<IPeerConnectionManager>();
            connManager.Setup(m => m.GetOrAddMessageConnectionAsync(username, ip, port, It.IsAny<CancellationToken>()))
                .Returns(Task.FromResult(conn.Object));

            var s = new SoulseekClient("127.0.0.1", 1, waiter: waiter.Object, serverConnection: conn.Object, peerConnectionManager: connManager.Object);
            s.SetProperty("Username", localUsername);
            s.SetProperty("State", SoulseekClientStates.Connected | SoulseekClientStates.LoggedIn);

            BrowseResponse result = null;
            var ex = await Record.ExceptionAsync(async () => result = await s.BrowseAsync(username));

            Assert.NotNull(ex);
            Assert.IsType<BrowseException>(ex);
            Assert.IsType<ConnectionWriteException>(ex.InnerException);
        }

        [Trait("Category", "BrowseInternalAsync")]
        [Theory(DisplayName = "BrowseInternalAsync throws BrowseException on disconnect"), AutoData]
        public async Task BrowseInternalAsync_Throws_BrowseException_On_Disconnect(string username, IPAddress ip, int port, string localUsername)
        {
            var waiter = new Mock<IWaiter>();
            waiter.Setup(m => m.Wait<GetPeerAddressResponse>(It.IsAny<WaitKey>(), null, It.IsAny<CancellationToken>()))
                .Returns(Task.FromResult(new GetPeerAddressResponse(username, ip, port)));
            waiter.Setup(m => m.WaitIndefinitely<BrowseResponse>(It.IsAny<WaitKey>(), It.IsAny<CancellationToken>()))
                .Returns(Task.FromException<BrowseResponse>(new ConnectionException("disconnected unexpectedly")));

            var conn = new Mock<IMessageConnection>();
            conn.Setup(m => m.WriteMessageAsync(It.IsAny<Message>(), It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask)
                .Raises(m => m.Disconnected += null, conn.Object, string.Empty);

            var connManager = new Mock<IPeerConnectionManager>();
            connManager.Setup(m => m.GetOrAddMessageConnectionAsync(username, ip, port, It.IsAny<CancellationToken>()))
                .Returns(Task.FromResult(conn.Object));

            var s = new SoulseekClient("127.0.0.1", 1, waiter: waiter.Object, serverConnection: conn.Object, peerConnectionManager: connManager.Object);
            s.SetProperty("Username", localUsername);
            s.SetProperty("State", SoulseekClientStates.Connected | SoulseekClientStates.LoggedIn);

            BrowseResponse result = null;
            var ex = await Record.ExceptionAsync(async () => result = await s.BrowseAsync(username));

            Assert.NotNull(ex);
            Assert.IsType<BrowseException>(ex);
            Assert.IsType<ConnectionException>(ex.InnerException);
            Assert.Contains("disconnected unexpectedly", ex.InnerException.Message, StringComparison.InvariantCultureIgnoreCase);
        }
    }
}
