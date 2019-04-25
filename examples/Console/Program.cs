﻿namespace Console
{
    using global::Utility.CommandLine;
    using Newtonsoft.Json;
    using Soulseek;
    using Soulseek.NET;
    using Soulseek.NET.Messaging.Messages;
    using Soulseek.NET.Tcp;
    using System;
    using System.Collections.Concurrent;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Text;
    using System.Threading.Tasks;
    using System.Timers;

    public static class Program
    {
        private static readonly Action<string> o = (s) => Console.WriteLine(s);

        [Argument('l', "album")]
        private static string Album { get; set; } = string.Empty;

        [Argument('a', "artist")]
        private static string Artist { get; set; }

        [Argument('b', "browse")]
        private static string Browse { get; set; }

        [EnvironmentVariable("SLSK_OUTPUT_DIR")]
        [Argument('o', "output-directory")]
        private static string OutputDirectory { get; set; }

        [Argument('d', "download")]
        private static string Download { get; set; }

        [Argument('f', "file")]
        private static List<string> Files { get; set; } = new List<string>();

        [Argument('p', "password")]
        [EnvironmentVariable("SLSK_PASSWORD")]
        private static string Password { get; set; }

        private static ConcurrentDictionary<(string Username, string Filename, int Token), (DownloadStates State, Spinner Spinner, ProgressBar ProgressBar)> Downloads { get; set; } 
            = new ConcurrentDictionary<(string Username, string Filename, int Token), (DownloadStates State, Spinner Spinner, ProgressBar ProgressBar)>();

        [Argument('s', "search")]
        private static string Search { get; set; }

        [Argument('u', "username")]
        [EnvironmentVariable("SLSK_USERNAME")]
        private static string Username { get; set; } = "foo";

        private static async Task ConnectAndLogin(SoulseekClient client)
        {
            Console.Write("\nConnecting...");
            await client.ConnectAsync();
            Console.Write("\rConnected.  Logging in...");
            await client.LoginAsync(Username, Password);
            o("\rConnected and logged in.    \n");
        }

        public static async Task Main(string[] args)
        {
            Console.OutputEncoding = Encoding.UTF8;

            EnvironmentVariables.Populate();
            Arguments.Populate(clearExistingValues: false);

            var options = new SoulseekClientOptions(
                minimumDiagnosticLevel: DiagnosticLevel.None,
                peerConnectionOptions: new ConnectionOptions(connectTimeout: 30, readTimeout: 5),
                transferConnectionOptions: new ConnectionOptions(connectTimeout: 30, readTimeout: 10)
            );

            using (var client = new SoulseekClient(options))
            {
                client.StateChanged += Client_ServerStateChanged;
                client.DiagnosticGenerated += Client_DiagnosticMessageGenerated;
                client.PrivateMessageReceived += Client_PrivateMessageReceived;

                if (!string.IsNullOrEmpty(Search))
                {
                    await ConnectAndLogin(client);

                    var responses = await SearchAsync(client, Search, 1);

                    responses = responses
                        .OrderByDescending(r => r.FreeUploadSlots)
                        .ThenByDescending(r => r.UploadSpeed);

                    var response = SelectSearchResponse(responses);

                    o($"\nDownloading {response.Files.Count()} file{(response.Files.Count() > 1 ? "s" : string.Empty)} from {response.Username}...\n");

                    await DownloadFilesAsync(client, response.Username, response.Files.Select(f => f.Filename).ToList()).ConfigureAwait(false);

                    o($"\nDownload{(response.Files.Count() > 1 ? "s" : string.Empty)} complete.");
                }
                if (!string.IsNullOrEmpty(Download) && Files != null && Files.Count > 0)
                {
                    await ConnectAndLogin(client);

                    o($"\nDownloading {Files.Count()} file{(Files.Count() > 1 ? "s" : string.Empty)} from {Download}...\n");

                    await DownloadFilesAsync(client, Download, Files);

                    o($"\nDownload{(Files.Count() > 1 ? "s" : string.Empty)} complete.");
                }
                else if (!string.IsNullOrEmpty(Browse))
                {
                    await ConnectAndLogin(client);

                    o($"Browsing user {Browse}...");
                    var results = await client.BrowseAsync(Browse);

                    var file = new FileInfo(Path.Combine(OutputDirectory, "browse", $"{Browse}-{DateTime.Now.ToString().ToSafeFilename()}.json"));
                    file.Directory.Create();
                    System.IO.File.WriteAllText(file.FullName, JsonConvert.SerializeObject(results));
                }
                else if (!string.IsNullOrEmpty(Artist))
                {
                    var artist = await SelectArtist(Artist);
                    var releaseGroup = await SelectReleaseGroup(artist, Album);
                    var release = await SelectRelease(releaseGroup);
                    IEnumerable<SearchResponse> responses = null;

                    await ConnectAndLogin(client);

                    var searchText = artist.Name == release.Title ? $"{artist.Name} {release.Date.ToFuzzyDateTime().ToString("yyyy")}" : $"{artist.Name} {release.Title}";
                    responses = await SearchAsync(client, searchText, release.TrackCount);

                    responses = responses
                        .OrderByDescending(r => r.FreeUploadSlots)
                        .ThenByDescending(r => r.UploadSpeed);

                    var response = SelectSearchResponse(responses);
                    
                    o($"\nDownloading {response.Files.Count()} file{(response.Files.Count() > 1 ? "s" : string.Empty)} from {response.Username}...\n");

                    await DownloadFilesAsync(client, response.Username, response.Files.Select(f => f.Filename).ToList()).ConfigureAwait(false);

                    o($"\nDownload{(response.Files.Count() > 1 ? "s" : string.Empty)} complete.");
                }

                client.StateChanged -= Client_ServerStateChanged;
                client.DiagnosticGenerated -= Client_DiagnosticMessageGenerated;
                client.PrivateMessageReceived -= Client_PrivateMessageReceived;
            }
        }

        private static void Client_DiagnosticMessageGenerated(object sender, DiagnosticGeneratedEventArgs e)
        {
            Console.WriteLine($"[DIAGNOSTICS] [{e.Level}]: {e.Message}");
        }

        private static void Client_PrivateMessageReceived(object sender, PrivateMessage e)
        {
            Console.WriteLine($"[{e.Timestamp}] [{e.Username}]: {e.Message}");
        }

        private static void Client_ServerStateChanged(object sender, SoulseekClientStateChangedEventArgs e)
        {
            if (e.State == SoulseekClientStates.Disconnected)
            {
                o("\n×  Disconnected from server" + (!string.IsNullOrEmpty(e.Message) ? $": {e.Message}" : "." ));
            }
        }

        private static async Task DownloadFilesAsync(SoulseekClient client, string username, List<string> files)
        {
            var index = 0;

            var tasks = files.Select(async file =>
            {
                try
                {
                    var bytes = await client.DownloadAsync(username, file, index++, new DownloadOptions(stateChanged: (e) =>
                    {
                        var key = (e.Username, e.Filename, e.Token);
                        var download = Downloads.GetOrAdd(key, (e.State, null, new ProgressBar(10)));
                        download.State = e.State;
                        download.ProgressBar = new ProgressBar(10, format: new ProgressBarFormat(left: "[", right: "]", full: '=', tip: '>', empty: ' ', emptyWhen: () => Downloads[key].State.HasFlag(DownloadStates.Completed)));
                        download.Spinner = new Spinner("⠋⠙⠹⠸⠼⠴⠦⠧⠇⠏", format: new SpinnerFormat(completeWhen: () => Downloads[key].State.HasFlag(DownloadStates.Completed)));
                        
                        Downloads.AddOrUpdate(key, download, (k, v) => download);

                        if (download.State.HasFlag(DownloadStates.Completed))
                        {
                            o(string.Empty); // new line
                        }
                    }, progressUpdated: (e) =>
                    {
                        var key = (e.Username, e.Filename, e.Token);
                        Downloads.TryGetValue(key, out var download);

                        download.State = e.State;
                        download.ProgressBar.Value = (int)e.PercentComplete;

                        var status = $"{$"{Downloads.Where(d => d.Value.State.HasFlag(DownloadStates.Completed)).Count() + 1}".PadLeft(Downloads.Count.ToString().Length)}/{Downloads.Count}"; // [ 1/17]

                        Downloads.AddOrUpdate(key, download, (k, v) => download);

                        var longest = Downloads.Max(d => Path.GetFileName(d.Key.Filename.ToLocalOSPath()).Length);
                        var fn = Path.GetFileName(e.Filename.ToLocalOSPath()).PadRight(longest);

                        var size = $"{e.BytesDownloaded.ToMB()}/{e.Size.ToMB()}".PadLeft(15);
                        var percent = $"({e.PercentComplete.ToString("N0").PadLeft(3)}%)";

                        Console.Write($"\r {download.Spinner}  {fn}  {size}  {percent}  [{status}]  {download.ProgressBar} {e.AverageSpeed.ToMB()}/s");

                    })).ConfigureAwait(false);

                    // GetDirectoryName() and GetFileName() only work when the path separator is the same as the current OS' DirectorySeparatorChar.
                    // normalize for both Windows and Linux by replacing / and \ with Path.DirectorySeparatorChar.
                    file = file.ToLocalOSPath();

                    var path = $"{OutputDirectory}{Path.DirectorySeparatorChar}{Path.GetDirectoryName(file).Replace(Path.GetDirectoryName(Path.GetDirectoryName(file)), "")}";

                    if (!System.IO.Directory.Exists(path))
                    {
                        System.IO.Directory.CreateDirectory(path);
                    }

                    var filename = Path.Combine(path, Path.GetFileName(file));

                    System.IO.File.WriteAllBytes(filename, bytes);
                }
                catch (Exception ex)
                {
                    o($"Error downloading {file}: {ex.Message}");
                }
            });

            await Task.WhenAll(tasks).ConfigureAwait(false);
        }

        private static void ListReleaseTracks(Release release)
        {
            var discs = release.Media.OrderBy(m => m.Position);

            foreach (var disc in discs)
            {
                o($"\n{disc.Format} {disc.Position}{(string.IsNullOrEmpty(disc.Title) ? string.Empty : $": {disc.Title}")}\n");

                var longest = disc.Tracks.Max(t => t.Title.Length);
                var digitCount = disc.TrackCount.ToString().Length;

                foreach (var track in disc.Tracks)
                {
                    o($"  {track.Position.ToString("D2")}  {track.Title.PadRight(longest)}  {TimeSpan.FromMilliseconds(track.Length ?? 0).ToString(@"m\:ss")}");
                }
            }
        }

        private static void ListResponseFiles(Dictionary<string, List<Soulseek.NET.File>> directories)
        {
            for (int i = 0; i < directories.Count; i++)
            {
                var key = directories.Keys.ToList()[i];
                o($"\n{(i + 1)}.  {key}\n");

                var longest = directories[key].Max(f => Path.GetFileName(f.Filename.ToLocalOSPath()).Length);

                foreach (var file in directories[key])
                {
                    var filename = file.Filename.ToLocalOSPath();
                    o($"    {Path.GetFileName(filename).PadRight(longest)}  {file.Size.ToMB().PadLeft(7)}  {$"{file.BitRate}kbps".PadLeft(9)}  {TimeSpan.FromSeconds(file.Length ?? 0).ToString(@"m\:ss").PadLeft(7)}");
                }
            }
        }

        private static async Task<IEnumerable<SearchResponse>> SearchAsync(SoulseekClient client, string searchText, int minimumFileCount = 0)
        {
            var complete = false;
            var spinner = new Spinner("⠋⠙⠹⠸⠼⠴⠦⠧⠇⠏", format: new SpinnerFormat(completeWhen: () => complete));
            var totalResponses = 0;
            var totalFiles = 0;
            var state = SearchStates.None;

            var timer = new Timer(100);
            timer.Elapsed += (e, a) => updateStatus();

            void updateStatus()
            {
                Console.Write($"\r{spinner}  {(complete ? "Search complete." : $"Searching for '{searchText}':")} found {totalFiles} files from {totalResponses} users".PadRight(Console.WindowWidth - 1) + (complete ? "\n" : string.Empty));
            }

            timer.Start();

            IEnumerable<SearchResponse> responses = await client.SearchAsync(searchText,
                new SearchOptions(
                    filterResponses: true,
                    minimumResponseFileCount: minimumFileCount,
                    searchTimeout: 5,
                    stateChanged: (e) => state = e.State,
                    responseReceived: (e) =>
                    {
                        totalResponses++;
                        totalFiles += e.Response.FileCount;
                    }, 
                    fileFilter: (file) => Path.GetExtension(file.Filename) == ".mp3"));

            timer.Stop();
            complete = true;
            updateStatus();

            return responses;
        }

        private static async Task<Artist> SelectArtist(string artist)
        {
            o($"\nSearching for artist '{artist}'...");

            var artists = await MusicBrainz.GetMatchingArtists(artist);
            var artistList = artists.OrderByDescending(a => a.Score).ToList();

            var longest = artistList.Max(a => a.DisambiguatedName.Length);

            o($"\nBest matching Artists:\n");

            for (int i = 0; i < artistList.Count; i++)
            {
                o($"  {(i + 1).ToString().PadLeft(3)}.  {artistList[i].DisambiguatedName.PadRight(longest)}  {artistList[i].Score.ToString().PadLeft(3)}%");
            }

            do
            {
                Console.Write($"\nSelect artist (1-{artistList.Count}): ");

                var selection = Console.ReadLine();

                try
                {
                    var num = Int32.Parse(selection) - 1;
                    return artistList[num];
                }
                catch (Exception)
                {
                    Console.Write($"Invalid input.  ");
                }
            } while (true);
        }

        private static async Task<Release> SelectRelease(ReleaseGroup releaseGroup)
        {
            o($"\nSearching for releases in release group '{releaseGroup.Title}'...");

            var releases = await MusicBrainz.GetReleaseGroupReleases(Guid.Parse(releaseGroup.ID));
            var releaseList = releases
                .OrderBy(r => r.Date.ToFuzzyDateTime())
                .ToList();

            var longest = releases.Max(r => r.DisambiguatedTitle.Length);
            var longestFormat = releases.Max(r => r.Format.Length);
            var longestTrackCount = releases.Max(r => r.TrackCountExtended.Length);

            o("\nReleases:\n");

            for (int i = 0; i < releaseList.Count; i++)
            {
                var r = releaseList[i];
                var format = string.Join("+", r.Media.Select(m => m.Format));
                var tracks = string.Join("+", r.Media.Select(m => m.TrackCount));
                o($"  {(i + 1).ToString().PadLeft(3)}.  {r.Date.ToFuzzyDateTime().ToString("yyyy-MM-dd")}  {r.DisambiguatedTitle.PadRight(longest)}  {r.Format.PadRight(longestFormat)}  {r.TrackCountExtended.PadRight(longestTrackCount)}  {r.Country}");
            }

            do
            {
                Console.Write($"\nSelect release (1-{releaseList.Count}): ");

                var selection = Console.ReadLine();

                try
                {
                    var num = Int32.Parse(selection) - 1;
                    var release = releaseList[num];

                    o($"\nTrack list for '{release.DisambiguatedTitle}', {release.Date.ToFuzzyDateTime().ToString("yyyy-MM-dd")}, {release.Format}, {release.TrackCountExtended}, {release.Country}:");
                    ListReleaseTracks(release);

                    Console.Write($"\nProceed with this track list? (Y/N): ");

                    var proceed = Console.ReadLine();

                    if (proceed.Equals("Y", StringComparison.InvariantCultureIgnoreCase))
                    {
                        return release;
                    }

                    continue;
                }
                catch (Exception)
                {
                    Console.Write($"Invalid input.  ");
                }
            } while (true);
        }

        private static async Task<ReleaseGroup> SelectReleaseGroup(Artist artist, string album)
        {
            var showAll = string.IsNullOrEmpty(album);

            o($"\nSearching for '{artist.Name}' release groups{(showAll ? string.Empty : $" matching '{album}'")}...");

            var limit = showAll ? Int32.MaxValue : 25;

            var releaseGroups = await MusicBrainz.GetArtistReleaseGroups(Guid.Parse(artist.ID));
            var releaseGroupList = releaseGroups
                .Select(r => r.WithScore(r.Title.SimilarityCaseInsensitive(album)))
                .OrderByDescending(r => r.Score)
                .ThenBy(r => r.Type)
                .ThenBy(r => r.Year, new SemiNumericComparer())
                .ThenBy(r => r.DisambiguatedTitle)
                .Take(limit)
                .ToList();

            var longest = releaseGroupList.Max(r => r.DisambiguatedTitle.Length);
            var longestType = releaseGroupList.Max(r => r.Type.Length);

            o(showAll ? "\nRelease groups:\n" : "\nBest matching release groups:\n");

            for (int i = 0; i < releaseGroupList.Count; i++)
            {
                var r = releaseGroupList[i];

                o($"  {(i + 1).ToString().PadLeft(3)}.  {r.Year}  {r.DisambiguatedTitle.PadRight(longest)}  {r.Type.PadRight(longestType)}  {(string.IsNullOrEmpty(album) ? string.Empty : Math.Round(r.Score * 100, 0).ToString().PadLeft(3) + "%")}");
            }

            do
            {
                Console.Write($"\nSelect release group (1-{releaseGroupList.Count}): ");

                var selection = Console.ReadLine();

                try
                {
                    var num = Int32.Parse(selection) - 1;
                    return releaseGroupList[num];
                }
                catch (Exception)
                {
                    Console.Write($"Invalid input.  ");
                }
            } while (true);
        }

        private static (string Username, IEnumerable<Soulseek.NET.File> Files) SelectSearchResponse(IEnumerable<SearchResponse> responses)
        {
            var index = 0;

            do
            {
                var response = responses.ToList()[index];

                var cnt = $"Response {index + 1}/{responses.Count()}";
                var res = $"User: {response.Username}, Upload speed: {response.UploadSpeed.ToKB()}/s, Free upload slots: {response.FreeUploadSlots}, Queue length: {response.QueueLength}";

                o($"\n┌{new string('─', res.Length - 29)} ──────── ──      ─ ─");
                o($"│ {cnt}");
                o($"│ {res} │");
                o($"└{new string('─', res.Length - 19)}  ───── ─── ─     ─ ─┘");

                var directories = response.Files
                    .GroupBy(f => Path.GetDirectoryName(f.Filename))
                    .ToDictionary(g => g.Key, g => g.ToList());

                ListResponseFiles(directories);

                Console.Write($"\nSelect directory (1-{directories.Count}) or press ENTER to show next result: ");

                var proceed = Console.ReadLine();

                if (!proceed.Equals(string.Empty, StringComparison.InvariantCultureIgnoreCase))
                {
                    var num = int.Parse(proceed) - 1;
                    var key = directories.Keys.ToList()[num];
                    return (response.Username, directories[key]);
                }

                index++;
            } while (true);
        }
    }
}