﻿// <copyright file="ConnectionFactoryTests.cs" company="JP Dillingham">
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
    using Soulseek.Network;
    using Soulseek.Network.Tcp;
    using Xunit;

    public class ConnectionFactoryTests
    {
        [Trait("Category", "GetTransferConnection")]
        [Theory(DisplayName = "GetTransferConnection returns the expected connection"), AutoData]
        internal void GetTransferConneciton_Returns_The_Expected_Connection(IPEndPoint endpoint, ConnectionOptions options)
        {
            var c = new ConnectionFactory().GetTransferConnection(endpoint, options);

            Assert.Equal(endpoint.Address, c.IPEndPoint.Address);
            Assert.Equal(endpoint.Port, c.IPEndPoint.Port);

            Assert.Equal(options.ReadBufferSize, c.Options.ReadBufferSize);
            Assert.Equal(options.WriteBufferSize, c.Options.WriteBufferSize);
            Assert.Equal(options.ConnectTimeout, c.Options.ConnectTimeout);
            Assert.Equal(-1, c.Options.InactivityTimeout);
        }

        [Trait("Category", "GetServerConnection")]
        [Theory(DisplayName = "GetServerConnection returns the expected connection"), AutoData]
        internal void GetServerConneciton_Returns_The_Expected_Connection(IPEndPoint endpoint, ConnectionOptions options)
        {
            bool connect = false;
            EventHandler connected = (s, a) => { connect = true;  };

            bool disconnect = false;
            EventHandler<ConnectionDisconnectedEventArgs> disconnected = (s, a) => { disconnect = true; };

            bool read = false;
            EventHandler<MessageReadEventArgs> messageRead = (s, a) => { read = true; };

            var c = new ConnectionFactory().GetServerConnection(endpoint, connected, disconnected, messageRead, options);

            Assert.Equal(endpoint.Address, c.IPEndPoint.Address);
            Assert.Equal(endpoint.Port, c.IPEndPoint.Port);

            c.RaiseEvent(typeof(Connection), "Connected", EventArgs.Empty);
            Assert.True(connect);

            c.RaiseEvent(typeof(Connection), "Disconnected", new ConnectionDisconnectedEventArgs("foo"));
            Assert.True(disconnect);

            c.RaiseEvent("MessageRead", new MessageReadEventArgs(Array.Empty<byte>()));
            Assert.True(read);

            Assert.Equal(options.ReadBufferSize, c.Options.ReadBufferSize);
            Assert.Equal(options.WriteBufferSize, c.Options.WriteBufferSize);
            Assert.Equal(options.ConnectTimeout, c.Options.ConnectTimeout);
            Assert.Equal(-1, c.Options.InactivityTimeout);
        }

        [Trait("Category", "GetMessageConnection")]
        [Theory(DisplayName = "GetMessageConnection returns the expected connection"), AutoData]
        internal void GetMessageConneciton_Returns_The_Expected_Connection(string username, IPEndPoint endpoint, ConnectionOptions options)
        {
            var c = new ConnectionFactory().GetMessageConnection(username, endpoint, options);

            Assert.Equal(endpoint.Address, c.IPEndPoint.Address);
            Assert.Equal(endpoint.Port, c.IPEndPoint.Port);

            Assert.Equal(options.ReadBufferSize, c.Options.ReadBufferSize);
            Assert.Equal(options.WriteBufferSize, c.Options.WriteBufferSize);
            Assert.Equal(options.ConnectTimeout, c.Options.ConnectTimeout);
            Assert.Equal(options.InactivityTimeout, c.Options.InactivityTimeout);
        }
    }
}
