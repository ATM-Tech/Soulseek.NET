﻿namespace WebAPI
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Net;
    using System.Reflection;
    using System.Threading;
    using System.Threading.Tasks;
    using Microsoft.AspNetCore.Builder;
    using Microsoft.AspNetCore.Hosting;
    using Microsoft.AspNetCore.Mvc;
    using Microsoft.AspNetCore.Mvc.ApiExplorer;
    using Microsoft.Extensions.Configuration;
    using Microsoft.Extensions.DependencyInjection;
    using Microsoft.Extensions.FileProviders;
    using Newtonsoft.Json;
    using Newtonsoft.Json.Converters;
    using Soulseek;
    using Soulseek.Diagnostics;
    using Soulseek.Exceptions;
    using Swashbuckle.AspNetCore.Swagger;
    using WebAPI.Trackers;

    public class Startup
    {
        private static string Username { get; set; }
        private static string Password { get; set; }
        private static string WebRoot { get; set; }
        private static int ListenPort { get; set; }
        public static string OutputDirectory { get; private set; }
        private static string SharedDirectory { get; set; }

        private SoulseekClient Client { get; set; }
        private object ConsoleSyncRoot { get; } = new object();

        public Startup(IConfiguration configuration)
        {
            Configuration = configuration;

            Username = Configuration.GetValue<string>("USERNAME");
            Password = Configuration.GetValue<string>("PASSWORD");
            WebRoot = Configuration.GetValue<string>("WEBROOT");
            ListenPort = Configuration.GetValue<int>("LISTEN_PORT");
            OutputDirectory = Configuration.GetValue<string>("OUTPUT_DIR");
            SharedDirectory = Configuration.GetValue<string>("SHARED_DIR");

            SharedDirectory = @"\\WSE\Music\Processed\Rage Against the Machine\Bootlegs\Killing Your Enemy In 1995";
        }

        public IConfiguration Configuration { get; }

        public void ConfigureServices(IServiceCollection services)
        {
            services.AddCors(options => options.AddPolicy("AllowAll", builder => builder.AllowAnyOrigin().AllowAnyMethod().AllowAnyHeader()));

            services.AddMvc()
                .SetCompatibilityVersion(CompatibilityVersion.Latest)
                .AddJsonOptions(options =>
                {
                    options.SerializerSettings.Converters.Add(new StringEnumConverter());
                    options.SerializerSettings.Converters.Add(new IPAddressConverter());
                    options.SerializerSettings.ReferenceLoopHandling = ReferenceLoopHandling.Ignore;
                    options.SerializerSettings.NullValueHandling = NullValueHandling.Ignore;
                });

            services.AddApiVersioning(options => options.ReportApiVersions = true);
            services.AddVersionedApiExplorer(options =>
            {
                options.GroupNameFormat = "'v'VVV";
                options.SubstituteApiVersionInUrl = true;
            });

            services.AddSwaggerGen(options =>
            {
                services.BuildServiceProvider().GetRequiredService<IApiVersionDescriptionProvider>()
                    .ApiVersionDescriptions.ToList()
                        .ForEach(description => options.SwaggerDoc(description.GroupName, new Info { Title = "Soulseek.NET Example API", Version = description.GroupName }));

                options.IncludeXmlComments(Path.Combine(AppContext.BaseDirectory, typeof(Startup).GetTypeInfo().Assembly.GetName().Name + ".xml"));
            });

            services.AddSingleton<ISoulseekClient, SoulseekClient>(serviceProvider => Client);
            services.AddSingleton<ITransferTracker, TransferTracker>();
            services.AddSingleton<ISearchTracker, SearchTracker>();
        }

        public void Configure(IApplicationBuilder app, IHostingEnvironment env, IApiVersionDescriptionProvider provider, ITransferTracker tracker)
        {
            if (!env.IsDevelopment())
            {
                app.UseHsts();
            }

            app.UseCors("AllowAll");

            WebRoot = WebRoot ?? Path.Combine(Path.GetDirectoryName(new Uri(Assembly.GetExecutingAssembly().GetName().CodeBase).AbsolutePath), "wwwroot");
            Console.WriteLine($"Serving static content from {WebRoot}");

            app.UseFileServer(new FileServerOptions
            {
                FileProvider = new PhysicalFileProvider(WebRoot ?? Path.Combine(Path.GetDirectoryName(new Uri(Assembly.GetExecutingAssembly().GetName().CodeBase).AbsolutePath), "wwwroot")),
                RequestPath = "",
                EnableDirectoryBrowsing = false,
                EnableDefaultFiles = true
            });

            app.UseMvc();

            app.UseSwagger(options => 
            {
                // use camelCasing for routes and properties
                options.PreSerializeFilters.Add((document, request) =>
                {
                    string camelCase(string key) =>
                        string.Join('/', key.Split('/').Select(x => x.Contains("{") || x.Length < 2 ? x : char.ToLowerInvariant(x[0]) + x.Substring(1)));

                    document.Paths = document.Paths.ToDictionary(p => camelCase(p.Key), p => p.Value);
                    document.Paths.ToList()
                        .ForEach(path => typeof(PathItem).GetProperties().Where(p => p.PropertyType == typeof(Operation)).ToList()
                            .ForEach(operation => ((Operation)operation.GetValue(path.Value, null))?.Parameters.ToList()
                                .ForEach(prop => prop.Name = camelCase(prop.Name))));
                });
            });

            app.UseSwaggerUI(options => provider.ApiVersionDescriptions.ToList()
                .ForEach(description => options.SwaggerEndpoint($"/swagger/{description.GroupName}/swagger.json", description.GroupName)));

            // ---------------------------------------------------------------------------------------------------------------------------------------------
            // begin SoulseekClient implementation
            // ---------------------------------------------------------------------------------------------------------------------------------------------

            // create options for the client
            var clientOptions = new SoulseekClientOptions(
                listenPort: ListenPort,
                concurrentDistributedChildrenLimit: 10,
                minimumDiagnosticLevel: DiagnosticLevel.Debug,
                concurrentPeerMessageConnectionLimit: 1000000,
                serverConnectionOptions: new ConnectionOptions(inactivityTimeout: 15),
                peerConnectionOptions: new ConnectionOptions(inactivityTimeout: 5),
                transferConnectionOptions: new ConnectionOptions(inactivityTimeout: 30),
                userInfoResponseResolver: UserInfoResponseResolver,
                browseResponseResolver: BrowseResponseResolver, 
                enqueueDownloadAction: (username, ipAddress, port, filename) => EnqueueDownloadAction(username, ipAddress, port, filename, tracker), 
                searchResponseResolver: SearchResponseResolver);

            Client = new SoulseekClient(options: clientOptions);

            // bind the DiagnosticGenerated event so we can trap and display diagnostic messages
            Client.DiagnosticGenerated += (e, args) =>
            {
                lock (ConsoleSyncRoot)
                {
                    if (args.Level == DiagnosticLevel.Debug) Console.ForegroundColor = ConsoleColor.DarkGray;
                    if (args.Level == DiagnosticLevel.Warning) Console.ForegroundColor = ConsoleColor.Yellow;

                    Console.WriteLine($"[DIAGNOSTIC:{e.GetType().Name}] [{args.Level}] {args.Message}");
                    Console.ResetColor();
                }
            };

            Client.TransferStateChanged += (e, args) => Console.WriteLine($"[{args.Transfer.Direction.ToString().ToUpper()}] [{args.Transfer.Username}/{Path.GetFileName(args.Transfer.Filename)}] {args.PreviousState} => {args.Transfer.State}");
            Client.UserStatusChanged += (e, args) => Console.WriteLine($"[USER] {args.Username}: {args.Status}");
            //Client.TransferProgressUpdated += (e, args) => Console.WriteLine($"[{args.Direction.ToString().ToUpper()}] [{args.Username}/{Path.GetFileName(args.Filename)}] {args.PercentComplete} {args.AverageSpeed}kb/s");

            async Task ConnectAndLogIn()
            {
                await Client.ConnectAsync();
                await Client.LoginAsync(Username, Password);
            }

            Client.Disconnected += async (e, args) =>
            {
                Console.WriteLine($"Disconnected from Soulseek server: {args.Message}");

                if (!(args.Exception is KickedFromServerException || args.Exception is ObjectDisposedException))
                {
                    Console.WriteLine($"Attepting to reconnect...");
                    await ConnectAndLogIn();
                }
            };

            Task.Run(async () => {
                await Client.ConnectAsync();
                await Client.LoginAsync(Username, Password);
            }).GetAwaiter().GetResult();

            Console.WriteLine($"Connected and logged in.");
        }

        private Task<UserInfo> UserInfoResponseResolver(string username, IPAddress ipAddress, int port) 
        {
            var info = new UserInfo(
                description: $"i'm a test! also, your username is {username}, IP address is {ipAddress}, and the port on which you connected to me is {port}",
                picture: System.IO.File.ReadAllBytes(@"etc/slsk_bird.jpg"),
                uploadSlots: 0,
                queueLength: 0,
                hasFreeUploadSlot: false);

            return Task.FromResult(info);
        }

        private Task<IEnumerable<Soulseek.Directory>> BrowseResponseResolver(string username, IPAddress ipAddress, int port)
        {
            // limited to just the root for now
            var files = System.IO.Directory.GetFiles(SharedDirectory)
                .Select(f => new Soulseek.File(1, Path.GetFileName(f), new FileInfo(f).Length, Path.GetExtension(f), 0));

            var dir = new Soulseek.Directory(SharedDirectory, files.Count(), files);

            IEnumerable<Soulseek.Directory> result = new List<Soulseek.Directory>() { dir };
            return Task.FromResult(result);
        }

        private Task EnqueueDownloadAction(string username, IPAddress ipAddress, int port, string filename, ITransferTracker tracker)
        {
            // accept all download requests.
            var cts = new CancellationTokenSource();
            var topts = new TransferOptions(stateChanged: (e) => tracker.AddOrUpdate(e, cts), progressUpdated: (e) => tracker.AddOrUpdate(e, cts));

            Task.Run(async () =>
            {
                using (var stream = new FileStream(filename, FileMode.Open))
                {
                    await Client.UploadAsync(username, filename, new FileInfo(filename).Length, stream, options: topts, cancellationToken: cts.Token);
                }
            }).ContinueWith(t => { throw t.Exception; }, TaskContinuationOptions.OnlyOnFaulted); // fire and forget

            return Task.CompletedTask;
        }

        private Task<SearchResponse> SearchResponseResolver(string username, int token, string query)
        {
            //Console.WriteLine($"Search request: {q}");

            if (query == "killing your enemy in 1995")
            {
                var files = System.IO.Directory.GetFiles(SharedDirectory)
                    .Select(f => new Soulseek.File(1, f, new FileInfo(f).Length, Path.GetExtension(f), 0));

                return Task.FromResult(new SearchResponse(Username, token, files.Count(), 0, 0, 0, files));
            }

            return Task.FromResult<SearchResponse>(null);
        }
    }

    class IPAddressConverter : JsonConverter
    {
        public override bool CanConvert(Type objectType)
        {
            return (objectType == typeof(IPAddress));
        }

        public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer)
        {
            writer.WriteValue(value.ToString());
        }

        public override object ReadJson(JsonReader reader, Type objectType, object existingValue, JsonSerializer serializer)
        {
            return IPAddress.Parse((string)reader.Value);
        }
    }
}
