﻿// <copyright file="Extensions.cs" company="JP Dillingham">
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
    using System;
    using System.Collections.Concurrent;
    using System.Diagnostics.CodeAnalysis;
    using System.Linq;
    using System.Net;
    using System.Security.Cryptography;
    using System.Text;
    using System.Threading.Tasks;
    using System.Timers;

    /// <summary>
    ///     Extension methods.
    /// </summary>
    public static class Extensions
    {
        /// <summary>
        ///     Dequeues and disposes of all instances within the specified <see cref="ConcurrentQueue{T}"/>.
        /// </summary>
        /// <typeparam name="T">The contained type of the queue.</typeparam>
        /// <param name="concurrentQueue">The queue from which to dequeue and dispose.</param>
        public static void DequeueAndDisposeAll<T>(this ConcurrentQueue<T> concurrentQueue)
            where T : IDisposable
        {
            while (!concurrentQueue.IsEmpty)
            {
                if (concurrentQueue.TryDequeue(out var value))
                {
                    value.Dispose();
                }
            }
        }

        /// <summary>
        ///     Continue a task and report an Exception if one is raised.
        /// </summary>
        /// <typeparam name="T">The type of Exception to throw.</typeparam>
        /// <param name="task">The task to continue.</param>
        public static void ForgetButThrowWhenFaulted<T>(this Task task)
            where T : Exception
        {
            task.ContinueWith(t => { throw (T)Activator.CreateInstance(typeof(T), t.Exception.Message, t.Exception); }, TaskContinuationOptions.OnlyOnFaulted);
        }

        /// <summary>
        ///     Removes all instances within the specified <see cref="ConcurrentDictionary{TKey, TValue}"/>.
        /// </summary>
        /// <typeparam name="TKey">The type of the dictionary key.</typeparam>
        /// <typeparam name="TValue">The type of the dictionary value.</typeparam>
        /// <param name="concurrentDictionary">The dictionary from which to remove.</param>
        public static void RemoveAll<TKey, TValue>(this ConcurrentDictionary<TKey, TValue> concurrentDictionary)
        {
            while (!concurrentDictionary.IsEmpty)
            {
                concurrentDictionary.TryRemove(concurrentDictionary.Keys.First(), out var _);
            }
        }

        /// <summary>
        ///     Removes and disposes of all instances within the specified <see cref="ConcurrentDictionary{TKey, TValue}"/>.
        /// </summary>
        /// <typeparam name="TKey">The type of the dictionary key.</typeparam>
        /// <typeparam name="TValue">The type of the dictionary value.</typeparam>
        /// <param name="concurrentDictionary">The dictionary from which to remove.</param>
        public static void RemoveAndDisposeAll<TKey, TValue>(this ConcurrentDictionary<TKey, TValue> concurrentDictionary)
                    where TValue : IDisposable
        {
            while (!concurrentDictionary.IsEmpty)
            {
                if (concurrentDictionary.TryRemove(concurrentDictionary.Keys.First(), out var value))
                {
                    value.Dispose();
                }
            }
        }

        /// <summary>
        ///     Reset a timer.
        /// </summary>
        /// <param name="timer">The timer to reset.</param>
        public static void Reset(this Timer timer)
        {
            timer.Stop();
            timer.Start();
        }

        /// <summary>
        ///     Resolves the IP address in the given string to an instance of <see cref="IPAddress"/>.
        /// </summary>
        /// <param name="address">The IP address string to resolve.</param>
        /// <returns>The resolved IPAddress.</returns>
        public static IPAddress ResolveIPAddress(this string address)
        {
            if (IPAddress.TryParse(address, out IPAddress ip))
            {
                return ip;
            }
            else
            {
                return Dns.GetHostEntry(address).AddressList[0];
            }
        }

        /// <summary>
        ///     Returns the MD5 hash of a string.
        /// </summary>
        /// <param name="str">The string to hash.</param>
        /// <returns>The MD5 hash of the input string.</returns>
        [SuppressMessage("Microsoft.NetCore.CSharp.Analyzers", "CA5351", Justification = "Required by the Soulseek protocol.")]
        public static string ToMD5Hash(this string str)
        {
            using (MD5 md5 = MD5.Create())
            {
                byte[] inputBytes = Encoding.ASCII.GetBytes(str);
                byte[] hashBytes = md5.ComputeHash(inputBytes);
                return Encoding.ASCII.GetString(hashBytes);
            }
        }
    }
}