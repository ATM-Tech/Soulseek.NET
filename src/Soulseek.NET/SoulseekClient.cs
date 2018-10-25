﻿namespace Soulseek.NET
{
    using Soulseek.NET.Messaging;
    using Soulseek.NET.Messaging.Requests;
    using Soulseek.NET.Messaging.Responses;
    using Soulseek.NET.Tcp;
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using System.Timers;
    using SystemTimer = System.Timers.Timer;

    public class SoulseekClient : IDisposable
    {
        public SoulseekClient(string address = "server.slsknet.org", int port = 2242)
        {
            Address = address;
            Port = port;

            Connection = new Connection(ConnectionType.Server, Address, Port);
            Connection.StateChanged += OnServerConnectionStateChanged;
            Connection.DataReceived += OnConnectionDataReceived;

            PeerConnectionMonitorTimer = new SystemTimer(5000);
            PeerConnectionMonitorTimer.Elapsed += PeerConnectionMonitor_Elapsed;
        }

        public event EventHandler<ConnectionStateChangedEventArgs> ConnectionStateChanged;
        public event EventHandler<DataReceivedEventArgs> DataReceived;
        public event EventHandler<MessageReceivedEventArgs> MessageReceived;

        public string Address { get; private set; }
        public int ParentMinSpeed { get; private set; }
        public int ParentSpeedRatio { get; private set; }
        public int Port { get; private set; }
        public IEnumerable<string> PrivilegedUsers { get; private set; }
        public IEnumerable<Room> Rooms { get; private set; }
        public int WishlistInterval { get; private set; }

        private List<Search> ActiveSearches { get; set; } = new List<Search>();
        private ReaderWriterLockSlim ActiveSearchesLock { get; set; } = new ReaderWriterLockSlim();
        private Connection Connection { get; set; }
        private bool Disposed { get; set; } = false;
        private MessageWaiter MessageWaiter { get; set; } = new MessageWaiter();
        private SystemTimer PeerConnectionMonitorTimer { get; set; }
        private List<Connection> PeerConnections { get; set; } = new List<Connection>();
        private ReaderWriterLockSlim PeerConnectionsLock { get; set; } = new ReaderWriterLockSlim();
        private Random Random { get; set; } = new Random();

        public void Connect()
        {
            Task.Run(() => ConnectAsync()).GetAwaiter().GetResult();
        }

        public async Task ConnectAsync()
        {
            await Connection.ConnectAsync();
        }

        public Search CreateSearch(string searchText)
        {
            var search = new Search(Connection, searchText);
            search.SearchCompleted += OnSearchCompleted;

            ActiveSearchesLock.EnterWriteLock();

            try
            {
                ActiveSearches.Add(search);
            }
            finally
            {
                ActiveSearchesLock.ExitWriteLock();
            }

            return search;
        }

        public void Disconnect(string message)
        {
            if (string.IsNullOrEmpty(message))
            {
                message = "User initiated shutdown";
            }

            Connection.Disconnect(message);

            PeerConnectionsLock.EnterUpgradeableReadLock();

            try
            {
                var connections = new List<Connection>(PeerConnections);

                PeerConnectionsLock.EnterWriteLock();

                try
                {
                    foreach (var connection in PeerConnections)
                    {
                        connection.Disconnect(message);
                        connection.Dispose();
                        PeerConnections.Remove(connection);
                    }
                }
                finally
                {
                    PeerConnectionsLock.ExitWriteLock();
                }
            }
            finally
            {
                PeerConnectionsLock.ExitUpgradeableReadLock();
            }

            ActiveSearchesLock.EnterUpgradeableReadLock();

            try
            {
                var searches = new List<Search>(ActiveSearches);

                ActiveSearchesLock.EnterWriteLock();

                try
                {
                    foreach (var search in ActiveSearches)
                    {
                        search.Cancel();
                        search.Dispose();
                        ActiveSearches.Remove(search);
                    }
                }
                finally
                {
                    ActiveSearchesLock.ExitWriteLock();
                }
            }
            finally
            {
                ActiveSearchesLock.ExitUpgradeableReadLock();
            }
        }

        public void Dispose()
        {
            Dispose(true);
        }

        public async Task<LoginResponse> LoginAsync(string username, string password)
        {
            try
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

                Rooms = (IEnumerable<Room>)roomList.Result;
                ParentMinSpeed = ((int)parentMinSpeed.Result);
                ParentSpeedRatio = ((int)parentSpeedRatio.Result);
                WishlistInterval = ((int)wishlistInterval.Result);
                PrivilegedUsers = (IEnumerable<string>)privilegedUsers.Result;

                return (LoginResponse)login.Result;
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex);
                return null;
            }
        }

        public Search Search(string searchText)
        {
            return Task.Run(() => SearchAsync(searchText)).GetAwaiter().GetResult();
        }

        public async Task<Search> SearchAsync(string searchText)
        {
            //todo: create and execute search, spin until it is complete, return results
            var search = CreateSearch(searchText);
            await search.StartAsync();
            var result = await MessageWaiter.Wait(MessageCode.ServerFileSearch, search.Ticket).Task;
            return (Search)result;
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!Disposed)
            {
                if (disposing)
                {
                    Connection?.Dispose();
                    ActiveSearches?.ForEach(s => s.Dispose());
                    PeerConnections?.ForEach(c => c.Dispose());
                    PeerConnectionMonitorTimer?.Dispose();
                }

                Disposed = true;
            }
        }

        private async Task HandlePeerSearchReply(SearchResponse response, NetworkEventArgs e)
        {
            if (response.FileCount > 0)
            {
                var search = default(Search);

                ActiveSearchesLock.EnterReadLock();

                try
                {
                    search = ActiveSearches
                        .Where(s => s.State == SearchState.InProgress)
                        .Where(s => s.Ticket == response.Ticket)
                        .SingleOrDefault();
                }
                finally
                {
                    ActiveSearchesLock.ExitReadLock();
                }

                if (search != default(Search))
                {
                    search.AddResult(new SearchResponseReceivedEventArgs(e) { Response = response });
                }
            }
        }

        private async Task HandlePrivateMessage(PrivateMessage message, NetworkEventArgs e)
        {
            Console.WriteLine($"[{message.Timestamp}][{message.Username}]: {message.Message}");
            await Connection.SendAsync(new AcknowledgePrivateMessageRequest(message.Id).ToByteArray());
        }

        private async Task HandleServerConnectToPeer(ConnectToPeerResponse response, NetworkEventArgs e)
        {
            var connection = new Connection(ConnectionType.Peer, response.IPAddress.ToString(), response.Port);
            connection.DataReceived += OnConnectionDataReceived;
            connection.StateChanged += OnPeerConnectionStateChanged;

            PeerConnectionsLock.EnterWriteLock();

            try
            {
                PeerConnections.Add(connection);
            }
            finally
            {
                PeerConnectionsLock.ExitWriteLock();
            }

            try
            {
                await connection.ConnectAsync();

                var request = new PierceFirewallRequest(response.Token);
                await connection.SendAsync(request.ToByteArray(), suppressCodeNormalization: true);
            }
            catch (ConnectionException ex)
            {
                connection.Disconnect($"Failed to connect to peer {response.Username}@{response.IPAddress}:{response.Port}: {ex.Message}");
            }
        }

        private async void OnConnectionDataReceived(object sender, DataReceivedEventArgs e)
        {
            //Console.WriteLine($"Data received: {e.Data.Length} bytes");
            Task.Run(() => DataReceived?.Invoke(this, e)).Forget();

            var message = new Message(e.Data);
            var messageEventArgs = new MessageReceivedEventArgs(e) { Message = message };

            //Console.WriteLine($"Message receiveD: {message.Code}, {message.Payload.Length} bytes");
            Task.Run(() => MessageReceived?.Invoke(this, messageEventArgs)).Forget();

            switch (message.Code)
            {
                case MessageCode.ServerParentMinSpeed:
                case MessageCode.ServerParentSpeedRatio:
                case MessageCode.ServerWishlistInterval:
                    MessageWaiter.Complete(message.Code, Integer.Parse(message));
                    break;

                case MessageCode.ServerLogin:
                    MessageWaiter.Complete(message.Code, LoginResponse.Parse(message));
                    break;

                case MessageCode.ServerRoomList:
                    MessageWaiter.Complete(message.Code, RoomList.Parse(message));
                    break;

                case MessageCode.ServerPrivilegedUsers:
                    MessageWaiter.Complete(message.Code, PrivilegedUserList.Parse(message));
                    break;

                case MessageCode.PeerSearchReply:
                    await HandlePeerSearchReply(SearchResponse.Parse(message), e);
                    break;

                case MessageCode.ServerConnectToPeer:
                    await HandleServerConnectToPeer(ConnectToPeerResponse.Parse(message), e);
                    break;

                case MessageCode.ServerPrivateMessages:
                    await HandlePrivateMessage(PrivateMessage.Parse(message), e);
                    break;

                default:
                    Console.WriteLine($"Unknown message: [{e.IPAddress}] {message.Code}: {message.Payload.Length} bytes");
                    break;
            }
        }

        private void OnPeerConnectionStateChanged(object sender, ConnectionStateChangedEventArgs e)
        {
            if (e.State == ConnectionState.Disconnected && sender is Connection connection)
            {
                PeerConnectionsLock.EnterWriteLock();

                try
                {
                    connection.Dispose();
                    PeerConnections.Remove(connection);
                }
                finally
                {
                    PeerConnectionsLock.ExitWriteLock();
                }
            }
        }

        private async void OnSearchCompleted(object sender, SearchCompletedEventArgs e)
        {
            Console.WriteLine($"Search #{e.Search.Ticket} for '{e.Search.SearchText}' completed.");

            ActiveSearchesLock.EnterWriteLock();

            try
            {
                ActiveSearches.Remove(e.Search);
            }
            finally
            {
                ActiveSearchesLock.ExitWriteLock();
            }

            MessageWaiter.Complete(MessageCode.ServerFileSearch, e.Search.Ticket, e.Search);
        }

        private async void OnServerConnectionStateChanged(object sender, ConnectionStateChangedEventArgs e)
        {
            if (e.State == ConnectionState.Connected)
            {
                PeerConnectionMonitorTimer.Start();
            }

            await Task.Run(() => ConnectionStateChanged?.Invoke(this, e));
        }

        private void PeerConnectionMonitor_Elapsed(object sender, ElapsedEventArgs e)
        {
            try
            {
                var total = 0;
                var connecting = 0;
                var connected = 0;

                PeerConnectionsLock.EnterUpgradeableReadLock();

                try
                {
                    total = PeerConnections.Count();
                    connecting = PeerConnections.Where(c => c?.State == ConnectionState.Connecting).Count();
                    connected = PeerConnections.Where(c => c?.State == ConnectionState.Connected).Count();
                    var disconnectedPeers = new List<Connection>(PeerConnections.Where(c => c == null || c.State == ConnectionState.Disconnected));

                    Console.WriteLine($"████████████████████ Peers: Total: {total}, Connecting: {connecting}, Connected: {connected}, Disconnected: {disconnectedPeers.Count()}");

                    PeerConnectionsLock.EnterWriteLock();

                    try
                    {
                        foreach (var connection in disconnectedPeers)
                        {
                            connection?.Dispose();
                            PeerConnections.Remove(connection);
                        }
                    }
                    finally
                    {
                        PeerConnectionsLock.ExitWriteLock();
                    }
                }
                finally
                {
                    PeerConnectionsLock.ExitUpgradeableReadLock();
                }

                PeerConnectionMonitorTimer.Reset();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in peer connection monitor: {ex}");
            }
        }
    }
}