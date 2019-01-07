﻿// <copyright file="MessageReaderTests.cs" company="JP Dillingham">
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

namespace Soulseek.NET.Tests.Unit.Messaging
{
    using Soulseek.NET.Exceptions;
    using Soulseek.NET.Messaging;
    using System;
    using Xunit;

    public class MessageReaderTests
    {
        [Trait("Category", "Instantiation")]
        [Fact(DisplayName = "Instantiation throws ArgumentNullException given null byte array")]
        public void Instantiation_Throws_ArgumentNullException_Given_Null_Byte_Array()
        {
            byte[] bytes = null;
            var ex = Record.Exception(() => new MessageReader(bytes));

            Assert.NotNull(ex);
            Assert.IsType<ArgumentNullException>(ex);
        }

        [Trait("Category", "Instantiation")]
        [Fact(DisplayName = "Instantiation throws ArgumentOutOfRangeException given short byte array")]
        public void Instantiation_Throws_ArgumentOutOfRangeException_Given_Short_Byte_Array()
        {
            byte[] bytes = new byte[7];
            var ex = Record.Exception(() => new MessageReader(bytes));

            Assert.NotNull(ex);
            Assert.IsType<ArgumentOutOfRangeException>(ex);
        }

        [Trait("Category", "Instantiation")]
        [Fact(DisplayName = "Instantiation throws ArgumentOutOfRangeException given empty byte array")]
        public void Instantiation_Throws_ArgumentOutOfRangeException_Given_Empty_Byte_Array()
        {
            byte[] bytes = new byte[0];
            var ex = Record.Exception(() => new MessageReader(bytes));

            Assert.NotNull(ex);
            Assert.IsType<ArgumentOutOfRangeException>(ex);
        }

        [Trait("Category", "Instantiation")]
        [Fact(DisplayName = "Instantiation throws ArgumentNullException given null message")]
        public void Instantiation_Throws_ArgumentNullException_Given_Null_Message()
        {
            Message msg = null;
            var ex = Record.Exception(() => new MessageReader(msg));

            Assert.NotNull(ex);
            Assert.IsType<ArgumentNullException>(ex);
        }

        [Trait("Category", "Instantiation")]
        [Fact(DisplayName = "Instantiates properly given valid Message")]
        public void Instantiates_Properly_Given_Valid_Message()
        {
            var num = new Random().Next();
            var msg = new MessageBuilder()
                .Code(MessageCode.PeerBrowseRequest)
                .WriteInteger(num)
                .Build();

            var reader = new MessageReader(msg);

            Assert.Equal(MessageCode.PeerBrowseRequest, reader.Code);
            Assert.Equal(BitConverter.GetBytes(num), reader.Payload);
            Assert.Equal(0, reader.Position);
            Assert.Equal(num, reader.ReadInteger());
            Assert.Equal(4, reader.Position);
        }

        [Trait("Category", "Instantiation")]
        [Fact(DisplayName = "Instantiates properly given valid byte array")]
        public void Instantiates_Properly_Given_Valid_Byte_Array()
        {
            var num = new Random().Next();
            var msgBytes = new MessageBuilder()
                .Code(MessageCode.PeerBrowseRequest)
                .WriteInteger(num)
                .Build()
                .ToByteArray();

            var reader = new MessageReader(msgBytes);

            Assert.Equal(MessageCode.PeerBrowseRequest, reader.Code);
            Assert.Equal(BitConverter.GetBytes(num), reader.Payload);
            Assert.Equal(0, reader.Position);
            Assert.Equal(num, reader.ReadInteger());
            Assert.Equal(4, reader.Position);
        }

        [Trait("Category", "Seek")]
        [Fact(DisplayName = "Seek changes position")]
        public void Seek_Changes_Position()
        {
            var num = new Random().Next();
            var msg = new MessageBuilder()
                .Code(MessageCode.PeerBrowseRequest)
                .WriteInteger(num)
                .Build();

            var reader = new MessageReader(msg);

            var initial = reader.Position;

            reader.Seek(2);

            Assert.Equal(0, initial);
            Assert.Equal(2, reader.Position);
        }

        [Trait("Category", "Seek")]
        [Fact(DisplayName = "Seek throws ArgumentOutOfRangeException on negative")]
        public void Seek_Throws_ArgumentOutOfRangeException_On_Negative()
        {
            var num = new Random().Next();
            var msg = new MessageBuilder()
                .Code(MessageCode.PeerBrowseRequest)
                .WriteInteger(num)
                .Build();

            var reader = new MessageReader(msg);

            var ex = Record.Exception(() => reader.Seek(-1));

            Assert.NotNull(ex);
            Assert.IsType<ArgumentOutOfRangeException>(ex);
        }


        [Trait("Category", "Seek")]
        [Fact(DisplayName = "Seek throws ArgumentOutOfRangeException on too large")]
        public void Seek_Throws_ArgumentOutOfRangeException_On_Too_Large()
        {
            var num = new Random().Next();
            var msg = new MessageBuilder()
                .Code(MessageCode.PeerBrowseRequest)
                .WriteInteger(num)
                .Build();

            var reader = new MessageReader(msg);

            var ex = Record.Exception(() => reader.Seek(5));

            Assert.NotNull(ex);
            Assert.IsType<ArgumentOutOfRangeException>(ex);
        }

        [Trait("Category", "ReadInteger")]
        [Fact(DisplayName = "ReadInteger returns expected data")]
        public void ReadInteger_Returns_Expected_Data()
        {
            var num = new Random().Next();
            var msg = new MessageBuilder()
                .Code(MessageCode.PeerBrowseRequest)
                .WriteInteger(num)
                .Build();

            var reader = new MessageReader(msg);

            Assert.Equal(num, reader.ReadInteger());
        }

        [Trait("Category", "ReadInteger")]
        [Fact(DisplayName = "ReadInteger throws MessageReadException if no data")]
        public void ReadInteger_Throws_MessageReadException_If_No_Data()
        {
            var msg = new MessageBuilder()
                .Code(MessageCode.PeerBrowseRequest)
                .Build();

            var reader = new MessageReader(msg);

            var ex = Record.Exception(() => reader.ReadInteger());

            Assert.NotNull(ex);
            Assert.IsType<MessageReadException>(ex);
        }

        [Trait("Category", "ReadLong")]
        [Fact(DisplayName = "ReadLong returns expected data")]
        public void ReadLong_Returns_Expected_Data()
        {
            var num = new Random().Next();
            var msg = new MessageBuilder()
                .Code(MessageCode.PeerBrowseRequest)
                .WriteLong((long)num)
                .Build();

            var reader = new MessageReader(msg);

            Assert.Equal(num, reader.ReadLong());
        }

        [Trait("Category", "ReadLong")]
        [Fact(DisplayName = "ReadLong throws MessageReadException if no data")]
        public void ReadLong_Throws_MessageReadException_If_No_Data()
        {
            var msg = new MessageBuilder()
                .Code(MessageCode.PeerBrowseRequest)
                .Build();

            var reader = new MessageReader(msg);

            var ex = Record.Exception(() => reader.ReadLong());

            Assert.NotNull(ex);
            Assert.IsType<MessageReadException>(ex);
        }

        [Trait("Category", "ReadByte")]
        [Fact(DisplayName = "ReadByte returns expected data")]
        public void ReadByte_Returns_Expected_Data()
        {
            var bytes = new byte[1];
            new Random().NextBytes(bytes);

            var msg = new MessageBuilder()
                .Code(MessageCode.PeerBrowseRequest)
                .WriteByte(bytes[0])
                .Build();

            var reader = new MessageReader(msg);

            Assert.Equal(bytes[0], reader.ReadByte());
        }

        [Trait("Category", "ReadByte")]
        [Fact(DisplayName = "ReadByte throws MessageReadException if no data")]
        public void ReadByte_Throws_MessageReadException_If_No_Data()
        {
            var msg = new MessageBuilder()
                .Code(MessageCode.PeerBrowseRequest)
                .Build();

            var reader = new MessageReader(msg);

            var ex = Record.Exception(() => reader.ReadByte());

            Assert.NotNull(ex);
            Assert.IsType<MessageReadException>(ex);
        }

        [Trait("Category", "ReadBytes")]
        [Fact(DisplayName = "ReadBytes returns expected data")]
        public void ReadBytes_Returns_Expected_Data()
        {
            var rand = new Random();

            var bytes = new byte[rand.Next(100)];
            new Random().NextBytes(bytes);

            var msg = new MessageBuilder()
                .Code(MessageCode.PeerBrowseRequest)
                .WriteBytes(bytes)
                .Build();

            var reader = new MessageReader(msg);

            Assert.Equal(bytes.Length, reader.Payload.Length);
            Assert.Equal(bytes, reader.ReadBytes(bytes.Length));
        }

        [Trait("Category", "ReadBytes")]
        [Fact(DisplayName = "ReadBytes throws MessageReadException if no data")]
        public void ReadBytes_Throws_MessageReadException_If_No_Data()
        {
            var msg = new MessageBuilder()
                .Code(MessageCode.PeerBrowseRequest)
                .Build();

            var reader = new MessageReader(msg);

            var ex = Record.Exception(() => reader.ReadBytes(1));

            Assert.NotNull(ex);
            Assert.IsType<MessageReadException>(ex);
        }

        [Trait("Category", "ReadBytes")]
        [Fact(DisplayName = "ReadBytes throws MessageReadException if length greater than payload length")]
        public void ReadBytes_Throws_MessageReadException_If_Length_Greater_Than_Payload_Length()
        {
            var msg = new MessageBuilder()
                .Code(MessageCode.PeerBrowseRequest)
                .WriteBytes(new byte[] { 0x0, 0x1, 0x2 })
                .Build();

            var reader = new MessageReader(msg);

            var ex = Record.Exception(() => reader.ReadBytes(4));

            Assert.NotNull(ex);
            Assert.IsType<MessageReadException>(ex);
        }

        [Trait("Category", "ReadString")]
        [Fact(DisplayName = "ReadString returns expected data")]
        public void ReadString_Returns_Expected_Data()
        {
            var str = Guid.NewGuid().ToString();

            var msg = new MessageBuilder()
                .Code(MessageCode.PeerBrowseRequest)
                .WriteString(str)
                .Build();

            var reader = new MessageReader(msg);

            Assert.Equal(str, reader.ReadString());
        }
    }
}
