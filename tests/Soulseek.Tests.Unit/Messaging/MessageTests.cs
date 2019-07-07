﻿//// <copyright file="MessageTests.cs" company="JP Dillingham">
////     Copyright (c) JP Dillingham. All rights reserved.
////
////     This program is free software: you can redistribute it and/or modify it under the terms of the GNU General Public License as
////     published by the Free Software Foundation, either version 3 of the License, or (at your option) any later version.
////
////     This program is distributed in the hope that it will be useful, but WITHOUT ANY WARRANTY; without even the implied warranty
////     of MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.See the GNU General Public License for more details.
////
////     You should have received a copy of the GNU General Public License along with this program. If not, see https://www.gnu.org/licenses/.
//// </copyright>

//namespace Soulseek.Tests.Unit.Messaging
//{
//    using System;
//    using Soulseek.Messaging;
//    using Xunit;

//    public class MessageTests
//    {
//        [Trait("Category", "Instantiation")]
//        [Fact(DisplayName = "Instantiates without exception given good data")]
//        public void Instantiates_Without_Exception_Given_Good_Data()
//        {
//            var num = new Random().Next();
//            var bytes = new MessageBuilder()
//                .Code(MessageCode.Server.Login)
//                .WriteInteger(num)
//                .Build()
//                .ToByteArray();

//            var msg = default(Message);

//            var ex = Record.Exception(() => msg = new Message(bytes));

//            Assert.Null(ex);
//            Assert.NotNull(msg);
//        }

//        [Trait("Category", "Instantiation")]
//        [Fact(DisplayName = "Instantiate throws exception given too short data")]
//        public void Instantiate_Throws_Exception_Given_Too_Short_Data()
//        {
//            var ex = Record.Exception(() => new byte[] { 0x0, 0x1, 0x2, 0x3 });

//            Assert.NotNull(ex);
//            Assert.IsType<ArgumentOutOfRangeException>(ex);
//        }

//        [Trait("Category", "ToByteArray")]
//        [Fact(DisplayName = "ToByteArray returns given bytes")]
//        public void ToByteArray_Returns_Given_Bytes()
//        {
//            var num = new Random().Next();
//            var bytes = new MessageBuilder()
//                .WriteCode(MessageCode.Server.Login)
//                .WriteInteger(num)
//                .Build();

//            Assert.Equal(bytes, bytes);
//        }

//        [Trait("Category", "Properties")]
//        [Fact(DisplayName = "Properties return expected data")]
//        public void Properties_Return_Expected_Data()
//        {
//            var num = new Random().Next();
//            var bytes = new MessageBuilder()
//                .WriteCode(MessageCode.Server.Login)
//                .WriteInteger(num)
//                .Build();

//            Assert.Equal(MessageCode.Server.Login, new MessageReader(bytes).ReadCode<MessageCode>());
//            Assert.Equal(8, bytes.Length);
//            Assert.Equal(BitConverter.GetBytes(num), bytes);
//        }
//    }
//}
