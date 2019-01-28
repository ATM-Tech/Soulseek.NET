﻿// <copyright file="DiagnosticEventArgs.cs" company="JP Dillingham">
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
    ///     Generic event arguments for diagnostic events.
    /// </summary>
    public class DiagnosticEventArgs : EventArgs
    {
    }

    /// <summary>
    ///     Event arguments for events raised by diagnostic messages.
    /// </summary>
    public class DiagnosticGeneratedEventArgs : DiagnosticEventArgs
    {
        /// <summary>
        ///     Initializes a new instance of the <see cref="DiagnosticGeneratedEventArgs"/> class.
        /// </summary>
        /// <param name="level">The digagnostic level of the event.</param>
        /// <param name="message">The event message.</param>
        /// <param name="exception">The Exception associated with the event, if applicable.</param>
        public DiagnosticGeneratedEventArgs(DiagnosticLevel level, string message, Exception exception = null)
        {
            Level = level;
            Message = message;
            Exception = exception;
        }

        /// <summary>
        ///     Gets the diagnostic level of the event.
        /// </summary>
        public DiagnosticLevel Level { get; }

        /// <summary>
        ///     Gets the event message.
        /// </summary>
        public string Message { get; }

        /// <summary>
        ///     Gets a value indicating whether an <see cref="Exception"/> is included with the event.
        /// </summary>
        public bool IncludesException => Exception != null;

        /// <summary>
        ///     Gets the Exception associated with the event, if applicable.
        /// </summary>
        public Exception Exception { get; }
    }
}
