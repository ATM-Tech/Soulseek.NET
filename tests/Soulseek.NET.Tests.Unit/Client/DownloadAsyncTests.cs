﻿// <copyright file="DownloadAsyncTests.cs" company="JP Dillingham">
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
    using System.Collections.Concurrent;
    using System.Collections.Generic;
    using System.Threading.Tasks;
    using Moq;
    using Soulseek.NET.Exceptions;
    using Soulseek.NET.Messaging;
    using Soulseek.NET.Messaging.Messages;
    using Soulseek.NET.Messaging.Tcp;
    using Soulseek.NET.Tcp;
    using Xunit;

    public class DownloadAsyncTests
    {
        [Trait("Category", "DownloadAsync")]
        [Theory(DisplayName = "DownloadAsync throws ArgumentException given bad username")]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        public async Task DownloadAsync_Throws_ArgumentException_Given_Bad_Username(string username)
        {
            var s = new SoulseekClient();

            var ex = await Record.ExceptionAsync(async () => await s.DownloadAsync(username, "filename"));

            Assert.NotNull(ex);
            Assert.IsType<ArgumentException>(ex);
        }

        [Trait("Category", "DownloadAsync")]
        [Theory(DisplayName = "DownloadAsync throws ArgumentException given bad filename")]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        public async Task DownloadAsync_Throws_ArgumentException_Given_Bad_Filename(string filename)
        {
            var s = new SoulseekClient();

            var ex = await Record.ExceptionAsync(async () => await s.DownloadAsync("username", filename));

            Assert.NotNull(ex);
            Assert.IsType<ArgumentException>(ex);
        }

        [Trait("Category", "DownloadAsync")]
        [Fact(DisplayName = "DownloadAsync throws InvalidOperationException when not connected")]
        public async Task DownloadAsync_Throws_InvalidOperationException_When_Not_Connected()
        {
            var s = new SoulseekClient();

            var ex = await Record.ExceptionAsync(async () => await s.DownloadAsync("username", "filename"));

            Assert.NotNull(ex);
            Assert.IsType<InvalidOperationException>(ex);
            Assert.Contains("Connected", ex.Message);
        }

        [Trait("Category", "DownloadAsync")]
        [Fact(DisplayName = "DownloadAsync throws InvalidOperationException when not logged in")]
        public async Task DownloadAsync_Throws_InvalidOperationException_When_Not_Logged_In()
        {
            var s = new SoulseekClient();
            s.SetProperty("State", SoulseekClientStates.Connected);

            var ex = await Record.ExceptionAsync(async () => await s.DownloadAsync("username", "filename"));

            Assert.NotNull(ex);
            Assert.IsType<InvalidOperationException>(ex);
            Assert.Contains("logged in", ex.Message);
        }

        [Trait("Category", "DownloadAsync")]
        [Fact(DisplayName = "DownloadAsync throws ArgumentException when token used")]
        public async Task DownloadAsync_Throws_ArgumentException_When_Token_Used()
        {
            var s = new SoulseekClient();
            s.SetProperty("State", SoulseekClientStates.Connected | SoulseekClientStates.LoggedIn);

            var queued = new ConcurrentDictionary<int, Download>();
            queued.TryAdd(1, new Download("foo", "bar", 1));

            s.SetProperty("QueuedDownloads", queued);

            var ex = await Record.ExceptionAsync(async () => await s.DownloadAsync("username", "filename", 1));

            Assert.NotNull(ex);
            Assert.IsType<ArgumentException>(ex);
            Assert.Contains("token", ex.Message);
        }

        [Trait("Category", "DownloadAsync")]
        [Fact(DisplayName = "DownloadAsync throws DownloadException on token generation failure")]
        public async Task DownloadAsync_Throws_DownloadException_On_Token_Generation_Failure()
        {
            var tokenFactory = new Mock<ITokenFactory>();
            tokenFactory.Setup(m => m.TryGetToken(It.IsAny<Func<int, bool>>(), out It.Ref<int?>.IsAny))
                .Returns(false);

            var s = new SoulseekClient("127.0.0.1", 1, tokenFactory: tokenFactory.Object);
            s.SetProperty("State", SoulseekClientStates.Connected | SoulseekClientStates.LoggedIn);

            var ex = await Record.ExceptionAsync(async () => await s.DownloadAsync("username", "filename"));

            Assert.NotNull(ex);
            Assert.IsType<DownloadException>(ex);
            Assert.Contains("Unable to generate a unique token", ex.Message);
        }

        [Trait("Category", "DownloadAsync")]
        [Fact(DisplayName = "DownloadAsync throws DownloadException on peer message connection timeout")]
        public async Task DownloadAsync_Throws_DownloadException_On_Peer_Message_Connection_Timeout()
        {
            var options = new SoulseekClientOptions() { };
            options.MessageTimeout = 1;

            var s = new SoulseekClient("127.0.0.1", 1, options);
            s.SetProperty("State", SoulseekClientStates.Connected | SoulseekClientStates.LoggedIn);

            var ex = await Record.ExceptionAsync(async () => await s.DownloadAsync("username", "filename"));

            Assert.NotNull(ex);
            Assert.IsType<DownloadException>(ex);
            Assert.IsType<TimeoutException>(ex.InnerException);
        }

        [Trait("Category", "DownloadAsync")]
        [Fact(DisplayName = "DownloadAsync throws DownloadException when WriteMessageAsync throws")]
        public async Task DownloadAsync_Throws_DownloadException_When_WriteMessageAsync_Throws()
        {
            var options = new SoulseekClientOptions() { };
            options.MessageTimeout = 1;

            var conn = new Mock<IMessageConnection>();
            conn.Setup(m => m.WriteMessageAsync(It.IsAny<Message>()))
                .Throws(new ConnectionWriteException());

            var s = new SoulseekClient("127.0.0.1", 1, options);
            s.SetProperty("State", SoulseekClientStates.Connected | SoulseekClientStates.LoggedIn);

            var ex = await Record.ExceptionAsync(async () => await s.InvokeMethod<Task<byte[]>>("DownloadInternalAsync", "username", "filename", 1, null, conn.Object));

            Assert.NotNull(ex);
            Assert.IsType<DownloadException>(ex);
            Assert.IsType<ConnectionWriteException>(ex.InnerException);
        }

        [Trait("Category", "DownloadAsync")]
        [Fact(DisplayName = "DownloadAsync throws DownloadException on PeerTransferResponse timeout")]
        public async Task DownloadAsync_Throws_DownloadException_On_PeerTransferResponse_Timeout()
        {
            var options = new SoulseekClientOptions() { };
            options.MessageTimeout = 1;

            var conn = new Mock<IMessageConnection>();

            var s = new SoulseekClient("127.0.0.1", 1, options);
            s.SetProperty("State", SoulseekClientStates.Connected | SoulseekClientStates.LoggedIn);

            var ex = await Record.ExceptionAsync(async () => await s.InvokeMethod<Task<byte[]>>("DownloadInternalAsync", "username", "filename", 1, null, conn.Object));

            Assert.NotNull(ex);
            Assert.IsType<DownloadException>(ex);
            Assert.IsType<TimeoutException>(ex.InnerException);
        }

        [Trait("Category", "DownloadAsync")]
        [Fact(DisplayName = "DownloadAsync throws DownloadException on PeerTransferResponse cancellation")]
        public async Task DownloadAsync_Throws_DownloadException_On_PeerTransferResponse_Cancellation()
        {
            var options = new SoulseekClientOptions() { };
            options.MessageTimeout = 5;

            var waitKey = new WaitKey(MessageCode.PeerTransferResponse, "username", 1);

            var waiter = new Mock<IWaiter>();
            waiter.Setup(m => m.Wait<PeerTransferResponse>(It.Is<WaitKey>(w => w.Equals(waitKey)), null, null))
                .Returns(Task.FromException<PeerTransferResponse>(new MessageCancelledException()));

            var conn = new Mock<IMessageConnection>();

            var s = new SoulseekClient("127.0.0.1", 1, options, messageWaiter: waiter.Object);
            s.SetProperty("State", SoulseekClientStates.Connected | SoulseekClientStates.LoggedIn);

            var ex = await Record.ExceptionAsync(async () => await s.InvokeMethod<Task<byte[]>>("DownloadInternalAsync", "username", "filename", 1, null, conn.Object));

            Assert.NotNull(ex);
            Assert.IsType<DownloadException>(ex);
            Assert.IsType<MessageCancelledException>(ex.InnerException);
        }

        [Trait("Category", "DownloadAsync")]
        [Fact(DisplayName = "DownloadAsync throws DownloadException on PeerTransferResponse allowed")]
        public async Task DownloadAsync_Throws_DownloadException_On_PeerTransferResponse_Allowed()
        {
            var options = new SoulseekClientOptions() { };
            options.MessageTimeout = 5;

            var response = new PeerTransferResponse(1, true, 1, string.Empty);
            var waitKey = new WaitKey(MessageCode.PeerTransferResponse, "username", 1);

            var waiter = new Mock<IWaiter>();
            waiter.Setup(m => m.Wait<PeerTransferResponse>(It.Is<WaitKey>(w => w.Equals(waitKey)), null, null))
                .Returns(Task.FromResult(response));

            var conn = new Mock<IMessageConnection>();

            var s = new SoulseekClient("127.0.0.1", 1, options, messageWaiter: waiter.Object);
            s.SetProperty("State", SoulseekClientStates.Connected | SoulseekClientStates.LoggedIn);

            var ex = await Record.ExceptionAsync(async () => await s.InvokeMethod<Task<byte[]>>("DownloadInternalAsync", "username", "filename", 1, null, conn.Object));

            Assert.NotNull(ex);
            Assert.IsType<DownloadException>(ex);
            Assert.Contains("unreachable", ex.Message);
        }

        [Trait("Category", "DownloadAsync")]
        [Fact(DisplayName = "DownloadAsync throws DownloadException on PeerTransferRequest cancellation")]
        public async Task DownloadAsync_Throws_DownloadException_On_PeerTransferRequest_Cancellation()
        {
            var options = new SoulseekClientOptions() { };
            options.MessageTimeout = 5;

            var response = new PeerTransferResponse(1, false, 1, string.Empty);
            var responseWaitKey = new WaitKey(MessageCode.PeerTransferResponse, "username", 1);

            var waiter = new Mock<IWaiter>();
            waiter.Setup(m => m.Wait<PeerTransferResponse>(It.Is<WaitKey>(w => w.Equals(responseWaitKey)), null, null))
                .Returns(Task.FromResult(response));
            waiter.Setup(m => m.WaitIndefinitely<PeerTransferRequest>(It.IsAny<WaitKey>(), null))
                .Returns(Task.FromException<PeerTransferRequest>(new MessageCancelledException()));

            var conn = new Mock<IMessageConnection>();

            var s = new SoulseekClient("127.0.0.1", 1, options, messageWaiter: waiter.Object);
            s.SetProperty("State", SoulseekClientStates.Connected | SoulseekClientStates.LoggedIn);

            var ex = await Record.ExceptionAsync(async () => await s.InvokeMethod<Task<byte[]>>("DownloadInternalAsync", "username", "filename", 1, null, conn.Object));

            Assert.NotNull(ex);
            Assert.IsType<DownloadException>(ex);
            Assert.IsType<MessageCancelledException>(ex.InnerException);
        }

        [Trait("Category", "DownloadAsync")]
        [Fact(DisplayName = "DownloadAsync throws DownloadException on download cancellation")]
        public async Task DownloadAsync_Throws_DownloadException_On_Download_Cancellation()
        {
            var options = new SoulseekClientOptions() { };
            options.MessageTimeout = 5;

            var response = new PeerTransferResponse(1, false, 1, string.Empty);
            var responseWaitKey = new WaitKey(MessageCode.PeerTransferResponse, "username", 1);

            var request = new PeerTransferRequest(TransferDirection.Download, 1, "filename", 42);

            var waiter = new Mock<IWaiter>();
            waiter.Setup(m => m.Wait<PeerTransferResponse>(It.Is<WaitKey>(w => w.Equals(responseWaitKey)), null, null))
                .Returns(Task.FromResult(response));
            waiter.Setup(m => m.WaitIndefinitely<PeerTransferRequest>(It.IsAny<WaitKey>(), null))
                .Returns(Task.FromResult(request));
            waiter.Setup(m => m.Wait(It.IsAny<WaitKey>(), null, null))
                .Returns(Task.CompletedTask);
            waiter.Setup(m => m.WaitIndefinitely<byte[]>(It.IsAny<WaitKey>(), null))
                .Returns(Task.FromException<byte[]>(new OperationCanceledException()));

            var conn = new Mock<IMessageConnection>();

            var s = new SoulseekClient("127.0.0.1", 1, options, messageWaiter: waiter.Object);
            s.SetProperty("State", SoulseekClientStates.Connected | SoulseekClientStates.LoggedIn);

            var ex = await Record.ExceptionAsync(async () => await s.InvokeMethod<Task<byte[]>>("DownloadInternalAsync", "username", "filename", 1, null, conn.Object));

            Assert.NotNull(ex);
            Assert.IsType<DownloadException>(ex);
            Assert.IsType<OperationCanceledException>(ex.InnerException);
        }

        [Trait("Category", "DownloadAsync")]
        [Fact(DisplayName = "DownloadAsync throws DownloadException on download start timeout")]
        public async Task DownloadAsync_Throws_DownloadException_On_Download_Start_Timeout()
        {
            var options = new SoulseekClientOptions() { };
            options.MessageTimeout = 5;

            var response = new PeerTransferResponse(1, false, 1, "");
            var responseWaitKey = new WaitKey(MessageCode.PeerTransferResponse, "username", 1);

            var request = new PeerTransferRequest(TransferDirection.Download, 1, "filename", 42);
            var requestWaitKey = new WaitKey(MessageCode.PeerTransferRequest, "username", "filename");

            var waiter = new Mock<IWaiter>();
            waiter.Setup(m => m.Wait<PeerTransferResponse>(It.Is<WaitKey>(w => w.Equals(responseWaitKey)), null, null))
                .Returns(Task.FromResult(response));
            waiter.Setup(m => m.WaitIndefinitely<PeerTransferRequest>(It.IsAny<WaitKey>(), null))
                .Returns(Task.FromResult(request));
            waiter.Setup(m => m.Wait(It.IsAny<WaitKey>(), null, null))
                .Returns(Task.FromException<object>(new TimeoutException()));

            var conn = new Mock<IMessageConnection>();

            var s = new SoulseekClient("127.0.0.1", 1, options, messageWaiter: waiter.Object);
            s.SetProperty("State", SoulseekClientStates.Connected | SoulseekClientStates.LoggedIn);

            var ex = await Record.ExceptionAsync(async () => await s.InvokeMethod<Task<byte[]>>("DownloadInternalAsync", "username", "filename", 1, null, conn.Object));

            Assert.NotNull(ex);
            Assert.IsType<DownloadException>(ex);
            Assert.IsType<TimeoutException>(ex.InnerException);
        }

        [Trait("Category", "DownloadAsync")]
        [Fact(DisplayName = "DownloadAsync returns expected data on completion")]
        public async Task DownloadAsync_Returns_Expected_Data_On_Completion()
        {
            var options = new SoulseekClientOptions() { };
            options.MessageTimeout = 5;

            var response = new PeerTransferResponse(1, false, 1, "");
            var responseWaitKey = new WaitKey(MessageCode.PeerTransferResponse, "username", 1);

            var request = new PeerTransferRequest(TransferDirection.Download, 1, "filename", 42);
            var requestWaitKey = new WaitKey(MessageCode.PeerTransferRequest, "username", "filename");

            var data = new byte[] { 0x0, 0x1, 0x2, 0x3 };

            var waiter = new Mock<IWaiter>();
            waiter.Setup(m => m.Wait<PeerTransferResponse>(It.Is<WaitKey>(w => w.Equals(responseWaitKey)), null, null))
                .Returns(Task.FromResult(response));
            waiter.Setup(m => m.WaitIndefinitely<PeerTransferRequest>(It.IsAny<WaitKey>(), null))
                .Returns(Task.FromResult(request));
            waiter.Setup(m => m.Wait(It.IsAny<WaitKey>(), null, null))
                .Returns(Task.CompletedTask);
            waiter.Setup(m => m.WaitIndefinitely<byte[]>(It.IsAny<WaitKey>(), null))
                .Returns(Task.FromResult<byte[]>(data));

            var conn = new Mock<IMessageConnection>();

            var s = new SoulseekClient("127.0.0.1", 1, options, messageWaiter: waiter.Object);
            s.SetProperty("State", SoulseekClientStates.Connected | SoulseekClientStates.LoggedIn);

            byte[] downloadedData = null;
            var ex = await Record.ExceptionAsync(async () => downloadedData = await s.InvokeMethod<Task<byte[]>>("DownloadInternalAsync", "username", "filename", 1, null, conn.Object));

            Assert.Null(ex);
            Assert.Equal(data, downloadedData);
        }

        [Trait("Category", "DownloadAsync")]
        [Fact(DisplayName = "DownloadAsync raises expected events on success")]
        public async Task DownloadAsync_Raises_Expected_Events_On_Success()
        {
            var options = new SoulseekClientOptions() { };
            options.MessageTimeout = 5;

            var response = new PeerTransferResponse(1, false, 1, "");
            var responseWaitKey = new WaitKey(MessageCode.PeerTransferResponse, "username", 1);

            var request = new PeerTransferRequest(TransferDirection.Download, 1, "filename", 42);
            var requestWaitKey = new WaitKey(MessageCode.PeerTransferRequest, "username", "filename");

            var data = new byte[] { 0x0, 0x1, 0x2, 0x3 };

            var waiter = new Mock<IWaiter>();
            waiter.Setup(m => m.Wait<PeerTransferResponse>(It.Is<WaitKey>(w => w.Equals(responseWaitKey)), null, null))
                .Returns(Task.FromResult(response));
            waiter.Setup(m => m.WaitIndefinitely<PeerTransferRequest>(It.IsAny<WaitKey>(), null))
                .Returns(Task.FromResult(request));
            waiter.Setup(m => m.Wait(It.IsAny<WaitKey>(), null, null))
                .Returns(Task.CompletedTask);
            waiter.Setup(m => m.WaitIndefinitely<byte[]>(It.IsAny<WaitKey>(), null))
                .Returns(Task.FromResult<byte[]>(data));

            var conn = new Mock<IMessageConnection>();

            var s = new SoulseekClient("127.0.0.1", 1, options, messageWaiter: waiter.Object);
            s.SetProperty("State", SoulseekClientStates.Connected | SoulseekClientStates.LoggedIn);

            var events = new List<DownloadStateChangedEventArgs>();

            s.DownloadStateChanged += (sender, e) =>
            {
                events.Add(e);
            };

            await s.InvokeMethod<Task<byte[]>>("DownloadInternalAsync", "username", "filename", 1, null, conn.Object);

            Assert.Equal(5, events.Count);

            Assert.Equal(DownloadStates.None, events[0].PreviousState);
            Assert.Equal(DownloadStates.Queued, events[0].State);

            Assert.Equal(DownloadStates.Queued, events[1].PreviousState);
            Assert.Equal(DownloadStates.Initializing, events[1].State);

            Assert.Equal(DownloadStates.Initializing, events[2].PreviousState);
            Assert.Equal(DownloadStates.InProgress, events[2].State);

            Assert.Equal(DownloadStates.InProgress, events[3].PreviousState);
            Assert.Equal(DownloadStates.Succeeded, events[3].State);

            Assert.Equal(DownloadStates.Succeeded, events[4].PreviousState);
            Assert.Equal(DownloadStates.Completed | DownloadStates.Succeeded, events[4].State);
        }

        [Trait("Category", "DownloadAsync")]
        [Fact(DisplayName = "DownloadAsync raises Download events on failure")]
        public async Task DownloadAsync_Raises_Expected_Final_Event_On_Failure()
        {
            var options = new SoulseekClientOptions() { };
            options.MessageTimeout = 5;

            var response = new PeerTransferResponse(1, false, 1, "");
            var responseWaitKey = new WaitKey(MessageCode.PeerTransferResponse, "username", 1);

            var request = new PeerTransferRequest(TransferDirection.Download, 1, "filename", 42);
            var requestWaitKey = new WaitKey(MessageCode.PeerTransferRequest, "username", "filename");

            var data = new byte[] { 0x0, 0x1, 0x2, 0x3 };

            var waiter = new Mock<IWaiter>();
            waiter.Setup(m => m.Wait<PeerTransferResponse>(It.Is<WaitKey>(w => w.Equals(responseWaitKey)), null, null))
                .Returns(Task.FromResult(response));
            waiter.Setup(m => m.WaitIndefinitely<PeerTransferRequest>(It.IsAny<WaitKey>(), null))
                .Returns(Task.FromResult(request));
            waiter.Setup(m => m.Wait(It.IsAny<WaitKey>(), null, null))
                .Returns(Task.CompletedTask);
            waiter.Setup(m => m.WaitIndefinitely<byte[]>(It.IsAny<WaitKey>(), null))
                .Returns(Task.FromException<byte[]>(new MessageReadException()));

            var conn = new Mock<IMessageConnection>();

            var s = new SoulseekClient("127.0.0.1", 1, options, messageWaiter: waiter.Object);
            s.SetProperty("State", SoulseekClientStates.Connected | SoulseekClientStates.LoggedIn);

            var events = new List<DownloadStateChangedEventArgs>();

            s.DownloadStateChanged += (sender, e) =>
            {
                events.Add(e);
            };

            var ex = await Record.ExceptionAsync(async () => await s.InvokeMethod<Task<byte[]>>("DownloadInternalAsync", "username", "filename", 1, null, conn.Object));

            Assert.Equal(DownloadStates.Errored, events[events.Count - 1].PreviousState);
            Assert.Equal(DownloadStates.Completed | DownloadStates.Errored, events[events.Count - 1].State);
        }

        [Trait("Category", "DownloadAsync")]
        [Fact(DisplayName = "DownloadAsync raises Download events on timeout")]
        public async Task DownloadAsync_Raises_Expected_Final_Event_On_Timeout()
        {
            var options = new SoulseekClientOptions() { };
            options.MessageTimeout = 5;

            var response = new PeerTransferResponse(1, false, 1, "");
            var responseWaitKey = new WaitKey(MessageCode.PeerTransferResponse, "username", 1);

            var request = new PeerTransferRequest(TransferDirection.Download, 1, "filename", 42);
            var requestWaitKey = new WaitKey(MessageCode.PeerTransferRequest, "username", "filename");

            var data = new byte[] { 0x0, 0x1, 0x2, 0x3 };

            var waiter = new Mock<IWaiter>();
            waiter.Setup(m => m.Wait<PeerTransferResponse>(It.Is<WaitKey>(w => w.Equals(responseWaitKey)), null, null))
                .Returns(Task.FromResult(response));
            waiter.Setup(m => m.WaitIndefinitely<PeerTransferRequest>(It.IsAny<WaitKey>(), null))
                .Returns(Task.FromResult(request));
            waiter.Setup(m => m.Wait(It.IsAny<WaitKey>(), null, null))
                .Returns(Task.CompletedTask);
            waiter.Setup(m => m.WaitIndefinitely<byte[]>(It.IsAny<WaitKey>(), null))
                .Returns(Task.FromException<byte[]>(new TimeoutException()));

            var conn = new Mock<IMessageConnection>();

            var s = new SoulseekClient("127.0.0.1", 1, options, messageWaiter: waiter.Object);
            s.SetProperty("State", SoulseekClientStates.Connected | SoulseekClientStates.LoggedIn);

            var events = new List<DownloadStateChangedEventArgs>();

            s.DownloadStateChanged += (sender, e) =>
            {
                events.Add(e);
            };

            var ex = await Record.ExceptionAsync(async () => await s.InvokeMethod<Task<byte[]>>("DownloadInternalAsync", "username", "filename", 1, null, conn.Object));

            Assert.Equal(DownloadStates.TimedOut, events[events.Count - 1].PreviousState);
            Assert.Equal(DownloadStates.Completed | DownloadStates.TimedOut, events[events.Count - 1].State);
        }

        [Trait("Category", "DownloadAsync")]
        [Fact(DisplayName = "DownloadAsync raises Download events on cancellation")]
        public async Task DownloadAsync_Raises_Expected_Final_Event_On_Cancellation()
        {
            var options = new SoulseekClientOptions() { };
            options.MessageTimeout = 5;

            var response = new PeerTransferResponse(1, false, 1, "");
            var responseWaitKey = new WaitKey(MessageCode.PeerTransferResponse, "username", 1);

            var request = new PeerTransferRequest(TransferDirection.Download, 1, "filename", 42);
            var requestWaitKey = new WaitKey(MessageCode.PeerTransferRequest, "username", "filename");

            var data = new byte[] { 0x0, 0x1, 0x2, 0x3 };

            var waiter = new Mock<IWaiter>();
            waiter.Setup(m => m.Wait<PeerTransferResponse>(It.Is<WaitKey>(w => w.Equals(responseWaitKey)), null, null))
                .Returns(Task.FromResult(response));
            waiter.Setup(m => m.WaitIndefinitely<PeerTransferRequest>(It.IsAny<WaitKey>(), null))
                .Returns(Task.FromResult(request));
            waiter.Setup(m => m.Wait(It.IsAny<WaitKey>(), null, null))
                .Returns(Task.CompletedTask);
            waiter.Setup(m => m.WaitIndefinitely<byte[]>(It.IsAny<WaitKey>(), null))
                .Returns(Task.FromException<byte[]>(new OperationCanceledException()));

            var conn = new Mock<IMessageConnection>();

            var s = new SoulseekClient("127.0.0.1", 1, options, messageWaiter: waiter.Object);
            s.SetProperty("State", SoulseekClientStates.Connected | SoulseekClientStates.LoggedIn);

            var events = new List<DownloadStateChangedEventArgs>();

            s.DownloadStateChanged += (sender, e) =>
            {
                events.Add(e);
            };

            var ex = await Record.ExceptionAsync(async () => await s.InvokeMethod<Task<byte[]>>("DownloadInternalAsync", "username", "filename", 1, null, conn.Object));

            Assert.Equal(DownloadStates.Cancelled, events[events.Count - 1].PreviousState);
            Assert.Equal(DownloadStates.Completed | DownloadStates.Cancelled, events[events.Count - 1].State);
        }
    }
}
