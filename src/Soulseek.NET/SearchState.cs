﻿// <copyright file="SearchState.cs" company="JP Dillingham">
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
    ///     Search state.
    /// </summary>
    [Flags]
    public enum SearchStates
    {
        /// <summary>
        ///     None.
        /// </summary>
        None = 0,

        /// <summary>
        ///     In progress.
        /// </summary>
        InProgress = 1,

        /// <summary>
        ///     Completed.
        /// </summary>
        Completed = 2,

        /// <summary>
        ///     Completed due to cancellation.
        /// </summary>
        Cancelled = 4,

        /// <summary>
        ///     Completed due to the timeout value specified in search options having been reached.
        /// </summary>
        /// <remarks>
        ///     The timeout duration is from the time of the last response.
        /// </remarks>
        TimedOut = 8,

        /// <summary>
        ///     Completed due to the file limit specified in search options having been reached.
        /// </summary>
        FileLimitReached = 16,
    }
}
