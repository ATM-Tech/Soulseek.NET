﻿namespace Soulseek.NET.Messaging.Login
{ 
    public class LoginRequest
    {
        public LoginRequest(string username, string password)
        {
            Username = username;
            Password = password;
        }

        public string Username { get; set; }
        public string Password { get; set; }
        public int Version => 181;
        public string Hash => $"{Username}{Password}".ToMD5Hash();
        public int MinorVersion => 1;

        public byte[] ToBytes()
        {
            return new MessageBuilder()
                .Code(MessageCode.Login)
                .WriteString(Username)
                .WriteString(Password)
                .WriteInteger(Version)
                .WriteString(Hash)
                .WriteInteger(MinorVersion)
                .Build();
        }
    }
}
