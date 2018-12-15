#load "build/Settings.cake"
#tool "nuget:?package=ReportGenerator&version=4.0.4"

///////////////////////////////////////////////////////////////////////////////
// ARGUMENTS
///////////////////////////////////////////////////////////////////////////////

var target = Argument("target", "Build");


///////////////////////////////////////////////////////////////////////////////
// SETUP / TEARDOWN
///////////////////////////////////////////////////////////////////////////////

Setup<Settings>(ctx =>
{
    var settings = new Settings(ctx, MakeAbsolute(Directory("./out")));;
    Information("Cleaning up and ensuring directories exist...");
    if(DirectoryExists(settings.BaseOutputDirectory))
        DeleteDirectory(
            settings.BaseOutputDirectory, 
            new DeleteDirectorySettings()
            {
                Recursive = true
            }
        );
    foreach(var directory in settings.AllDirectories())
        EnsureDirectoryExists(directory);
    return settings;
});

///////////////////////////////////////////////////////////////////////////////
// TASKS
///////////////////////////////////////////////////////////////////////////////

// Until Coverlet supports merging as a single separate task instead of attaching it
// to the overarching coverage task 
FilePath prevProject = null;
Task("TestWithCoverage")
.Does<Settings>(
    settings => 
    {
        foreach(var currentProject in GetFiles("./tests/**/*.csproj"))
        {
            var dotnetTestSettings = new DotNetCoreTestSettings()
            {
                Logger = "trx",
                ResultsDirectory = settings.TestResultsDirectory,
                Configuration = "Release",
                ArgumentCustomization = args =>
                {
                    args.Append("/p:EnableCoverage=true");
					args.Append("/p:CoverletOutputFormat=opencover");
                    if(prevProject != null)
                        args.Append($"/p:MergeWith=`{prevProject.GetDirectory().CombineWithFilePath("bin/cov/coverage.opencover.xml")}`");
                    return args;
                }
            };
            DotNetCoreTest(currentProject.FullPath, dotnetTestSettings);
            prevProject = currentProject;
        }
        Information($"Publishing test coverage results to '{settings.CoverageResultsDirectory}'...");
        CopyFiles(prevProject.GetDirectory().FullPath + "/bin/cov/**/*",settings.CoverageResultsDirectory);
    }
);

Task("GenerateCoverageReport")
.IsDependentOn("TestWithCoverage")
.Does<Settings>(
    settings =>
    {
        ReportGenerator(
            new [] { settings.CoverageResultsDirectory.CombineWithFilePath("coverage.opencover.xml")},
            settings.CoverageReportDirectory.FullPath,
            new ReportGeneratorSettings() {
                ReportTypes = new [] { ReportGeneratorReportType.HtmlSummary, ReportGeneratorReportType.Html }
            }
        );
    }
);

Task("Pack")
.Does<Settings>(
    settings =>
    {
        DotNetCorePack(
            "./src/Soulseek.NET/Soulseek.NET.csproj",
            new DotNetCorePackSettings()
            {
                OutputDirectory = settings.PackageOutputDirectory,
                Configuration = "Release"
            }
        );
    }
);

///////////////////////////////////////////////////////////////////////////////
// Task Groups
// These are intended to be the major entrypoints of the build script.
///////////////////////////////////////////////////////////////////////////////

Task("Build")
.IsDependentOn("TestWithCoverage")
.IsDependentOn("Pack");

Task("BuildWithCoverageReport")
.IsDependentOn("TestWithCoverage")
.IsDependentOn("GenerateCoverageReport")
.IsDependentOn("Pack");

RunTarget(target);