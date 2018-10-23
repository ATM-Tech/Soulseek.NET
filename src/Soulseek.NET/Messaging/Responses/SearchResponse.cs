﻿namespace Soulseek.NET.Messaging.Responses
{
    using System.Collections.Generic;

    public sealed class SearchResponse
    {
        public string Username { get; private set; }
        public int Ticket { get; private set; }
        public int FileCount { get; private set; }
        public IEnumerable<File> Files => FileList;
        public int FreeUploadSlots { get; private set; }
        public int UploadSpeed { get; set; }
        public int InQueue { get; set; }

        private List<File> FileList { get; set; } = new List<File>();

        private SearchResponse()
        {
        }

        public static SearchResponse Parse(Message message)
        {
            var reader = new MessageReader(message);

            if (reader.Code != MessageCode.PeerSearchReply)
            {
                throw new MessageException($"Message Code mismatch creating Peer Search Reply (expected: {(int)MessageCode.PeerSearchReply}, received: {(int)reader.Code}");
            }

            reader.Decompress();

            var response = new SearchResponse
            {
                Username = reader.ReadString(),
                Ticket = reader.ReadInteger(),
                FileCount = reader.ReadInteger()
            };

            //Console.WriteLine($"User: {Username}, Ticket: {Ticket}, FileCount: {FileCount}");

            for (int i = 0; i < response.FileCount; i++)
            {
                //Console.WriteLine($"#{i}");
                var file = new File
                {
                    Code = reader.ReadByte(),
                    //Console.WriteLine($"Code: {file.Code}");
                    Filename = reader.ReadString(),
                    //Console.WriteLine($"Filename: {file.Filename}");
                    Size = reader.ReadLong(),
                    //Console.WriteLine($"Size: {file.Size}");
                    Extension = reader.ReadString(),
                    //Console.WriteLine($"Ext: {file.Extension}");
                    AttributeCount = reader.ReadInteger()
                };
                //Console.WriteLine($"Attributes: {file.AttributeCount}");

                for (int j = 0; j < file.AttributeCount; j++)
                {
                    //Console.WriteLine($"#{j}");
                    var attribute = new FileAttribute
                    {
                        Type = (FileAttributeType)reader.ReadInteger(),
                        Value = reader.ReadInteger()
                    };
                    //Console.WriteLine($"Attribute type: {attribute.Type}, value: {attribute.Value}");
                    ((List<FileAttribute>)file.Attributes).Add(attribute);
                }

                response.FileList.Add(file);
            }

            response.FreeUploadSlots = reader.ReadByte();
            response.UploadSpeed = reader.ReadInteger();
            response.InQueue = reader.ReadInteger();

            return response;
        }
    }

    public sealed class File
    {
        public int Code { get; internal set; }
        public string Filename { get; internal set; }
        public long Size { get; internal set; }
        public string Extension { get; internal set; }
        public int AttributeCount { get; internal set; }
        public IEnumerable<FileAttribute> Attributes { get; internal set; } = new List<FileAttribute>();

        internal File()
        {
        }
    }

    public sealed class FileAttribute
    {
        public FileAttributeType Type { get; internal set; }
        public int Value { get; internal set; }

        internal FileAttribute()
        {
        }
    }

    public enum FileAttributeType
    {
        BitRate = 0,
        Length = 1,
        Unknown = 2,
        SampleRate = 4,
        BitDepth = 5,
    }
}
