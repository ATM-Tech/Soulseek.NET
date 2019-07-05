﻿// <copyright file="TransferRequestTests.cs" company="JP Dillingham">
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

namespace Soulseek.Tests.Unit.Messaging.Messages
{
    using System;
    using AutoFixture.Xunit2;
    using Soulseek.Exceptions;
    using Soulseek.Messaging;
    using Soulseek.Messaging.Messages;
    using Xunit;

    public class TransferRequestTests
    {
        private Random Random { get; } = new Random();

        [Trait("Category", "Instantiation")]
        [Fact(DisplayName = "Instantiates with the given data")]
        public void Instantiates_With_The_Given_Data()
        {
            var dir = (TransferDirection)Random.Next(2);
            var token = Random.Next();
            var file = Guid.NewGuid().ToString();
            var size = Random.Next();

            TransferRequest response = null;

            var ex = Record.Exception(() => response = new TransferRequest(dir, token, file, size));

            Assert.Null(ex);

            Assert.Equal(dir, response.Direction);
            Assert.Equal(token, response.Token);
            Assert.Equal(file, response.Filename);
            Assert.Equal(size, response.FileSize);
        }

        [Trait("Category", "Parse")]
        [Fact(DisplayName = "Parse throws MessageExcepton on code mismatch")]
        public void Parse_Throws_MessageException_On_Code_Mismatch()
        {
            var msg = new MessageBuilder()
                .WriteCode(MessageCode.PeerBrowseRequest)
                .Build();

            var ex = Record.Exception(() => TransferRequest.Parse(msg));

            Assert.NotNull(ex);
            Assert.IsType<MessageException>(ex);
        }

        [Trait("Category", "Parse")]
        [Fact(DisplayName = "Parse throws MessageReadException on missing data")]
        public void Parse_Throws_MessageReadException_On_Missing_Data()
        {
            var msg = new MessageBuilder()
                .WriteCode(MessageCode.PeerTransferRequest)
                .Build();

            var ex = Record.Exception(() => TransferRequest.Parse(msg));

            Assert.NotNull(ex);
            Assert.IsType<MessageReadException>(ex);
        }

        [Trait("Category", "Parse")]
        [Fact(DisplayName = "Parse returns expected data")]
        public void Parse_Returns_Expected_Data()
        {
            var dir = Random.Next(2);
            var token = Random.Next();
            var file = Guid.NewGuid().ToString();
            var size = Random.Next();

            var msg = new MessageBuilder()
                .WriteCode(MessageCode.PeerTransferRequest)
                .WriteInteger(dir)
                .WriteInteger(token)
                .WriteString(file)
                .WriteLong(size)
                .Build();

            var response = TransferRequest.Parse(msg);

            Assert.Equal(dir, (int)response.Direction);
            Assert.Equal(token, response.Token);
            Assert.Equal(file, response.Filename);
            Assert.Equal(size, response.FileSize);
        }

        [Trait("Category", "ToMessage")]
        [Theory(DisplayName = "ToMessage constructs the correct Message"), AutoData]
        public void ToMessage_Constructs_The_Correct_Message(TransferDirection dir, int token, string file, long size)
        {
            var a = new TransferRequest(dir, token, file, size);
            var msg = a.ToMessage();

            var reader = new MessageReader<MessageCode>(msg);
            var code = reader.ReadCode();

            Assert.Equal(MessageCode.PeerTransferRequest, code);

            // code + direction + token + file length + filename + size
            Assert.Equal(4 + 4 + 4 + 4 + file.Length + 8, msg.Length);
            Assert.Equal(0, reader.ReadInteger()); // direction
            Assert.Equal(token, reader.ReadInteger());
            Assert.Equal(file, reader.ReadString());
            Assert.Equal(size, reader.ReadLong());
        }
    }
}
