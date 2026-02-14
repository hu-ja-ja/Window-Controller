using System.IO;
using Serilog;
using WindowController.App;
using WindowController.Core;
using WindowController.Core.Models;
using WindowController.Win32;
using Xunit;

namespace WindowController.App.Tests;

public class ProfileApplierTests
{
    [Fact]
    public async Task ApplyByIdAsync_ProfileMissing_ReturnsFailure_AndDoesNotScheduleRebuild()
    {
        var log = Serilog.Core.Logger.None;
        var storePath = CreateTempProfilesJson(new ProfilesRoot { Profiles = new() });
        var store = new ProfileStore(storePath, log);
        store.Load();

        var scheduled = 0;
        var applier = CreateApplier(store, log, scheduleRebuild: () => scheduled++);

        var result = await applier.ApplyByIdAsync("missing", launchMissing: false);

        Assert.Equal(0, result.Applied);
        Assert.Equal(0, result.Total);
        Assert.False(result.Success);
        Assert.Contains("プロファイルが見つかりません", result.Failures);
        Assert.Equal(0, scheduled);
    }

    [Fact]
    public async Task ApplyByNameAsync_ProfileMissing_ReturnsFailure_AndDoesNotScheduleRebuild()
    {
        var log = Serilog.Core.Logger.None;
        var storePath = CreateTempProfilesJson(new ProfilesRoot { Profiles = new() });
        var store = new ProfileStore(storePath, log);
        store.Load();

        var scheduled = 0;
        var applier = CreateApplier(store, log, scheduleRebuild: () => scheduled++);

        var result = await applier.ApplyByNameAsync("missing", launchMissing: false);

        Assert.Equal(0, result.Applied);
        Assert.Equal(0, result.Total);
        Assert.False(result.Success);
        Assert.Contains("プロファイルが見つかりません", result.Failures);
        Assert.Equal(0, scheduled);
    }

    [Fact]
    public async Task ApplyByIdAsync_InvalidWindowHandle_ReportsFailure_AndSchedulesRebuild()
    {
        var log = Serilog.Core.Logger.None;

        var root = new ProfilesRoot
        {
            Settings = new Settings { SyncMinMax = 0, ShowGuiOnStartup = 1 },
            Profiles = new()
            {
                new Profile
                {
                    Id = "p1",
                    Name = "Profile1",
                    SyncMinMax = 0,
                    Windows = new()
                    {
                        new WindowEntry
                        {
                            Match = new MatchInfo
                            {
                                Exe = "notepad.exe",
                                Class = "",
                                Title = "Untitled - Notepad",
                                Url = "",
                                UrlKey = ""
                            },
                            Path = "",
                            Rect = new Rect { X = 0, Y = 0, W = 100, H = 100 },
                            MinMax = 0
                        }
                    }
                }
            }
        };

        var storePath = CreateTempProfilesJson(root);
        var store = new ProfileStore(storePath, log);
        store.Load();

        var scheduled = 0;
        var applier = CreateApplier(
            store,
            log,
            scheduleRebuild: () => scheduled++,
            candidatesProvider: () => new List<WindowCandidate>());

        var result = await applier.ApplyByIdAsync("p1", launchMissing: false);

        Assert.Equal(0, result.Applied);
        Assert.Equal(1, result.Total);
        Assert.False(result.Success);
        Assert.Single(result.Failures);
        Assert.Contains("見つかりません", result.Failures[0]);
        Assert.Equal(1, scheduled);
    }

    private static ProfileApplier CreateApplier(
        ProfileStore store,
        ILogger log,
        Action scheduleRebuild,
        Func<List<WindowCandidate>>? candidatesProvider = null)
    {
        var enumerator = new WindowEnumerator(log);
        var arranger = new WindowArranger(log);
        return new ProfileApplier(
            store,
            enumerator,
            arranger,
            scheduleRebuild,
            log,
            candidatesProvider);
    }

    private static string CreateTempProfilesJson(ProfilesRoot root)
    {
        var dir = Path.Combine(Path.GetTempPath(), "WindowController.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, "profiles.json");

        var json = System.Text.Json.JsonSerializer.Serialize(root, new System.Text.Json.JsonSerializerOptions
        {
            WriteIndented = true,
            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
            PropertyNamingPolicy = null
        });

        File.WriteAllText(path, json);
        return path;
    }
}
