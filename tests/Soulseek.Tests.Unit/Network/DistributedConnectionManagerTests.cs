﻿// <copyright file="DistributedConnectionManagerTests.cs" company="JP Dillingham">
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

namespace Soulseek.Tests.Unit.Network
{
    using System;
    using System.Collections.Concurrent;
    using System.Collections.Generic;
    using System.Linq;
    using System.Net;
    using System.Net.Sockets;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;
    using AutoFixture.Xunit2;
    using Moq;
    using Soulseek.Messaging;
    using Soulseek.Messaging.Handlers;
    using Soulseek.Messaging.Messages;
    using Soulseek.Network;
    using Soulseek.Network.Tcp;
    using Xunit;

    public class DistributedConnectionManagerTests
    {
        [Trait("Category", "Instantiation")]
        [Fact(DisplayName = "Instantiates properly")]
        public void Instantiates_Properly()
        {
            DistributedConnectionManager c = null;

            var ex = Record.Exception(() => (c, _) = GetFixture());

            Assert.Null(ex);
            Assert.NotNull(c);

            Assert.Equal(0, c.BranchLevel);
            Assert.Equal(string.Empty, c.BranchRoot);
            Assert.True(c.CanAcceptChildren);
            Assert.Empty(c.Children);
            Assert.Equal(new ClientOptions().ConcurrentDistributedChildrenLimit, c.ConcurrentChildLimit);
            Assert.False(c.HasParent);
            Assert.Equal((string.Empty, IPAddress.None, 0), c.Parent);
            Assert.Empty(c.PendingSolicitations);
        }

        [Trait("Category", "Dispose")]
        [Fact(DisplayName = "Disposes without throwing")]
        public void Disposes_Without_Throwing()
        {
            var (manager, mocks) = GetFixture();

            using (manager)
            using (var c = new DistributedConnectionManager(mocks.Client.Object))
            {
                var ex = Record.Exception(() => c.Dispose());

                Assert.Null(ex);
            }
        }

        [Trait("Category", "SetBranchLevel")]
        [Theory(DisplayName = "SetBranchLevel sets branch level"), AutoData]
        public void SetBranchLevel_Sets_Branch_Level(int branchLevel)
        {
            var (manager, _) = GetFixture();

            using (manager)
            {
                manager.SetBranchLevel(branchLevel);

                Assert.Equal(branchLevel, manager.BranchLevel);
            }
        }

        [Trait("Category", "SetBranchRoot")]
        [Theory(DisplayName = "SetBranchRoot sets branch root"), AutoData]
        public void SetBranchRoot_Sets_Branch_Root(string branchRoot)
        {
            var (manager, _) = GetFixture();

            using (manager)
            {
                manager.SetBranchRoot(branchRoot);

                Assert.Equal(branchRoot, manager.BranchRoot);
            }
        }

        [Trait("Category", "BroadcastMessageAsync")]
        [Theory(DisplayName = "BroadcastMessageAsync resets watchdog timer"), AutoData]
        public async Task BroadcastMessageAsync_Resets_Watchdog_Timer(byte[] bytes)
        {
            var (manager, _) = GetFixture();

            var timer = manager.GetProperty<System.Timers.Timer>("ParentWatchdogTimer");
            timer.Stop();

            using (timer)
            using (manager)
            {
                await manager.BroadcastMessageAsync(bytes, CancellationToken.None);

                Assert.True(timer.Enabled);
            }
        }

        [Trait("Category", "BroadcastMessageAsync")]
        [Theory(DisplayName = "BroadcastMessageAsync broadcasts message"), AutoData]
        public async Task BroadcastMessageAsync_Broadcasts_Message(byte[] bytes)
        {
            var (manager, mocks) = GetFixture();

            var c1 = new Mock<IMessageConnection>();
            var c2 = new Mock<IMessageConnection>();

            var dict = manager.GetProperty<ConcurrentDictionary<string, IMessageConnection>>("ChildConnections");
            dict.TryAdd("c1", c1.Object);
            dict.TryAdd("c2", c2.Object);

            using (manager)
            {
                await manager.BroadcastMessageAsync(bytes, CancellationToken.None);
            }

            c1.Verify(m => m.WriteAsync(It.Is<byte[]>(b => b.Matches(bytes)), It.IsAny<CancellationToken?>()));
            c2.Verify(m => m.WriteAsync(It.Is<byte[]>(b => b.Matches(bytes)), It.IsAny<CancellationToken?>()));
        }

        [Trait("Category", "BroadcastMessageAsync")]
        [Theory(DisplayName = "BroadcastMessageAsync disposes on throw"), AutoData]
        public async Task BroadcastMessageAsync_Disposes_On_Throw(byte[] bytes)
        {
            var (manager, mocks) = GetFixture();

            var c1 = new Mock<IMessageConnection>();
            var c2 = new Mock<IMessageConnection>();
            c2.Setup(m => m.WriteAsync(It.IsAny<byte[]>(), It.IsAny<CancellationToken?>()))
                .Throws(new Exception("foo"));

            var dict = manager.GetProperty<ConcurrentDictionary<string, IMessageConnection>>("ChildConnections");
            dict.TryAdd("c1", c1.Object);
            dict.TryAdd("c2", c2.Object);

            using (manager)
            {
                await manager.BroadcastMessageAsync(bytes, CancellationToken.None);
            }

            c1.Verify(m => m.WriteAsync(It.Is<byte[]>(b => b.Matches(bytes)), It.IsAny<CancellationToken?>()));

            c2.Verify(m => m.WriteAsync(It.Is<byte[]>(b => b.Matches(bytes)), It.IsAny<CancellationToken?>()));
            c2.Verify(m => m.Dispose(), Times.AtLeastOnce);
        }

        [Trait("Category", "ParentConnection_Disconnected")]
        [Theory(DisplayName = "ParentConnection_Disconnected_Cleans_Up"), AutoData]
        public void ParentConnection_Disconnected_Cleans_Up(string username, IPAddress ip, int port, string message)
        {
            var c = GetMessageConnectionMock(username, ip, port);

            var (manager, _) = GetFixture();

            using (manager)
            {
                manager.SetProperty("ParentConnection", new Mock<IMessageConnection>().Object);
                manager.SetProperty("BranchLevel", 1);
                manager.SetProperty("BranchRoot", "foo");

                manager.InvokeMethod("ParentConnection_Disconnected", c.Object, message);

                Assert.Null(manager.GetProperty<IMessageConnection>("ParentConnection"));
                Assert.Equal(0, manager.BranchLevel);
                Assert.Equal(string.Empty, manager.BranchRoot);
            }
        }

        [Trait("Category", "AddChildConnectionAsync")]
        [Theory(DisplayName = "AddChildConnectionAsync rejects if over child limit"), AutoData]
        internal async Task AddChildConnectionAsync_Ctpr_Rejects_If_Over_Child_Limit(ConnectToPeerResponse ctpr)
        {
            var (manager, mocks) = GetFixture(options: new ClientOptions(concurrentDistributedChildrenLimit: 0));

            using (manager)
            {
                await manager.AddChildConnectionAsync(ctpr);
            }

            mocks.Diagnostic.Verify(m => m.Debug(It.Is<string>(s => s.Contains("rejected", StringComparison.InvariantCultureIgnoreCase))), Times.Once);
        }

        [Trait("Category", "AddChildConnectionAsync")]
        [Theory(DisplayName = "AddChildConnectionAsync updates status on rejection"), AutoData]
        internal async Task AddChildConnectionAsync_Ctpr_Updates_Status_On_Rejection(ConnectToPeerResponse ctpr)
        {
            var (manager, mocks) = GetFixture(options: new ClientOptions(concurrentDistributedChildrenLimit: 0));

            mocks.Client.Setup(m => m.State)
                .Returns(SoulseekClientStates.Connected | SoulseekClientStates.LoggedIn);

            using (manager)
            {
                await manager.AddChildConnectionAsync(ctpr);
            }

            mocks.ServerConnection.Verify(m => m.WriteAsync(It.IsAny<byte[]>(), It.IsAny<CancellationToken?>()), Times.Once);
        }

        [Trait("Category", "AddChildConnectionAsync")]
        [Theory(DisplayName = "AddChildConnectionAsync adds child on successful connection"), AutoData]
        internal async Task AddChildConnectionAsync_Ctpr_Adds_Child_On_Successful_Connection(ConnectToPeerResponse ctpr)
        {
            var (manager, mocks) = GetFixture();

            var conn = GetMessageConnectionMock(ctpr.Username, ctpr.IPAddress, ctpr.Port);

            mocks.ConnectionFactory.Setup(m => m.GetMessageConnection(ctpr.Username, ctpr.IPAddress, ctpr.Port, It.IsAny<ConnectionOptions>(), It.IsAny<ITcpClient>()))
                .Returns(conn.Object);

            mocks.Waiter.Setup(m => m.Wait<int>(It.IsAny<WaitKey>(), It.IsAny<int?>(), It.IsAny<CancellationToken?>()))
                .Returns(Task.FromResult(1));

            using (manager)
            {
                await manager.AddChildConnectionAsync(ctpr);

                var child = manager.Children.FirstOrDefault();

                Assert.Single(manager.Children);
                Assert.NotEqual(default((string, IPAddress, int)), child);
                Assert.Equal(ctpr.Username, child.Username);
                Assert.Equal(ctpr.IPAddress, child.IPAddress);
                Assert.Equal(ctpr.Port, child.Port);
            }
        }

        [Trait("Category", "AddChildConnectionAsync")]
        [Theory(DisplayName = "AddChildConnectionAsync disposes connection on throw"), AutoData]
        internal async Task AddChildConnectionAsync_Ctpr_Disposes_Connection_On_Throw(ConnectToPeerResponse ctpr)
        {
            var expectedEx = new Exception("foo");

            var (manager, mocks) = GetFixture();

            var conn = GetMessageConnectionMock(ctpr.Username, ctpr.IPAddress, ctpr.Port);
            conn.Setup(m => m.ConnectAsync(It.IsAny<CancellationToken?>()))
                .Throws(expectedEx);

            mocks.ConnectionFactory.Setup(m => m.GetMessageConnection(ctpr.Username, ctpr.IPAddress, ctpr.Port, It.IsAny<ConnectionOptions>(), It.IsAny<ITcpClient>()))
                .Returns(conn.Object);

            mocks.Waiter.Setup(m => m.Wait<int>(It.IsAny<WaitKey>(), It.IsAny<int?>(), It.IsAny<CancellationToken?>()))
                .Returns(Task.FromResult(1));

            using (manager)
            {
                var ex = await Record.ExceptionAsync(() => manager.AddChildConnectionAsync(ctpr));

                Assert.NotNull(ex);
                Assert.Equal(expectedEx, ex);
            }

            conn.Verify(m => m.Dispose(), Times.Once);
        }

        [Trait("Category", "AddChildConnectionAsync")]
        [Theory(DisplayName = "AddChildConnectionAsync generates expected diagnostics on successful connection"), AutoData]
        internal async Task AddChildConnectionAsync_Ctpr_Generates_Expected_Diagnostics_On_Successful_Connection(ConnectToPeerResponse ctpr)
        {
            var (manager, mocks) = GetFixture();

            var conn = GetMessageConnectionMock(ctpr.Username, ctpr.IPAddress, ctpr.Port);

            mocks.ConnectionFactory.Setup(m => m.GetMessageConnection(ctpr.Username, ctpr.IPAddress, ctpr.Port, It.IsAny<ConnectionOptions>(), It.IsAny<ITcpClient>()))
                .Returns(conn.Object);

            mocks.Waiter.Setup(m => m.Wait<int>(It.IsAny<WaitKey>(), It.IsAny<int?>(), It.IsAny<CancellationToken?>()))
                .Returns(Task.FromResult(1));

            using (manager)
            {
                await manager.AddChildConnectionAsync(ctpr);
            }

            mocks.Diagnostic
                .Verify(m => m.Debug(It.Is<string>(s => s.ContainsInsensitive($"Attempting child connection to {ctpr.Username}"))), Times.Once);
            mocks.Diagnostic
                .Verify(m => m.Debug(It.Is<string>(s => s.ContainsInsensitive($"Child connection to {ctpr.Username}") && s.ContainsInsensitive("established"))), Times.Once);
            mocks.Diagnostic
                .Verify(m => m.Info(It.Is<string>(s => s.ContainsInsensitive($"Added child {ctpr.Username}"))), Times.Once);
        }

        [Trait("Category", "AddChildConnectionAsync")]
        [Theory(DisplayName = "AddChildConnectionAsync generates expected diagnostic on error"), AutoData]
        internal async Task AddChildConnectionAsync_Ctpr_Generates_Expected_Diagnostic_On_Error(ConnectToPeerResponse ctpr)
        {
            var expectedEx = new Exception("foo");

            var (manager, mocks) = GetFixture();

            var conn = GetMessageConnectionMock(ctpr.Username, ctpr.IPAddress, ctpr.Port);
            conn.Setup(m => m.ConnectAsync(It.IsAny<CancellationToken?>()))
                .Throws(expectedEx);

            mocks.ConnectionFactory.Setup(m => m.GetMessageConnection(ctpr.Username, ctpr.IPAddress, ctpr.Port, It.IsAny<ConnectionOptions>(), It.IsAny<ITcpClient>()))
                .Returns(conn.Object);

            mocks.Waiter.Setup(m => m.Wait<int>(It.IsAny<WaitKey>(), It.IsAny<int?>(), It.IsAny<CancellationToken?>()))
                .Returns(Task.FromResult(1));

            using (manager)
            {
                await Record.ExceptionAsync(() => manager.AddChildConnectionAsync(ctpr));
            }

            mocks.Diagnostic
                .Verify(m => m.Debug(It.Is<string>(s => s.ContainsInsensitive($"Discarded child connection to {ctpr.Username}"))), Times.Once);
        }

        [Trait("Category", "AddChildConnectionAsync")]
        [Theory(DisplayName = "AddChildConnectionAsync rejects if over child limit"), AutoData]
        internal async Task AddChildConnectionAsync_Rejects_If_Over_Child_Limit(string username, IPAddress ip, int port)
        {
            var (manager, mocks) = GetFixture(options: new ClientOptions(concurrentDistributedChildrenLimit: 0));

            mocks.TcpClient.Setup(m => m.RemoteEndPoint)
                .Returns(new IPEndPoint(ip, port));

            using (manager)
            {
                await manager.AddChildConnectionAsync(username, mocks.TcpClient.Object);
            }

            mocks.Diagnostic.Verify(m => m.Debug(It.Is<string>(s => s.Contains("rejected", StringComparison.InvariantCultureIgnoreCase))), Times.Once);
        }


        [Trait("Category", "AddChildConnectionAsync")]
        [Theory(DisplayName = "AddChildConnectionAsync disposes TcpClient on rejection"), AutoData]
        internal async Task AddChildConnectionAsync_Disposes_TcpClient_On_Rejection(string username, IPAddress ip, int port)
        {
            var (manager, mocks) = GetFixture(options: new ClientOptions(concurrentDistributedChildrenLimit: 0));

            mocks.TcpClient.Setup(m => m.RemoteEndPoint)
                .Returns(new IPEndPoint(ip, port));

            using (manager)
            {
                await manager.AddChildConnectionAsync(username, mocks.TcpClient.Object);
            }

            mocks.Diagnostic.Verify(m => m.Debug(It.Is<string>(s => s.Contains("rejected", StringComparison.InvariantCultureIgnoreCase))), Times.Once);
            mocks.TcpClient.Verify(m => m.Dispose(), Times.Once);
        }

        [Trait("Category", "AddChildConnectionAsync")]
        [Theory(DisplayName = "AddChildConnectionAsync disposes connection on throw"), AutoData]
        internal async Task AddChildConnectionAsync_Disposes_Connection_On_Throw(string username, IPAddress ip, int port)
        {
            var expectedEx = new Exception("foo");

            var (manager, mocks) = GetFixture();

            var conn = GetMessageConnectionMock(username, ip, port);
            conn.Setup(m => m.ConnectAsync(It.IsAny<CancellationToken?>()))
                .Throws(expectedEx);

            mocks.ConnectionFactory.Setup(m => m.GetMessageConnection(username, ip, port, It.IsAny<ConnectionOptions>(), It.IsAny<ITcpClient>()))
                .Returns(conn.Object);

            mocks.TcpClient.Setup(m => m.RemoteEndPoint)
                .Returns(new IPEndPoint(ip, port));

            mocks.Waiter.Setup(m => m.Wait<int>(It.IsAny<WaitKey>(), It.IsAny<int?>(), It.IsAny<CancellationToken?>()))
                .Returns(Task.FromException<int>(expectedEx));

            using (manager)
            {
                var ex = await Record.ExceptionAsync(() => manager.AddChildConnectionAsync(username, mocks.TcpClient.Object));

                Assert.NotNull(ex);
                Assert.Equal(expectedEx, ex);
            }

            conn.Verify(m => m.Dispose(), Times.Once);
        }

        [Trait("Category", "AddChildConnectionAsync")]
        [Theory(DisplayName = "AddChildConnectionAsync updates status on rejection"), AutoData]
        internal async Task AddChildConnectionAsync_Updates_Status_On_Rejection(string username, IPAddress ip, int port)
        {
            var (manager, mocks) = GetFixture(options: new ClientOptions(concurrentDistributedChildrenLimit: 0));

            mocks.Client.Setup(m => m.State)
                .Returns(SoulseekClientStates.Connected | SoulseekClientStates.LoggedIn);

            mocks.TcpClient.Setup(m => m.RemoteEndPoint)
                .Returns(new IPEndPoint(ip, port));

            using (manager)
            {
                await manager.AddChildConnectionAsync(username, mocks.TcpClient.Object);
            }

            mocks.ServerConnection.Verify(m => m.WriteAsync(It.IsAny<byte[]>(), It.IsAny<CancellationToken?>()), Times.Once);
        }

        [Trait("Category", "AddChildConnectionAsync")]
        [Theory(DisplayName = "AddChildConnectionAsync adds child on successful connection"), AutoData]
        internal async Task AddChildConnectionAsync_Adds_Child_On_Successful_Connection(string username, IPAddress ip, int port)
        {
            var (manager, mocks) = GetFixture();

            var conn = GetMessageConnectionMock(username, ip, port);

            mocks.ConnectionFactory.Setup(m => m.GetMessageConnection(username, ip, port, It.IsAny<ConnectionOptions>(), It.IsAny<ITcpClient>()))
                .Returns(conn.Object);

            mocks.TcpClient.Setup(m => m.RemoteEndPoint)
                .Returns(new IPEndPoint(ip, port));

            mocks.Waiter.Setup(m => m.Wait<int>(It.IsAny<WaitKey>(), It.IsAny<int?>(), It.IsAny<CancellationToken?>()))
                .Returns(Task.FromResult(1));

            using (manager)
            {
                await manager.AddChildConnectionAsync(username, mocks.TcpClient.Object);

                var child = manager.Children.FirstOrDefault();

                Assert.Single(manager.Children);
                Assert.NotEqual(default((string, IPAddress, int)), child);
                Assert.Equal(username, child.Username);
                Assert.Equal(ip, child.IPAddress);
                Assert.Equal(port, child.Port);
            }
        }

        [Trait("Category", "AddChildConnectionAsync")]
        [Theory(DisplayName = "AddChildConnectionAsync invokes StartReadingContinuously on successful connection"), AutoData]
        internal async Task AddChildConnectionAsync_Invokes_StartReadingContinuously_On_Successful_Connection(string username, IPAddress ip, int port)
        {
            var (manager, mocks) = GetFixture();

            var conn = GetMessageConnectionMock(username, ip, port);

            mocks.ConnectionFactory.Setup(m => m.GetMessageConnection(username, ip, port, It.IsAny<ConnectionOptions>(), It.IsAny<ITcpClient>()))
                .Returns(conn.Object);

            mocks.TcpClient.Setup(m => m.RemoteEndPoint)
                .Returns(new IPEndPoint(ip, port));

            mocks.Waiter.Setup(m => m.Wait<int>(It.IsAny<WaitKey>(), It.IsAny<int?>(), It.IsAny<CancellationToken?>()))
                .Returns(Task.FromResult(1));

            using (manager)
            {
                await manager.AddChildConnectionAsync(username, mocks.TcpClient.Object);
            }

            conn.Verify(m => m.StartReadingContinuously(), Times.Once);
        }

        [Trait("Category", "AddChildConnectionAsync")]
        [Theory(DisplayName = "AddChildConnectionAsync generates expected diagnostics on success"), AutoData]
        internal async Task AddChildConnectionAsync_Generates_Expected_Diagnostics_On_Success(string username, IPAddress ip, int port)
        {
            var (manager, mocks) = GetFixture();

            var conn = GetMessageConnectionMock(username, ip, port);

            mocks.Client.Setup(m => m.State)
                .Returns(SoulseekClientStates.Connected | SoulseekClientStates.LoggedIn);

            mocks.TcpClient.Setup(m => m.RemoteEndPoint)
                .Returns(new IPEndPoint(ip, port));

            mocks.ConnectionFactory.Setup(m => m.GetMessageConnection(username, ip, port, It.IsAny<ConnectionOptions>(), It.IsAny<ITcpClient>()))
                .Returns(conn.Object);

            mocks.Waiter.Setup(m => m.Wait<int>(It.IsAny<WaitKey>(), It.IsAny<int?>(), It.IsAny<CancellationToken?>()))
                .Returns(Task.FromResult(1));

            using (manager)
            {
                await manager.AddChildConnectionAsync(username, mocks.TcpClient.Object);
            }

            mocks.Diagnostic
                .Verify(m => m.Debug(It.Is<string>(s => s.ContainsInsensitive($"Accepted child connection to {username}"))), Times.Once);
            mocks.Diagnostic
                .Verify(m => m.Info(It.Is<string>(s => s.ContainsInsensitive($"Added child {username}"))), Times.Once);
        }

        [Trait("Category", "AddChildConnectionAsync")]
        [Theory(DisplayName = "AddChildConnectionAsync generates expected diagnostics on throw"), AutoData]
        internal async Task AddChildConnectionAsync_Generates_Expected_Diagnostics_On_Throw(string username, IPAddress ip, int port)
        {
            var (manager, mocks) = GetFixture();

            var conn = GetMessageConnectionMock(username, ip, port);

            mocks.Client.Setup(m => m.State)
                .Returns(SoulseekClientStates.Connected | SoulseekClientStates.LoggedIn);

            mocks.TcpClient.Setup(m => m.RemoteEndPoint)
                .Returns(new IPEndPoint(ip, port));

            mocks.ConnectionFactory.Setup(m => m.GetMessageConnection(username, ip, port, It.IsAny<ConnectionOptions>(), It.IsAny<ITcpClient>()))
                .Returns(conn.Object);

            mocks.Waiter.Setup(m => m.Wait<int>(It.IsAny<WaitKey>(), It.IsAny<int?>(), It.IsAny<CancellationToken?>()))
                .Returns(Task.FromException<int>(new Exception()));

            using (manager)
            {
                await Record.ExceptionAsync(() => manager.AddChildConnectionAsync(username, mocks.TcpClient.Object));
            }

            mocks.Diagnostic
                .Verify(m => m.Debug(It.Is<string>(s => s.ContainsInsensitive($"Discarded child connection to {username}"))), Times.Once);
        }

        [Trait("Category", "UpdateStatusAsync")]
        [Fact(DisplayName = "UpdateStatusAsync skips update client isn't connected")]
        internal async Task UpdateStatusAsync_Skips_Update_If_Client_Not_Connected()
        {
            var (manager, mocks) = GetFixture();

            mocks.Client.Setup(m => m.State)
                .Returns(SoulseekClientStates.Disconnected);

            using (manager)
            {
                await manager.InvokeMethod<Task>("UpdateStatusAsync");
            }

            mocks.ServerConnection.Verify(m => m.WriteAsync(It.IsAny<byte[]>(), It.IsAny<CancellationToken?>()), Times.Never);
        }

        [Trait("Category", "UpdateStatusAsync")]
        [Fact(DisplayName = "UpdateStatusAsync skips update if no change and parent connected")]
        internal async Task UpdateStatusAsync_Skips_Update_If_No_Change_And_Parent_Connected()
        {
            var (manager, mocks) = GetFixture();

            mocks.Client.Setup(m => m.State)
                .Returns(SoulseekClientStates.Connected | SoulseekClientStates.LoggedIn);

            var conn = GetMessageConnectionMock("foo", IPAddress.None, 1);
            conn.Setup(m => m.State)
                .Returns(ConnectionState.Connected);

            using (manager)
            {
                // bit of a hack here, but this is the expected hash on an uninitialized instance
                manager.SetProperty("StatusHash", "BQAAAEcAAAAACAAAAEkAAAD/////CAAAAH4AAAAAAAAACAAAAH8AAAAAAAAACAAAAIEAAAAAAAAABQAAAGQAAAAB");
                manager.SetProperty("ParentConnection", conn.Object);
                await manager.InvokeMethod<Task>("UpdateStatusAsync");
            }

            mocks.ServerConnection.Verify(m => m.WriteAsync(It.IsAny<byte[]>(), It.IsAny<CancellationToken?>()), Times.Never);
        }

        [Trait("Category", "UpdateStatusAsync")]
        [Fact(DisplayName = "UpdateStatusAsync writes payload to server")]
        internal async Task UpdateStatusAsync_Writes_Payload_To_Server()
        {
            var expectedPayload = Convert.FromBase64String("BQAAAEcAAAAACAAAAEkAAAD/////CAAAAH4AAAAAAAAACAAAAH8AAAAAAAAACAAAAIEAAAAAAAAABQAAAGQAAAAB");

            var (manager, mocks) = GetFixture();

            mocks.Client.Setup(m => m.State)
                .Returns(SoulseekClientStates.Connected | SoulseekClientStates.LoggedIn);

            var conn = GetMessageConnectionMock("foo", IPAddress.None, 1);
            conn.Setup(m => m.State)
                .Returns(ConnectionState.Connected);

            using (manager)
            {
                manager.SetProperty("ParentConnection", conn.Object);
                await manager.InvokeMethod<Task>("UpdateStatusAsync");
            }

            mocks.ServerConnection.Verify(m => m.WriteAsync(It.Is<byte[]>(b => b.Matches(expectedPayload)), It.IsAny<CancellationToken?>()), Times.Once);
        }

        [Trait("Category", "UpdateStatusAsync")]
        [Fact(DisplayName = "UpdateStatusAsync broadcasts branch level and root to children")]
        internal async Task UpdateStatusAsync_Broadcasts_Branc_Level_And_Root_To_Children()
        {
            var (manager, mocks) = GetFixture();

            mocks.Client.Setup(m => m.State)
                .Returns(SoulseekClientStates.Connected | SoulseekClientStates.LoggedIn);

            var conn = GetMessageConnectionMock("foo", IPAddress.None, 1);
            conn.Setup(m => m.State)
                .Returns(ConnectionState.Connected);

            var child = GetMessageConnectionMock("child", IPAddress.None, 1);

            var children = new ConcurrentDictionary<string, IMessageConnection>();
            children.TryAdd("child", child.Object);

            using (manager)
            {
                manager.SetProperty("ParentConnection", conn.Object);
                manager.SetProperty("ChildConnections", children);
                await manager.InvokeMethod<Task>("UpdateStatusAsync");
            }

            var expectedBranchLevelBytes = new DistributedBranchLevel(manager.BranchLevel).ToByteArray();
            var expectedBranchRootBytes = new DistributedBranchRoot(manager.BranchRoot ?? string.Empty).ToByteArray();

            child.Verify(m => m.WriteAsync(It.Is<byte[]>(b => b.Matches(expectedBranchLevelBytes)), It.IsAny<CancellationToken?>()), Times.Once);
            child.Verify(m => m.WriteAsync(It.Is<byte[]>(b => b.Matches(expectedBranchRootBytes)), It.IsAny<CancellationToken?>()), Times.Once);
        }

        [Trait("Category", "UpdateStatusAsync")]
        [Fact(DisplayName = "UpdateStatusAsync updates child depth if HasParent")]
        internal async Task UpdateStatusAsync_Updates_Child_Depth_If_HasParent()
        {
            var (manager, mocks) = GetFixture();

            mocks.Client.Setup(m => m.State)
                .Returns(SoulseekClientStates.Connected | SoulseekClientStates.LoggedIn);

            var conn = GetMessageConnectionMock("foo", IPAddress.None, 1);
            conn.Setup(m => m.State)
                .Returns(ConnectionState.Connected);

            using (manager)
            {
                manager.SetProperty("ParentConnection", conn.Object);
                await manager.InvokeMethod<Task>("UpdateStatusAsync");
            }

            var expectedBytes = new DistributedChildDepth(0).ToByteArray();
            conn.Verify(m => m.WriteAsync(It.Is<byte[]>(b => b.Matches(expectedBytes)), It.IsAny<CancellationToken?>()), Times.Once);
        }

        [Trait("Category", "UpdateStatusAsync")]
        [Fact(DisplayName = "UpdateStatusAsync produces status diagnostic on success")]
        internal async Task UpdateStatusAsync_Produces_Status_Diagnostic_On_Success()
        {
            var (manager, mocks) = GetFixture();

            mocks.Client.Setup(m => m.State)
                .Returns(SoulseekClientStates.Connected | SoulseekClientStates.LoggedIn);

            var conn = GetMessageConnectionMock("foo", IPAddress.None, 1);
            conn.Setup(m => m.State)
                .Returns(ConnectionState.Connected);

            using (manager)
            {
                manager.SetProperty("ParentConnection", conn.Object);
                await manager.InvokeMethod<Task>("UpdateStatusAsync");
            }

            mocks.Diagnostic.Verify(m => m.Debug(It.Is<string>(s => s.ContainsInsensitive("Updated distributed status"))), Times.Once);
        }

        [Trait("Category", "UpdateStatusAsync")]
        [Fact(DisplayName = "UpdateStatusAsync produces diagnostic warning on failure")]
        internal async Task UpdateStatusAsync_Produces_Diagnostic_Warning_On_Failure()
        {
            var expectedEx = new Exception(string.Empty);

            var (manager, mocks) = GetFixture();

            mocks.Client.Setup(m => m.State)
                .Returns(SoulseekClientStates.Connected | SoulseekClientStates.LoggedIn);

            mocks.ServerConnection.Setup(m => m.WriteAsync(It.IsAny<byte[]>(), It.IsAny<CancellationToken?>()))
                .Returns(Task.FromException(expectedEx));

            var conn = GetMessageConnectionMock("foo", IPAddress.None, 1);
            conn.Setup(m => m.State)
                .Returns(ConnectionState.Connected);

            using (manager)
            {
                manager.SetProperty("ParentConnection", conn.Object);
                await manager.InvokeMethod<Task>("UpdateStatusAsync");
            }

            mocks.Diagnostic.Verify(m => m.Warning(It.Is<string>(s => s.ContainsInsensitive("Failed to update distributed status")), It.Is<Exception>(e => e == expectedEx)), Times.Once);
        }

        [Trait("Category", "ChildConnection_Disconnected")]
        [Theory(DisplayName = "ChildConnection_Disconnected removes child"), AutoData]
        internal void ChildConnection_Disconnected_Removes_Child(string message)
        {
            var (manager, _) = GetFixture();

            var conn = GetMessageConnectionMock("foo", IPAddress.None, 1);

            var children = new ConcurrentDictionary<string, IMessageConnection>();
            children.TryAdd("foo", conn.Object);

            using (manager)
            {
                manager.SetProperty("ChildConnections", children);

                manager.InvokeMethod("ChildConnection_Disconnected", conn.Object, message);

                Assert.Empty(children);
            }
        }

        [Trait("Category", "ChildConnection_Disconnected")]
        [Theory(DisplayName = "ChildConnection_Disconnected disposes connection"), AutoData]
        internal void ChildConnection_Disconnected_Disposes_Connection(string message)
        {
            var (manager, _) = GetFixture();

            var conn = GetMessageConnectionMock("foo", IPAddress.None, 1);

            var children = new ConcurrentDictionary<string, IMessageConnection>();
            children.TryAdd("foo", conn.Object);

            using (manager)
            {
                manager.SetProperty("ChildConnections", children);

                manager.InvokeMethod("ChildConnection_Disconnected", conn.Object, message);
            }

            conn.Verify(m => m.Dispose(), Times.Once);
        }

        [Trait("Category", "ChildConnection_Disconnected")]
        [Theory(DisplayName = "ChildConnection_Disconnected produces expected diagnostic"), AutoData]
        internal void ChildConnection_Disconnected_Produces_Expected_Diagnostic(string message)
        {
            var (manager, mocks) = GetFixture();

            var conn = GetMessageConnectionMock("foo", IPAddress.None, 1);

            var children = new ConcurrentDictionary<string, IMessageConnection>();
            children.TryAdd("foo", conn.Object);

            using (manager)
            {
                manager.SetProperty("ChildConnections", children);

                manager.InvokeMethod("ChildConnection_Disconnected", conn.Object, message);
            }

            mocks.Diagnostic.Verify(m => m.Debug(It.Is<string>(s => s.ContainsInsensitive("Child foo") && s.ContainsInsensitive("disconnected"))), Times.Once);
        }

        [Trait("Category", "ParentCandidateConnection_Disconnected")]
        [Theory(DisplayName = "ChildConnection_Disconnected disposes connection"), AutoData]
        internal void ParentCandidateConnection_Disconnected_Disposes_Connection(string message)
        {
            var (manager, _) = GetFixture();

            var conn = GetMessageConnectionMock("foo", IPAddress.None, 1);

            using (manager)
            {
                manager.InvokeMethod("ParentCandidateConnection_Disconnected", conn.Object, message);
            }

            conn.Verify(m => m.Dispose(), Times.Once);
        }

        [Trait("Category", "ParentCandidateConnection_Disconnected")]
        [Theory(DisplayName = "ParentCandidateConnection_Disconnected produces expected diagnostic"), AutoData]
        internal void ParentCandidateConnection_Disconnected_Produces_Expected_Diagnostic(string message)
        {
            var (manager, mocks) = GetFixture();

            var conn = GetMessageConnectionMock("foo", IPAddress.None, 1);

            using (manager)
            {
                manager.InvokeMethod("ParentCandidateConnection_Disconnected", conn.Object, message);
            }

            mocks.Diagnostic.Verify(m => m.Debug(It.Is<string>(s => s.ContainsInsensitive("Parent candidate") && s.ContainsInsensitive("disconnected"))), Times.Once);
        }

        [Trait("Category", "GetParentConnectionIndirectAsync")]
        [Theory(DisplayName = "GetParentConnectionIndirectAsync removes solicitation on throw"), AutoData]
        internal async Task GetParentConnectionIndirectAsync_Removes_Solicitation_On_Throw(string username, IPAddress ip, int port)
        {
            var (manager, mocks) = GetFixture(options: new ClientOptions());

            mocks.Waiter.Setup(m => m.Wait<IConnection>(It.IsAny<WaitKey>(), It.IsAny<int?>(), It.IsAny<CancellationToken?>()))
                .Returns(Task.FromException<IConnection>(new Exception()));

            using (manager)
            {
                await Record.ExceptionAsync(() => manager.InvokeMethod<Task<IMessageConnection>>("GetParentConnectionIndirectAsync", username, CancellationToken.None));

                Assert.Empty(manager.PendingSolicitations);
            }
        }

        [Trait("Category", "GetParentConnectionIndirectAsync")]
        [Theory(DisplayName = "GetParentConnectionIndirectAsync returns expected connection"), AutoData]
        internal async Task GetParentConnectionIndirectAsync_Returns_Expected_Connection(string username, IPAddress ip, int port)
        {
            var (manager, mocks) = GetFixture(options: new ClientOptions());

            var conn = GetConnectionMock(ip, port);
            conn.Setup(m => m.HandoffTcpClient())
                .Returns(mocks.TcpClient.Object);

            var msgConn = GetMessageConnectionMock(username, ip, port);

            mocks.ConnectionFactory.Setup(m => m.GetMessageConnection(username, ip, port, It.IsAny<ConnectionOptions>(), It.IsAny<ITcpClient>()))
                .Returns(msgConn.Object);

            mocks.Waiter.Setup(m => m.Wait<IConnection>(It.IsAny<WaitKey>(), It.IsAny<int?>(), It.IsAny<CancellationToken?>()))
                .Returns(Task.FromResult(conn.Object));

            using (manager)
            {
                using (var actualConn = await manager.InvokeMethod<Task<IMessageConnection>>("GetParentConnectionIndirectAsync", username, CancellationToken.None))
                {
                    Assert.Equal(msgConn.Object, actualConn);
                }
            }
        }

        [Trait("Category", "GetParentConnectionIndirectAsync")]
        [Theory(DisplayName = "GetParentConnectionIndirectAsync produces expected diagnostic"), AutoData]
        internal async Task GetParentConnectionIndirectAsync_Produces_Expected_Diagnostic(string username, IPAddress ip, int port)
        {
            var (manager, mocks) = GetFixture(options: new ClientOptions());

            var conn = GetConnectionMock(ip, port);
            conn.Setup(m => m.HandoffTcpClient())
                .Returns(mocks.TcpClient.Object);

            var msgConn = GetMessageConnectionMock(username, ip, port);

            mocks.ConnectionFactory.Setup(m => m.GetMessageConnection(username, ip, port, It.IsAny<ConnectionOptions>(), It.IsAny<ITcpClient>()))
                .Returns(msgConn.Object);

            mocks.Waiter.Setup(m => m.Wait<IConnection>(It.IsAny<WaitKey>(), It.IsAny<int?>(), It.IsAny<CancellationToken?>()))
                .Returns(Task.FromResult(conn.Object));

            using (manager)
            using (var _ = await manager.InvokeMethod<Task<IMessageConnection>>("GetParentConnectionIndirectAsync", username, CancellationToken.None))
            {
                mocks.Diagnostic.Verify(m =>
                    m.Debug(It.Is<string>(s => s.ContainsInsensitive($"Indirect parent candidate connection to {username}"))));
            }
        }

        [Trait("Category", "GetParentConnectionDirectAsync")]
        [Theory(DisplayName = "GetParentConnectionDirectAsync returns expected connection"), AutoData]
        internal async Task GetParentConnectionDirectAsync_Returns_Expected_Connection(string localUser, string username, IPAddress ip, int port)
        {
            var (manager, mocks) = GetFixture(options: new ClientOptions());

            mocks.Client.Setup(m => m.Username)
                .Returns(localUser);

            var conn = GetMessageConnectionMock(username, ip, port);

            mocks.ConnectionFactory.Setup(m => m.GetMessageConnection(username, ip, port, It.IsAny<ConnectionOptions>(), It.IsAny<ITcpClient>()))
                .Returns(conn.Object);

            using (manager)
            using (var actualConn = await manager.InvokeMethod<Task<IMessageConnection>>("GetParentConnectionDirectAsync", username, ip, port, CancellationToken.None))
            {
                Assert.Equal(conn.Object, actualConn);
            }
        }

        [Trait("Category", "GetParentConnectionDirectAsync")]
        [Theory(DisplayName = "GetParentConnectionDirectAsync disposes connection on throw"), AutoData]
        internal async Task GetParentConnectionDirectAsync_Disposes_Connection_On_Throw(string localUser, string username, IPAddress ip, int port)
        {
            Assert.True(false);
        }

        [Trait("Category", "GetParentConnectionDirectAsync")]
        [Theory(DisplayName = "GetParentConnectionDirectAsync connects and writes PeerInit"), AutoData]
        internal async Task GetParentConnectionDirectAsync_Connects_And_Writes_PeerInit(string localUser, string username, IPAddress ip, int port)
        {
            Assert.True(false);
        }

        [Trait("Category", "GetParentConnectionDirectAsync")]
        [Theory(DisplayName = "GetParentConnectionDirectAsync produces expected diagnostic"), AutoData]
        internal async Task GetParentConnectionDirectAsync_Produces_Expected_Diagnostic(string localUser, string username, IPAddress ip, int port)
        {
            Assert.True(false);
        }

        private (DistributedConnectionManager Manager, Mocks Mocks) GetFixture(string username = null, IPAddress ip = null, int port = 0, ClientOptions options = null)
        {
            var mocks = new Mocks(options);

            mocks.ServerConnection.Setup(m => m.Username)
                .Returns(username ?? "username");
            mocks.ServerConnection.Setup(m => m.IPAddress)
                .Returns(ip ?? IPAddress.Parse("0.0.0.0"));
            mocks.ServerConnection.Setup(m => m.Port)
                .Returns(port);

            var handler = new DistributedConnectionManager(
                mocks.Client.Object,
                mocks.ConnectionFactory.Object,
                mocks.Diagnostic.Object);

            return (handler, mocks);
        }

        private Mock<IMessageConnection> GetMessageConnectionMock(string username, IPAddress ip, int port)
        {
            var mock = new Mock<IMessageConnection>();
            mock.Setup(m => m.Username).Returns(username);
            mock.Setup(m => m.IPAddress).Returns(ip);
            mock.Setup(m => m.Port).Returns(port);

            return mock;
        }

        private Mock<IConnection> GetConnectionMock(IPAddress ip, int port)
        {
            var mock = new Mock<IConnection>();
            mock.Setup(m => m.IPAddress)
                .Returns(ip);
            mock.Setup(m => m.Port)
                .Returns(port);

            return mock;
        }

        private class Mocks
        {
            public Mocks(ClientOptions clientOptions = null)
            {
                Client = new Mock<SoulseekClient>(clientOptions)
                {
                    CallBase = true,
                };

                Client.Setup(m => m.ServerConnection).Returns(ServerConnection.Object);
                Client.Setup(m => m.Waiter).Returns(Waiter.Object);
                Client.Setup(m => m.Listener).Returns(Listener.Object);
                Client.Setup(m => m.DistributedMessageHandler).Returns(DistributedMessageHandler.Object);
            }

            public Mock<SoulseekClient> Client { get; }
            public Mock<IMessageConnection> ServerConnection { get; } = new Mock<IMessageConnection>();
            public Mock<IWaiter> Waiter { get; } = new Mock<IWaiter>();
            public Mock<IListener> Listener { get; } = new Mock<IListener>();
            public Mock<IDistributedMessageHandler> DistributedMessageHandler { get; } = new Mock<IDistributedMessageHandler>();
            public Mock<IConnectionFactory> ConnectionFactory { get; } = new Mock<IConnectionFactory>();
            public Mock<IDiagnosticFactory> Diagnostic { get; } = new Mock<IDiagnosticFactory>();
            public Mock<ITcpClient> TcpClient { get; } = new Mock<ITcpClient>();
        }
    }
}
