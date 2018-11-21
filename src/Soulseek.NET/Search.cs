﻿// <copyright file="Search.cs" company="JP Dillingham">
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
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using Soulseek.NET.Messaging.Responses;
    using SystemTimer = System.Timers.Timer;

    /// <summary>
    ///     A single file search.
    /// </summary>
    internal sealed class Search : IDisposable
    {
        private int resultCount = 0;

        /// <summary>
        ///     Initializes a new instance of the <see cref="Search"/> class with the specified <paramref name="searchText"/> and
        ///     optionally specified <paramref name="token"/> and <paramref name="options"/>.
        /// </summary>
        /// <param name="searchText">The text for which to search.</param>
        /// <param name="token">The unique search token.</param>
        /// <param name="options">The options for the search.</param>
        public Search(string searchText, int token, SearchOptions options)
        {
            SearchText = searchText;
            Token = token;
            Options = options;

            SearchTimeoutTimer = new SystemTimer()
            {
                Interval = Options.SearchTimeout * 1000,
                Enabled = false,
                AutoReset = false,
            };

            SearchTimeoutTimer.Elapsed += (sender, e) => { Complete($"The search completed after {options.SearchTimeout} seconds of inactivity."); };
            SearchTimeoutTimer.Reset();
        }

        /// <summary>
        ///     Gets the options for the search.
        /// </summary>
        public SearchOptions Options { get; private set; }

        /// <summary>
        ///     Gets the collection of responses received from peers.
        /// </summary>
        public IEnumerable<SearchResponse> Responses => ResponseList.AsReadOnly();

        /// <summary>
        ///     Gets the text for which to search.
        /// </summary>
        public string SearchText { get; private set; }

        /// <summary>
        ///     Gets the unique identifier for the search.
        /// </summary>
        public int Token { get; private set; }

        /// <summary>
        ///     Gets or sets the action invoked upon completion of the search.
        /// </summary>
        public Action<Search, string> CompleteHandler { get; set; } = (search, message) => { };

        /// <summary>
        ///     Gets or sets the action invoked upon receipt of a search response.
        /// </summary>
        public Action<Search, SearchResponse> ResponseHandler { get; set; } = (search, response) => { };

        private bool Disposed { get; set; } = false;
        private List<SearchResponse> ResponseList { get; set; } = new List<SearchResponse>();
        private SystemTimer SearchTimeoutTimer { get; set; }

        /// <summary>
        ///     Disposes this instance.
        /// </summary>
        public void Dispose()
        {
            Dispose(true);
        }

        /// <summary>
        ///     Adds the specified <paramref name="response"/> to the list of responses after applying the filters specified in the search options.
        /// </summary>
        /// <param name="response">The response to add.</param>
        public void AddResponse(SearchResponse response)
        {
            if (response.Token == Token && ResponseMeetsOptionCriteria(response))
            {
                response.ParseFiles();

                if (Options.FilterFiles)
                {
                    response.Files = response.Files.Where(f => FileMeetsOptionCriteria(f));
                }

                Interlocked.Add(ref resultCount, response.Files.Count());

                if (resultCount >= Options.FileLimit)
                {
                    Complete($"The search completed after receiving {Options.FileLimit} results.");
                    return;
                }

                ResponseList.Add(response);

                Task.Run(() => ResponseHandler(this, response)).Forget();

                SearchTimeoutTimer.Reset();
            }
        }

        /// <summary>
        ///     Completes the search with the specified <paramref name="message"/>.
        /// </summary>
        /// <param name="message">A message indicating how the search was completed.</param>
        internal void Complete(string message)
        {
            SearchTimeoutTimer.Stop();
            CompleteHandler(this, message);
        }

        private void Dispose(bool disposing)
        {
            if (!Disposed)
            {
                if (disposing)
                {
                    SearchTimeoutTimer.Dispose();
                    ResponseList = default(List<SearchResponse>);
                }

                Disposed = true;
            }
        }

        private bool FileMeetsOptionCriteria(File file)
        {
            if (!Options.FilterFiles)
            {
                return true;
            }

            bool fileHasIgnoredExtension(File f)
            {
                return Options.IgnoredFileExtensions == null ? false :
                    Options.IgnoredFileExtensions.Any(e => e == System.IO.Path.GetExtension(f.Filename));
            }

            if (file.Size < Options.MinimumFileSize || fileHasIgnoredExtension(file))
            {
                return false;
            }

            var bitRate = file.GetAttributeValue(FileAttributeType.BitRate);
            var length = file.GetAttributeValue(FileAttributeType.Length);
            var bitDepth = file.GetAttributeValue(FileAttributeType.BitDepth);
            var sampleRate = file.GetAttributeValue(FileAttributeType.SampleRate);

            if ((bitRate != null && bitRate < Options.MinimumFileBitRate) ||
                (length != null && length < Options.MinimumFileLength) ||
                (bitDepth != null && bitDepth < Options.MinimumFileBitDepth) ||
                (sampleRate != null && sampleRate < Options.MinimumFileSampleRate))
            {
                return false;
            }

            var constantBitRates = new[] { 32, 64, 128, 192, 256, 320 };
            var isConstant = constantBitRates.Any(b => b == bitRate);

            if (bitRate != null && ((!Options.IncludeConstantBitRate && isConstant) || (!Options.IncludeVariableBitRate && !isConstant)))
            {
                return false;
            }

            return true;
        }

        private bool ResponseMeetsOptionCriteria(SearchResponse response)
        {
            if (Options.FilterResponses && (
                    response.FileCount < Options.MinimumResponseFileCount ||
                    response.FreeUploadSlots < Options.MinimumPeerFreeUploadSlots ||
                    response.UploadSpeed < Options.MinimumPeerUploadSpeed ||
                    response.QueueLength > Options.MaximumPeerQueueLength))
            {
                return false;
            }

            return true;
        }
    }
}