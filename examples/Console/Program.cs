﻿namespace Console
{
    using Newtonsoft.Json;
    using Soulseek.NET;
    using Soulseek.NET.Messaging.Responses;
    using Soulseek.NET.Tcp;
    using System;
    using System.Linq;
    using System.Threading.Tasks;

    class Program
    {
        public static string ActiveSearchText { get; set; }
        public static int ActiveSearchTicket { get; set; }
        public static System.Timers.Timer StatusTimer { get; set; } = new System.Timers.Timer();

        static async Task Main(string[] args)
        {
            using (var client = new SoulseekClient())
            {
                client.ConnectionStateChanged += Client_ServerStateChanged;

                await client.ConnectAsync();

                Console.WriteLine("Enter username and password:");

                while (true)
                {
                    var cmd = Console.ReadLine();

                    if (cmd == "stop")
                    {
                        client.Disconnect("User requested Disconnect");
                    }
                    if (cmd.StartsWith("search-lazy"))
                    {
                        ActiveSearchText = string.Join(' ', cmd.Split(' ').Skip(1));

                        StatusTimer.Interval = 1000;
                        StatusTimer.Elapsed += (sender, e) => DisplayInfo(client.Peers);
                        StatusTimer.Start();

                        var search = await client.StartSearchAsync(ActiveSearchText);
                        Console.WriteLine($"Search started.  do stuff.");

                        var ended = await client.StopSearchAsync(search);

                        Console.WriteLine($"Search complete.  {ended.Responses.Count()}");
                    }
                    if (cmd.StartsWith("search"))
                    {
                        ActiveSearchText = string.Join(' ', cmd.Split(' ').Skip(1));

                        //var search = client.CreateSearch(ActiveSearchText);
                        //search.SearchResponseReceived += Client_SearchResultReceived;


                        StatusTimer.Interval = 1000;
                        StatusTimer.Elapsed += (sender, e) => DisplayInfo(client.Peers);
                        StatusTimer.Start();

                        //ActiveSearchTicket = search.Ticket;
                        var result = await client.SearchAsync(ActiveSearchText);
                        //search.Start();

                        Console.WriteLine($"Search complete.  {result.Responses.Count()}");
                    }
                    else
                    {
                        var r = await client.LoginAsync(cmd.Split(' ')[0], cmd.Split(' ')[1]);

                        

                        if (r.Succeeded)
                        {
                            Console.WriteLine("Login succeeded");
                            Console.WriteLine(JsonConvert.SerializeObject(r));
                            //break;
                        }
                        else
                        {
                            Console.WriteLine("Login failed");
                        }
                    }
                }
            }
        }

        private static void DisplayInfo(PeerInfo peers)
        {
            Console.WriteLine($"███ Queued: {peers.Queued}, Active: {peers.Active}, Connecting: {peers.Connecting}, Connected: {peers.Connected}, Disconnecting: {peers.Disconnecting}, Disconnected: {peers.Disconnected}");
        }

        private static void Client_SearchResultReceived(object sender, SearchResponseReceivedEventArgs e)
        {
            //Console.WriteLine(JsonConvert.SerializeObject(e, Formatting.Indented, new Newtonsoft.Json.Converters.StringEnumConverter()));
            var t = string.Empty;

            if (e.Response.Ticket != ActiveSearchTicket)
            {
                t = $"<unknown search> ({ActiveSearchTicket} != {e.Response.Ticket})";
            }
            else
            {
                t = $"'{ActiveSearchText}' ({ActiveSearchTicket}): ";
            }

            Console.WriteLine($"[SEARCH] {t} {e.Response.FileCount} results from {e.Response.Username}");

            foreach (var file in e.Response.Files)
            {
                var br = file.Attributes.Where(a => a.Type == FileAttributeType.BitRate).FirstOrDefault();
                Console.WriteLine($"{t}: [{br.Value}] {file.Filename}");
            }
        }

        private static void Client_ServerStateChanged(object sender, ConnectionStateChangedEventArgs e)
        {
            Console.WriteLine($"Server state changed to {e.State} ({e.Message})");
        }
    }
}
