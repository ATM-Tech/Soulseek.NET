﻿namespace Console
{
    using global::Utility.CommandLine;
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

    public class Program
    {
        private static readonly Action<string> o = (s) => Console.WriteLine(s);

        [Argument('l', "album")]
        private static string Album { get; set; } = string.Empty;

        [Argument('a', "artist")]
        private static string Artist { get; set; }

        [Argument('i', "ignore-user")]
        private static string[] IgnoredUsers { get; set; } = new string[] { };

        [Argument('p', "password")]
        private static string Password { get; set; }

        private static ConcurrentDictionary<string, ProgressBar> Progress { get; set; } = new ConcurrentDictionary<string, ProgressBar>();

        [Argument('u', "username")]
        private static string Username { get; set; } = "foo";

        private static void Client_DiagnosticMessageGenerated(object sender, DiagnosticGeneratedEventArgs e)
        {
            Console.WriteLine($"[DIAGNOSTICS] [{e.Level}]: {e.Message}");
        }

        private static void Client_DownloadProgress(object sender, DownloadProgressUpdatedEventArgs e)
        {
            var key = $"{e.Username}:{e.Filename}:{e.Token}";
            Progress.AddOrUpdate(key, new ProgressBar(30, 0, 100, 1, (int)e.PercentComplete), (k, v) =>
            {
                Progress[k].Value = (int)e.PercentComplete;
                return Progress[k];
            });

            Console.Write($"\r[PROGRESS]: {e.Filename}: {Progress[key]}%");

            if (e.PercentComplete == 100)
            {
                Console.Write("\n");
            }
        }

        private static void Client_DownloadStateChanged(object sender, DownloadStateChangedEventArgs e)
        {
            Console.WriteLine($"[DOWNLOAD] [{e.Filename}]: {e.PreviousState} ==> {e.State}");
        }

        private static void Client_PrivateMessageReceived(object sender, PrivateMessage e)
        {
            Console.WriteLine($"[{e.Timestamp}] [{e.Username}]: {e.Message}");
        }

        private static void Client_ServerStateChanged(object sender, SoulseekClientStateChangedEventArgs e)
        {
            Console.WriteLine($"Server state changed to {e.State} ({e.Message})");
        }

        private static async Task DownloadFilesAsync(SoulseekClient client, string username, IEnumerable<string> files)
        {
            var random = new Random();

            var tasks = files.Select(async file =>
            {
                Console.WriteLine($"Attempting to download {file}");
                try
                {
                    var bytes = await client.DownloadAsync(username, file, random.Next());

                    var path = $@"downloads" + Path.GetDirectoryName(file).Replace(Path.GetDirectoryName(Path.GetDirectoryName(file)), "");

                    if (!System.IO.Directory.Exists(path))
                    {
                        System.IO.Directory.CreateDirectory(path);
                    }

                    var filename = Path.Combine(path, Path.GetFileName(file));

                    Console.WriteLine($"Bytes received: {bytes.Length}; writing to file {filename}...");
                    System.IO.File.WriteAllBytes(filename, bytes);
                    Console.WriteLine("Download complete!");
                }
                catch (Exception ex)
                {
                    o($"Error downloading {file}: {ex.Message}");
                }
            });

            await Task.WhenAll(tasks);
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
                    o($"   {track.Position.ToString("D2")}  {track.Title.PadRight(longest)}  {TimeSpan.FromMilliseconds(track.Length ?? 0).ToString(@"m\:ss")}");
                }
            }
        }

        private static async Task Main(string[] args)
        {
            Console.OutputEncoding = Encoding.UTF8;
            Arguments.Populate(clearExistingValues: false);

            var artist = await SelectArtist(Artist);
            var releaseGroup = await SelectReleaseGroup(artist, Album);
            var release = await SelectRelease(releaseGroup);

            var options = new SoulseekClientOptions(
                minimumDiagnosticLevel: DiagnosticLevel.Warning,
                peerConnectionOptions: new ConnectionOptions(connectTimeout: 30, readTimeout: 30),
                transferConnectionOptions: new ConnectionOptions(connectTimeout: 30, readTimeout: 10)
            );

            using (var client = new SoulseekClient(options))
            {
                client.StateChanged += Client_ServerStateChanged;
                client.DownloadProgressUpdated += Client_DownloadProgress;
                client.DownloadStateChanged += Client_DownloadStateChanged;
                client.DiagnosticGenerated += Client_DiagnosticMessageGenerated;
                client.PrivateMessageReceived += Client_PrivateMessageReceived;

                await client.ConnectAsync();
                await client.LoginAsync(Username, Password);

                await SearchAsync(client, artist, release);
            }
        }

        private static async Task SearchAsync(SoulseekClient client, Artist artist, Release release)
        {
            var searchText = $"{artist.Name} {release.Title}";

            var complete = false;
            var spinner = new Spinner("⠋⠙⠹⠸⠼⠴⠦⠧⠇⠏", format: new SpinnerFormat(completeWhen: () => complete));
            var totalResponses = 0;
            var totalFiles = 0;

            var timer = new Timer(100);
            timer.Elapsed += (e, a) => updateStatus();

            void updateStatus()
            {
                Console.Write($"\r{spinner} {(complete ? "Search complete." : "Performing search:")} found {totalFiles} files from {totalResponses} users".PadRight(Console.WindowWidth - 1) + (complete ? "\n" : string.Empty));
            }

            o($"Searching for '{searchText}'...");
            timer.Start();

            IEnumerable<SearchResponse> responses = await client.SearchAsync(searchText,
                new SearchOptions(
                    filterResponses: true,
                    minimumResponseFileCount: release.TrackCount,
                    filterFiles: true,
                    ignoredFileExtensions: new string[] { "flac", "m4a", "wav" }
                ), eventHandler: (sender, e) =>
                {
                    totalResponses++;
                    totalFiles += e.Response.FileCount;
                });

            timer.Stop();
            complete = true;
            updateStatus();

            //var bannedUsers = new string[] {  };
            //responses = responses.Where(r => !bannedUsers.Contains(r.Username));

            //var freeResponses = responses.Where(r => r.FreeUploadSlots > 0);
            //SearchResponse bestResponse = null;

            //if (freeResponses.Any())
            //{
            //    responses = freeResponses;
            //    o($"Users with free upload slots: {responses.Count()}");

            //    bestResponse = responses
            //        .OrderByDescending(r => r.UploadSpeed)
            //        .First();
            //}
            //else
            //{
            //    o($"No users with free upload slots.");

            //    bestResponse = responses
            //        .OrderBy(r => r.QueueLength)
            //        .First();
            //}

            //o($"Best response from: {bestResponse.Username}");

            //var maxLen = bestResponse.Files.Max(f => f.Filename.Length);

            //foreach (var file in bestResponse.Files)
            //{
            //    o($"{file.Filename.PadRight(maxLen)}\t{file.Length}\t{file.BitRate}\t{file.Size}");
            //}

            ////await DownloadFilesAsync(client, bestResponse.Username, bestResponse.Files.Select(f => f.Filename));

            //Console.WriteLine($"All files complete.");
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
    }
}