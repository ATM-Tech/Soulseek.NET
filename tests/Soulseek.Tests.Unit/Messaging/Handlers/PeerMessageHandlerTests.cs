﻿// <copyright file="PeerMessageHandlerTests.cs" company="JP Dillingham">
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

namespace Soulseek.Tests.Unit.Messaging.Handlers
{
    using System;
    using System.Collections.Concurrent;
    using System.Collections.Generic;
    using System.Linq;
    using System.Net;
    using AutoFixture.Xunit2;
    using Moq;
    using Soulseek.Exceptions;
    using Soulseek.Messaging;
    using Soulseek.Messaging.Handlers;
    using Soulseek.Messaging.Messages;
    using Soulseek.Network;
    using Xunit;

    public class PeerMessageHandlerTests
    {
        [Trait("Category", "Diagnostic")]
        [Theory(DisplayName = "Creates diagnostic on message"), AutoData]
        public void Creates_Diagnostic_On_Message(string username, IPAddress ip, int port)
        {
            List<string> messages = new List<string>();

            var (handler, mocks) = GetFixture(username, ip, port);

            mocks.Diagnostic.Setup(m => m.Debug(It.IsAny<string>()))
                .Callback<string>(msg => messages.Add(msg));

            var message = new MessageBuilder()
                .WriteCode(MessageCode.Server.ParentMinSpeed)
                .WriteInteger(1)
                .Build();

            handler.HandleMessage(mocks.Connection.Object, message);

            Assert.Contains(messages, m => m.IndexOf("peer message received", StringComparison.InvariantCultureIgnoreCase) > -1);
        }

        [Trait("Category", "Message")]
        [Theory(DisplayName = "Creates diagnostic on PeerUploadFailed message"), AutoData]
        public void Creates_Diagnostic_On_PeerUploadFailed_Message(string username, IPAddress ip, int port)
        {
            List<string> messages = new List<string>();

            var (handler, mocks) = GetFixture(username, ip, port);

            mocks.Diagnostic.Setup(m => m.Debug(It.IsAny<string>()))
                .Callback<string>(msg => messages.Add(msg));

            var message = new MessageBuilder()
                .WriteCode(MessageCode.Peer.UploadFailed)
                .WriteString("foo")
                .Build();

            handler.HandleMessage(mocks.Connection.Object, message);

            Assert.Contains(messages, m => m.IndexOf("peer message received", StringComparison.InvariantCultureIgnoreCase) > -1);
            Assert.Contains(messages, m => m.IndexOf("upload", StringComparison.InvariantCultureIgnoreCase) > -1 && m.IndexOf("failed", StringComparison.InvariantCultureIgnoreCase) > -1);
        }

        [Trait("Category", "Message")]
        [Theory(DisplayName = "Creates diagnostic on Exception"), AutoData]
        public void Creates_Diagnostic_On_Exception(string username, IPAddress ip, int port)
        {
            List<string> messages = new List<string>();

            var (handler, mocks) = GetFixture(username, ip, port);

            mocks.Diagnostic.Setup(m => m.Warning(It.IsAny<string>(), It.IsAny<Exception>()))
                .Callback<string, Exception>((msg, ex) => messages.Add(msg));

            var message = new MessageBuilder()
                .WriteCode(MessageCode.Peer.TransferResponse)
                .Build(); // malformed message

            handler.HandleMessage(mocks.Connection.Object, message);

            Assert.Contains(messages, m => m.IndexOf("error handling peer message", StringComparison.InvariantCultureIgnoreCase) > -1);
        }

        [Trait("Category", "Message")]
        [Theory(DisplayName = "Completes wait for TransferResponse"), AutoData]
        public void Completes_Wait_For_TransferResponse(string username, IPAddress ip, int port, int token, int fileSize)
        {
            var (handler, mocks) = GetFixture(username, ip, port);

            var msg = new TransferResponse(token, fileSize).ToByteArray();

            handler.HandleMessage(mocks.Connection.Object, msg);

            mocks.Waiter.Verify(m => m.Complete(new WaitKey(MessageCode.Peer.TransferResponse, username, token), It.Is<TransferResponse>(r => r.Token == token)), Times.Once);
        }

        [Trait("Category", "Message")]
        [Theory(DisplayName = "Completes wait for PeerInfoResponse"), AutoData]
        public void Completes_Wait_For_PeerInfoResponse(string username, IPAddress ip, int port, string description, byte[] picture, int uploadSlots, int queueLength, bool hasFreeSlot)
        {
            var (handler, mocks) = GetFixture(username, ip, port);

            var msg = new MessageBuilder()
                .WriteCode(MessageCode.Peer.InfoResponse)
                .WriteString(description)
                .WriteByte(1)
                .WriteInteger(picture.Length)
                .WriteBytes(picture)
                .WriteInteger(uploadSlots)
                .WriteInteger(queueLength)
                .WriteByte((byte)(hasFreeSlot ? 1 : 0))
                .Build();

            handler.HandleMessage(mocks.Connection.Object, msg);

            mocks.Waiter.Verify(m => m.Complete(new WaitKey(MessageCode.Peer.InfoResponse, username), It.IsAny<UserInfoResponse>()), Times.Once);
        }

        [Trait("Category", "Message")]
        [Theory(DisplayName = "Completes wait for PeerPlaceInQueueResponse"), AutoData]
        public void Completes_Wait_For_PeerPlaceInQueueResponse(string username, IPAddress ip, int port, string filename, int placeInQueue)
        {
            var (handler, mocks) = GetFixture(username, ip, port);

            var msg = new MessageBuilder()
                .WriteCode(MessageCode.Peer.PlaceInQueueResponse)
                .WriteString(filename)
                .WriteInteger(placeInQueue)
                .Build();

            handler.HandleMessage(mocks.Connection.Object, msg);

            mocks.Waiter.Verify(
                m => m.Complete(
                    new WaitKey(MessageCode.Peer.PlaceInQueueResponse, username, filename),
                    It.Is<PlaceInQueueResponse>(r => r.Filename == filename && r.PlaceInQueue == placeInQueue)), Times.Once);
        }

        [Trait("Category", "Message")]
        [Theory(DisplayName = "Completes wait for PeerBrowseResponse"), AutoData]
        public void Completes_Wait_For_PeerBrowseResponse(string username, IPAddress ip, int port, string directoryName)
        {
            var (handler, mocks) = GetFixture(username, ip, port);

            var msg = new MessageBuilder()
                .WriteCode(MessageCode.Peer.BrowseResponse)
                .WriteInteger(1) // directory count
                .WriteString(directoryName) // first directory name
                .WriteInteger(0) // first directory file count
                .Compress()
                .Build();

            handler.HandleMessage(mocks.Connection.Object, msg);

            mocks.Waiter.Verify(m => m.Complete(new WaitKey(MessageCode.Peer.BrowseResponse, username), It.Is<BrowseResponse>(r => r.Directories.First().Directoryname == directoryName)), Times.Once);
        }

        [Trait("Category", "Message")]
        [Theory(DisplayName = "Throws wait for PeerBrowseResponse given bad message"), AutoData]
        public void Throws_Wait_For_PeerBrowseResponse_Given_Bad_Message(string username, IPAddress ip, int port)
        {
            var (handler, mocks) = GetFixture(username, ip, port);

            var msg = new MessageBuilder()
                .WriteCode(MessageCode.Peer.BrowseResponse)
                .Build();

            handler.HandleMessage(mocks.Connection.Object, msg);

            mocks.Waiter.Verify(m => m.Throw(new WaitKey(MessageCode.Peer.BrowseResponse, username), It.IsAny<MessageReadException>()), Times.Once);
        }

        [Trait("Category", "Message")]
        [Theory(DisplayName = "Ignores inactive search response"), AutoData]
        public void Ignores_Inactive_Search_Response(string username, IPAddress ip, int port, int token, byte freeUploadSlots, int uploadSpeed, int queueLength)
        {
            var (handler, mocks) = GetFixture(username, ip, port);

            var msg = new MessageBuilder()
                .WriteCode(MessageCode.Peer.SearchResponse)
                .WriteString(username)
                .WriteInteger(token)
                .WriteInteger(1) // file count
                .WriteByte(0x2) // code
                .WriteString("filename") // filename
                .WriteLong(3) // size
                .WriteString("ext") // extension
                .WriteInteger(1) // attribute count
                .WriteInteger((int)FileAttributeType.BitDepth) // attribute[0].type
                .WriteInteger(4) // attribute[0].value
                .WriteByte(freeUploadSlots)
                .WriteInteger(uploadSpeed)
                .WriteLong(queueLength)
                .WriteBytes(new byte[4]) // unknown 4 bytes
                .Compress()
                .Build();

            var ex = Record.Exception(() => handler.HandleMessage(mocks.Connection.Object, msg));

            Assert.Null(ex);
        }

        [Trait("Category", "Message")]
        [Theory(DisplayName = "Throws TransferRequest wait on PeerQueueFailed"), AutoData]
        public void Throws_TransferRequest_Wait_On_PeerQueueFailed(string username, IPAddress ip, int port, string filename, string message)
        {
            var (handler, mocks) = GetFixture(username, ip, port);

            var msg = new MessageBuilder()
                .WriteCode(MessageCode.Peer.QueueFailed)
                .WriteString(filename)
                .WriteString(message)
                .Build();

            handler.HandleMessage(mocks.Connection.Object, msg);

            mocks.Waiter.Verify(m => m.Throw(new WaitKey(MessageCode.Peer.TransferRequest, username, filename), It.IsAny<Exception>()), Times.Once);
        }

        [Trait("Category", "Message")]
        [Theory(DisplayName = "Appends active search response"), AutoData]
        public void Appends_Active_Search_Response(string username, IPAddress ip, int port, int token, byte freeUploadSlots, int uploadSpeed, int queueLength)
        {
            var (handler, mocks) = GetFixture(username, ip, port);

            var msg = new MessageBuilder()
                .WriteCode(MessageCode.Peer.SearchResponse)
                .WriteString(username)
                .WriteInteger(token)
                .WriteInteger(1) // file count
                .WriteByte(0x2) // code
                .WriteString("filename") // filename
                .WriteLong(3) // size
                .WriteString("ext") // extension
                .WriteInteger(1) // attribute count
                .WriteInteger((int)FileAttributeType.BitDepth) // attribute[0].type
                .WriteInteger(4) // attribute[0].value
                .WriteByte(freeUploadSlots)
                .WriteInteger(uploadSpeed)
                .WriteLong(queueLength)
                .WriteBytes(new byte[4]) // unknown 4 bytes
                .Compress()
                .Build();

            using (var search = new Search("foo", token)
            {
                State = SearchStates.InProgress,
            })
            {
                mocks.Searches.TryAdd(token, search);

                handler.HandleMessage(mocks.Connection.Object, msg);

                Assert.Single(search.Responses);
                Assert.Contains(search.Responses, r => r.Username == username && r.Token == token);
            }
        }

        private (PeerMessageHandler Handler, Mocks Mocks) GetFixture(string username = null, IPAddress ip = null, int port = 0)
        {
            var mocks = new Mocks();

            mocks.Connection.Setup(m => m.Username)
                .Returns(username ?? "username");
            mocks.Connection.Setup(m => m.IPAddress)
                .Returns(ip ?? IPAddress.Parse("0.0.0.0"));
            mocks.Connection.Setup(m => m.Port)
                .Returns(port);

            var handler = new PeerMessageHandler(
                mocks.Client.Object,
                mocks.Downloads,
                mocks.Searches,
                mocks.Waiter.Object,
                mocks.Diagnostic.Object);

            return (handler, mocks);
        }

        private class Mocks
        {
            public Mock<ISoulseekClient> Client { get; } = new Mock<ISoulseekClient>();
            public Mock<IWaiter> Waiter { get; } = new Mock<IWaiter>();
            public ConcurrentDictionary<int, Transfer> Downloads { get; } = new ConcurrentDictionary<int, Transfer>();
            public ConcurrentDictionary<int, Search> Searches { get; } = new ConcurrentDictionary<int, Search>();
            public Mock<IDiagnosticFactory> Diagnostic { get; } = new Mock<IDiagnosticFactory>();
            public Mock<IMessageConnection> Connection { get; } = new Mock<IMessageConnection>();
        }
    }
}
