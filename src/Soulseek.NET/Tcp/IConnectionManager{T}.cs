﻿// <copyright file="IConnectionManager{T}.cs" company="JP Dillingham">
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
    using System.Threading.Tasks;

    internal interface IConnectionManager<T> : IDisposable
        where T : IConnection
    {
        int Active { get; }
        int Queued { get; }

        new void Dispose();

        Task Add(T connection);

        T Get(ConnectionKey key);

        Task Remove(T connection);

        void RemoveAll();
    }
}