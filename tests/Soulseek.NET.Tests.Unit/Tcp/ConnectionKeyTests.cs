﻿using Soulseek.NET.Tcp;
using System.Net;
using Xunit;

namespace Soulseek.NET.Tests.Unit.Tcp
{
    public class ConnectionKeyTests
    {
        [Fact]
        public void HashCodeMatches()
        {
            var a = new ConnectionKey() { Username = "a", IPAddress = new IPAddress(0x0), Port = 1, Type = ConnectionType.Default };
            var b = new ConnectionKey() { Username = "a", IPAddress = new IPAddress(0x0), Port = 1, Type = ConnectionType.Default };

            Assert.Equal(a.GetHashCode(), b.GetHashCode());
        }
    }
}
