﻿// <copyright file="DownloadState.cs" company="JP Dillingham">
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

    /// <summary>
    ///     Download state.
    /// </summary>
    /// <remarks>
    ///     The Completed state will be accompanied by one other flag consisting of <see cref="Successful"/>,
    ///     <see cref="Cancelled"/>, <see cref="TimedOut"/> or <see cref="Errored"/>.
    /// </remarks>
    [Flags]
    public enum DownloadStates
    {
        /// <summary>
        ///     None.
        /// </summary>
        None = 0,

        /// <summary>
        ///     Queued remotely.
        /// </summary>
        Queued = 1,

        /// <summary>
        ///     In progress.
        /// </summary>
        InProgress = 2,

        /// <summary>
        ///     Completed; check remaining state flags for disposition.
        /// </summary>
        Completed = 4,

        /// <summary>
        ///     Completed due to a successful transfer.
        /// </summary>
        Successful = 8,

        /// <summary>
        ///     Completed due to cancellation.
        /// </summary>
        Cancelled = 16,

        /// <summary>
        ///     Completed due to timeout.
        /// </summary>
        TimedOut = 32,

        /// <summary>
        ///     Completed due to transfer error.
        /// </summary>
        Errored = 64,
    }
}