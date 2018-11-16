﻿// <copyright file="IConnection.cs" company="JP Dillingham">
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

namespace Soulseek.NET.Tcp
{
    using System;
    using System.Net;
    using System.Threading.Tasks;

    internal interface IConnection : IDisposable
    {
        ConnectionOptions Options { get; }
        string Address { get; }
        IPAddress IPAddress { get; }
        int Port { get; }
        ConnectionState State { get; }
        ConnectionKey Key { get; }
        object Context { get; }
        Action<IConnection> ConnectHandler { get; }
        Action<IConnection, string> DisconnectHandler { get; }

        event EventHandler<ConnectionStateChangedEventArgs> StateChanged;

        Task ConnectAsync();

        void Disconnect(string message = null);
    }
}