﻿namespace Soulseek.NET.Tests.Unit
{
    using Moq;
    using Newtonsoft.Json;
    using Soulseek.NET.Exceptions;
    using Soulseek.NET.Messaging.Tcp;
    using Soulseek.NET.Tcp;
    using System;
    using System.Collections.Concurrent;
    using Xunit;

    public class SoulseekClientTests
    {
        [Trait("Category", "Instantiation")]
        [Fact(DisplayName = "Instantiates with defaults for minimal constructor")]
        public void Instantiates_With_Defaults_For_Minimal_Constructor()
        {
            var s = new SoulseekClient();

            var defaultServer = s.GetField<string>("DefaultAddress");
            var defaultPort = s.GetField<int>("DefaultPort");

            Assert.Equal(defaultServer, s.Address);
            Assert.Equal(defaultPort, s.Port);
        }

        [Trait("Category", "Instantiation")]
        [Fact(DisplayName = "Instantiates without exception")]
        public void Instantiates_Without_Exception()
        {
            SoulseekClient s = null;

            var ex = Record.Exception(() => s = new SoulseekClient());

            Assert.Null(ex);
            Assert.NotNull(s);
        }

        [Trait("Category", "Instantiation")]
        [Fact(DisplayName = "State is Disconnected initially")]
        public void State_Is_Disconnected_Initially()
        {
            var s = new SoulseekClient();

            Assert.Equal(SoulseekClientState.Disconnected, s.State);
        }

        [Trait("Category", "Instantiation")]
        [Fact(DisplayName = "Username is null initially")]
        public void Username_Is_Null_Initially()
        {
            var s = new SoulseekClient();

            Assert.Null(s.Username);
        }

        [Trait("Category", "Connect")]
        [Fact(DisplayName = "Connect fails if connected")]
        public async void Connect_Fails_If_Connected()
        {
            var s = new SoulseekClient();
            s.SetProperty("State", SoulseekClientState.Connected);

            var ex = await Record.ExceptionAsync(async () => await s.ConnectAsync());

            Assert.NotNull(ex);
            Assert.IsType<InvalidOperationException>(ex);
        }

        [Trait("Category", "Connect")]
        [Fact(DisplayName = "Connect throws when TcpConnection throws")]
        public async void Connect_Throws_When_TcpConnection_Throws()
        {
            var c = new Mock<IMessageConnection>();
            c.Setup(m => m.ConnectAsync()).Throws(new ConnectionException());

            var s = new SoulseekClient(Guid.NewGuid().ToString(), new Random().Next(), serverConnection: c.Object);

            var ex = await Record.ExceptionAsync(async () => await s.ConnectAsync());

            Assert.NotNull(ex);
            Assert.IsType<ConnectionException>(ex);
        }

        [Trait("Category", "Connect")]
        [Fact(DisplayName = "Connect succeeds when TcpConnection succeeds")]
        public async void Connect_Succeeds_When_TcpConnection_Succeeds()
        {
            var c = new Mock<IMessageConnection>();

            var s = new SoulseekClient(Guid.NewGuid().ToString(), new Random().Next(), serverConnection: c.Object);

            var ex = await Record.ExceptionAsync(async () => await s.ConnectAsync());

            Assert.Null(ex);
        }

        [Trait("Category", "Instantiation")]
        [Fact(DisplayName = "Instantiation throws on a bad address")]
        public void Instantiation_Throws_On_A_Bad_Address()
        {
            var ex = Record.Exception(() => new SoulseekClient(Guid.NewGuid().ToString(), new Random().Next(), new SoulseekClientOptions()));

            Assert.NotNull(ex);
            Assert.IsType<SoulseekClientException>(ex);
        }

        [Trait("Category", "Disconnect")]
        [Fact(DisplayName = "Disconnect disconnects")]
        public async void Disconnect_Disconnects()
        {
            var c = new Mock<IMessageConnection>();

            var s = new SoulseekClient(Guid.NewGuid().ToString(), new Random().Next(), serverConnection: c.Object);
            await s.ConnectAsync();

            var ex = Record.Exception(() => s.Disconnect());

            Assert.Null(ex);
            Assert.Equal(SoulseekClientState.Disconnected, s.State);
        }

        [Trait("Category", "Disconnect")]
        [Fact(DisplayName = "Disconnect clears searches")]
        public async void Disconnect_Clears_Searches()
        {
            var c = new Mock<IMessageConnection>();

            var s = new SoulseekClient(Guid.NewGuid().ToString(), new Random().Next(), serverConnection: c.Object);
            await s.ConnectAsync();

            var searches = new ConcurrentDictionary<int, Search>();
            searches.TryAdd(0, new Search(string.Empty, 0, new SearchOptions()));
            searches.TryAdd(1, new Search(string.Empty, 1, new SearchOptions()));

            s.SetProperty("ActiveSearches", searches);

            var ex = Record.Exception(() => s.Disconnect());

            Assert.Null(ex);
            Assert.Equal(SoulseekClientState.Disconnected, s.State);
            Assert.Empty(searches);
        }

        [Trait("Category", "Disconnect")]
        [Fact(DisplayName = "Disconnect clears downloads")]
        public async void Disconnect_Clears_Downloads()
        {
            var c = new Mock<IMessageConnection>();

            var s = new SoulseekClient(Guid.NewGuid().ToString(), new Random().Next(), serverConnection: c.Object);
            await s.ConnectAsync();

            var activeDownloads = new ConcurrentDictionary<int, Download>();
            activeDownloads.TryAdd(0, new Download(string.Empty, string.Empty, 0));
            activeDownloads.TryAdd(1, new Download(string.Empty, string.Empty, 1));

            var queuedDownloads = new ConcurrentDictionary<int, Download>();
            queuedDownloads.TryAdd(0, new Download(string.Empty, string.Empty, 0));
            queuedDownloads.TryAdd(1, new Download(string.Empty, string.Empty, 1));

            s.SetProperty("ActiveDownloads", activeDownloads);
            s.SetProperty("QueuedDownloads", queuedDownloads);

            var ex = Record.Exception(() => s.Disconnect());

            Assert.Null(ex);
            Assert.Equal(SoulseekClientState.Disconnected, s.State);
            Assert.Empty(activeDownloads);
            Assert.Empty(queuedDownloads);
        }

        [Trait("Category", "Disconnect")]
        [Fact(DisplayName = "Disconnect clears peer queue")]
        public async void Disconnect_Clears_Peer_Queue()
        {
            var c = new Mock<IMessageConnection>();

            var p = new Mock<IConnectionManager<IMessageConnection>>();

            var s = new SoulseekClient(Guid.NewGuid().ToString(), new Random().Next(), serverConnection: c.Object, peerConnectionManager: p.Object);
            await s.ConnectAsync();

            var ex = Record.Exception(() => s.Disconnect());

            Assert.Null(ex);
            Assert.Equal(SoulseekClientState.Disconnected, s.State);

            p.Verify(m => m.RemoveAll(), Times.AtLeastOnce);
        }

        [Trait("Category", "Dispose/Finalize")]
        [Fact(DisplayName = "Disposes without exception")]
        public void Disposes_Without_Exception()
        {
            var s = new SoulseekClient();

            var ex = Record.Exception(() => s.Dispose());

            Assert.Null(ex);
        }

        [Trait("Category", "Dispose/Finalize")]
        [Fact(DisplayName = "Finalizes without exception")]
        public void Finalizes_Without_Exception()
        {
            var s = new SoulseekClient();

            var ex = Record.Exception(() => s.InvokeMethod("Finalize"));

            Assert.Null(ex);
        }

        [Trait("Category", "Login")]
        [Fact(DisplayName = "Login throws on null username")]
        public async void Login_Throws_On_Null_Username()
        {
            var s = new SoulseekClient();
            s.SetProperty("State", SoulseekClientState.Connected);

            var ex = await Record.ExceptionAsync(async () => await s.LoginAsync(null, Guid.NewGuid().ToString()));

            Assert.NotNull(ex);
            Assert.IsType<ArgumentException>(ex);
        }

        [Trait("Category", "Login")]
        [Theory(DisplayName = "Login throws on bad input")]
        [InlineData(null, "a")]
        [InlineData("", "a")]
        [InlineData("a", null)]
        [InlineData("a", "")]
        [InlineData("", "")]
        [InlineData(null, null)]
        public async void Login_Throws_On_Bad_Input(string username, string password)
        {
            var s = new SoulseekClient();
            s.SetProperty("State", SoulseekClientState.Connected);

            var ex = await Record.ExceptionAsync(async () => await s.LoginAsync(username, password));

            Assert.NotNull(ex);
            Assert.IsType<ArgumentException>(ex);
        }

        [Trait("Category", "Login")]
        [Fact(DisplayName = "Login throws if logged in")]
        public async void Login_Throws_If_Logged_In()
        {
            var s = new SoulseekClient();
            s.SetProperty("State", SoulseekClientState.Connected | SoulseekClientState.LoggedIn);

            var ex = await Record.ExceptionAsync(async () => await s.LoginAsync("a", "b"));

            Assert.NotNull(ex);
            Assert.IsType<InvalidOperationException>(ex);
        }

        [Trait("Category", "Login")]
        [Fact(DisplayName = "Login throws if not connected")]
        public async void Login_Throws_If_Not_Connected()
        {
            var s = new SoulseekClient();
            s.SetProperty("State", SoulseekClientState.Disconnected);

            var ex = await Record.ExceptionAsync(async () => await s.LoginAsync("a", "b"));

            Assert.NotNull(ex);
            Assert.IsType<InvalidOperationException>(ex);
        }
    }
}
