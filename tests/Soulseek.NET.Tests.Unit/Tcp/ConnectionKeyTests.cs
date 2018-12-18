﻿using Soulseek.NET.Tcp;
using System.Collections.Generic;
using System.Net;
using Xunit;

namespace Soulseek.NET.Tests.Unit.Tcp
{
    public class ConnectionKeyTests
    {
        [Trait("Category", "Instantiation")]
        [Fact(DisplayName = "Instantiates with ip and port")]
        public void Instantiates_With_IP_And_Port()
        {
            var ip = new IPAddress(0x0);

            ConnectionKey k = null;
            var ex = Record.Exception(() => k = new ConnectionKey(ip, 0));

            Assert.Null(ex);
            Assert.NotNull(k);

            Assert.Equal(ip, k.IPAddress);
            Assert.Equal(0, k.Port);
        }

        [Trait("Category", "Instantiation")]
        [Fact(DisplayName = "Instantiates with username, ip, port and type")]
        public void Instantiates_With_Username_IP_Port_And_Type()
        {
            var ip = new IPAddress(0x0);

            ConnectionKey k = null;
            var ex = Record.Exception(() => k = new ConnectionKey("username", ip, 0, MessageConnectionType.Server));

            Assert.Null(ex);
            Assert.NotNull(k);

            Assert.Equal("username", k.Username);
            Assert.Equal(ip, k.IPAddress);
            Assert.Equal(0, k.Port);
            Assert.Equal(MessageConnectionType.Server, k.Type);
        }

        public static IEnumerable<object[]> GetHashCodeData => new List<object[]>
        {
            new object[] { null, null, 0, MessageConnectionType.Default },
            new object[] { null, new IPAddress(0x0), 0, MessageConnectionType.Default },
            new object[] { null, null, 1, MessageConnectionType.Default },
            new object[] { "a", new IPAddress(0x1), 0, MessageConnectionType.Peer },
        };

        [Trait("Category", "Hash Code")]
        [Theory(DisplayName = "GetHashCode matches")]
        [MemberData(nameof(GetHashCodeData))]
        public void GetHashCode_Matches(string username, IPAddress ipAddress, int port, MessageConnectionType type)
        {
            var a = new ConnectionKey(username, ipAddress, port, type);
            var b = new ConnectionKey(username, ipAddress, port, type);

            Assert.Equal(a.GetHashCode(), b.GetHashCode());
        }

        [Trait("Category", "Hash Code")]
        [Fact(DisplayName = "GetHashCode does not match if key differs")]
        public void GetHashCode_Does_Not_Match_If_Key_Differs()
        {
            var a = new ConnectionKey("a", new IPAddress(0x0), 1, MessageConnectionType.Default);
            var b = new ConnectionKey("b", new IPAddress(0x0), 1, MessageConnectionType.Default);

            Assert.NotEqual(a.GetHashCode(), b.GetHashCode());
        }

        [Trait("Category", "Equals")]
        [Fact(DisplayName = "Equals returns true when equal")]
        public void Equals_Returns_True_When_Equal()
        {
            var a = new ConnectionKey("a", new IPAddress(0x0), 1, MessageConnectionType.Default);
            var b = new ConnectionKey("a", new IPAddress(0x0), 1, MessageConnectionType.Default);

            Assert.True(a.Equals(b));
            Assert.True(b.Equals(a));
        }

        [Trait("Category", "Equals")]
        [Fact(DisplayName = "Equals returns false when not equal")]
        public void Equals_Returns_False_When_Not_Equal()
        {
            var a = new ConnectionKey("a", new IPAddress(0x0), 1, MessageConnectionType.Default);
            var b = new ConnectionKey("a", new IPAddress(0x1), 1, MessageConnectionType.Default);

            Assert.False(a.Equals(b));
            Assert.False(b.Equals(a));
        }

        [Trait("Category", "Equals")]
        [Fact(DisplayName = "Equals returns false when different type")]
        public void Equals_Returns_False_When_Different_Type()
        {
            var a = new ConnectionKey("a", new IPAddress(0x0), 1, MessageConnectionType.Default);
            var b = "foo";

            Assert.False(a.Equals(b));
            Assert.False(b.Equals(a));
        }

        [Trait("Category", "Equals")]
        [Fact(DisplayName = "Equals handles boxed instances")]
        public void Equals_Handles_Boxed_Instances()
        {
            var a = new ConnectionKey("a", new IPAddress(0x0), 1, MessageConnectionType.Default);
            var b = new ConnectionKey("a", new IPAddress(0x0), 1, MessageConnectionType.Default);

            Assert.True(a.Equals((object)b));
            Assert.True(b.Equals((object)a));
        }
    }
}
