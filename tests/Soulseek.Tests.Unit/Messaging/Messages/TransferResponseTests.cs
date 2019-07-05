﻿// <copyright file="TransferResponseTests.cs" company="JP Dillingham">
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

    public class TransferResponseTests
    {
        [Trait("Category", "Instantiation")]
        [Theory(DisplayName = "Instantiates with the proper data when disallowed"), AutoData]
        public void Instantiates_With_The_Proper_Data_When_Disallowed(int token, string msg)
        {
            TransferResponse response = null;
            Exception ex = null;

            ex = Record.Exception(() => response = new TransferResponse(token, msg));

            Assert.Null(ex);

            Assert.Equal(token, response.Token);
            Assert.False(response.Allowed);

            Assert.Equal(msg, response.Message);
        }

        [Trait("Category", "Instantiation")]
        [Theory(DisplayName = "Instantiates with the proper data when allowed"), AutoData]
        public void Instantiates_With_The_Proper_Data_When_Allowed(int token, long size)
        {
            TransferResponse response = null;
            Exception ex = null;

            ex = Record.Exception(() => response = new TransferResponse(token, size));

            Assert.Null(ex);

            Assert.Equal(token, response.Token);
            Assert.True(response.Allowed);
            Assert.Equal(size, response.FileSize);
        }

        [Trait("Category", "Parse")]
        [Fact(DisplayName = "Parse throws MessageExcepton on code mismatch")]
        public void Parse_Throws_MessageException_On_Code_Mismatch()
        {
            var msg = new MessageBuilder()
                .WriteCode(MessageCode.PeerBrowseRequest)
                .Build();

            var ex = Record.Exception(() => TransferResponse.Parse(msg));

            Assert.NotNull(ex);
            Assert.IsType<MessageException>(ex);
        }

        [Trait("Category", "Parse")]
        [Fact(DisplayName = "Parse throws MessageReadException on missing data")]
        public void Parse_Throws_MessageReadException_On_Missing_Data()
        {
            var msg = new MessageBuilder()
                .WriteCode(MessageCode.PeerTransferResponse)
                .Build();

            var ex = Record.Exception(() => TransferResponse.Parse(msg));

            Assert.NotNull(ex);
            Assert.IsType<MessageReadException>(ex);
        }

        [Trait("Category", "Parse")]
        [Theory(DisplayName = "Parse returns expected data when allowed"), AutoData]
        public void Parse_Returns_Expected_Data_When_Allowed(int token, long size)
        {
            var msg = new MessageBuilder()
                .WriteCode(MessageCode.PeerTransferResponse)
                .WriteInteger(token)
                .WriteByte(0x1)
                .WriteLong(size)
                .Build();

            var response = TransferResponse.Parse(msg);

            Assert.Equal(token, response.Token);
            Assert.True(response.Allowed);
            Assert.Equal(size, response.FileSize);
        }

        [Trait("Category", "Parse")]
        [Theory(DisplayName = "Parse returns expected data when disallowed"), AutoData]
        public void Parse_Returns_Expected_Data_When_Disallowed(int token, string message)
        {
            var msg = new MessageBuilder()
                .WriteCode(MessageCode.PeerTransferResponse)
                .WriteInteger(token)
                .WriteByte(0x0)
                .WriteString(message)
                .Build();

            var response = TransferResponse.Parse(msg);

            Assert.Equal(token, response.Token);
            Assert.False(response.Allowed);
            Assert.Equal(message, response.Message);
        }

        [Trait("Category", "ToMessage")]
        [Theory(DisplayName = "ToMessage constructs the correct Message when allowed"), AutoData]
        public void ToMessage_Constructs_The_Correct_Message_When_Allowed(int token, long size)
        {
            var a = new TransferResponse(token, size);
            var msg = a.ToMessage();

            var reader = new MessageReader<MessageCode>(msg);
            var code = reader.ReadCode();

            Assert.Equal(MessageCode.PeerTransferResponse, code);

            // code + token + allowed + size
            Assert.Equal(4 + 4 + 1 + 8, msg.Length);
            Assert.Equal(token, reader.ReadInteger());
            Assert.Equal(1, reader.ReadByte());
            Assert.Equal(size, reader.ReadLong());
        }

        [Trait("Category", "ToMessage")]
        [Theory(DisplayName = "ToMessage constructs the correct Message when disallowed"), AutoData]
        public void ToMessage_Constructs_The_Correct_Message_When_Disallowed(int token, string message)
        {
            var a = new TransferResponse(token, message);
            var msg = a.ToMessage();

            var code = new MessageReader<MessageCode>(msg).ReadCode();

            Assert.Equal(MessageCode.PeerTransferResponse, code);

            // code + token + allowed + message len + message
            Assert.Equal(4 + 4 + 1 + 4 + message.Length, msg.Length);

            var reader = new MessageReader<MessageCode>(msg);

            Assert.Equal(token, reader.ReadInteger());
            Assert.Equal(0, reader.ReadByte());
            Assert.Equal(message, reader.ReadString());
        }
    }
}
