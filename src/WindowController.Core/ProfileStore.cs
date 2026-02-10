using System.Text.Json;
using System.Text.Json.Serialization;
using WindowController.Core.Models;
using Serilog;

namespace WindowController.Core;

/// <summary>
/// Reads and writes profiles.json with AHK-compatible schema.
/// </summary>
public class ProfileStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNamingPolicy = null, // respect JsonPropertyName attributes
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    private readonly string _filePath;
    private readonly ILogger _log;

    public ProfilesRoot Data { get; private set; } = new();

    public ProfileStore(string filePath, ILogger logger)
    {
        _filePath = filePath;
        _log = logger;
    }

    public void Load()
    {
        var dir = Path.GetDirectoryName(_filePath);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            Directory.CreateDirectory(dir);

        if (!File.Exists(_filePath))
        {
            Data = CreateDefault();
            Save();
            return;
        }

        try
        {
            var json = File.ReadAllText(_filePath);
            var parsed = JsonSerializer.Deserialize<ProfilesRoot>(json, JsonOptions);
            Data = NormalizeData(parsed ?? new ProfilesRoot());
        }
        catch (Exception ex)
        {
            _log.Error(ex, "LoadConfig failed");
            try
            {
                var bak = $"{_filePath}.broken.{DateTime.Now:yyyyMMdd_HHmmss}";
                File.Move(_filePath, bak);
            }
            catch { /* best effort */ }
            Data = CreateDefault();
            Save();
        }
    }

    public void Save()
    {
        try
        {
            var dir = Path.GetDirectoryName(_filePath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            var json = JsonSerializer.Serialize(Data, JsonOptions);
            var tmp = _filePath + ".tmp";
            File.WriteAllText(tmp, json);
            File.Move(tmp, _filePath, overwrite: true);
        }
        catch (Exception ex)
        {
            _log.Error(ex, "SaveConfig failed");
            throw;
        }
    }

    public Profile? FindByName(string name)
        => Data.Profiles.FirstOrDefault(p => p.Name == name);

    public void SaveProfile(Profile profile)
    {
        var idx = Data.Profiles.FindIndex(p => p.Name == profile.Name);
        if (idx >= 0)
            Data.Profiles[idx] = profile;
        else
            Data.Profiles.Add(profile);
        Save();
    }

    public bool DeleteProfile(string name)
    {
        var removed = Data.Profiles.RemoveAll(p => p.Name == name) > 0;
        if (removed)
            Save();
        return removed;
    }

    private static ProfilesRoot CreateDefault() => new()
    {
        Version = 1,
        Settings = new Settings { SyncMinMax = 0, ShowGuiOnStartup = 1 },
        Profiles = new List<Profile>()
    };

    private static ProfilesRoot NormalizeData(ProfilesRoot root)
    {
        root.Version = root.Version == 0 ? 1 : root.Version;

        foreach (var p in root.Profiles)
        {
            foreach (var w in p.Windows)
            {
                // Normalize path backslashes
                w.Path = PathNormalizer.Normalize(w.Path);

                // If urlKey is missing, derive from url
                if (string.IsNullOrEmpty(w.Match.UrlKey) && !string.IsNullOrEmpty(w.Match.Url))
                    w.Match.UrlKey = UrlNormalizer.Normalize(w.Match.Url);
            }
        }

        return root;
    }
}
