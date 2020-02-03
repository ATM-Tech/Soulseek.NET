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
    using System.Threading;
    using System.Threading.Tasks;
    using AutoFixture.Xunit2;
    using Moq;
    using Soulseek.Diagnostics;
    using Soulseek.Exceptions;
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
            Assert.Equal(new SoulseekClientOptions().ConcurrentDistributedChildrenLimit, c.ConcurrentChildLimit);
            Assert.False(c.HasParent);
            Assert.Equal((string.Empty, default(IPEndPoint)), c.Parent);
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
        public void ParentConnection_Disconnected_Cleans_Up(string username, IPEndPoint endpoint, string message)
        {
            var c = GetMessageConnectionMock(username, endpoint);

            var (manager, _) = GetFixture();

            using (manager)
            {
                manager.SetProperty("ParentConnection", new Mock<IMessageConnection>().Object);
                manager.SetProperty("BranchLevel", 1);
                manager.SetProperty("BranchRoot", "foo");

                manager.InvokeMethod("ParentConnection_Disconnected", c.Object, new ConnectionDisconnectedEventArgs(message));

                Assert.Null(manager.GetProperty<IMessageConnection>("ParentConnection"));
                Assert.Equal(0, manager.BranchLevel);
                Assert.Equal(string.Empty, manager.BranchRoot);
            }
        }

        [Trait("Category", "AddChildConnectionAsync")]
        [Theory(DisplayName = "AddChildConnectionAsync rejects if over child limit"), AutoData]
        internal async Task AddChildConnectionAsync_Ctpr_Rejects_If_Over_Child_Limit(ConnectToPeerResponse ctpr)
        {
            var (manager, mocks) = GetFixture(options: new SoulseekClientOptions(concurrentDistributedChildrenLimit: 0));

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
            var (manager, mocks) = GetFixture(options: new SoulseekClientOptions(concurrentDistributedChildrenLimit: 0));

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

            var conn = GetMessageConnectionMock(ctpr.Username, ctpr.IPEndPoint);

            mocks.ConnectionFactory.Setup(m => m.GetMessageConnection(ctpr.Username, ctpr.IPEndPoint, It.IsAny<ConnectionOptions>(), It.IsAny<ITcpClient>()))
                .Returns(conn.Object);

            mocks.Waiter.Setup(m => m.Wait<int>(It.IsAny<WaitKey>(), It.IsAny<int?>(), It.IsAny<CancellationToken?>()))
                .Returns(Task.FromResult(1));

            using (manager)
            {
                await manager.AddChildConnectionAsync(ctpr);

                var child = manager.Children.FirstOrDefault();

                Assert.Single(manager.Children);
                Assert.NotEqual(default((string, IPEndPoint)), child);
                Assert.Equal(ctpr.Username, child.Username);
                Assert.Equal(ctpr.IPAddress, child.IPEndPoint.Address);
                Assert.Equal(ctpr.Port, child.IPEndPoint.Port);
            }
        }

        [Trait("Category", "AddChildConnectionAsync")]
        [Theory(DisplayName = "AddChildConnectionAsync disposes connection on throw"), AutoData]
        internal async Task AddChildConnectionAsync_Ctpr_Disposes_Connection_On_Throw(ConnectToPeerResponse ctpr)
        {
            var expectedEx = new Exception("foo");

            var (manager, mocks) = GetFixture();

            var conn = GetMessageConnectionMock(ctpr.Username, ctpr.IPEndPoint);
            conn.Setup(m => m.ConnectAsync(It.IsAny<CancellationToken?>()))
                .Throws(expectedEx);

            mocks.ConnectionFactory.Setup(m => m.GetMessageConnection(ctpr.Username, ctpr.IPEndPoint, It.IsAny<ConnectionOptions>(), It.IsAny<ITcpClient>()))
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

            var conn = GetMessageConnectionMock(ctpr.Username, ctpr.IPEndPoint);

            mocks.ConnectionFactory.Setup(m => m.GetMessageConnection(ctpr.Username, ctpr.IPEndPoint, It.IsAny<ConnectionOptions>(), It.IsAny<ITcpClient>()))
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

            var conn = GetMessageConnectionMock(ctpr.Username, ctpr.IPEndPoint);
            conn.Setup(m => m.ConnectAsync(It.IsAny<CancellationToken?>()))
                .Throws(expectedEx);

            mocks.ConnectionFactory.Setup(m => m.GetMessageConnection(ctpr.Username, ctpr.IPEndPoint, It.IsAny<ConnectionOptions>(), It.IsAny<ITcpClient>()))
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
        internal async Task AddChildConnectionAsync_Rejects_If_Over_Child_Limit(string username, IPEndPoint endpoint)
        {
            var (manager, mocks) = GetFixture(options: new SoulseekClientOptions(concurrentDistributedChildrenLimit: 0));

            mocks.TcpClient.Setup(m => m.RemoteEndPoint)
                .Returns(endpoint);

            using (manager)
            {
                await manager.AddChildConnectionAsync(username, mocks.TcpClient.Object);
            }

            mocks.Diagnostic.Verify(m => m.Debug(It.Is<string>(s => s.Contains("rejected", StringComparison.InvariantCultureIgnoreCase))), Times.Once);
        }

        [Trait("Category", "AddChildConnectionAsync")]
        [Theory(DisplayName = "AddChildConnectionAsync disposes TcpClient on rejection"), AutoData]
        internal async Task AddChildConnectionAsync_Disposes_TcpClient_On_Rejection(string username, IPEndPoint endpoint)
        {
            var (manager, mocks) = GetFixture(options: new SoulseekClientOptions(concurrentDistributedChildrenLimit: 0));

            mocks.TcpClient.Setup(m => m.RemoteEndPoint)
                .Returns(endpoint);

            using (manager)
            {
                await manager.AddChildConnectionAsync(username, mocks.TcpClient.Object);
            }

            mocks.Diagnostic.Verify(m => m.Debug(It.Is<string>(s => s.Contains("rejected", StringComparison.InvariantCultureIgnoreCase))), Times.Once);
            mocks.TcpClient.Verify(m => m.Dispose(), Times.Once);
        }

        [Trait("Category", "AddChildConnectionAsync")]
        [Theory(DisplayName = "AddChildConnectionAsync disposes connection on throw"), AutoData]
        internal async Task AddChildConnectionAsync_Disposes_Connection_On_Throw(string username, IPEndPoint endpoint)
        {
            var expectedEx = new Exception("foo");

            var (manager, mocks) = GetFixture();

            var conn = GetMessageConnectionMock(username, endpoint);
            conn.Setup(m => m.WriteAsync(It.IsAny<byte[]>(), It.IsAny<CancellationToken?>()))
                .Throws(expectedEx);

            mocks.ConnectionFactory.Setup(m => m.GetMessageConnection(username, endpoint, It.IsAny<ConnectionOptions>(), It.IsAny<ITcpClient>()))
                .Returns(conn.Object);

            mocks.TcpClient.Setup(m => m.RemoteEndPoint)
                .Returns(endpoint);

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
        internal async Task AddChildConnectionAsync_Updates_Status_On_Rejection(string username, IPEndPoint endpoint)
        {
            var (manager, mocks) = GetFixture(options: new SoulseekClientOptions(concurrentDistributedChildrenLimit: 0));

            mocks.Client.Setup(m => m.State)
                .Returns(SoulseekClientStates.Connected | SoulseekClientStates.LoggedIn);

            mocks.TcpClient.Setup(m => m.RemoteEndPoint)
                .Returns(endpoint);

            using (manager)
            {
                await manager.AddChildConnectionAsync(username, mocks.TcpClient.Object);
            }

            mocks.ServerConnection.Verify(m => m.WriteAsync(It.IsAny<byte[]>(), It.IsAny<CancellationToken?>()), Times.Once);
        }

        [Trait("Category", "AddChildConnectionAsync")]
        [Theory(DisplayName = "AddChildConnectionAsync adds child on successful connection"), AutoData]
        internal async Task AddChildConnectionAsync_Adds_Child_On_Successful_Connection(string username, IPEndPoint endpoint)
        {
            var (manager, mocks) = GetFixture();

            var conn = GetMessageConnectionMock(username, endpoint);

            mocks.ConnectionFactory.Setup(m => m.GetMessageConnection(username, endpoint, It.IsAny<ConnectionOptions>(), It.IsAny<ITcpClient>()))
                .Returns(conn.Object);

            mocks.TcpClient.Setup(m => m.RemoteEndPoint)
                .Returns(endpoint);

            mocks.Waiter.Setup(m => m.Wait<int>(It.IsAny<WaitKey>(), It.IsAny<int?>(), It.IsAny<CancellationToken?>()))
                .Returns(Task.FromResult(1));

            using (manager)
            {
                await manager.AddChildConnectionAsync(username, mocks.TcpClient.Object);

                var child = manager.Children.FirstOrDefault();

                Assert.Single(manager.Children);
                Assert.NotEqual(default((string, IPEndPoint)), child);
                Assert.Equal(username, child.Username);
                Assert.Equal(endpoint.Address, child.IPEndPoint.Address);
                Assert.Equal(endpoint.Port, child.IPEndPoint.Port);
            }
        }

        [Trait("Category", "AddChildConnectionAsync")]
        [Theory(DisplayName = "AddChildConnectionAsync invokes StartReadingContinuously on successful connection"), AutoData]
        internal async Task AddChildConnectionAsync_Invokes_StartReadingContinuously_On_Successful_Connection(string username, IPEndPoint endpoint)
        {
            var (manager, mocks) = GetFixture();

            var conn = GetMessageConnectionMock(username, endpoint);

            mocks.ConnectionFactory.Setup(m => m.GetMessageConnection(username, endpoint, It.IsAny<ConnectionOptions>(), It.IsAny<ITcpClient>()))
                .Returns(conn.Object);

            mocks.TcpClient.Setup(m => m.RemoteEndPoint)
                .Returns(endpoint);

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
        internal async Task AddChildConnectionAsync_Generates_Expected_Diagnostics_On_Success(string username, IPEndPoint endpoint)
        {
            var (manager, mocks) = GetFixture();

            var conn = GetMessageConnectionMock(username, endpoint);

            mocks.Client.Setup(m => m.State)
                .Returns(SoulseekClientStates.Connected | SoulseekClientStates.LoggedIn);

            mocks.TcpClient.Setup(m => m.RemoteEndPoint)
                .Returns(endpoint);

            mocks.ConnectionFactory.Setup(m => m.GetMessageConnection(username, endpoint, It.IsAny<ConnectionOptions>(), It.IsAny<ITcpClient>()))
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
        internal async Task AddChildConnectionAsync_Generates_Expected_Diagnostics_On_Throw(string username, IPEndPoint endpoint)
        {
            var (manager, mocks) = GetFixture();

            var conn = GetMessageConnectionMock(username, endpoint);
            conn.Setup(m => m.WriteAsync(It.IsAny<byte[]>(), It.IsAny<CancellationToken?>()))
                .Throws(new Exception("foo"));

            mocks.Client.Setup(m => m.State)
                .Returns(SoulseekClientStates.Connected | SoulseekClientStates.LoggedIn);

            mocks.TcpClient.Setup(m => m.RemoteEndPoint)
                .Returns(endpoint);

            mocks.ConnectionFactory.Setup(m => m.GetMessageConnection(username, endpoint, It.IsAny<ConnectionOptions>(), It.IsAny<ITcpClient>()))
                .Returns(conn.Object);

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

            var conn = GetMessageConnectionMock("foo", null);
            conn.Setup(m => m.State)
                .Returns(ConnectionState.Connected);

            using (manager)
            {
                // bit of a hack here, but this is the expected hash on an uninitialized instance
                manager.SetProperty("StatusHash", "BQAAAEcAAAAACAAAAEkAAAD/////CAAAAH4AAAABAAAACAAAAIEAAAAAAAAABQAAAGQAAAAB");
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

            var conn = GetMessageConnectionMock("foo", null);
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
        [Theory(DisplayName = "UpdateStatusAsync broadcasts branch level and root to children"), AutoData]
        internal async Task UpdateStatusAsync_Broadcasts_Branc_Level_And_Root_To_Children(int level, string root)
        {
            var (manager, mocks) = GetFixture();

            mocks.Client.Setup(m => m.State)
                .Returns(SoulseekClientStates.Connected | SoulseekClientStates.LoggedIn);

            var conn = GetMessageConnectionMock("foo", null);
            conn.Setup(m => m.State)
                .Returns(ConnectionState.Connected);

            var child = GetMessageConnectionMock("child", null);

            var children = new ConcurrentDictionary<string, IMessageConnection>();
            children.TryAdd("child", child.Object);

            using (manager)
            {
                manager.SetProperty("ParentConnection", conn.Object);
                manager.SetProperty("ChildConnections", children);
                manager.SetBranchLevel(level);
                manager.SetBranchRoot(root);
                await manager.InvokeMethod<Task>("UpdateStatusAsync");
            }

            var bytes = new List<byte>();
            bytes.AddRange(new DistributedBranchLevel(manager.BranchLevel + 1).ToByteArray());
            bytes.AddRange(new DistributedBranchRoot(manager.BranchRoot).ToByteArray());

            child.Verify(m => m.WriteAsync(It.Is<byte[]>(b => b.Matches(bytes.ToArray())), It.IsAny<CancellationToken?>()), Times.Once);
        }

        [Trait("Category", "UpdateStatusAsync")]
        [Fact(DisplayName = "UpdateStatusAsync updates child depth if HasParent")]
        internal async Task UpdateStatusAsync_Updates_Child_Depth_If_HasParent()
        {
            var (manager, mocks) = GetFixture();

            mocks.Client.Setup(m => m.State)
                .Returns(SoulseekClientStates.Connected | SoulseekClientStates.LoggedIn);

            var conn = GetMessageConnectionMock("foo", null);
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

            var conn = GetMessageConnectionMock("foo", null);
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
        [Fact(DisplayName = "UpdateStatusAsync produces diagnostic warning on failure when connected")]
        internal async Task UpdateStatusAsync_Produces_Diagnostic_Warning_On_Failure_When_Connected()
        {
            var expectedEx = new Exception(string.Empty);

            var (manager, mocks) = GetFixture();

            mocks.Client.Setup(m => m.State)
                .Returns(SoulseekClientStates.Connected | SoulseekClientStates.LoggedIn);

            mocks.ServerConnection.Setup(m => m.WriteAsync(It.IsAny<byte[]>(), It.IsAny<CancellationToken?>()))
                .Returns(Task.FromException(expectedEx));

            var conn = GetMessageConnectionMock("foo", null);
            conn.Setup(m => m.State)
                .Returns(ConnectionState.Connected);

            using (manager)
            {
                manager.SetProperty("ParentConnection", conn.Object);
                await manager.InvokeMethod<Task>("UpdateStatusAsync");
            }

            mocks.Diagnostic.Verify(m => m.Warning(It.Is<string>(s => s.ContainsInsensitive("Failed to update distributed status")), It.Is<Exception>(e => e == expectedEx)), Times.Once);
        }

        [Trait("Category", "UpdateStatusAsync")]
        [Fact(DisplayName = "UpdateStatusAsync produces diagnostic debug on failure when disconnected")]
        internal async Task UpdateStatusAsync_Produces_Diagnostic_Debug_On_Failure_When_Disconnected()
        {
            var expectedEx = new Exception(string.Empty);

            var (manager, mocks) = GetFixture();

            mocks.Client.Setup(m => m.State)
                .Returns(SoulseekClientStates.Connected | SoulseekClientStates.LoggedIn);

            mocks.ServerConnection.Setup(m => m.WriteAsync(It.IsAny<byte[]>(), It.IsAny<CancellationToken?>()))
                .Returns(Task.FromException(expectedEx))
                .Callback(() => mocks.Client.Setup(m => m.State).Returns(SoulseekClientStates.Disconnected));

            var conn = GetMessageConnectionMock("foo", null);
            conn.Setup(m => m.State)
                .Returns(ConnectionState.Connected);

            using (manager)
            {
                manager.SetProperty("ParentConnection", conn.Object);
                await manager.InvokeMethod<Task>("UpdateStatusAsync");
            }

            mocks.Diagnostic.Verify(m => m.Debug(It.Is<string>(s => s.ContainsInsensitive("Failed to update distributed status")), It.Is<Exception>(e => e == expectedEx)), Times.Once);
        }

        [Trait("Category", "ChildConnection_Disconnected")]
        [Theory(DisplayName = "ChildConnection_Disconnected removes child"), AutoData]
        internal void ChildConnection_Disconnected_Removes_Child(string message)
        {
            var (manager, _) = GetFixture();

            var conn = GetMessageConnectionMock("foo", null);

            var children = new ConcurrentDictionary<string, IMessageConnection>();
            children.TryAdd("foo", conn.Object);

            using (manager)
            {
                manager.SetProperty("ChildConnections", children);

                manager.InvokeMethod("ChildConnection_Disconnected", conn.Object, new ConnectionDisconnectedEventArgs(message));

                Assert.Empty(children);
            }
        }

        [Trait("Category", "ChildConnection_Disconnected")]
        [Theory(DisplayName = "ChildConnection_Disconnected disposes connection"), AutoData]
        internal void ChildConnection_Disconnected_Disposes_Connection(string message)
        {
            var (manager, _) = GetFixture();

            var conn = GetMessageConnectionMock("foo", null);

            var children = new ConcurrentDictionary<string, IMessageConnection>();
            children.TryAdd("foo", conn.Object);

            using (manager)
            {
                manager.SetProperty("ChildConnections", children);

                manager.InvokeMethod("ChildConnection_Disconnected", conn.Object, new ConnectionDisconnectedEventArgs(message));
            }

            conn.Verify(m => m.Dispose(), Times.Once);
        }

        [Trait("Category", "ChildConnection_Disconnected")]
        [Theory(DisplayName = "ChildConnection_Disconnected produces expected diagnostic"), AutoData]
        internal void ChildConnection_Disconnected_Produces_Expected_Diagnostic(string message)
        {
            var (manager, mocks) = GetFixture();

            var conn = GetMessageConnectionMock("foo", null);

            var children = new ConcurrentDictionary<string, IMessageConnection>();
            children.TryAdd("foo", conn.Object);

            using (manager)
            {
                manager.SetProperty("ChildConnections", children);

                manager.InvokeMethod("ChildConnection_Disconnected", conn.Object, new ConnectionDisconnectedEventArgs(message));
            }

            mocks.Diagnostic.Verify(m => m.Debug(It.Is<string>(s => s.ContainsInsensitive("Child foo") && s.ContainsInsensitive("disconnected"))), Times.Once);
        }

        [Trait("Category", "ParentCandidateConnection_Disconnected")]
        [Theory(DisplayName = "ChildConnection_Disconnected disposes connection"), AutoData]
        internal void ParentCandidateConnection_Disconnected_Disposes_Connection(string message)
        {
            var (manager, _) = GetFixture();

            var conn = GetMessageConnectionMock("foo", null);

            using (manager)
            {
                manager.InvokeMethod("ParentCandidateConnection_Disconnected", conn.Object, new ConnectionDisconnectedEventArgs(message));
            }

            conn.Verify(m => m.Dispose(), Times.Once);
        }

        [Trait("Category", "ParentCandidateConnection_Disconnected")]
        [Theory(DisplayName = "ParentCandidateConnection_Disconnected produces expected diagnostic"), AutoData]
        internal void ParentCandidateConnection_Disconnected_Produces_Expected_Diagnostic(string message)
        {
            var (manager, mocks) = GetFixture();

            var conn = GetMessageConnectionMock("foo", null);

            using (manager)
            {
                manager.InvokeMethod("ParentCandidateConnection_Disconnected", conn.Object, new ConnectionDisconnectedEventArgs(message));
            }

            mocks.Diagnostic.Verify(m => m.Debug(It.Is<string>(s => s.ContainsInsensitive("Parent candidate") && s.ContainsInsensitive("disconnected"))), Times.Once);
        }

        [Trait("Category", "GetParentConnectionIndirectAsync")]
        [Theory(DisplayName = "GetParentConnectionIndirectAsync removes solicitation on throw"), AutoData]
        internal async Task GetParentConnectionIndirectAsync_Removes_Solicitation_On_Throw(string username)
        {
            var (manager, mocks) = GetFixture(options: new SoulseekClientOptions());

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
        internal async Task GetParentConnectionIndirectAsync_Returns_Expected_Connection(string username, IPEndPoint endpoint)
        {
            var (manager, mocks) = GetFixture(options: new SoulseekClientOptions());

            var conn = GetConnectionMock(endpoint);
            conn.Setup(m => m.HandoffTcpClient())
                .Returns(mocks.TcpClient.Object);

            var msgConn = GetMessageConnectionMock(username, endpoint);

            mocks.ConnectionFactory.Setup(m => m.GetMessageConnection(username, endpoint, It.IsAny<ConnectionOptions>(), It.IsAny<ITcpClient>()))
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
        internal async Task GetParentConnectionIndirectAsync_Produces_Expected_Diagnostic(string username, IPEndPoint endpoint)
        {
            var (manager, mocks) = GetFixture(options: new SoulseekClientOptions());

            var conn = GetConnectionMock(endpoint);
            conn.Setup(m => m.HandoffTcpClient())
                .Returns(mocks.TcpClient.Object);

            var msgConn = GetMessageConnectionMock(username, endpoint);

            mocks.ConnectionFactory.Setup(m => m.GetMessageConnection(username, endpoint, It.IsAny<ConnectionOptions>(), It.IsAny<ITcpClient>()))
                .Returns(msgConn.Object);

            mocks.Waiter.Setup(m => m.Wait<IConnection>(It.IsAny<WaitKey>(), It.IsAny<int?>(), It.IsAny<CancellationToken?>()))
                .Returns(Task.FromResult(conn.Object));

            using (manager)
            using (var actualConn = await manager.InvokeMethod<Task<IMessageConnection>>("GetParentConnectionIndirectAsync", username, CancellationToken.None))
            {
                mocks.Diagnostic.Verify(m =>
                    m.Debug(It.Is<string>(s => s.ContainsInsensitive($"Indirect parent candidate connection to {username}"))));
            }
        }

        [Trait("Category", "GetParentConnectionDirectAsync")]
        [Theory(DisplayName = "GetParentConnectionDirectAsync returns expected connection"), AutoData]
        internal async Task GetParentConnectionDirectAsync_Returns_Expected_Connection(string localUser, string username, IPEndPoint endpoint)
        {
            var (manager, mocks) = GetFixture(options: new SoulseekClientOptions());

            mocks.Client.Setup(m => m.Username)
                .Returns(localUser);

            var conn = GetMessageConnectionMock(username, endpoint);

            mocks.ConnectionFactory.Setup(m => m.GetMessageConnection(username, endpoint, It.IsAny<ConnectionOptions>(), It.IsAny<ITcpClient>()))
                .Returns(conn.Object);

            using (manager)
            using (var actualConn = await manager.InvokeMethod<Task<IMessageConnection>>("GetParentConnectionDirectAsync", username, endpoint, CancellationToken.None))
            {
                Assert.Equal(conn.Object, actualConn);
            }
        }

        [Trait("Category", "GetParentConnectionDirectAsync")]
        [Theory(DisplayName = "GetParentConnectionDirectAsync disposes connection on throw"), AutoData]
        internal async Task GetParentConnectionDirectAsync_Disposes_Connection_On_Throw(string localUser, string username, IPEndPoint endpoint)
        {
            var (manager, mocks) = GetFixture(options: new SoulseekClientOptions());

            mocks.Client.Setup(m => m.Username)
                .Returns(localUser);

            var conn = GetMessageConnectionMock(username, endpoint);
            conn.Setup(m => m.ConnectAsync(It.IsAny<CancellationToken>()))
                .Returns(Task.FromException<Task>(new Exception()));

            mocks.ConnectionFactory.Setup(m => m.GetMessageConnection(username, endpoint, It.IsAny<ConnectionOptions>(), It.IsAny<ITcpClient>()))
                .Returns(conn.Object);

            using (manager)
            {
                await Record.ExceptionAsync(() => manager.InvokeMethod<Task<IMessageConnection>>("GetParentConnectionDirectAsync", username, endpoint, CancellationToken.None));
            }

            conn.Verify(m => m.Dispose(), Times.Once);
        }

        [Trait("Category", "GetParentConnectionDirectAsync")]
        [Theory(DisplayName = "GetParentConnectionDirectAsync connects and writes PeerInit"), AutoData]
        internal async Task GetParentConnectionDirectAsync_Connects_And_Writes_PeerInit(string localUser, string username, IPEndPoint endpoint, int token)
        {
            var expectedMessage = new PeerInit(localUser, Constants.ConnectionType.Distributed, token);

            var (manager, mocks) = GetFixture(options: new SoulseekClientOptions());

            mocks.Client.Setup(m => m.Username)
                .Returns(localUser);
            mocks.Client.Setup(m => m.GetNextToken())
                .Returns(token);

            var conn = GetMessageConnectionMock(username, endpoint);

            mocks.ConnectionFactory.Setup(m => m.GetMessageConnection(username, endpoint, It.IsAny<ConnectionOptions>(), It.IsAny<ITcpClient>()))
                .Returns(conn.Object);

            using (manager)
            using (var actualConn = await manager.InvokeMethod<Task<IMessageConnection>>("GetParentConnectionDirectAsync", username, endpoint, CancellationToken.None))
            {
                // noop
            }

            conn.Verify(m => m.ConnectAsync(It.IsAny<CancellationToken?>()), Times.Once);
            conn.Verify(m => m.WriteAsync(It.Is<byte[]>(b => b.Matches(expectedMessage.ToByteArray())), It.IsAny<CancellationToken?>()), Times.Once);
        }

        [Trait("Category", "GetParentConnectionDirectAsync")]
        [Theory(DisplayName = "GetParentConnectionDirectAsync produces expected diagnostic"), AutoData]
        internal async Task GetParentConnectionDirectAsync_Produces_Expected_Diagnostic(string localUser, string username, IPEndPoint endpoint)
        {
            var (manager, mocks) = GetFixture(options: new SoulseekClientOptions());

            mocks.Client.Setup(m => m.Username)
                .Returns(localUser);

            var conn = GetMessageConnectionMock(username, endpoint);

            mocks.ConnectionFactory.Setup(m => m.GetMessageConnection(username, endpoint, It.IsAny<ConnectionOptions>(), It.IsAny<ITcpClient>()))
                .Returns(conn.Object);

            using (manager)
            using (var actualConn = await manager.InvokeMethod<Task<IMessageConnection>>("GetParentConnectionDirectAsync", username, endpoint, CancellationToken.None))
            {
                Assert.Equal(conn.Object, actualConn);
            }

            mocks.Diagnostic.Verify(m =>
                m.Debug(It.Is<string>(s => s.ContainsInsensitive($"Direct parent candidate connection to {username}"))));
        }

        [Trait("Category", "GetParentConnectionAsync")]
        [Theory(DisplayName = "GetParentConnectionAsync returns direct when direct connects first"), AutoData]
        internal async Task GetParentConnectionAsync_Returns_Direct_When_Direct_Connects_First(string localUser, string username, IPEndPoint endpoint, int branchLevel, string branchRoot)
        {
            var (manager, mocks) = GetFixture(options: new SoulseekClientOptions());

            mocks.Client.Setup(m => m.Username)
                .Returns(localUser);

            var conn = GetMessageConnectionMock(username, endpoint);
            conn.Setup(m => m.Context)
                .Returns(Constants.ConnectionMethod.Direct);

            mocks.ConnectionFactory.Setup(m => m.GetMessageConnection(username, endpoint, It.IsAny<ConnectionOptions>(), It.IsAny<ITcpClient>()))
                .Returns(conn.Object);

            // indirect wait fails
            mocks.Waiter.Setup(m => m.Wait<IConnection>(It.IsAny<WaitKey>(), It.IsAny<int?>(), It.IsAny<CancellationToken?>()))
                .Returns(Task.FromException<IConnection>(new Exception()));

            var branchLevelWaitKey = new WaitKey(Constants.WaitKey.BranchLevelMessage, conn.Object.Context, conn.Object.Key);
            var branchRootWaitKey = new WaitKey(Constants.WaitKey.BranchRootMessage, conn.Object.Context, conn.Object.Key);
            var searchWaitKey = new WaitKey(Constants.WaitKey.SearchRequestMessage, conn.Object.Context, conn.Object.Key);
            mocks.Waiter.Setup(m => m.Wait<int>(branchLevelWaitKey, It.IsAny<int?>(), It.IsAny<CancellationToken?>()))
                .Returns(Task.FromResult(branchLevel));
            mocks.Waiter.Setup(m => m.Wait<string>(branchRootWaitKey, It.IsAny<int?>(), It.IsAny<CancellationToken?>()))
                .Returns(Task.FromResult(branchRoot));
            mocks.Waiter.Setup(m => m.Wait(searchWaitKey, It.IsAny<int?>(), It.IsAny<CancellationToken?>()))
                .Returns(Task.CompletedTask);

            using (manager)
            {
                var actual = await manager.InvokeMethod<Task<(IMessageConnection Connection, int BranchLevel, string BranchRoot)>>("GetParentConnectionAsync", username, endpoint, CancellationToken.None);

                Assert.Equal(conn.Object, actual.Connection);
                Assert.Equal(Constants.ConnectionMethod.Direct, actual.Connection.Context);
                Assert.Equal(branchLevel, actual.BranchLevel);
                Assert.Equal(branchRoot, actual.BranchRoot);
            }
        }

        [Trait("Category", "GetParentConnectionAsync")]
        [Theory(DisplayName = "GetParentConnectionAsync returns indirect when indirect connects first"), AutoData]
        internal async Task GetParentConnectionAsync_Returns_Indirect_When_Inirect_Connects_First(string localUser, string username, IPEndPoint endpoint, int branchLevel, string branchRoot)
        {
            var (manager, mocks) = GetFixture(options: new SoulseekClientOptions());

            mocks.Client.Setup(m => m.Username)
                .Returns(localUser);

            var initConn = GetConnectionMock(endpoint);
            initConn.Setup(m => m.HandoffTcpClient())
                .Returns(mocks.TcpClient.Object);

            var conn = GetMessageConnectionMock(username, endpoint);
            conn.Setup(m => m.Context)
                .Returns(Constants.ConnectionMethod.Indirect);

            // direct fetch fails
            mocks.ConnectionFactory.Setup(m => m.GetMessageConnection(username, endpoint, It.IsAny<ConnectionOptions>(), null))
                .Throws(new Exception());

            // indirect succeeds
            mocks.ConnectionFactory.Setup(m => m.GetMessageConnection(username, endpoint, It.IsAny<ConnectionOptions>(), mocks.TcpClient.Object))
                .Returns(conn.Object);

            mocks.Waiter.Setup(m => m.Wait<IConnection>(It.IsAny<WaitKey>(), It.IsAny<int?>(), It.IsAny<CancellationToken?>()))
                .Returns(Task.FromResult(initConn.Object));

            var branchLevelWaitKey = new WaitKey(Constants.WaitKey.BranchLevelMessage, conn.Object.Context, conn.Object.Key);
            var branchRootWaitKey = new WaitKey(Constants.WaitKey.BranchRootMessage, conn.Object.Context, conn.Object.Key);
            var searchWaitKey = new WaitKey(Constants.WaitKey.SearchRequestMessage, conn.Object.Context, conn.Object.Key);
            mocks.Waiter.Setup(m => m.Wait<int>(branchLevelWaitKey, It.IsAny<int?>(), It.IsAny<CancellationToken?>()))
                .Returns(Task.FromResult(branchLevel));
            mocks.Waiter.Setup(m => m.Wait<string>(branchRootWaitKey, It.IsAny<int?>(), It.IsAny<CancellationToken?>()))
                .Returns(Task.FromResult(branchRoot));
            mocks.Waiter.Setup(m => m.Wait(searchWaitKey, It.IsAny<int?>(), It.IsAny<CancellationToken?>()))
                .Returns(Task.CompletedTask);

            using (manager)
            {
                var actual = await manager.InvokeMethod<Task<(IMessageConnection Connection, int BranchLevel, string BranchRoot)>>("GetParentConnectionAsync", username, endpoint, CancellationToken.None);

                Assert.Equal(conn.Object, actual.Connection);
                Assert.Equal(Constants.ConnectionMethod.Indirect, actual.Connection.Context);
            }

            conn.Verify(m => m.StartReadingContinuously(), Times.Once);
        }

        [Trait("Category", "GetParentConnectionAsync")]
        [Theory(DisplayName = "GetParentConnectionAsync throws when neither direct nor indirect connects"), AutoData]
        internal async Task GetParentConnectionAsync_Throws_When_Neither_Direct_Nor_Indirect_Connects(string localUser, string username, IPEndPoint endpoint)
        {
            var (manager, mocks) = GetFixture(options: new SoulseekClientOptions());

            mocks.Client.Setup(m => m.Username)
                .Returns(localUser);

            var initConn = GetConnectionMock(endpoint);
            initConn.Setup(m => m.HandoffTcpClient())
                .Returns(mocks.TcpClient.Object);

            var conn = GetMessageConnectionMock(username, endpoint);
            conn.Setup(m => m.Context)
                .Returns(Constants.ConnectionMethod.Indirect);

            // direct fetch fails
            mocks.ConnectionFactory.Setup(m => m.GetMessageConnection(username, endpoint, It.IsAny<ConnectionOptions>(), null))
                .Throws(new Exception());

            // indirect fails
            mocks.Waiter.Setup(m => m.Wait<IConnection>(It.IsAny<WaitKey>(), It.IsAny<int?>(), It.IsAny<CancellationToken?>()))
                .Returns(Task.FromException<IConnection>(new Exception()));

            using (manager)
            {
                var ex = await Record.ExceptionAsync(() => manager.InvokeMethod<Task<(IMessageConnection Connection, int BranchLevel, string BranchRoot)>>("GetParentConnectionAsync", username, endpoint, CancellationToken.None));

                Assert.NotNull(ex);
                Assert.IsType<ConnectionException>(ex);
                Assert.True(ex.Message.ContainsInsensitive($"Failed to establish a distributed parent connection to {username}"));
            }
        }

        [Trait("Category", "GetParentConnectionAsync")]
        [Theory(DisplayName = "GetParentConnectionAsync returns expected branch info"), AutoData]
        internal async Task GetParentConnectionAsync_Returns_Expected_Branch_Info(string localUser, string username, IPEndPoint endpoint, int branchLevel, string branchRoot)
        {
            var (manager, mocks) = GetFixture(options: new SoulseekClientOptions());

            mocks.Client.Setup(m => m.Username)
                .Returns(localUser);

            var conn = GetMessageConnectionMock(username, endpoint);
            conn.Setup(m => m.Context)
                .Returns(Constants.ConnectionMethod.Direct);

            mocks.ConnectionFactory.Setup(m => m.GetMessageConnection(username, endpoint, It.IsAny<ConnectionOptions>(), It.IsAny<ITcpClient>()))
                .Returns(conn.Object);

            // indirect wait fails
            mocks.Waiter.Setup(m => m.Wait<IConnection>(It.IsAny<WaitKey>(), It.IsAny<int?>(), It.IsAny<CancellationToken?>()))
                .Returns(Task.FromException<IConnection>(new Exception()));

            var branchLevelWaitKey = new WaitKey(Constants.WaitKey.BranchLevelMessage, conn.Object.Context, conn.Object.Key);
            var branchRootWaitKey = new WaitKey(Constants.WaitKey.BranchRootMessage, conn.Object.Context, conn.Object.Key);
            var searchWaitKey = new WaitKey(Constants.WaitKey.SearchRequestMessage, conn.Object.Context, conn.Object.Key);
            mocks.Waiter.Setup(m => m.Wait<int>(branchLevelWaitKey, It.IsAny<int?>(), It.IsAny<CancellationToken?>()))
                .Returns(Task.FromResult(branchLevel));
            mocks.Waiter.Setup(m => m.Wait<string>(branchRootWaitKey, It.IsAny<int?>(), It.IsAny<CancellationToken?>()))
                .Returns(Task.FromResult(branchRoot));
            mocks.Waiter.Setup(m => m.Wait(searchWaitKey, It.IsAny<int?>(), It.IsAny<CancellationToken?>()))
                .Returns(Task.CompletedTask);

            using (manager)
            {
                var actual = await manager.InvokeMethod<Task<(IMessageConnection Connection, int BranchLevel, string BranchRoot)>>("GetParentConnectionAsync", username, endpoint, CancellationToken.None);

                Assert.Equal(branchLevel, actual.BranchLevel);
                Assert.Equal(branchRoot, actual.BranchRoot);
            }
        }

        [Trait("Category", "GetParentConnectionAsync")]
        [Theory(DisplayName = "GetParentConnectionAsync throws when branch level not received"), AutoData]
        internal async Task GetParentConnectionAsync_Throws_When_Branch_Level_Not_Received(string localUser, string username, IPEndPoint endpoint, string branchRoot)
        {
            var (manager, mocks) = GetFixture(options: new SoulseekClientOptions());

            mocks.Client.Setup(m => m.Username)
                .Returns(localUser);

            var conn = GetMessageConnectionMock(username, endpoint);
            conn.Setup(m => m.Context)
                .Returns(Constants.ConnectionMethod.Direct);

            mocks.ConnectionFactory.Setup(m => m.GetMessageConnection(username, endpoint, It.IsAny<ConnectionOptions>(), It.IsAny<ITcpClient>()))
                .Returns(conn.Object);

            // indirect wait fails
            mocks.Waiter.Setup(m => m.Wait<IConnection>(It.IsAny<WaitKey>(), It.IsAny<int?>(), It.IsAny<CancellationToken?>()))
                .Returns(Task.FromException<IConnection>(new Exception()));

            var branchLevelWaitKey = new WaitKey(Constants.WaitKey.BranchLevelMessage, conn.Object.Context, conn.Object.Key);
            var branchRootWaitKey = new WaitKey(Constants.WaitKey.BranchRootMessage, conn.Object.Context, conn.Object.Key);
            var searchWaitKey = new WaitKey(Constants.WaitKey.SearchRequestMessage, conn.Object.Context, conn.Object.Key);
            mocks.Waiter.Setup(m => m.Wait<int>(branchLevelWaitKey, It.IsAny<int?>(), It.IsAny<CancellationToken?>()))
                .Returns(Task.FromException<int>(new Exception()));
            mocks.Waiter.Setup(m => m.Wait<string>(branchRootWaitKey, It.IsAny<int?>(), It.IsAny<CancellationToken?>()))
                .Returns(Task.FromResult(branchRoot));
            mocks.Waiter.Setup(m => m.Wait(searchWaitKey, It.IsAny<int?>(), It.IsAny<CancellationToken?>()))
                .Returns(Task.CompletedTask);

            using (manager)
            {
                var ex = await Record.ExceptionAsync(() => manager.InvokeMethod<Task<(IMessageConnection Connection, int BranchLevel, string BranchRoot)>>("GetParentConnectionAsync", username, endpoint, CancellationToken.None));

                Assert.NotNull(ex);
                Assert.IsType<ConnectionException>(ex);
                Assert.True(ex.Message.ContainsInsensitive($"Failed to initialize parent connection to {username}"));
            }
        }

        [Trait("Category", "GetParentConnectionAsync")]
        [Theory(DisplayName = "GetParentConnectionAsync throws when branch root not received"), AutoData]
        internal async Task GetParentConnectionAsync_Throws_When_Branch_Root_Not_Received(string localUser, string username, IPEndPoint endpoint, int branchLevel)
        {
            var (manager, mocks) = GetFixture(options: new SoulseekClientOptions());

            mocks.Client.Setup(m => m.Username)
                .Returns(localUser);

            var conn = GetMessageConnectionMock(username, endpoint);
            conn.Setup(m => m.Context)
                .Returns(Constants.ConnectionMethod.Direct);

            mocks.ConnectionFactory.Setup(m => m.GetMessageConnection(username, endpoint, It.IsAny<ConnectionOptions>(), It.IsAny<ITcpClient>()))
                .Returns(conn.Object);

            // indirect wait fails
            mocks.Waiter.Setup(m => m.Wait<IConnection>(It.IsAny<WaitKey>(), It.IsAny<int?>(), It.IsAny<CancellationToken?>()))
                .Returns(Task.FromException<IConnection>(new Exception()));

            var branchLevelWaitKey = new WaitKey(Constants.WaitKey.BranchLevelMessage, conn.Object.Context, conn.Object.Key);
            var branchRootWaitKey = new WaitKey(Constants.WaitKey.BranchRootMessage, conn.Object.Context, conn.Object.Key);
            var searchWaitKey = new WaitKey(Constants.WaitKey.SearchRequestMessage, conn.Object.Context, conn.Object.Key);
            mocks.Waiter.Setup(m => m.Wait<int>(branchLevelWaitKey, It.IsAny<int?>(), It.IsAny<CancellationToken?>()))
                .Returns(Task.FromResult(branchLevel));
            mocks.Waiter.Setup(m => m.Wait<string>(branchRootWaitKey, It.IsAny<int?>(), It.IsAny<CancellationToken?>()))
                .Returns(Task.FromException<string>(new Exception()));
            mocks.Waiter.Setup(m => m.Wait(searchWaitKey, It.IsAny<int?>(), It.IsAny<CancellationToken?>()))
                .Returns(Task.CompletedTask);

            using (manager)
            {
                var ex = await Record.ExceptionAsync(() => manager.InvokeMethod<Task<(IMessageConnection Connection, int BranchLevel, string BranchRoot)>>("GetParentConnectionAsync", username, endpoint, CancellationToken.None));

                Assert.NotNull(ex);
                Assert.IsType<ConnectionException>(ex);
                Assert.True(ex.Message.ContainsInsensitive($"Failed to initialize parent connection to {username}"));
            }
        }

        [Trait("Category", "GetParentConnectionAsync")]
        [Theory(DisplayName = "GetParentConnectionAsync throws when initial search request not received"), AutoData]
        internal async Task GetParentConnectionAsync_Throws_When_Initial_Search_Request_Not_Received(string localUser, string username, IPEndPoint endpoint, int branchLevel, string branchRoot)
        {
            var (manager, mocks) = GetFixture(options: new SoulseekClientOptions());

            mocks.Client.Setup(m => m.Username)
                .Returns(localUser);

            var conn = GetMessageConnectionMock(username, endpoint);
            conn.Setup(m => m.Context)
                .Returns(Constants.ConnectionMethod.Direct);

            mocks.ConnectionFactory.Setup(m => m.GetMessageConnection(username, endpoint, It.IsAny<ConnectionOptions>(), It.IsAny<ITcpClient>()))
                .Returns(conn.Object);

            // indirect wait fails
            mocks.Waiter.Setup(m => m.Wait<IConnection>(It.IsAny<WaitKey>(), It.IsAny<int?>(), It.IsAny<CancellationToken?>()))
                .Returns(Task.FromException<IConnection>(new Exception()));

            var branchLevelWaitKey = new WaitKey(Constants.WaitKey.BranchLevelMessage, conn.Object.Context, conn.Object.Key);
            var branchRootWaitKey = new WaitKey(Constants.WaitKey.BranchRootMessage, conn.Object.Context, conn.Object.Key);
            var searchWaitKey = new WaitKey(Constants.WaitKey.SearchRequestMessage, conn.Object.Context, conn.Object.Key);
            mocks.Waiter.Setup(m => m.Wait<int>(branchLevelWaitKey, It.IsAny<int?>(), It.IsAny<CancellationToken?>()))
                .Returns(Task.FromResult(branchLevel));
            mocks.Waiter.Setup(m => m.Wait<string>(branchRootWaitKey, It.IsAny<int?>(), It.IsAny<CancellationToken?>()))
                .Returns(Task.FromResult(branchRoot));
            mocks.Waiter.Setup(m => m.Wait(searchWaitKey, It.IsAny<int?>(), It.IsAny<CancellationToken?>()))
                .Returns(Task.FromException(new Exception()));

            using (manager)
            {
                var ex = await Record.ExceptionAsync(() => manager.InvokeMethod<Task<(IMessageConnection Connection, int BranchLevel, string BranchRoot)>>("GetParentConnectionAsync", username, endpoint, CancellationToken.None));

                Assert.NotNull(ex);
                Assert.IsType<ConnectionException>(ex);
                Assert.True(ex.Message.ContainsInsensitive($"Failed to initialize parent connection to {username}"));
            }
        }

        [Trait("Category", "GetParentConnectionAsync")]
        [Theory(DisplayName = "GetParentConnectionAsync disconnects and disposes connection when init fails"), AutoData]
        internal async Task GetParentConnectionAsync_Disconnects_And_Disposes_Connection_When_Init_Fails(string localUser, string username, IPEndPoint endpoint, string branchRoot)
        {
            var (manager, mocks) = GetFixture(options: new SoulseekClientOptions());

            mocks.Client.Setup(m => m.Username)
                .Returns(localUser);

            var conn = GetMessageConnectionMock(username, endpoint);
            conn.Setup(m => m.Context)
                .Returns(Constants.ConnectionMethod.Direct);

            mocks.ConnectionFactory.Setup(m => m.GetMessageConnection(username, endpoint, It.IsAny<ConnectionOptions>(), It.IsAny<ITcpClient>()))
                .Returns(conn.Object);

            // indirect wait fails
            mocks.Waiter.Setup(m => m.Wait<IConnection>(It.IsAny<WaitKey>(), It.IsAny<int?>(), It.IsAny<CancellationToken?>()))
                .Returns(Task.FromException<IConnection>(new Exception()));

            var branchLevelWaitKey = new WaitKey(Constants.WaitKey.BranchLevelMessage, conn.Object.Context, conn.Object.Key);
            var branchRootWaitKey = new WaitKey(Constants.WaitKey.BranchRootMessage, conn.Object.Context, conn.Object.Key);
            var searchWaitKey = new WaitKey(Constants.WaitKey.SearchRequestMessage, conn.Object.Context, conn.Object.Key);
            mocks.Waiter.Setup(m => m.Wait<int>(branchLevelWaitKey, It.IsAny<int?>(), It.IsAny<CancellationToken?>()))
                .Returns(Task.FromException<int>(new Exception()));
            mocks.Waiter.Setup(m => m.Wait<string>(branchRootWaitKey, It.IsAny<int?>(), It.IsAny<CancellationToken?>()))
                .Returns(Task.FromResult(branchRoot));
            mocks.Waiter.Setup(m => m.Wait(searchWaitKey, It.IsAny<int?>(), It.IsAny<CancellationToken?>()))
                .Returns(Task.CompletedTask);

            using (manager)
            {
                var ex = await Record.ExceptionAsync(() => manager.InvokeMethod<Task<(IMessageConnection Connection, int BranchLevel, string BranchRoot)>>("GetParentConnectionAsync", username, endpoint, CancellationToken.None));

                Assert.NotNull(ex);
                Assert.IsType<ConnectionException>(ex);
                Assert.True(ex.Message.ContainsInsensitive($"Failed to initialize parent connection to {username}"));
            }

            conn.Verify(m => m.Disconnect("One or more required messages was not received.", It.IsAny<Exception>()), Times.Once);
            conn.Verify(m => m.Dispose(), Times.Once);
        }

        [Trait("Category", "GetParentConnectionAsync")]
        [Theory(DisplayName = "GetParentConnectionAsync produces expected diagnostic on success"), AutoData]
        internal async Task GetParentConnectionAsync_Produces_Expected_Diagnostic_On_Success(string localUser, string username, IPEndPoint endpoint, int branchLevel, string branchRoot)
        {
            var (manager, mocks) = GetFixture(options: new SoulseekClientOptions());

            mocks.Client.Setup(m => m.Username)
                .Returns(localUser);

            var initConn = GetConnectionMock(endpoint);
            initConn.Setup(m => m.HandoffTcpClient())
                .Returns(mocks.TcpClient.Object);

            var conn = GetMessageConnectionMock(username, endpoint);
            conn.Setup(m => m.Context)
                .Returns(Constants.ConnectionMethod.Indirect);

            // direct fetch fails
            mocks.ConnectionFactory.Setup(m => m.GetMessageConnection(username, endpoint, It.IsAny<ConnectionOptions>(), null))
                .Throws(new Exception());

            // indirect succeeds
            mocks.ConnectionFactory.Setup(m => m.GetMessageConnection(username, endpoint, It.IsAny<ConnectionOptions>(), mocks.TcpClient.Object))
                .Returns(conn.Object);

            mocks.Waiter.Setup(m => m.Wait<IConnection>(It.IsAny<WaitKey>(), It.IsAny<int?>(), It.IsAny<CancellationToken?>()))
                .Returns(Task.FromResult(initConn.Object));

            var branchLevelWaitKey = new WaitKey(Constants.WaitKey.BranchLevelMessage, conn.Object.Context, conn.Object.Key);
            var branchRootWaitKey = new WaitKey(Constants.WaitKey.BranchRootMessage, conn.Object.Context, conn.Object.Key);
            var searchWaitKey = new WaitKey(Constants.WaitKey.SearchRequestMessage, conn.Object.Context, conn.Object.Key);
            mocks.Waiter.Setup(m => m.Wait<int>(branchLevelWaitKey, It.IsAny<int?>(), It.IsAny<CancellationToken?>()))
                .Returns(Task.FromResult(branchLevel));
            mocks.Waiter.Setup(m => m.Wait<string>(branchRootWaitKey, It.IsAny<int?>(), It.IsAny<CancellationToken?>()))
                .Returns(Task.FromResult(branchRoot));
            mocks.Waiter.Setup(m => m.Wait(searchWaitKey, It.IsAny<int?>(), It.IsAny<CancellationToken?>()))
                .Returns(Task.CompletedTask);

            using (manager)
            {
                await manager.InvokeMethod<Task<(IMessageConnection Connection, int BranchLevel, string BranchRoot)>>("GetParentConnectionAsync", username, endpoint, CancellationToken.None);
            }

            mocks.Diagnostic.Verify(m => m.Debug($"{conn.Object.Context} Parent candidate connection to {username} ({endpoint}) established.  Waiting for branch information and first SearchRequest message"), Times.Once);
            mocks.Diagnostic.Verify(m => m.Debug($"Received branch level {branchLevel}, root {branchRoot} and first search request from {username} ({endpoint})"), Times.Once);
        }

        [Trait("Category", "AddParentConnectionAsync")]
        [Fact(DisplayName = "AddParentConnectionAsync returns if HasParent")]
        internal async Task AddParentConnectionAsync_Returns_If_HasParent()
        {
            var (manager, mocks) = GetFixture();

            var candidates = new List<(string Username, IPEndPoint IPEndPoint)>
            {
                ("foo", new IPEndPoint(IPAddress.None, 1)),
            };

            var parent = new Mock<IMessageConnection>();
            parent.Setup(m => m.State)
                .Returns(ConnectionState.Connected);

            using (manager)
            {
                manager.SetProperty("ParentConnection", parent.Object);
                await manager.AddParentConnectionAsync(candidates);
            }

            mocks.Diagnostic.Verify(m => m.Warning(It.IsAny<string>(), It.IsAny<Exception>()), Times.Never);
        }

        [Trait("Category", "AddParentConnectionAsync")]
        [Fact(DisplayName = "AddParentConnectionAsync returns if ParentCandidates is empty")]
        internal async Task AddParentConnectionAsync_Returns_If_ParentCandidates_Is_Empty()
        {
            var (manager, mocks) = GetFixture();

            var candidates = new List<(string Username, IPEndPoint IPEndPoint)>();

            using (manager)
            {
                await manager.AddParentConnectionAsync(candidates);
            }

            mocks.Diagnostic.Verify(m => m.Warning(It.IsAny<string>(), It.IsAny<Exception>()), Times.Never);
        }

        [Trait("Category", "AddParentConnectionAsync")]
        [Fact(DisplayName = "AddParentConnectionAsync produces warning diagnostic and updates status if no candidates connect")]
        internal async Task AddParentConnectionAsync_Produces_Warning_Diagnostic_And_Updates_Status_If_No_Candidates_Connect()
        {
            var (manager, mocks) = GetFixture();

            var candidates = new List<(string Username, IPEndPoint IPEndPoint)>
            {
                ("foo", new IPEndPoint(IPAddress.None, 1)),
                ("bar", new IPEndPoint(IPAddress.None, 2)),
            };

            using (manager)
            {
                var ex = await Record.ExceptionAsync(() => manager.AddParentConnectionAsync(candidates));

                Assert.NotNull(ex);
                Assert.IsType<ConnectionException>(ex);
            }

            mocks.Diagnostic.Verify(m => m.Warning("Failed to connect to any of the distributed parent candidates.", It.IsAny<Exception>()), Times.Once);
        }

        [Trait("Category", "AddParentConnectionAsync")]
        [Theory(DisplayName = "AddParentConnectionAsync sets Parent to successful connection"), AutoData]
        internal async Task AddParentConnectionAsync_Sets_Parent_To_Successful_Connection(string localUser, string username1, IPEndPoint endpoint1, string username2, IPEndPoint endpoint2)
        {
            var (manager, mocks) = GetFixture();

            var candidates = new List<(string Username, IPEndPoint IPEndPoint)>
            {
                (username1, endpoint1),
                (username2, endpoint2),
            };

            mocks.Client.Setup(m => m.Username)
                .Returns(localUser);

            // mocks for connection #1
            var conn1 = GetMessageConnectionMock(username1, endpoint1);
            conn1.Setup(m => m.Context)
                .Returns(Constants.ConnectionMethod.Direct);

            mocks.ConnectionFactory.Setup(m => m.GetMessageConnection(username1, endpoint1, It.IsAny<ConnectionOptions>(), It.IsAny<ITcpClient>()))
                .Returns(conn1.Object);

            // mocks for connection #2
            var conn2 = GetMessageConnectionMock(username2, endpoint1);
            conn2.Setup(m => m.Context)
                .Returns(Constants.ConnectionMethod.Direct);

            mocks.ConnectionFactory.Setup(m => m.GetMessageConnection(username2, endpoint2, It.IsAny<ConnectionOptions>(), It.IsAny<ITcpClient>()))
                .Returns(conn2.Object);

            // message mocks, to allow either connection to be established fully
            var branchLevelWaitKey1 = new WaitKey(Constants.WaitKey.BranchLevelMessage, conn1.Object.Context, conn1.Object.Key);
            var branchRootWaitKey1 = new WaitKey(Constants.WaitKey.BranchRootMessage, conn1.Object.Context, conn1.Object.Key);
            var searchWaitKey1 = new WaitKey(Constants.WaitKey.SearchRequestMessage, conn1.Object.Context, conn1.Object.Key);
            mocks.Waiter.Setup(m => m.Wait<int>(branchLevelWaitKey1, It.IsAny<int?>(), It.IsAny<CancellationToken?>()))
                .Returns(Task.FromResult(0));
            mocks.Waiter.Setup(m => m.Wait<string>(branchRootWaitKey1, It.IsAny<int?>(), It.IsAny<CancellationToken?>()))
                .Returns(Task.FromResult("foo"));
            mocks.Waiter.Setup(m => m.Wait(searchWaitKey1, It.IsAny<int?>(), It.IsAny<CancellationToken?>()))
                .Returns(Task.CompletedTask);

            var branchLevelWaitKey2 = new WaitKey(Constants.WaitKey.BranchLevelMessage, conn2.Object.Context, conn2.Object.Key);
            var branchRootWaitKey2 = new WaitKey(Constants.WaitKey.BranchRootMessage, conn2.Object.Context, conn2.Object.Key);
            var searchWaitKey2 = new WaitKey(Constants.WaitKey.SearchRequestMessage, conn2.Object.Context, conn2.Object.Key);
            mocks.Waiter.Setup(m => m.Wait<int>(branchLevelWaitKey2, It.IsAny<int?>(), It.IsAny<CancellationToken?>()))
                .Returns(Task.FromResult(0));
            mocks.Waiter.Setup(m => m.Wait<string>(branchRootWaitKey2, It.IsAny<int?>(), It.IsAny<CancellationToken?>()))
                .Returns(Task.FromResult("foo"));
            mocks.Waiter.Setup(m => m.Wait(searchWaitKey2, It.IsAny<int?>(), It.IsAny<CancellationToken?>()))
                .Returns(Task.Delay(5000)); // ensure conn1 completes first

            using (manager)
            {
                await manager.AddParentConnectionAsync(candidates);

                Assert.Equal(conn1.Object.Username, manager.Parent.Username);
                Assert.Equal(conn1.Object.IPEndPoint.Address, manager.Parent.IPEndPoint.Address);
                Assert.Equal(conn1.Object.IPEndPoint.Port, manager.Parent.IPEndPoint.Port);
            }
        }

        [Trait("Category", "AddParentConnectionAsync")]
        [Theory(DisplayName = "AddParentConnectionAsync disposes unselected candidates"), AutoData]
        internal async Task AddParentConnectionAsync_Disposes_Unselected_Candidates(string localUser, string username1, IPEndPoint endpoint1, string username2, IPEndPoint endpoint2)
        {
            var (manager, mocks) = GetFixture();

            var candidates = new List<(string Username, IPEndPoint IPEndPoint)>
            {
                (username1, endpoint1),
                (username2, endpoint2),
            };

            mocks.Client.Setup(m => m.Username)
                .Returns(localUser);

            // mocks for connection #1
            var conn1 = GetMessageConnectionMock(username1, endpoint1);
            conn1.Setup(m => m.Context)
                .Returns(Constants.ConnectionMethod.Direct);

            mocks.ConnectionFactory.Setup(m => m.GetMessageConnection(username1, endpoint1, It.IsAny<ConnectionOptions>(), It.IsAny<ITcpClient>()))
                .Returns(conn1.Object);

            // mocks for connection #2
            var conn2 = GetMessageConnectionMock(username2, endpoint2);
            conn2.Setup(m => m.Context)
                .Returns(Constants.ConnectionMethod.Direct);

            mocks.ConnectionFactory.Setup(m => m.GetMessageConnection(username2, endpoint2, It.IsAny<ConnectionOptions>(), It.IsAny<ITcpClient>()))
                .Returns(conn2.Object);

            // message mocks, to allow either connection to be established fully
            var branchLevelWaitKey1 = new WaitKey(Constants.WaitKey.BranchLevelMessage, conn1.Object.Context, conn1.Object.Key);
            var branchRootWaitKey1 = new WaitKey(Constants.WaitKey.BranchRootMessage, conn1.Object.Context, conn1.Object.Key);
            var searchWaitKey1 = new WaitKey(Constants.WaitKey.SearchRequestMessage, conn1.Object.Context, conn1.Object.Key);
            mocks.Waiter.Setup(m => m.Wait<int>(branchLevelWaitKey1, It.IsAny<int?>(), It.IsAny<CancellationToken?>()))
                .Returns(Task.FromResult(0));
            mocks.Waiter.Setup(m => m.Wait<string>(branchRootWaitKey1, It.IsAny<int?>(), It.IsAny<CancellationToken?>()))
                .Returns(Task.FromResult("foo"));
            mocks.Waiter.Setup(m => m.Wait(searchWaitKey1, It.IsAny<int?>(), It.IsAny<CancellationToken?>()))
                .Returns(Task.CompletedTask);

            var branchLevelWaitKey2 = new WaitKey(Constants.WaitKey.BranchLevelMessage, conn2.Object.Context, conn2.Object.Key);
            var branchRootWaitKey2 = new WaitKey(Constants.WaitKey.BranchRootMessage, conn2.Object.Context, conn2.Object.Key);
            var searchWaitKey2 = new WaitKey(Constants.WaitKey.SearchRequestMessage, conn2.Object.Context, conn2.Object.Key);
            mocks.Waiter.Setup(m => m.Wait<int>(branchLevelWaitKey2, It.IsAny<int?>(), It.IsAny<CancellationToken?>()))
                .Returns(Task.FromResult(0));
            mocks.Waiter.Setup(m => m.Wait<string>(branchRootWaitKey2, It.IsAny<int?>(), It.IsAny<CancellationToken?>()))
                .Returns(Task.FromResult("foo"));
            mocks.Waiter.Setup(m => m.Wait(searchWaitKey2, It.IsAny<int?>(), It.IsAny<CancellationToken?>()))
                .Returns(Task.Delay(5000)); // ensure conn1 completes first

            using (manager)
            {
                await manager.AddParentConnectionAsync(candidates);
            }

            conn2.Verify(m => m.Dispose(), Times.Once);
        }

        [Trait("Category", "AddParentConnectionAsync")]
        [Theory(DisplayName = "AddParentConnectionAsync produces expected diagnostics on connect"), AutoData]
        internal async Task AddParentConnectionAsync_Produces_Expected_Diagnostic_On_Connect(string localUser, string username1, IPEndPoint endpoint1, string username2, IPEndPoint endpoint2)
        {
            var (manager, mocks) = GetFixture();

            var candidates = new List<(string Username, IPEndPoint IPEndPoint)>
            {
                (username1, endpoint1),
                (username2, endpoint2),
            };

            mocks.Client.Setup(m => m.Username)
                .Returns(localUser);

            // mocks for connection #1
            var conn1 = GetMessageConnectionMock(username1, endpoint1);
            conn1.Setup(m => m.Context)
                .Returns(Constants.ConnectionMethod.Direct);

            mocks.ConnectionFactory.Setup(m => m.GetMessageConnection(username1, endpoint1, It.IsAny<ConnectionOptions>(), It.IsAny<ITcpClient>()))
                .Returns(conn1.Object);

            // mocks for connection #2
            var conn2 = GetMessageConnectionMock(username2, endpoint2);
            conn2.Setup(m => m.Context)
                .Returns(Constants.ConnectionMethod.Direct);

            mocks.ConnectionFactory.Setup(m => m.GetMessageConnection(username2, endpoint2, It.IsAny<ConnectionOptions>(), It.IsAny<ITcpClient>()))
                .Returns(conn2.Object);

            // message mocks, to allow either connection to be established fully
            var branchLevelWaitKey1 = new WaitKey(Constants.WaitKey.BranchLevelMessage, conn1.Object.Context, conn1.Object.Key);
            var branchRootWaitKey1 = new WaitKey(Constants.WaitKey.BranchRootMessage, conn1.Object.Context, conn1.Object.Key);
            var searchWaitKey1 = new WaitKey(Constants.WaitKey.SearchRequestMessage, conn1.Object.Context, conn1.Object.Key);
            mocks.Waiter.Setup(m => m.Wait<int>(branchLevelWaitKey1, It.IsAny<int?>(), It.IsAny<CancellationToken?>()))
                .Returns(Task.FromResult(0));
            mocks.Waiter.Setup(m => m.Wait<string>(branchRootWaitKey1, It.IsAny<int?>(), It.IsAny<CancellationToken?>()))
                .Returns(Task.FromResult("foo"));
            mocks.Waiter.Setup(m => m.Wait(searchWaitKey1, It.IsAny<int?>(), It.IsAny<CancellationToken?>()))
                .Returns(Task.CompletedTask);

            var branchLevelWaitKey2 = new WaitKey(Constants.WaitKey.BranchLevelMessage, conn2.Object.Context, conn2.Object.Key);
            var branchRootWaitKey2 = new WaitKey(Constants.WaitKey.BranchRootMessage, conn2.Object.Context, conn2.Object.Key);
            var searchWaitKey2 = new WaitKey(Constants.WaitKey.SearchRequestMessage, conn2.Object.Context, conn2.Object.Key);
            mocks.Waiter.Setup(m => m.Wait<int>(branchLevelWaitKey2, It.IsAny<int?>(), It.IsAny<CancellationToken?>()))
                .Returns(Task.FromResult(0));
            mocks.Waiter.Setup(m => m.Wait<string>(branchRootWaitKey2, It.IsAny<int?>(), It.IsAny<CancellationToken?>()))
                .Returns(Task.FromResult("foo"));
            mocks.Waiter.Setup(m => m.Wait(searchWaitKey2, It.IsAny<int?>(), It.IsAny<CancellationToken?>()))
                .Returns(Task.Delay(5000)); // ensure conn1 completes first

            using (manager)
            {
                await manager.AddParentConnectionAsync(candidates);
            }

            mocks.Diagnostic.Verify(m => m.Info(It.Is<string>(s => s == $"Attempting to select a new parent connection from {candidates.Count} candidates")), Times.Once);
            mocks.Diagnostic.Verify(m => m.Info(It.Is<string>(s => s == $"Adopted parent {conn1.Object.Username} ({conn1.Object.IPEndPoint})")), Times.Once);
        }

        private (DistributedConnectionManager Manager, Mocks Mocks) GetFixture(string username = null, IPEndPoint endpoint = null, SoulseekClientOptions options = null)
        {
            var mocks = new Mocks(options);

            mocks.ServerConnection.Setup(m => m.Username)
                .Returns(username ?? "username");
            mocks.ServerConnection.Setup(m => m.IPEndPoint)
                .Returns(endpoint ?? new IPEndPoint(IPAddress.Parse("0.0.0.0"), 0));

            var handler = new DistributedConnectionManager(
                mocks.Client.Object,
                mocks.ConnectionFactory.Object,
                mocks.Diagnostic.Object);

            return (handler, mocks);
        }

        private Mock<IMessageConnection> GetMessageConnectionMock(string username, IPEndPoint endpoint)
        {
            var mock = new Mock<IMessageConnection>();
            mock.Setup(m => m.Username).Returns(username);
            mock.Setup(m => m.IPEndPoint).Returns(endpoint ?? new IPEndPoint(IPAddress.None, 0));

            return mock;
        }

        private Mock<IConnection> GetConnectionMock(IPEndPoint endpoint)
        {
            var mock = new Mock<IConnection>();
            mock.Setup(m => m.IPEndPoint)
                .Returns(endpoint);

            return mock;
        }

        private class Mocks
        {
            public Mocks(SoulseekClientOptions clientOptions = null)
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
