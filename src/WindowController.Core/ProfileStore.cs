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

    private string _filePath;
    private readonly ILogger _log;

    public ProfilesRoot Data { get; private set; } = new();

    /// <summary>
    /// Current profiles.json path.
    /// </summary>
    public string FilePath => _filePath;

    public ProfileStore(string filePath, ILogger logger)
    {
        _filePath = filePath;
        _log = logger;
    }

    /// <summary>
    /// Change the profiles.json path and reload data from the new location.
    /// If the new file does not exist, creates a default one there.
    /// </summary>
    public void ChangePath(string newPath)
    {
        _filePath = newPath;
        Load();
        _log.Information("ProfileStore path changed to {Path}", newPath);
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
            
            // Detect migration need before normalization
            bool needsMigration = parsed != null && parsed.Profiles.Any(p => 
                string.IsNullOrEmpty(p.Id) || 
                parsed.Profiles.Count(x => x.Id == p.Id) > 1);
            
            var normalized = NormalizeData(parsed ?? new ProfilesRoot());
            Data = normalized;

            if (needsMigration)
                Save();
        }
        catch (Exception ex)
        {
            _log.Error(ex, "LoadConfig failed");
            try
            {
                var bak = $"{_filePath}.broken.{DateTime.Now:yyyyMMdd_HHmmss}";
                File.Move(_filePath, bak);
            }
            catch (Exception ex2) { _log.Warning(ex2, "Failed to rename broken config file"); }
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

    public Profile? FindById(string id)
        => Data.Profiles.FirstOrDefault(p => p.Id == id);

    public void SaveProfile(Profile profile)
    {
        // Prefer Id-based lookup when the profile has a valid Id
        var idx = !string.IsNullOrEmpty(profile.Id)
            ? Data.Profiles.FindIndex(p => p.Id == profile.Id)
            : Data.Profiles.FindIndex(p => p.Name == profile.Name);
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

    public bool DeleteProfileById(string id)
    {
        var removed = Data.Profiles.RemoveAll(p => p.Id == id) > 0;
        if (removed)
            Save();
        return removed;
    }

    /// <summary>
    /// Rename a profile identified by <paramref name="profileId"/>.
    /// If <paramref name="desiredName"/> conflicts with another profile,
    /// a numeric suffix like "(2)" is appended automatically.
    /// Returns the final (possibly adjusted) name, or null when the profile was not found.
    /// </summary>
    public string? RenameProfile(string profileId, string desiredName)
    {
        var profile = FindById(profileId);
        if (profile == null) return null;

        var trimmed = desiredName.Trim();
        var finalName = ResolveUniqueName(trimmed, profileId);
        
        // Skip save if the name hasn't actually changed
        if (finalName == profile.Name)
            return finalName;
        
        profile.Name = finalName;
        profile.UpdatedAt = DateTime.Now.ToString("yyyy-MM-dd'T'HH:mm:ss");
        Save();
        return finalName;
    }

    /// <summary>
    /// Returns a name that does not collide with existing profiles (excluding the one with <paramref name="excludeId"/>).
    /// </summary>
    internal string ResolveUniqueName(string desiredName, string? excludeId = null)
    {
        var candidate = desiredName;
        int suffix = 2;
        while (Data.Profiles.Any(p => p.Name == candidate && p.Id != excludeId))
        {
            candidate = $"{desiredName} ({suffix})";
            suffix++;
        }
        return candidate;
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

        // Assign stable Ids to profiles that lack one (migration from older schema)
        var seenIds = new HashSet<string>();
        foreach (var p in root.Profiles)
        {
            if (string.IsNullOrEmpty(p.Id) || !seenIds.Add(p.Id))
            {
                // Generate a new unique Id (duplicate Ids are also re-assigned)
                p.Id = Guid.NewGuid().ToString("D");
                seenIds.Add(p.Id);
            }
        }

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
