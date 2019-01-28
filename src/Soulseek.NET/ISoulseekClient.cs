﻿// <copyright file="ISoulseekClient.cs" company="JP Dillingham">
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
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using Soulseek.NET.Exceptions;
    using Soulseek.NET.Messaging.Messages;
    using Soulseek.NET.Tcp;

    /// <summary>
    ///     A client for the Soulseek file sharing network.
    /// </summary>
    public interface ISoulseekClient : IDisposable
    {
        #region Public Events

        /// <summary>
        ///     Occurs when an internal diagnostic message is generated.
        /// </summary>
        event EventHandler<DiagnosticGeneratedEventArgs> DiagnosticGenerated;

        /// <summary>
        ///     Occurs when an active download receives data.
        /// </summary>
        event EventHandler<DownloadProgressUpdatedEventArgs> DownloadProgressUpdated;

        /// <summary>
        ///     Occurs when a download changes state.
        /// </summary>
        event EventHandler<DownloadStateChangedEventArgs> DownloadStateChanged;

        /// <summary>
        ///     Occurs when a new search result is received.
        /// </summary>
        event EventHandler<SearchResponseReceivedEventArgs> SearchResponseReceived;

        /// <summary>
        ///     Occurs when a search changes state.
        /// </summary>
        event EventHandler<SearchStateChangedEventArgs> SearchStateChanged;

        /// <summary>
        ///     Occurs when the client changes state.
        /// </summary>
        event EventHandler<SoulseekClientStateChangedEventArgs> StateChanged;

        #endregion Public Events

        #region Public Properties

        /// <summary>
        ///     Gets or sets the address of the server to which to connect.
        /// </summary>
        string Address { get; set; }

        /// <summary>
        ///     Gets the client options.
        /// </summary>
        SoulseekClientOptions Options { get; }

        /// <summary>
        ///     Gets or sets the port to which to connect.
        /// </summary>
        int Port { get; set; }

        /// <summary>
        ///     Gets the current state of the underlying TCP connection.
        /// </summary>
        SoulseekClientStates State { get; }

        /// <summary>
        ///     Gets the name of the currently signed in user.
        /// </summary>
        string Username { get; }

        #endregion Public Properties

        #region Public Methods

        /// <summary>
        ///     Asynchronously fetches the list of files shared by the specified <paramref name="username"/> with the optionally
        ///     specified <paramref name="cancellationToken"/>.
        /// </summary>
        /// <param name="username">The user to browse.</param>
        /// <param name="cancellationToken">A cancellation token.</param>
        /// <returns>The operation context, including the fetched list of files.</returns>
        Task<BrowseResponse> BrowseAsync(string username, CancellationToken? cancellationToken = null);

        /// <summary>
        ///     Asynchronously connects the client to the server specified in the <see cref="Address"/> and <see cref="Port"/> properties.
        /// </summary>
        /// <returns>A task representing the asynchronous operation.</returns>
        /// <exception cref="InvalidOperationException">Thrown when the client is already connected.</exception>
        /// <exception cref="ConnectionException">
        ///     Thrown when the client is already connected, or is transitioning between states.
        /// </exception>
        Task ConnectAsync();

        /// <summary>
        ///     Disconnects the client from the server.
        /// </summary>
        /// <param name="message">An optional message describing the reason the client is being disconnected.</param>
        void Disconnect(string message = null);

        /// <summary>
        ///     Asynchronously downloads the specified <paramref name="filename"/> from the specified <paramref name="username"/> and with the optionally specified <paramref name="token"/> and <paramref name="cancellationToken"/>.
        /// </summary>
        /// <remarks>
        ///     If no <paramref name="token"/> is specified, one will be randomly generated internally.
        /// </remarks>
        /// <param name="username">The user from which to download the file.</param>
        /// <param name="filename">The file to download.</param>
        /// <param name="token">The unique download token.</param>
        /// <param name="cancellationToken">A cancellation token.</param>
        /// <returns>The operation context, including a byte array containing the file contents.</returns>
        Task<byte[]> DownloadAsync(string username, string filename, int? token = null, CancellationToken? cancellationToken = null);

        /// <summary>
        ///     Asynchronously logs in to the server with the specified <paramref name="username"/> and <paramref name="password"/>.
        /// </summary>
        /// <param name="username">The username with which to log in.</param>
        /// <param name="password">The password with which to log in.</param>
        /// <returns>A Task representing the operation.</returns>
        /// <exception cref="LoginException">Thrown when the login fails.</exception>
        Task LoginAsync(string username, string password);

        /// <summary>
        ///     Asynchronously searches for the specified <paramref name="searchText"/> and unique <paramref name="token"/> and
        ///     with the optionally specified <paramref name="options"/> and <paramref name="cancellationToken"/>.
        /// </summary>
        /// <param name="searchText">The text for which to search.</param>
        /// <param name="token">The unique search token.</param>
        /// <param name="options">The operation <see cref="SearchOptions"/>.</param>
        /// <param name="cancellationToken">A cancellation token.</param>
        /// <param name="waitForCompletion">A value indicating whether the search should wait completion before returning.</param>
        /// <returns>The operation context, including the search results.</returns>
        /// <exception cref="ConnectionException">Thrown when the client is not connected to the server, or no user is logged in.</exception>
        /// <exception cref="ArgumentException">Thrown when the specified <paramref name="searchText"/> is null, empty, or consists of only whitespace.</exception>
        /// <exception cref="ArgumentException">Thrown when a search with the specified <paramref name="token"/> is already in progress.</exception>
        /// <exception cref="SearchException">Thrown when an unhandled Exception is encountered during the operation.</exception>
        Task<IEnumerable<SearchResponse>> SearchAsync(string searchText, int token, SearchOptions options = null, CancellationToken? cancellationToken = null, bool waitForCompletion = true);

        #endregion Public Methods
    }
}