﻿// <copyright file="TransferTests.cs" company="JP Dillingham">
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

namespace Soulseek.Tests.Unit
{
    using System;
    using System.Net;
    using AutoFixture.Xunit2;
    using Xunit;

    public class TransferTests
    {
        [Trait("Category", "Instantiation")]
        [Theory(DisplayName = "Instantiates with expected data"), AutoData]
        public void Instantiates_With_Expected_Data(
            TransferDirection direction,
            string username,
            string filename,
            int token,
            TransferStates state,
            long size,
            long bytesTransferred,
            double averageSpeed,
            DateTime? startTime,
            DateTime? endTime,
            int? remoteToken,
            IPAddress ipAddress,
            int? port)
        {
            var t = new Transfer(
                direction,
                username,
                filename,
                token,
                state,
                size,
                bytesTransferred,
                averageSpeed,
                startTime,
                endTime,
                remoteToken,
                ipAddress,
                port);

            Assert.Equal(direction, t.Direction);
            Assert.Equal(username, t.Username);
            Assert.Equal(filename, t.Filename);
            Assert.Equal(token, t.Token);
            Assert.Equal(state, t.State);
            Assert.Equal(size, t.Size);
            Assert.Equal(bytesTransferred, t.BytesTransferred);
            Assert.Equal(averageSpeed, t.AverageSpeed);
            Assert.Equal(startTime, t.StartTime);
            Assert.Equal(endTime, t.EndTime);
            Assert.Equal(remoteToken, t.RemoteToken);
            Assert.Equal(ipAddress, t.IPAddress);
            Assert.Equal(port, t.Port);
        }

        [Trait("Category", "Instantiation")]
        [Theory(DisplayName = "Instantiates with expected data given TransferInternal"), AutoData]
        internal void Instantiates_With_Expected_Data_Given_TransferInternal(TransferInternal i)
        {
            var t = new Transfer(i);

            Assert.Equal(i.Direction, t.Direction);
            Assert.Equal(i.Username, t.Username);
            Assert.Equal(i.Filename, t.Filename);
            Assert.Equal(i.Token, t.Token);
            Assert.Equal(i.State, t.State);
            Assert.Equal(i.Size, t.Size);
            Assert.Equal(i.BytesTransferred, t.BytesTransferred);
            Assert.Equal(i.AverageSpeed, t.AverageSpeed);
            Assert.Equal(i.StartTime, t.StartTime);
            Assert.Equal(i.EndTime, t.EndTime);
            Assert.Equal(i.RemoteToken, t.RemoteToken);
            Assert.Equal(i.IPAddress, t.IPAddress);
            Assert.Equal(i.Port, t.Port);
        }
    }
}
