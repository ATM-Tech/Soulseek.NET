﻿namespace Soulseek.NET
{
    using Soulseek.NET.Messaging;
    using Soulseek.NET.Messaging.Requests;
    using Soulseek.NET.Messaging.Responses;
    using Soulseek.NET.Tcp;
    using System;
    using System.Collections.Generic;
    using System.Threading.Tasks;

    public class SoulseekClient
    {
        public SoulseekClient(string address = "server.slsknet.org", int port = 2242)
        {
            Address = address;
            Port = port;

            Connection = new Connection(ConnectionType.Server, Address, Port);
            Connection.StateChanged += OnConnectionStateChanged;
            Connection.DataReceived += OnConnectionDataReceived;

            MessageMapper = new MessageMapper();
        }

        public event EventHandler<ConnectionStateChangedEventArgs> ConnectionStateChanged;
        public event EventHandler<DataReceivedEventArgs> DataReceived;
        public event EventHandler<MessageReceivedEventArgs> MessageReceived;
        public event EventHandler<SearchResultReceivedEventArgs> SearchResultReceived;

        public string Address { get; private set; }
        public Connection Connection { get; private set; }
        public int Port { get; private set; }

        public IEnumerable<Room> Rooms { get; private set; }
        public int ParentMinSpeed { get; private set; }
        public int ParentSpeedRatio { get; private set; }
        public int WishlistInterval { get; private set; }
        public IEnumerable<string> PrivilegedUsers { get; private set; }

        private MessageMapper MessageMapper { get; set; }
        private MessageWaiter MessageWaiter { get; set; } = new MessageWaiter();

        private List<Connection> PeerConnections { get; set; } = new List<Connection>();
        
        public async Task ConnectAsync()
        {
            await Connection.ConnectAsync();
        }

        public async Task<LoginResponse> LoginAsync(string username, string password)
        {
            var request = new LoginRequest(username, password);

            var login = MessageWaiter.Wait(MessageCode.ServerLogin).Task;
            var roomList = MessageWaiter.Wait(MessageCode.ServerRoomList).Task;
            var parentMinSpeed = MessageWaiter.Wait(MessageCode.ServerParentMinSpeed).Task;
            var parentSpeedRatio = MessageWaiter.Wait(MessageCode.ServerParentSpeedRatio).Task;
            var wishlistInterval = MessageWaiter.Wait(MessageCode.ServerWishlistInterval).Task;
            var privilegedUsers = MessageWaiter.Wait(MessageCode.ServerPrivilegedUsers).Task;

            await Connection.SendAsync(request.ToMessage().ToByteArray());

            Task.WaitAll(login, roomList, parentMinSpeed, parentSpeedRatio, wishlistInterval, privilegedUsers);

            Rooms = ((RoomListResponse)roomList.Result).Rooms;
            ParentMinSpeed = ((IntegerResponse)parentMinSpeed.Result).Value;
            ParentSpeedRatio = ((IntegerResponse)parentSpeedRatio.Result).Value;
            WishlistInterval = ((IntegerResponse)wishlistInterval.Result).Value;
            PrivilegedUsers = ((PrivilegedUsersResponse)privilegedUsers.Result).PrivilegedUsers;

            return (LoginResponse)login.Result;
        }

        public async Task SearchAsync(string searchText)
        {
            var request = new SearchRequest(searchText, 1);
            Console.WriteLine($"Searching for {searchText}...");
            await Connection.SendAsync(request.ToMessage().ToByteArray());
        }

        private void OnConnectionDataReceived(object sender, DataReceivedEventArgs e)
        {
            //Console.WriteLine($"Data Received: {e.Data.Length} bytes");
            Task.Run(() => DataReceived?.Invoke(this, e)).Forget();
            Task.Run(() => OnMessageReceived(new Message(e.Data))).Forget();
        }

        private async void OnMessageReceived(Message message)
        {
            //Console.WriteLine($"Message Recieved: {message.Code}");

            var response = new object();
            var mappedResponse = MessageMapper.MapResponse(message);

            if (mappedResponse != null)
            {
                //Console.WriteLine($"Mapped: {mappedResponse}");
                MessageWaiter.Complete(message.Code, mappedResponse);
            }
            else
            {
                Console.WriteLine($"No Mapping for Code: {message.Code}");

                switch (message.Code)
                {
                    case MessageCode.ServerConnectToPeer:
                        Console.WriteLine("+++++++++++++++++++++++");
                        response = new ConnectToPeerResponse().Map(message);
                        break;
                    case MessageCode.PeerSearchReply:
                        Console.WriteLine("================================================================================================");
                        break;
                    default:
                        Console.WriteLine($"Message Received: Code: {message.Code}");
                        response = null;
                        break;
                }
            }

            MessageReceived?.Invoke(this, new MessageReceivedEventArgs() { Code = message.Code, Response = response });

            if (mappedResponse is ConnectToPeerResponse c)
            {
                //Console.WriteLine($"\tUsername: {c.Username}, Type: {c.Type}, IP: {c.IPAddress}, Port: {c.Port}, Token: {c.Token}");

                var connection = new Connection(ConnectionType.Peer, c.IPAddress.ToString(), c.Port);
                PeerConnections.Add(connection);

                connection.DataReceived += OnConnectionDataReceived;
                //connection.StateChanged += OnPeerConnectionStateChanged;
                try
                {
                    await connection.ConnectAsync();
                    //Console.WriteLine($"\tConnection to {c.Username} opened.");
                }
                catch (ConnectionException ex)
                {
                    Console.WriteLine($"Failed to connect to Peer {c.Username}@{c.IPAddress}: {ex.Message}");
                }

                if (connection.State == ConnectionState.Connected)
                {
                    var request = new PierceFirewallRequest(c.Token);
                    await connection.SendAsync(request.ToByteArray(), suppressCodeNormalization: true);
                }
            }

            if (mappedResponse is PeerSearchReply reply)
            {
                Task.Run(() => SearchResultReceived?.Invoke(this, (SearchResultReceivedEventArgs)reply)).Forget();
            }
        }

        private void OnPeerConnectionStateChanged(object sender, ConnectionStateChangedEventArgs e)
        {
            Console.WriteLine($"\tPeer Connection State Changed: {e.State} ({e.Message ?? "Unknown"})");
        }

        private void OnConnectionStateChanged(object sender, ConnectionStateChangedEventArgs e)
        {
            Console.WriteLine($"Connection State Changed: {e.State} ({e.Message ?? "Unknown"})");
            ConnectionStateChanged?.Invoke(this, e);
        }
    }
}