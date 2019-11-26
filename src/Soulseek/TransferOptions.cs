﻿// <copyright file="TransferOptions.cs" company="JP Dillingham">
//     Copyright (c) JP Dillingham. All rights reserved.
//
//     This program is free software: you can redistribute it and/or modify it under the terms of the GNU General Public License
//     as published by the Free Software Foundation, either version 3 of the License, or (at your option) any later version.
//
//     This program is distributed in the hope that it will be useful, but WITHOUT ANY WARRANTY; without even the implied warranty
//     of MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.See the GNU General Public License for more details.
//
//     You should have received a copy of the GNU General Public License along with this program. If not, see https://www.gnu.org/licenses/.
// </copyright>

namespace Soulseek
{
    using System;
    using System.Threading.Tasks;

    /// <summary>
    ///     Options for transfer operations.
    /// </summary>
    public class TransferOptions
    {
        private readonly Func<Transfer, Task> defaultGovernor =
            (t) => Task.CompletedTask;

        /// <summary>
        ///     Initializes a new instance of the <see cref="TransferOptions"/> class.
        /// </summary>
        /// <param name="governor">The delegate used to govern transfer speed.</param>
        /// <param name="stateChanged">The Action to invoke when the transfer changes state.</param>
        /// <param name="progressUpdated">The Action to invoke when the transfer receives data.</param>
        public TransferOptions(
            Func<Transfer, Task> governor = null,
            Action<TransferStateChangedEventArgs> stateChanged = null,
            Action<TransferProgressUpdatedEventArgs> progressUpdated = null)
        {
            Governor = governor ?? defaultGovernor;
            StateChanged = stateChanged;
            ProgressUpdated = progressUpdated;
        }

        /// <summary>
        ///     Gets the delegate used to govern transfer speed.
        /// </summary>
        public Func<Transfer, Task> Governor { get; }

        /// <summary>
        ///     Gets the Action to invoke when the transfer receives data.
        /// </summary>
        public Action<TransferProgressUpdatedEventArgs> ProgressUpdated { get; }

        /// <summary>
        ///     Gets the Action to invoke when the transfer changes state.
        /// </summary>
        public Action<TransferStateChangedEventArgs> StateChanged { get; }
    }
}