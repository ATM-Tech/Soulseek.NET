﻿// <copyright file="MessageReaderExtensions.cs" company="JP Dillingham">
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

namespace Soulseek.Messaging
{
    using System.Collections.Generic;

    /// <summary>
    ///     Extensions for <see cref="MessageReader{T}"/>.
    /// </summary>
    /// <remarks>
    ///     This keeps domain logic out of the reader.
    /// </remarks>
    internal static class MessageReaderExtensions
    {
        /// <summary>
        ///     Reads a file from the <paramref name="reader"/>.
        /// </summary>
        /// <param name="reader">The reader from which to read the file.</param>
        /// <returns>The file.</returns>
        internal static File ReadFile(this MessageReader<MessageCode.Peer> reader)
        {
            var file = new File(
                code: reader.ReadByte(),
                filename: reader.ReadString(),
                size: reader.ReadLong(),
                extension: reader.ReadString(),
                attributeCount: reader.ReadInteger());

            var attributeList = new List<FileAttribute>();

            for (int i = 0; i < file.AttributeCount; i++)
            {
                var attribute = new FileAttribute(
                    type: (FileAttributeType)reader.ReadInteger(),
                    value: reader.ReadInteger());

                attributeList.Add(attribute);
            }

            return new File(
                code: file.Code,
                filename: file.Filename,
                size: file.Size,
                extension: file.Extension,
                attributeCount: file.AttributeCount,
                attributeList: attributeList);
        }

        /// <summary>
        ///     Reads a directory from the <paramref name="reader"/>.
        /// </summary>
        /// <param name="reader">The reader from which to read the directory.</param>
        /// <returns>The directory.</returns>
        internal static Directory ReadDirectory(this MessageReader<MessageCode.Peer> reader)
        {
            var dir = new Directory(
                directoryName: reader.ReadString(),
                fileCount: reader.ReadInteger());

            var fileList = new List<File>();

            for (int j = 0; j < dir.FileCount; j++)
            {
                fileList.Add(reader.ReadFile());
            }

            return new Directory(
                directoryName: dir.DirectoryName,
                fileCount: dir.FileCount,
                fileList: fileList);
        }
    }
}
