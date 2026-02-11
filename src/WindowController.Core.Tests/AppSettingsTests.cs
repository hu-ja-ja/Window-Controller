using System.Text.Json;
using WindowController.Core.Models;

namespace WindowController.Core.Tests;

public class AppSettingsTests
{
    [Fact]
    public void AppSettings_DefaultValues_AreCorrect()
    {
        var settings = new AppSettings();

        Assert.Equal("", settings.ProfilesPath);
        Assert.NotNull(settings.Hotkeys);
    }

    [Fact]
    public void HotkeySettings_DefaultShowGui_IsCtrlAltW()
    {
        var hotkeySettings = new HotkeySettings();

        Assert.Equal("W", hotkeySettings.ShowGui.Key);
        Assert.True(hotkeySettings.ShowGui.Ctrl);
        Assert.True(hotkeySettings.ShowGui.Alt);
        Assert.False(hotkeySettings.ShowGui.Shift);
        Assert.False(hotkeySettings.ShowGui.Win);
    }

    [Fact]
    public void HotkeyBinding_IsEmpty_WhenNoKeySet()
    {
        var binding = new HotkeyBinding();
        Assert.True(binding.IsEmpty);

        var bindingWithKey = new HotkeyBinding { Key = "A" };
        Assert.False(bindingWithKey.IsEmpty);
    }

    [Fact]
    public void HotkeyBinding_ToString_FormatsCorrectly()
    {
        var binding = new HotkeyBinding();
        Assert.Equal("なし", binding.ToString());

        binding = new HotkeyBinding { Key = "W", Ctrl = true, Alt = true };
        Assert.Equal("Ctrl+Alt+W", binding.ToString());

        binding = new HotkeyBinding { Key = "F1", Shift = true, Win = true };
        Assert.Equal("Shift+Win+F1", binding.ToString());
    }

    [Fact]
    public void HotkeyBinding_Clone_CreatesIndependentCopy()
    {
        var original = new HotkeyBinding { Key = "W", Ctrl = true, Alt = true };
        var clone = original.Clone();

        Assert.Equal(original.Key, clone.Key);
        Assert.Equal(original.Ctrl, clone.Ctrl);
        Assert.Equal(original.Alt, clone.Alt);

        // Modify clone and verify original unchanged
        clone.Key = "X";
        Assert.Equal("W", original.Key);
    }

    [Fact]
    public void HotkeyBinding_Equals_ComparesCorrectly()
    {
        var a = new HotkeyBinding { Key = "W", Ctrl = true, Alt = true };
        var b = new HotkeyBinding { Key = "W", Ctrl = true, Alt = true };
        var c = new HotkeyBinding { Key = "X", Ctrl = true, Alt = true };
        var d = new HotkeyBinding { Key = "W", Ctrl = true, Shift = true };

        Assert.True(a.Equals(b));
        Assert.False(a.Equals(c));
        Assert.False(a.Equals(d));
        Assert.False(a.Equals(null));
    }

    [Fact]
    public void AppSettings_DeserializesWithMissingHotkeys_UsesDefaults()
    {
        // Simulate old appsettings.json without hotkeys field
        var json = """{ "profilesPath": "/some/path" }""";
        var settings = JsonSerializer.Deserialize<AppSettings>(json);

        Assert.NotNull(settings);
        Assert.Equal("/some/path", settings!.ProfilesPath);
        Assert.NotNull(settings.Hotkeys);
        // Default hotkey should be applied
        Assert.Equal("W", settings.Hotkeys.ShowGui.Key);
    }

    [Fact]
    public void AppSettings_DeserializesWithEmptyHotkeys_Succeeds()
    {
        var json = """{ "profilesPath": "", "hotkeys": {} }""";
        var settings = JsonSerializer.Deserialize<AppSettings>(json);

        Assert.NotNull(settings);
        Assert.NotNull(settings!.Hotkeys);
    }

    [Fact]
    public void AppSettings_SerializesAndDeserializesCorrectly()
    {
        var original = new AppSettings
        {
            ProfilesPath = "/custom/path/profiles.json",
            Hotkeys = new HotkeySettings
            {
                ShowGui = new HotkeyBinding { Key = "G", Ctrl = true, Shift = true },
                Profiles = new Dictionary<string, HotkeyBinding>
                {
                    ["profile-1"] = new HotkeyBinding { Key = "1", Ctrl = true, Alt = true },
                    ["profile-2"] = new HotkeyBinding { Key = "2", Ctrl = true, Alt = true }
                }
            }
        };

        var json = JsonSerializer.Serialize(original);
        var deserialized = JsonSerializer.Deserialize<AppSettings>(json);

        Assert.NotNull(deserialized);
        Assert.Equal(original.ProfilesPath, deserialized!.ProfilesPath);
        Assert.Equal("G", deserialized.Hotkeys.ShowGui.Key);
        Assert.True(deserialized.Hotkeys.ShowGui.Ctrl);
        Assert.True(deserialized.Hotkeys.ShowGui.Shift);
        Assert.Equal(2, deserialized.Hotkeys.Profiles.Count);
        Assert.True(deserialized.Hotkeys.Profiles.ContainsKey("profile-1"));
        Assert.Equal("1", deserialized.Hotkeys.Profiles["profile-1"].Key);
    }

    [Fact]
    public void HotkeySettings_ProfilesMap_DefaultsToEmpty()
    {
        var hotkeySettings = new HotkeySettings();

        Assert.NotNull(hotkeySettings.Profiles);
        Assert.Empty(hotkeySettings.Profiles);
    }
}
