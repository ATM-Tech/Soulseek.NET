﻿// <copyright file="ConnectionTests.cs" company="JP Dillingham">
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

namespace Soulseek.Tests.Unit.Network.Tcp
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Net;
    using System.Net.Sockets;
    using System.Threading;
    using System.Threading.Tasks;
    using AutoFixture.Xunit2;
    using Moq;
    using Soulseek.Exceptions;
    using Soulseek.Network.Tcp;
    using Xunit;
    using Xunit.Abstractions;

    public class ConnectionTests
    {
        private readonly Action<string> output;

        public ConnectionTests(ITestOutputHelper outputHelper)
        {
            output = (s) => outputHelper.WriteLine(s);
        }

        [Trait("Category", "Instantiation")]
        [Fact(DisplayName = "Instantiates properly")]
        public void Instantiates_Properly()
        {
            Connection c = null;

            var ip = new IPAddress(0x0);
            var port = 1;

            var ex = Record.Exception(() => c = new Connection(ip, port));

            Assert.Null(ex);
            Assert.NotNull(c);

            Assert.Equal(ip, c.IPAddress);
            Assert.Equal(port, c.Port);
            Assert.Equal(new ConnectionKey(ip, port), c.Key);
            Assert.Equal(ConnectionState.Pending, c.State);
            Assert.Null(c.Context);
        }

        [Trait("Category", "Instantiation")]
        [Fact(DisplayName = "Instantiates with given options")]
        public void Instantiates_With_Given_Options()
        {
            var ip = new IPAddress(0x0);
            var port = 1;

            var options = new ConnectionOptions(1, 1, 1);

            using (var c = new Connection(ip, port, options))
            {
                Assert.Equal(options, c.Options);
            }
        }

        [Trait("Category", "Instantiation")]
        [Fact(DisplayName = "Instantiates with given TcpClient")]
        public void Instantiates_With_Given_TcpClient()
        {
            var ip = new IPAddress(0x0);
            var port = 1;

            using (var socket = new Socket(SocketType.Stream, ProtocolType.IP))
            {
                var t = new Mock<ITcpClient>();
                t.Setup(m => m.Client).Returns(socket);

                using (var c = new Connection(ip, port, tcpClient: t.Object))
                {
                    var ct = c.GetProperty<ITcpClient>("TcpClient");

                    Assert.Equal(t.Object, ct);
                }
            }
        }

        [Trait("Category", "Dispose")]
        [Fact(DisplayName = "Disposes without throwing")]
        public void Disposes_Without_Throwing()
        {
            var ip = new IPAddress(0x0);
            var port = 1;

            using (var c = new Connection(ip, port))
            {
                var ex = Record.Exception(() => c.Dispose());

                Assert.Null(ex);
            }
        }

        [Trait("Category", "Disconnect")]
        [Fact(DisplayName = "Disconnects when disconnected without throwing")]
        public void Disconnects_When_Not_Connected_Without_Throwing()
        {
            var ip = new IPAddress(0x0);
            var port = 1;

            using (var c = new Connection(ip, port))
            {
                c.SetProperty("State", ConnectionState.Disconnected);

                var ex = Record.Exception(() => c.Disconnect());

                Assert.Null(ex);
                Assert.Equal(ConnectionState.Disconnected, c.State);
            }
        }

        [Trait("Category", "Disconnect")]
        [Fact(DisplayName = "Disconnects when not disconnected")]
        public void Disconnects_When_Not_Disconnected_Without_Throwing()
        {
            var ip = new IPAddress(0x0);
            var port = 1;

            using (var c = new Connection(ip, port))
            {
                c.SetProperty("State", ConnectionState.Connected);

                var ex = Record.Exception(() => c.Disconnect());

                Assert.Null(ex);
                Assert.Equal(ConnectionState.Disconnected, c.State);
            }
        }

        [Trait("Category", "Disconnect")]
        [Fact(DisplayName = "Disconnect raises StateChanged event")]
        public void Disconnect_Raises_StateChanged_Event()
        {
            var ip = new IPAddress(0x0);
            var port = 1;

            using (var c = new Connection(ip, port))
            {
                c.SetProperty("State", ConnectionState.Connected);

                var eventArgs = new List<ConnectionStateChangedEventArgs>();

                c.StateChanged += (sender, e) => eventArgs.Add(e);

                c.Disconnect("foo");

                Assert.Equal(ConnectionState.Disconnected, c.State);

                // the event will fire twice, once on transition to Disconnecting, and again on transition to Disconnected.
                Assert.Equal(2, eventArgs.Count);
                Assert.Equal(ConnectionState.Disconnecting, eventArgs[0].CurrentState);
                Assert.Equal(ConnectionState.Disconnected, eventArgs[1].CurrentState);
            }
        }

        [Trait("Category", "Disconnect")]
        [Fact(DisplayName = "Disconnect raises Disconnected event")]
        public void Disconnect_Raises_Disconnected_Event()
        {
            var ip = new IPAddress(0x0);
            var port = 1;

            using (var c = new Connection(ip, port))
            {
                c.SetProperty("State", ConnectionState.Connected);

                var eventArgs = new List<string>();

                c.Disconnected += (sender, e) => eventArgs.Add(e);

                c.Disconnect("foo");

                Assert.Equal(ConnectionState.Disconnected, c.State);

                Assert.Single(eventArgs);
                Assert.Equal("foo", eventArgs[0]);
            }
        }

        [Trait("Category", "Connect")]
        [Fact(DisplayName = "Connect throws when not pending or disconnected")]
        public async Task Connect_Throws_When_Not_Pending_Or_Disconnected()
        {
            var ip = new IPAddress(0x0);
            var port = 1;

            using (var c = new Connection(ip, port))
            {
                c.SetProperty("State", ConnectionState.Connected);

                var ex = await Record.ExceptionAsync(async () => await c.ConnectAsync());

                Assert.NotNull(ex);
                Assert.IsType<InvalidOperationException>(ex);
            }
        }

        [Trait("Category", "Connect")]
        [Fact(DisplayName = "Connect connects when not connected or transitioning")]
        public async Task Connect_Connects_When_Not_Connected_Or_Transitioning()
        {
            var ip = new IPAddress(0x0);
            var port = 1;

            using (var socket = new Socket(SocketType.Stream, ProtocolType.IP))
            {
                var t = new Mock<ITcpClient>();
                t.Setup(m => m.Client).Returns(socket);

                using (var c = new Connection(ip, port, tcpClient: t.Object))
                {
                    var ex = await Record.ExceptionAsync(async () => await c.ConnectAsync());

                    Assert.Null(ex);
                    Assert.Equal(ConnectionState.Connected, c.State);

                    t.Verify(m => m.ConnectAsync(It.IsAny<IPAddress>(), It.IsAny<int>()), Times.Once);
                }
            }
        }

        [Trait("Category", "Connect")]
        [Fact(DisplayName = "Connect throws when timed out")]
        public async Task Connect_Throws_When_Timed_Out()
        {
            var ip = new IPAddress(0x0);
            var port = 1;

            using (var socket = new Socket(SocketType.Stream, ProtocolType.IP))
            {
                var t = new Mock<ITcpClient>();
                t.Setup(m => m.Client).Returns(socket);
                t.Setup(m => m.ConnectAsync(It.IsAny<IPAddress>(), It.IsAny<int>()))
                    .Returns(Task.Run(() => Thread.Sleep(10000)));

                var o = new ConnectionOptions(connectTimeout: 0);
                using (var c = new Connection(ip, port, options: o, tcpClient: t.Object))
                {
                    var ex = await Record.ExceptionAsync(async () => await c.ConnectAsync());

                    Assert.NotNull(ex);
                    Assert.IsType<TimeoutException>(ex);

                    t.Verify(m => m.ConnectAsync(It.IsAny<IPAddress>(), It.IsAny<int>()), Times.Once);
                }
            }
        }

        [Trait("Category", "Connect")]
        [Fact(DisplayName = "Connect throws OperationCanceledException when token is cancelled")]
        public async Task Connect_Throws_OperationCanceledException_When_Token_Is_Cancelled()
        {
            var ip = new IPAddress(0x0);
            var port = 1;

            using (var socket = new Socket(SocketType.Stream, ProtocolType.IP))
            {
                var t = new Mock<ITcpClient>();
                t.Setup(m => m.Client).Returns(socket);
                t.Setup(m => m.ConnectAsync(It.IsAny<IPAddress>(), It.IsAny<int>()))
                    .Returns(Task.Run(() => Thread.Sleep(10000)));

                var o = new ConnectionOptions(connectTimeout: 10000);

                using (var c = new Connection(ip, port, options: o, tcpClient: t.Object))
                {
                    Exception ex = null;

                    using (var cts = new CancellationTokenSource())
                    {
                        cts.Cancel();
                        ex = await Record.ExceptionAsync(async () => await c.ConnectAsync(cts.Token));
                    }

                    Assert.NotNull(ex);
                    Assert.IsType<OperationCanceledException>(ex);

                    t.Verify(m => m.ConnectAsync(It.IsAny<IPAddress>(), It.IsAny<int>()), Times.Once);
                }
            }
        }

        [Trait("Category", "Connect")]
        [Fact(DisplayName = "Connect throws when TcpClient throws")]
        public async Task Connect_Throws_When_TcpClient_Throws()
        {
            var ip = new IPAddress(0x0);
            var port = 1;

            using (var socket = new Socket(SocketType.Stream, ProtocolType.IP))
            {
                var t = new Mock<ITcpClient>();
                t.Setup(m => m.Client).Returns(socket);
                t.Setup(m => m.ConnectAsync(It.IsAny<IPAddress>(), It.IsAny<int>()))
                    .Returns(Task.Run(() => throw new SocketException()));

                using (var c = new Connection(ip, port, tcpClient: t.Object))
                {
                    var ex = await Record.ExceptionAsync(async () => await c.ConnectAsync());

                    Assert.NotNull(ex);
                    Assert.IsType<ConnectionException>(ex);
                    Assert.IsType<SocketException>(ex.InnerException);

                    t.Verify(m => m.ConnectAsync(It.IsAny<IPAddress>(), It.IsAny<int>()), Times.Once);
                }
            }
        }

        [Trait("Category", "Connect")]
        [Fact(DisplayName = "Connect raises Connected event")]
        public async Task Connect_Raises_Connected_Event()
        {
            var ip = new IPAddress(0x0);
            var port = 1;

            using (var socket = new Socket(SocketType.Stream, ProtocolType.IP))
            {
                var t = new Mock<ITcpClient>();
                t.Setup(m => m.Client).Returns(socket);

                using (var c = new Connection(ip, port, tcpClient: t.Object))
                {
                    var eventArgs = new List<EventArgs>();

                    c.Connected += (sender, e) => eventArgs.Add(e);

                    await c.ConnectAsync();

                    Assert.Equal(ConnectionState.Connected, c.State);
                    Assert.Single(eventArgs);

                    t.Verify(m => m.ConnectAsync(It.IsAny<IPAddress>(), It.IsAny<int>()), Times.Once);
                }
            }
        }

        [Trait("Category", "Connect")]
        [Fact(DisplayName = "Connect raises StateChanged event")]
        public async Task Connect_Raises_StateChanged_Event()
        {
            var ip = new IPAddress(0x0);
            var port = 1;

            using (var socket = new Socket(SocketType.Stream, ProtocolType.IP))
            {
                var t = new Mock<ITcpClient>();
                t.Setup(m => m.Client).Returns(socket);

                using (var c = new Connection(ip, port, tcpClient: t.Object))
                {
                    var eventArgs = new List<ConnectionStateChangedEventArgs>();

                    c.StateChanged += (sender, e) => eventArgs.Add(e);

                    await c.ConnectAsync();

                    Assert.Equal(ConnectionState.Connected, c.State);

                    // the event will fire twice, once on transition to Connecting, and again on transition to Connected.
                    Assert.Equal(2, eventArgs.Count);
                    Assert.Equal(ConnectionState.Connecting, eventArgs[0].CurrentState);
                    Assert.Equal(ConnectionState.Connected, eventArgs[1].CurrentState);

                    t.Verify(m => m.ConnectAsync(It.IsAny<IPAddress>(), It.IsAny<int>()), Times.Once);
                }
            }
        }

        [Trait("Category", "Watchdog")]
        [Fact(DisplayName = "Watchdog disconnects when TcpClient disconnects")]
        public async Task Watchdog_Disconnects_When_TcpClient_Disconnects()
        {
            var ip = new IPAddress(0x0);
            var port = 1;

            using (var socket = new Socket(SocketType.Stream, ProtocolType.IP))
            {
                var t = new Mock<ITcpClient>();
                t.Setup(m => m.Client).Returns(socket);
                t.Setup(m => m.Connected).Returns(false);

                using (var c = new Connection(ip, port, tcpClient: t.Object))
                {
                    var disconnectRaisedByWatchdog = false;
                    c.Disconnected += (sender, e) => disconnectRaisedByWatchdog = true;

                    var timer = c.GetProperty<System.Timers.Timer>("WatchdogTimer");
                    timer.Interval = 1;
                    timer.Reset();

                    await c.ConnectAsync();

                    Assert.Equal(ConnectionState.Connected, c.State);

                    var start = DateTime.UtcNow;

                    while (!disconnectRaisedByWatchdog)
                    {
                        if ((DateTime.UtcNow - start).TotalMilliseconds > 10000)
                        {
                            throw new Exception("Watchdog didn't disconnect in 10000ms");
                        }
                    }

                    Assert.True(disconnectRaisedByWatchdog);

                    t.Verify(m => m.ConnectAsync(It.IsAny<IPAddress>(), It.IsAny<int>()), Times.Once);
                }
            }
        }

        [Trait("Category", "Write")]
        [Fact(DisplayName = "Write throws given null bytes")]
        public async Task Write_Throws_Given_Null_Bytes()
        {
            using (var c = new Connection(new IPAddress(0x0), 1))
            {
                var ex = await Record.ExceptionAsync(async () => await c.WriteAsync(null));

                Assert.NotNull(ex);
                Assert.IsType<ArgumentException>(ex);
            }
        }

        [Trait("Category", "Write")]
        [Fact(DisplayName = "Write throws given zero bytes")]
        public async Task Write_Throws_Given_Zero_Bytes()
        {
            using (var socket = new Socket(SocketType.Stream, ProtocolType.IP))
            {
                var t = new Mock<ITcpClient>();
                t.Setup(m => m.Client).Returns(socket);
                t.Setup(m => m.Connected).Returns(true);

                using (var c = new Connection(new IPAddress(0x0), 1, tcpClient: t.Object))
                {
                    var ex = await Record.ExceptionAsync(async () => await c.WriteAsync(Array.Empty<byte>()));

                    Assert.NotNull(ex);
                    Assert.IsType<ArgumentException>(ex);
                }
            }
        }

        [Trait("Category", "Write")]
        [Theory(DisplayName = "Write from stream throws given negative length")]
        [InlineData(-1)]
        [InlineData(-121412)]
        public async Task Write_From_Stream_Throws_Given_Negative_Length(long length)
        {
            using (var stream = new MemoryStream())
            using (var socket = new Socket(SocketType.Stream, ProtocolType.IP))
            {
                var t = new Mock<ITcpClient>();
                t.Setup(m => m.Client).Returns(socket);
                t.Setup(m => m.Connected).Returns(true);

                using (var c = new Connection(new IPAddress(0x0), 1, tcpClient: t.Object))
                {
                    var ex = await Record.ExceptionAsync(async () => await c.WriteAsync(length, stream, (ct) => Task.CompletedTask));

                    Assert.NotNull(ex);
                    Assert.IsType<ArgumentException>(ex);
                }
            }
        }

        [Trait("Category", "Write")]
        [Fact(DisplayName = "Write from stream throws given null stream")]
        public async Task Write_From_Stream_Throws_Given_Null_Stream()
        {
            using (var socket = new Socket(SocketType.Stream, ProtocolType.IP))
            {
                var t = new Mock<ITcpClient>();
                t.Setup(m => m.Client).Returns(socket);
                t.Setup(m => m.Connected).Returns(true);

                using (var c = new Connection(new IPAddress(0x0), 1, tcpClient: t.Object))
                {
                    var ex = await Record.ExceptionAsync(async () => await c.WriteAsync(1, null, (ct) => Task.CompletedTask));

                    Assert.NotNull(ex);
                    Assert.IsType<ArgumentNullException>(ex);
                }
            }
        }

        [Trait("Category", "Write")]
        [Fact(DisplayName = "Write from stream throws given unreadable stream")]
        public async Task Write_From_Stream_Throws_Given_Unreadable_Stream()
        {
            using (var stream = new UnReadableWriteableStream())
            using (var socket = new Socket(SocketType.Stream, ProtocolType.IP))
            {
                var t = new Mock<ITcpClient>();
                t.Setup(m => m.Client).Returns(socket);
                t.Setup(m => m.Connected).Returns(true);

                using (var c = new Connection(new IPAddress(0x0), 1, tcpClient: t.Object))
                {
                    var ex = await Record.ExceptionAsync(async () => await c.WriteAsync(1, stream, (ct) => Task.CompletedTask));

                    Assert.NotNull(ex);
                    Assert.IsType<InvalidOperationException>(ex);
                }
            }
        }

        [Trait("Category", "Write")]
        [Fact(DisplayName = "Write throws if TcpClient is not connected")]
        public async Task Write_Throws_If_TcpClient_Is_Not_Connected()
        {
            var t = new Mock<ITcpClient>();

            using (var socket = new Socket(SocketType.Stream, ProtocolType.IP))
            {
                t.Setup(m => m.Client).Returns(socket);
                t.Setup(m => m.Connected).Returns(false);

                using (var c = new Connection(new IPAddress(0x0), 1, tcpClient: t.Object))
                {
                    var ex = await Record.ExceptionAsync(async () => await c.WriteAsync(new byte[] { 0x0, 0x1 }));

                    Assert.NotNull(ex);
                    Assert.IsType<InvalidOperationException>(ex);
                }
            }
        }

        [Trait("Category", "Write")]
        [Theory(DisplayName = "Write from stream throws if TcpClient is not connected"), AutoData]
        public async Task Write_From_Stream_Throws_If_TcpClient_Is_Not_Connected(int length, Func<CancellationToken, Task> governor)
        {
            var t = new Mock<ITcpClient>();

            using (var stream = new MemoryStream())
            using (var socket = new Socket(SocketType.Stream, ProtocolType.IP))
            {
                t.Setup(m => m.Client).Returns(socket);
                t.Setup(m => m.Connected).Returns(false);

                using (var c = new Connection(new IPAddress(0x0), 1, tcpClient: t.Object))
                {
                    var ex = await Record.ExceptionAsync(async () => await c.WriteAsync(length, stream, governor));

                    Assert.NotNull(ex);
                    Assert.IsType<InvalidOperationException>(ex);
                }
            }
        }

        [Trait("Category", "Write")]
        [Fact(DisplayName = "Write throws if connection is not connected")]
        public async Task Write_Throws_If_Connection_Is_Not_Connected()
        {
            var t = new Mock<ITcpClient>();

            using (var socket = new Socket(SocketType.Stream, ProtocolType.IP))
            {
                t.Setup(m => m.Client).Returns(socket);
                t.Setup(m => m.Connected).Returns(true);

                using (var c = new Connection(new IPAddress(0x0), 1, tcpClient: t.Object))
                {
                    c.SetProperty("State", ConnectionState.Disconnected);

                    var ex = await Record.ExceptionAsync(async () => await c.WriteAsync(new byte[] { 0x0, 0x1 }));

                    Assert.NotNull(ex);
                    Assert.IsType<InvalidOperationException>(ex);
                }
            }
        }

        [Trait("Category", "Write")]
        [Theory(DisplayName = "Write from stream throws if connection is not connected"), AutoData]
        public async Task Write_From_Stream_Throws_If_Connection_Is_Not_Connected(int length, Func<CancellationToken, Task> governor)
        {
            var t = new Mock<ITcpClient>();

            using (var stream = new MemoryStream())
            using (var socket = new Socket(SocketType.Stream, ProtocolType.IP))
            {
                t.Setup(m => m.Client).Returns(socket);
                t.Setup(m => m.Connected).Returns(true);

                using (var c = new Connection(new IPAddress(0x0), 1, tcpClient: t.Object))
                {
                    c.SetProperty("State", ConnectionState.Disconnected);

                    var ex = await Record.ExceptionAsync(async () => await c.WriteAsync(length, stream, governor));

                    Assert.NotNull(ex);
                    Assert.IsType<InvalidOperationException>(ex);
                }
            }
        }

        [Trait("Category", "Write")]
        [Fact(DisplayName = "Write throws if Stream throws")]
        public async Task Write_Throws_If_Stream_Throws()
        {
            var s = new Mock<INetworkStream>();
            s.Setup(m => m.WriteAsync(It.IsAny<byte[]>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
                .Throws(new SocketException());

            var t = new Mock<ITcpClient>();

            using (var socket = new Socket(SocketType.Stream, ProtocolType.IP))
            {
                t.Setup(m => m.Client).Returns(socket);
                t.Setup(m => m.Connected).Returns(true);
                t.Setup(m => m.GetStream()).Returns(s.Object);

                using (var c = new Connection(new IPAddress(0x0), 1, tcpClient: t.Object))
                {
                    var ex = await Record.ExceptionAsync(async () => await c.WriteAsync(new byte[] { 0x0, 0x1 }));

                    Assert.NotNull(ex);
                    Assert.IsType<ConnectionWriteException>(ex);
                    Assert.IsType<SocketException>(ex.InnerException);

                    s.Verify(m => m.WriteAsync(It.IsAny<byte[]>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Once);
                }
            }
        }

        [Trait("Category", "Write")]
        [Fact(DisplayName = "Write does not throw given good input and if Stream does not throw")]
        public async Task Write_Does_Not_Throw_Given_Good_Input_And_If_Stream_Does_Not_Throw()
        {
            var s = new Mock<INetworkStream>();
            var t = new Mock<ITcpClient>();

            using (var socket = new Socket(SocketType.Stream, ProtocolType.IP))
            {
                t.Setup(m => m.Client).Returns(socket);
                t.Setup(m => m.Connected).Returns(true);
                t.Setup(m => m.GetStream()).Returns(s.Object);

                using (var c = new Connection(new IPAddress(0x0), 1, tcpClient: t.Object))
                {
                    var ex = await Record.ExceptionAsync(async () => await c.WriteAsync(new byte[] { 0x0, 0x1 }));

                    Assert.Null(ex);

                    s.Verify(m => m.WriteAsync(It.IsAny<byte[]>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Once);
                }
            }
        }

        [Trait("Category", "Write")]
        [Fact(DisplayName = "Write from stream does not throw given good input and if Stream does not throw")]
        public async Task Write_From_Stream_Does_Not_Throw_Given_Good_Input_And_If_Stream_Does_Not_Throw()
        {
            var s = new Mock<INetworkStream>();
            var t = new Mock<ITcpClient>();

            var data = new byte[] { 0x0, 0x1 };

            using (var stream = new MemoryStream(data))
            using (var socket = new Socket(SocketType.Stream, ProtocolType.IP))
            {
                t.Setup(m => m.Client).Returns(socket);
                t.Setup(m => m.Connected).Returns(true);
                t.Setup(m => m.GetStream()).Returns(s.Object);

                using (var c = new Connection(new IPAddress(0x0), 1, tcpClient: t.Object))
                {
                    var ex = await Record.ExceptionAsync(async () => await c.WriteAsync(data.Length, stream, (ct) => Task.CompletedTask));

                    Assert.Null(ex);

                    s.Verify(m => m.WriteAsync(It.IsAny<byte[]>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Once);
                }
            }
        }

        [Trait("Category", "Read")]
        [Fact(DisplayName = "Read throws if TcpClient is not connected")]
        public async Task Read_Throws_If_TcpClient_Is_Not_Connected()
        {
            var t = new Mock<ITcpClient>();

            using (var socket = new Socket(SocketType.Stream, ProtocolType.IP))
            {
                t.Setup(m => m.Client).Returns(socket);
                t.Setup(m => m.Connected).Returns(false);

                using (var c = new Connection(new IPAddress(0x0), 1, tcpClient: t.Object))
                {
                    var ex = await Record.ExceptionAsync(async () => await c.ReadAsync(1));

                    Assert.NotNull(ex);
                    Assert.IsType<InvalidOperationException>(ex);
                }
            }
        }

        [Trait("Category", "Read")]
        [Theory(DisplayName = "Read to stream throws if TcpClient is not connected"), AutoData]
        public async Task Read_To_Stream_Throws_If_TcpClient_Is_Not_Connected(Func<CancellationToken, Task> governor)
        {
            var t = new Mock<ITcpClient>();

            using (var stream = new MemoryStream())
            using (var socket = new Socket(SocketType.Stream, ProtocolType.IP))
            {
                t.Setup(m => m.Client).Returns(socket);
                t.Setup(m => m.Connected).Returns(false);

                using (var c = new Connection(new IPAddress(0x0), 1, tcpClient: t.Object))
                {
                    var ex = await Record.ExceptionAsync(async () => await c.ReadAsync(1, stream, governor));

                    Assert.NotNull(ex);
                    Assert.IsType<InvalidOperationException>(ex);
                }
            }
        }

        [Trait("Category", "Read")]
        [Fact(DisplayName = "Read throws if connection is not connected")]
        public async Task Read_Throws_If_Connection_Is_Not_Connected()
        {
            var t = new Mock<ITcpClient>();

            using (var socket = new Socket(SocketType.Stream, ProtocolType.IP))
            {
                t.Setup(m => m.Client).Returns(socket);
                t.Setup(m => m.Connected).Returns(true);

                using (var c = new Connection(new IPAddress(0x0), 1, tcpClient: t.Object))
                {
                    t.Setup(m => m.Client).Returns(socket);
                    c.SetProperty("State", ConnectionState.Disconnected);

                    var ex = await Record.ExceptionAsync(async () => await c.ReadAsync(1));

                    Assert.NotNull(ex);
                    Assert.IsType<InvalidOperationException>(ex);
                }
            }
        }

        [Trait("Category", "Read")]
        [Theory(DisplayName = "Read to stream throws if connection is not connected"), AutoData]
        public async Task Read_To_Stream_Throws_If_Connection_Is_Not_Connected(Func<CancellationToken, Task> governor)
        {
            var t = new Mock<ITcpClient>();

            using (var stream = new MemoryStream())
            using (var socket = new Socket(SocketType.Stream, ProtocolType.IP))
            {
                t.Setup(m => m.Client).Returns(socket);
                t.Setup(m => m.Connected).Returns(true);

                using (var c = new Connection(new IPAddress(0x0), 1, tcpClient: t.Object))
                {
                    t.Setup(m => m.Client).Returns(socket);
                    c.SetProperty("State", ConnectionState.Disconnected);

                    var ex = await Record.ExceptionAsync(async () => await c.ReadAsync(1, stream, governor));

                    Assert.NotNull(ex);
                    Assert.IsType<InvalidOperationException>(ex);
                }
            }
        }

        [Trait("Category", "Read")]
        [Theory(DisplayName = "Read does not throw if length is long and larger than int"), AutoData]
        public async Task Read_Does_Not_Throw_If_Length_Is_Long_And_Larger_Than_Int(long length)
        {
            var s = new Mock<INetworkStream>();
            s.Setup(m => m.ReadAsync(It.IsAny<byte[]>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
                .Returns(Task.FromResult((int)length));

            var t = new Mock<ITcpClient>();

            using (var socket = new Socket(SocketType.Stream, ProtocolType.IP))
            {
                t.Setup(m => m.Client).Returns(socket);
                t.Setup(m => m.Connected).Returns(true);
                t.Setup(m => m.GetStream()).Returns(s.Object);

                using (var c = new Connection(new IPAddress(0x0), 1, tcpClient: t.Object))
                {
                    var ex = await Record.ExceptionAsync(async () => await c.ReadAsync(length));

                    Assert.Null(ex);
                }
            }
        }

        [Trait("Category", "Read")]
        [Theory(DisplayName = "Read to stream does not throw if length is long and larger than int"), AutoData]
        public async Task Read_To_Stream_Does_Not_Throw_If_Length_Is_Long_And_Larger_Than_Int(long length, Func<CancellationToken, Task> governor)
        {
            var s = new Mock<INetworkStream>();
            s.Setup(m => m.ReadAsync(It.IsAny<byte[]>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
                .Returns(Task.FromResult((int)length));

            var t = new Mock<ITcpClient>();

            using (var stream = new MemoryStream())
            using (var socket = new Socket(SocketType.Stream, ProtocolType.IP))
            {
                t.Setup(m => m.Client).Returns(socket);
                t.Setup(m => m.Connected).Returns(true);
                t.Setup(m => m.GetStream()).Returns(s.Object);

                using (var c = new Connection(new IPAddress(0x0), 1, tcpClient: t.Object))
                {
                    var ex = await Record.ExceptionAsync(async () => await c.ReadAsync(length, stream, governor));

                    Assert.Null(ex);
                }
            }
        }

        [Trait("Category", "Read")]
        [Fact(DisplayName = "Read does not throw given good input and if Stream does not throw")]
        public async Task Read_Does_Not_Throw_Given_Good_Input_And_If_Stream_Does_Not_Throw()
        {
            var s = new Mock<INetworkStream>();
            s.Setup(m => m.ReadAsync(It.IsAny<byte[]>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
                .Returns(Task.Run(() => 1));

            var t = new Mock<ITcpClient>();

            using (var socket = new Socket(SocketType.Stream, ProtocolType.IP))
            {
                t.Setup(m => m.Client).Returns(socket);
                t.Setup(m => m.Connected).Returns(true);
                t.Setup(m => m.GetStream()).Returns(s.Object);

                using (var c = new Connection(new IPAddress(0x0), 1, tcpClient: t.Object))
                {
                    var ex = await Record.ExceptionAsync(async () => await c.ReadAsync(1));

                    Assert.Null(ex);

                    s.Verify(m => m.ReadAsync(It.IsAny<byte[]>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Once);
                }
            }
        }

        [Trait("Category", "Read")]
        [Fact(DisplayName = "Read to stream does not throw given good input and if Stream does not throw")]
        public async Task Read_To_Stream_Does_Not_Throw_Given_Good_Input_And_If_Stream_Does_Not_Throw()
        {
            var s = new Mock<INetworkStream>();
            s.Setup(m => m.ReadAsync(It.IsAny<byte[]>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
                .Returns(Task.Run(() => 1));

            var t = new Mock<ITcpClient>();

            using (var stream = new MemoryStream())
            using (var socket = new Socket(SocketType.Stream, ProtocolType.IP))
            {
                t.Setup(m => m.Client).Returns(socket);
                t.Setup(m => m.Connected).Returns(true);
                t.Setup(m => m.GetStream()).Returns(s.Object);

                using (var c = new Connection(new IPAddress(0x0), 1, tcpClient: t.Object))
                {
                    var ex = await Record.ExceptionAsync(async () => await c.ReadAsync(1, stream, (ct) => Task.CompletedTask));

                    Assert.Null(ex);

                    s.Verify(m => m.ReadAsync(It.IsAny<byte[]>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Once);
                }
            }
        }

        [Trait("Category", "Read")]
        [Fact(DisplayName = "Read loops over Stream.ReadAsync on partial read")]
        public async Task Read_Loops_Over_Stream_ReadAsync_On_Partial_Read()
        {
            var s = new Mock<INetworkStream>();
            s.Setup(m => m.ReadAsync(It.IsAny<byte[]>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
                .Returns(Task.Run(() => 1));

            var t = new Mock<ITcpClient>();

            using (var socket = new Socket(SocketType.Stream, ProtocolType.IP))
            {
                t.Setup(m => m.Client).Returns(socket);
                t.Setup(m => m.Connected).Returns(true);
                t.Setup(m => m.GetStream()).Returns(s.Object);

                using (var c = new Connection(new IPAddress(0x0), 1, tcpClient: t.Object))
                {
                    await c.ReadAsync(3);

                    s.Verify(m => m.ReadAsync(It.IsAny<byte[]>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Exactly(3));
                }
            }
        }

        [Trait("Category", "Read")]
        [Fact(DisplayName = "Read to stream loops over Stream.ReadAsync on partial read")]
        public async Task Read_To_Stream_Loops_Over_Stream_ReadAsync_On_Partial_Read()
        {
            var s = new Mock<INetworkStream>();
            s.Setup(m => m.ReadAsync(It.IsAny<byte[]>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
                .Returns(Task.Run(() => 1));

            var t = new Mock<ITcpClient>();

            using (var stream = new MemoryStream())
            using (var socket = new Socket(SocketType.Stream, ProtocolType.IP))
            {
                t.Setup(m => m.Client).Returns(socket);
                t.Setup(m => m.Connected).Returns(true);
                t.Setup(m => m.GetStream()).Returns(s.Object);

                using (var c = new Connection(new IPAddress(0x0), 1, tcpClient: t.Object))
                {
                    await c.ReadAsync(3, stream, (ct) => Task.CompletedTask);

                    s.Verify(m => m.ReadAsync(It.IsAny<byte[]>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Exactly(3));
                }
            }
        }

        [Trait("Category", "Read")]
        [Fact(DisplayName = "Read throws if Stream throws")]
        public async Task Read_Throws_If_Stream_Throws()
        {
            var s = new Mock<INetworkStream>();
            s.Setup(m => m.ReadAsync(It.IsAny<byte[]>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
                .Throws(new SocketException());

            var t = new Mock<ITcpClient>();

            using (var socket = new Socket(SocketType.Stream, ProtocolType.IP))
            {
                t.Setup(m => m.Client).Returns(socket);
                t.Setup(m => m.Connected).Returns(true);
                t.Setup(m => m.GetStream()).Returns(s.Object);

                using (var c = new Connection(new IPAddress(0x0), 1, tcpClient: t.Object))
                {
                    var ex = await Record.ExceptionAsync(async () => await c.ReadAsync(1));

                    Assert.NotNull(ex);
                    Assert.IsType<ConnectionReadException>(ex);
                    Assert.IsType<SocketException>(ex.InnerException);

                    s.Verify(m => m.ReadAsync(It.IsAny<byte[]>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Once);
                }
            }
        }

        [Trait("Category", "Read")]
        [Fact(DisplayName = "Read to stream throws if Stream throws")]
        public async Task Read_To_Stream_Throws_If_Stream_Throws()
        {
            var s = new Mock<INetworkStream>();
            s.Setup(m => m.ReadAsync(It.IsAny<byte[]>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
                .Throws(new SocketException());

            var t = new Mock<ITcpClient>();

            using (var socket = new Socket(SocketType.Stream, ProtocolType.IP))
            {
                t.Setup(m => m.Client).Returns(socket);
                t.Setup(m => m.Connected).Returns(true);
                t.Setup(m => m.GetStream()).Returns(s.Object);

                using (var c = new Connection(new IPAddress(0x0), 1, tcpClient: t.Object))
                {
                    var ex = await Record.ExceptionAsync(async () => await c.ReadAsync(1));

                    Assert.NotNull(ex);
                    Assert.IsType<ConnectionReadException>(ex);
                    Assert.IsType<SocketException>(ex.InnerException);

                    s.Verify(m => m.ReadAsync(It.IsAny<byte[]>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Once);
                }
            }
        }

        [Trait("Category", "Read")]
        [Fact(DisplayName = "Read does not throw given zero length")]
        public async Task Read_Does_Not_Throw_Given_Zero_Length()
        {
            var t = new Mock<ITcpClient>();
            t.Setup(m => m.Connected).Returns(true);

            using (var socket = new Socket(SocketType.Stream, ProtocolType.IP))
            {
                t.Setup(m => m.Client).Returns(socket);

                using (var c = new Connection(new IPAddress(0x0), 1, tcpClient: t.Object))
                {
                    var ex = await Record.ExceptionAsync(async () => await c.ReadAsync(0));

                    Assert.Null(ex);
                }
            }
        }

        [Trait("Category", "Read")]
        [Fact(DisplayName = "Read returns empty byte array given zero length")]
        public async Task Read_Returns_Empty_Byte_Array_Given_Zero_Length()
        {
            var t = new Mock<ITcpClient>();

            using (var socket = new Socket(SocketType.Stream, ProtocolType.IP))
            {
                t.Setup(m => m.Client).Returns(socket);
                t.Setup(m => m.Connected).Returns(true);

                using (var c = new Connection(new IPAddress(0x0), 1, tcpClient: t.Object))
                {
                    var bytes = await c.ReadAsync(0);

                    Assert.Empty(bytes);
                }
            }
        }

        [Trait("Category", "Read")]
        [Theory(DisplayName = "Read throws given zero or negative length")]
        [InlineData(-12151353)]
        [InlineData(-1)]
        public async Task Read_Throws_Given_Zero_Or_Negative_Length(int length)
        {
            var t = new Mock<ITcpClient>();

            using (var socket = new Socket(SocketType.Stream, ProtocolType.IP))
            {
                t.Setup(m => m.Client).Returns(socket);
                t.Setup(m => m.Connected).Returns(true);

                using (var c = new Connection(new IPAddress(0x0), 1, tcpClient: t.Object))
                {
                    var ex = await Record.ExceptionAsync(async () => await c.ReadAsync(length));

                    Assert.NotNull(ex);
                    Assert.IsType<ArgumentException>(ex);
                }
            }
        }

        [Trait("Category", "Read")]
        [Theory(DisplayName = "Read to stream throws given zero or negative length")]
        [InlineData(-12151353)]
        [InlineData(-1)]
        public async Task Read_To_Stream_Throws_Given_Zero_Or_Negative_Length(int length)
        {
            Func<CancellationToken, Task> governor = (c) => Task.CompletedTask;
            var t = new Mock<ITcpClient>();

            using (var stream = new MemoryStream())
            using (var socket = new Socket(SocketType.Stream, ProtocolType.IP))
            {
                t.Setup(m => m.Client).Returns(socket);
                t.Setup(m => m.Connected).Returns(true);

                using (var c = new Connection(new IPAddress(0x0), 1, tcpClient: t.Object))
                {
                    var ex = await Record.ExceptionAsync(async () => await c.ReadAsync(length, stream, governor));

                    Assert.NotNull(ex);
                    Assert.IsType<ArgumentException>(ex);
                }
            }
        }

        [Trait("Category", "Read")]
        [Theory(DisplayName = "Read to stream throws given null stream"), AutoData]
        public async Task Read_To_Stream_Throws_Given_Null_Stream(int length)
        {
            Func<CancellationToken, Task> governor = (c) => Task.CompletedTask;
            var t = new Mock<ITcpClient>();

            using (var socket = new Socket(SocketType.Stream, ProtocolType.IP))
            {
                t.Setup(m => m.Client).Returns(socket);
                t.Setup(m => m.Connected).Returns(true);

                using (var c = new Connection(new IPAddress(0x0), 1, tcpClient: t.Object))
                {
                    var ex = await Record.ExceptionAsync(async () => await c.ReadAsync(length, null, governor));

                    Assert.NotNull(ex);
                    Assert.IsType<ArgumentNullException>(ex);
                }
            }
        }

        [Trait("Category", "Read")]
        [Theory(DisplayName = "Read to stream throws given unwriteable stream"), AutoData]
        public async Task Read_To_Stream_Throws_Given_Unwriteable_Stream(int length)
        {
            Func<CancellationToken, Task> governor = (c) => Task.CompletedTask;
            var t = new Mock<ITcpClient>();

            using (var stream = new UnReadableWriteableStream())
            using (var socket = new Socket(SocketType.Stream, ProtocolType.IP))
            {
                t.Setup(m => m.Client).Returns(socket);
                t.Setup(m => m.Connected).Returns(true);

                using (var c = new Connection(new IPAddress(0x0), 1, tcpClient: t.Object))
                {
                    var ex = await Record.ExceptionAsync(async () => await c.ReadAsync(length, stream, governor));

                    Assert.NotNull(ex);
                    Assert.IsType<InvalidOperationException>(ex);
                }
            }
        }

        [Trait("Category", "Read")]
        [Fact(DisplayName = "Read disconnects if Stream returns 0")]
        public async Task Read_Disconnects_If_Stream_Returns_0()
        {
            var s = new Mock<INetworkStream>();
            s.Setup(m => m.ReadAsync(It.IsAny<byte[]>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
                .Returns(Task.Run(() => 0));

            var t = new Mock<ITcpClient>();

            using (var socket = new Socket(SocketType.Stream, ProtocolType.IP))
            {
                t.Setup(m => m.Client).Returns(socket);
                t.Setup(m => m.Connected).Returns(true);
                t.Setup(m => m.GetStream()).Returns(s.Object);

                using (var c = new Connection(new IPAddress(0x0), 1, tcpClient: t.Object))
                {
                    var ex = await Record.ExceptionAsync(async () => await c.ReadAsync(1));

                    Assert.NotNull(ex);
                    Assert.IsType<ConnectionReadException>(ex);

                    Assert.Equal(ConnectionState.Disconnected, c.State);

                    s.Verify(m => m.ReadAsync(It.IsAny<byte[]>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Once);
                }
            }
        }

        [Trait("Category", "Read")]
        [Fact(DisplayName = "Read raises DataRead event")]
        public async Task Read_Raises_DataRead_Event()
        {
            var s = new Mock<INetworkStream>();
            s.Setup(m => m.ReadAsync(It.IsAny<byte[]>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
                .Returns(Task.Run(() => 1));

            var t = new Mock<ITcpClient>();

            using (var socket = new Socket(SocketType.Stream, ProtocolType.IP))
            {
                t.Setup(m => m.Client).Returns(socket);
                t.Setup(m => m.Connected).Returns(true);
                t.Setup(m => m.GetStream()).Returns(s.Object);

                using (var c = new Connection(new IPAddress(0x0), 1, tcpClient: t.Object))
                {
                    var eventArgs = new List<ConnectionDataEventArgs>();

                    c.DataRead += (sender, e) => eventArgs.Add(e);

                    await c.ReadAsync(3);

                    Assert.Equal(3, eventArgs.Count);
                    Assert.Equal(1, eventArgs[0].CurrentLength);
                    Assert.Equal(3, eventArgs[0].TotalLength);
                    Assert.Equal(2, eventArgs[1].CurrentLength);
                    Assert.Equal(3, eventArgs[1].TotalLength);
                    Assert.Equal(3, eventArgs[2].CurrentLength);
                    Assert.Equal(3, eventArgs[2].TotalLength);

                    s.Verify(m => m.ReadAsync(It.IsAny<byte[]>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Exactly(3));
                }
            }
        }

        [Trait("Category", "Read")]
        [Fact(DisplayName = "Read times out on inactivity")]
        public async Task Read_Times_Out_On_Inactivity()
        {
            var s = new Mock<INetworkStream>();
            s.Setup(m => m.ReadAsync(It.IsAny<byte[]>(), It.IsAny<int>(), It.IsAny<int>()))
                .Returns(Task.Run(() => 1));

            var t = new Mock<ITcpClient>();
            t.Setup(m => m.Client).Returns(new Socket(SocketType.Stream, ProtocolType.IP));
            t.Setup(m => m.Connected).Returns(true);
            t.Setup(m => m.GetStream()).Returns(s.Object);

            using (var c = new Connection(new IPAddress(0x0), 1, tcpClient: t.Object))
            {
                c.GetProperty<System.Timers.Timer>("InactivityTimer").Interval = 100;

                var ex = await Record.ExceptionAsync(async () => await c.ReadAsync(1));

                Assert.NotNull(ex);
                output(ex.Message);
                Assert.IsType<ConnectionReadException>(ex);

                Assert.Equal(ConnectionState.Disconnected, c.State);
            }
        }

        [Trait("Category", "HandoffTcpClient")]
        [Fact(DisplayName = "HandoffTcpClient hands off")]
        public void HandoffTcpClient_Hands_Off()
        {
            var t = new Mock<ITcpClient>();

            using (var socket = new Socket(SocketType.Stream, ProtocolType.IP))
            {
                t.Setup(m => m.Client).Returns(socket);

                using (var c = new Connection(new IPAddress(0x0), 1, tcpClient: t.Object))
                {
                    var first = c.GetProperty<ITcpClient>("TcpClient");

                    var tcpClient = c.HandoffTcpClient();

                    var second = c.GetProperty<ITcpClient>("TcpClient");

                    Assert.Equal(t.Object, tcpClient);
                    Assert.NotNull(first);
                    Assert.Null(second);
                }
            }
        }

        private class UnReadableWriteableStream : Stream
        {
            public override bool CanRead => false;
            public override bool CanWrite => false;

            public override bool CanSeek => throw new NotImplementedException();

            public override long Length => throw new NotImplementedException();

            public override long Position { get => throw new NotImplementedException(); set => throw new NotImplementedException(); }

            public override void Flush()
            {
                throw new NotImplementedException();
            }

            public override int Read(byte[] buffer, int offset, int count)
            {
                throw new NotImplementedException();
            }

            public override long Seek(long offset, SeekOrigin origin)
            {
                throw new NotImplementedException();
            }

            public override void SetLength(long value)
            {
                throw new NotImplementedException();
            }

            public override void Write(byte[] buffer, int offset, int count)
            {
                throw new NotImplementedException();
            }
        }
    }
}
