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
    using Soulseek.NET.Messaging.Messages;
    using SystemTimer = System.Timers.Timer;

    /// <summary>
    ///     A single file search.
    /// </summary>
    internal sealed class Search : IDisposable
    {
        private int resultCount = 0;

        /// <summary>
        ///     Initializes a new instance of the <see cref="Search"/> class.
        /// </summary>
        /// <param name="searchText">The text for which to search.</param>
        /// <param name="token">The unique search token.</param>
        /// <param name="options">The options for the search.</param>
        public Search(string searchText, int token, SearchOptions options = null)
        {
            SearchText = searchText;
            Token = token;

            Options = options ?? new SearchOptions();

            SearchTimeoutTimer = new SystemTimer()
            {
                Interval = Options.SearchTimeout * 1000,
                Enabled = false,
                AutoReset = false,
            };

            SearchTimeoutTimer.Elapsed += (sender, e) => { Complete(SearchStates.TimedOut); };
            SearchTimeoutTimer.Reset();
        }

        /// <summary>
        ///     Occurs when the search is completed.
        /// </summary>
        public event EventHandler<SearchStates> Completed;

        /// <summary>
        ///     Occurs when a new search result is received.
        /// </summary>
        public event EventHandler<SearchResponse> ResponseReceived;

        /// <summary>
        ///     Gets the options for the search.
        /// </summary>
        public SearchOptions Options { get; }

        /// <summary>
        ///     Gets the collection of responses received from peers.
        /// </summary>
        public IReadOnlyCollection<SearchResponse> Responses => ResponseList.AsReadOnly();

        /// <summary>
        ///     Gets the text for which to search.
        /// </summary>
        public string SearchText { get; }

        /// <summary>
        ///     Gets or sets the state of the search.
        /// </summary>
        public SearchStates State { get; set; } = SearchStates.None;

        /// <summary>
        ///     Gets the unique identifier for the search.
        /// </summary>
        public int Token { get; }

        private bool Disposed { get; set; } = false;
        private List<SearchResponse> ResponseList { get; set; } = new List<SearchResponse>();
        private SystemTimer SearchTimeoutTimer { get; set; }

        /// <summary>
        ///     Adds the specified <paramref name="slimResponse"/> to the list of responses after parsing the files within it and
        ///     applying the filters specified in the search options.
        /// </summary>
        /// <param name="slimResponse">The response to add.</param>
        public void AddResponse(SearchResponseSlim slimResponse)
        {
            AddResponse(new SearchResponse(slimResponse));
        }

        /// <summary>
        ///     Adds the specified <paramref name="response"/> to the list of responses after applying the filters specified in the
        ///     search options.
        /// </summary>
        /// <param name="response">The response to add.</param>
        public void AddResponse(SearchResponse response)
        {
            if (State.HasFlag(SearchStates.InProgress) && response.Token == Token && ResponseMeetsOptionCriteria(response))
            {
                response = new SearchResponse(response, response.Files.Where(f => FileMeetsOptionCriteria(f)).ToList());

                if (Options.FilterResponses && response.FileCount < Options.MinimumResponseFileCount)
                {
                    return;
                }

                Interlocked.Add(ref resultCount, response.Files.Count);

                ResponseList.Add(response);

                ResponseReceived?.Invoke(this, response);
                SearchTimeoutTimer.Reset();

                if (resultCount >= Options.FileLimit)
                {
                    Complete(SearchStates.FileLimitReached);
                }
            }
        }

        /// <summary>
        ///     Completes the search with the specified <paramref name="state"/>.
        /// </summary>
        /// <param name="state">The terminal state of the search.</param>
        public void Complete(SearchStates state)
        {
            SearchTimeoutTimer.Stop();
            State = SearchStates.Completed | state;
            Completed?.Invoke(this, State);
        }

        /// <summary>
        ///     Disposes this instance.
        /// </summary>
        public void Dispose()
        {
            Dispose(true);
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
                return (Options.IgnoredFileExtensions != null) && Options.IgnoredFileExtensions.Any(e => e == f.Extension);
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
                    response.QueueLength >= Options.MaximumPeerQueueLength))
            {
                return false;
            }

            return true;
        }
    }
}