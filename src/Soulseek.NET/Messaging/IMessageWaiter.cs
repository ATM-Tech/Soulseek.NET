﻿// <copyright file="IMessageWaiter.cs" company="JP Dillingham">
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

namespace Soulseek.NET.Messaging
{
    using System;
    using System.Threading;
    using System.Threading.Tasks;

    /// <summary>
    ///     Enables await-able server messages.
    /// </summary>
    internal interface IMessageWaiter : IDisposable
    {
        /// <summary>
        ///     Gets the default timeout duration.
        /// </summary>
        int DefaultTimeout { get; }

        /// <summary>
        ///     Disposes this instance.
        /// </summary>
        new void Dispose();

        /// <summary>
        ///     Completes the oldest wait matching the specified <paramref name="messageCode"/> with the specified <paramref name="result"/>.
        /// </summary>
        /// <typeparam name="T">The wait result type.</typeparam>
        /// <param name="messageCode">The wait message code.</param>
        /// <param name="result">The wait result.</param>
        void Complete<T>(MessageCode messageCode, T result);

        /// <summary>
        ///     Completes the oldest wait matching the specified <paramref name="messageCode"/> and <paramref name="token"/> with
        ///     the specified <paramref name="result"/>.
        /// </summary>
        /// <typeparam name="T">The wait result type.</typeparam>
        /// <param name="messageCode">The wait message code.</param>
        /// <param name="token">The unique wait token.</param>
        /// <param name="result">The wait result.</param>
        void Complete<T>(MessageCode messageCode, object token, T result);

        /// <summary>
        ///     Throws the specified <paramref name="exception"/> on the oldest wait matching the specified <paramref name="messageCode"/>.
        /// </summary>
        /// <param name="messageCode">The wait message code.</param>
        /// <param name="exception">The Exception to throw.</param>
        void Throw(MessageCode messageCode, Exception exception);

        /// <summary>
        ///     Throws the specified <paramref name="exception"/> on the oldest wait matching the specified
        ///     <paramref name="messageCode"/> and <paramref name="token"/>.
        /// </summary>
        /// <param name="messageCode">The wait message code.</param>
        /// <param name="token">The unique wait token.</param>
        /// <param name="exception">The Exception to throw.</param>
        void Throw(MessageCode messageCode, object token, Exception exception);

        /// <summary>
        ///     Adds a new wait for the specified <paramref name="messageCode"/> and <paramref name="token"/> and with the
        ///     specified <paramref name="timeout"/>.
        /// </summary>
        /// <typeparam name="T">The wait result type.</typeparam>
        /// <param name="messageCode">The wait message code.</param>
        /// <param name="token">A unique token for the wait.</param>
        /// <param name="timeout">The wait timeout.</param>
        /// <param name="cancellationToken">The cancellation token for the wait.</param>
        /// <returns>A Task representing the wait.</returns>
        Task<T> Wait<T>(MessageCode messageCode, object token = null, int? timeout = null, CancellationToken? cancellationToken = null);

        /// <summary>
        ///     Adds a new wait for the specified <paramref name="messageCode"/> which does not time out.
        /// </summary>
        /// <typeparam name="T">The wait result type.</typeparam>
        /// <param name="messageCode">The wait message code.</param>
        /// <param name="token">A unique token for the wait.</param>
        /// <param name="cancellationToken">The cancellation token for the wait.</param>
        /// <returns>A Task representing the wait.</returns>
        Task<T> WaitIndefinitely<T>(MessageCode messageCode, object token = null, CancellationToken? cancellationToken = null);

        /// <summary>
        ///     Cancels all waits.
        /// </summary>
        void CancelAll();
    }
}