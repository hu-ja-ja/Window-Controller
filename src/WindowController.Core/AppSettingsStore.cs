using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Unicode;
using Serilog;
using WindowController.Core.Models;

namespace WindowController.Core;

/// <summary>
/// Reads and writes appsettings.json (stored next to the exe under config/).
/// Contains only the profiles.json path and other app-level prefs
/// that must be resolved before profiles.json is loaded.
/// </summary>
public class AppSettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Encoder = JavaScriptEncoder.Create(UnicodeRanges.All)
    };

    private readonly string _filePath;
    private readonly ILogger _log;

    public AppSettings Data { get; private set; } = new();

    /// <summary>
    /// The default profiles.json path when no custom path is configured.
    /// </summary>
    public string DefaultProfilesPath { get; }

    public AppSettingsStore(string filePath, string defaultProfilesPath, ILogger logger)
    {
        _filePath = filePath;
        DefaultProfilesPath = defaultProfilesPath;
        _log = logger;
    }

    /// <summary>
    /// Returns the effective profiles.json path (custom or default).
    /// </summary>
    public string EffectiveProfilesPath
        => string.IsNullOrWhiteSpace(Data.ProfilesPath) ? DefaultProfilesPath : Data.ProfilesPath;

    public void Load()
    {
        var dir = Path.GetDirectoryName(_filePath);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            Directory.CreateDirectory(dir);

        if (!File.Exists(_filePath))
        {
            Data = new AppSettings();
            Save();
            return;
        }

        try
        {
            var json = File.ReadAllText(_filePath);
            Data = JsonSerializer.Deserialize<AppSettings>(json, JsonOptions) ?? new AppSettings();
        }
        catch (Exception ex)
        {
            _log.Warning(ex, "Failed to load appsettings.json, using defaults");
            Data = new AppSettings();
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
            _log.Error(ex, "Failed to save appsettings.json");
        }
    }

    /// <summary>
    /// Update the profiles path. Pass empty/null to revert to default.
    /// </summary>
    public void SetProfilesPath(string? path)
    {
        Data.ProfilesPath = path?.Trim() ?? "";
        Save();
    }
}
