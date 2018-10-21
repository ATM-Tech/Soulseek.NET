﻿namespace Soulseek.NET.Tcp
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Net;
    using System.Net.Sockets;
    using System.Threading.Tasks;

    public class Connection : IConnection
    {
        public Connection(ConnectionType type, string address, int port, int readBufferSize = 1024, ITcpClient tcpClient = null)
        {
            Type = type;
            Address = address;
            Port = port;
            ReadBufferSize = readBufferSize;
            TcpClient = tcpClient ?? new TcpClientAdapter(new TcpClient());
        }

        public event EventHandler<DataReceivedEventArgs> DataReceived;
        public event EventHandler<ConnectionStateChangedEventArgs> StateChanged;

        public ConnectionType Type { get; private set; }
        public string Address { get; private set; }
        public IPAddress IPAddress { get; private set; }
        public int Port { get; private set; }
        public int ReadBufferSize { get; private set; }
        public ConnectionState State { get; private set; } = ConnectionState.Disconnected;

        private ITcpClient TcpClient { get; set; }
        private NetworkStream Stream { get; set; }

        public async Task ConnectAsync()
        {
            if (State != ConnectionState.Disconnected)
            {
                throw new ConnectionStateException($"Invalid attempt to connect a connected or transitioning connection (current state: {State})");
            }

            IPAddress ip;

            if (IPAddress.TryParse(Address, out ip))
            {
                IPAddress = ip;
            }
            else
            {
                IPAddress = Dns.GetHostEntry(Address).AddressList[0];
            }

            try
            {
                ChangeServerState(ConnectionState.Connecting, $"Connecting to {IPAddress}:{Port}");

                await TcpClient.ConnectAsync(IPAddress, Port);
                Stream = TcpClient.GetStream();

                ChangeServerState(ConnectionState.Connected, $"Connected to {IPAddress}:{Port}");
            }
            catch (Exception ex)
            {
                ChangeServerState(ConnectionState.Disconnected, $"Connection Error: {ex.Message}");

                throw new ConnectionException($"Failed to connect to {IPAddress}:{Port}: {ex.Message}", ex);
            }

            Task.Run(() => Read()).Forget();
        }

        public void Disconnect(string message = null)
        {
            if (State == ConnectionState.Disconnected || State == ConnectionState.Disconnecting)
            {
                throw new ConnectionStateException($"Invalid attempt to disconnect a disconnected or transitioning connection (current state: {State})");
            }

            ChangeServerState(ConnectionState.Disconnecting, message);

            Stream.Close();
            TcpClient.Close();
            TcpClient.Dispose();

            ChangeServerState(ConnectionState.Disconnected, message);
        }

        private void CheckConnection()
        {
            if (!TcpClient.Connected)
            {
                Disconnect($"The server connection was closed unexpectedly.");
            }
        }

        public async Task SendAsync(byte[] bytes, bool suppressCodeNormalization = false)
        {
            CheckConnection();

            if (State != ConnectionState.Connected)
            {
                throw new ConnectionStateException($"Invalid attempt to send to a disconnected or transitioning connection (current state: {State})");
            }

            if (bytes == null || bytes.Length == 0)
            {
                throw new ArgumentException($"Invalid attempt to send empty data.", nameof(bytes));
            }

            try
            {
                if (!suppressCodeNormalization)
                {
                    NormalizeMessageCode(bytes, 0 - (int)Type);
                }

                await Stream.WriteAsync(bytes, 0, bytes.Length);
            }
            catch (Exception ex)
            {
                Disconnect($"Write Error: {ex.Message}");
            }
        }

        private void ChangeServerState(ConnectionState state, string message)
        {
            State = state;
            StateChanged?.Invoke(this, new ConnectionStateChangedEventArgs() { State = state, Message = message });
        }

        private async Task Read()
        {
            var buffer = new List<byte>();

            try
            {
                while (true)
                {
                    do
                    {
                        var bytes = new byte[ReadBufferSize];
                        var bytesRead = await Stream.ReadAsync(bytes, 0, bytes.Length);

                        if (bytesRead == 0)
                        {
                            //Disconnect($"No data read.");
                            break;
                        }

                        buffer.AddRange(bytes.Take(bytesRead));

                        var headMessageLength = BitConverter.ToInt32(buffer.ToArray(), 0) + 4;

                        if (buffer.Count >= headMessageLength)
                        {
                            var data = buffer.Take(headMessageLength).ToArray();

                            NormalizeMessageCode(data, (int)Type);

                            DataReceived?.Invoke(this, new DataReceivedEventArgs() { Data = data });
                            buffer.RemoveRange(0, headMessageLength);
                        }
                    } while (Stream.DataAvailable);
                }
            }
            catch (Exception ex)
            {
                Disconnect($"Read Error: {ex.Message}");
            }
        }

        private void NormalizeMessageCode(byte[] messageBytes, int newCode)
        {
            var code = BitConverter.ToInt32(messageBytes, 4);
            var adjustedCode = BitConverter.GetBytes(code + newCode);

            Array.Copy(adjustedCode, 0, messageBytes, 4, 4);
        }
    }
}