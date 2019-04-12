﻿// <copyright file="ConnectionFactory.cs" company="JP Dillingham">
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

namespace Soulseek.NET
{
    using System.Net;
    using Soulseek.NET.Tcp;

    /// <summary>
    ///     Creates <see cref="Connection"/> instances.
    /// </summary>
    internal class ConnectionFactory : IConnectionFactory
    {
        ///// <summary>
        /////     Gets a <see cref="Connection"/> instance.
        ///// </summary>
        ///// <param name="ipAddress">The remote IP address of the connection.</param>
        ///// <param name="port">The remote port of the connection.</param>
        ///// <param name="options">The optional options for the connection.</param>
        ///// <returns>The created Connection.</returns>
        //public IConnection GetConnection(IPAddress ipAddress, int port, ConnectionOptions options = null) => new Connection(ipAddress, port, options);
    }
}
