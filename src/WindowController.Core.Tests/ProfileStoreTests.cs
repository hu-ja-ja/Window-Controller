using WindowController.Core;
using WindowController.Core.Models;
using Serilog;

namespace WindowController.Core.Tests;

public class ProfileStoreTests : IDisposable
{
    private readonly string _tempDir;
    private readonly ILogger _log = new LoggerConfiguration().CreateLogger();

    public ProfileStoreTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "WC_Tests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, true); } catch { }
    }

    private string TempFile() => Path.Combine(_tempDir, "profiles.json");

    private ProfileStore CreateStore(string? json = null)
    {
        var path = TempFile();
        if (json != null)
            File.WriteAllText(path, json);
        var store = new ProfileStore(path, _log);
        store.Load();
        return store;
    }

    // ── ID migration ──

    [Fact]
    public void Load_AssignsId_WhenMissing()
    {
        var json = """
        {
          "version": 1,
          "settings": { "syncMinMax": 0, "showGuiOnStartup": 1 },
          "profiles": [
            { "name": "A", "syncMinMax": 0, "createdAt": "", "updatedAt": "", "windows": [] },
            { "name": "B", "syncMinMax": 0, "createdAt": "", "updatedAt": "", "windows": [] }
          ]
        }
        """;
        var store = CreateStore(json);

        Assert.All(store.Data.Profiles, p => Assert.False(string.IsNullOrEmpty(p.Id)));
        // Each Id should be unique
        var ids = store.Data.Profiles.Select(p => p.Id).ToList();
        Assert.Equal(ids.Count, ids.Distinct().Count());
    }

    [Fact]
    public void Load_ReassignsDuplicateIds()
    {
        var json = """
        {
          "version": 1,
          "settings": { "syncMinMax": 0, "showGuiOnStartup": 1 },
          "profiles": [
            { "id": "same-id", "name": "A", "syncMinMax": 0, "createdAt": "", "updatedAt": "", "windows": [] },
            { "id": "same-id", "name": "B", "syncMinMax": 0, "createdAt": "", "updatedAt": "", "windows": [] }
          ]
        }
        """;
        var store = CreateStore(json);

        var ids = store.Data.Profiles.Select(p => p.Id).ToList();
        Assert.Equal(2, ids.Distinct().Count());
    }

    [Fact]
    public void Load_PreservesExistingValidIds()
    {
        var existingId = Guid.NewGuid().ToString("D");
        var json = $$"""
        {
          "version": 1,
          "settings": { "syncMinMax": 0, "showGuiOnStartup": 1 },
          "profiles": [
            { "id": "{{existingId}}", "name": "A", "syncMinMax": 0, "createdAt": "", "updatedAt": "", "windows": [] }
          ]
        }
        """;
        var store = CreateStore(json);

        Assert.Equal(existingId, store.Data.Profiles[0].Id);
    }

    // ── FindById ──

    [Fact]
    public void FindById_ReturnsProfile()
    {
        var store = CreateStore();
        var profile = new Profile { Id = "test-id", Name = "Test", CreatedAt = "", UpdatedAt = "" };
        store.SaveProfile(profile);

        var found = store.FindById("test-id");
        Assert.NotNull(found);
        Assert.Equal("Test", found.Name);
    }

    [Fact]
    public void FindById_ReturnsNull_WhenNotFound()
    {
        var store = CreateStore();
        Assert.Null(store.FindById("nonexistent"));
    }

    // ── SaveProfile (Id-based) ──

    [Fact]
    public void SaveProfile_UpdatesById_WhenIdMatches()
    {
        var store = CreateStore();
        var profile = new Profile { Id = "id-1", Name = "Original", CreatedAt = "", UpdatedAt = "" };
        store.SaveProfile(profile);

        var updated = new Profile { Id = "id-1", Name = "Renamed", CreatedAt = "", UpdatedAt = "" };
        store.SaveProfile(updated);

        Assert.Single(store.Data.Profiles);
        Assert.Equal("Renamed", store.Data.Profiles[0].Name);
    }

    // ── DeleteProfileById ──

    [Fact]
    public void DeleteProfileById_RemovesCorrectProfile()
    {
        var store = CreateStore();
        store.SaveProfile(new Profile { Id = "id-1", Name = "A", CreatedAt = "", UpdatedAt = "" });
        store.SaveProfile(new Profile { Id = "id-2", Name = "B", CreatedAt = "", UpdatedAt = "" });

        Assert.True(store.DeleteProfileById("id-1"));
        Assert.Single(store.Data.Profiles);
        Assert.Equal("B", store.Data.Profiles[0].Name);
    }

    [Fact]
    public void DeleteProfileById_ReturnsFalse_WhenNotFound()
    {
        var store = CreateStore();
        Assert.False(store.DeleteProfileById("nonexistent"));
    }

    // ── RenameProfile ──

    [Fact]
    public void RenameProfile_ChangesName()
    {
        var store = CreateStore();
        store.SaveProfile(new Profile { Id = "id-1", Name = "Old", CreatedAt = "", UpdatedAt = "" });

        var result = store.RenameProfile("id-1", "New");

        Assert.Equal("New", result);
        Assert.Equal("New", store.FindById("id-1")!.Name);
    }

    [Fact]
    public void RenameProfile_ReturnsNull_WhenNotFound()
    {
        var store = CreateStore();
        Assert.Null(store.RenameProfile("nonexistent", "Name"));
    }

    [Fact]
    public void RenameProfile_AutoSuffix_WhenNameConflicts()
    {
        var store = CreateStore();
        store.SaveProfile(new Profile { Id = "id-1", Name = "Layout", CreatedAt = "", UpdatedAt = "" });
        store.SaveProfile(new Profile { Id = "id-2", Name = "Other", CreatedAt = "", UpdatedAt = "" });

        var result = store.RenameProfile("id-2", "Layout");

        Assert.Equal("Layout (2)", result);
        Assert.Equal("Layout (2)", store.FindById("id-2")!.Name);
        // Original is untouched
        Assert.Equal("Layout", store.FindById("id-1")!.Name);
    }

    [Fact]
    public void RenameProfile_AutoSuffix_IncrementsWhenMultipleConflicts()
    {
        var store = CreateStore();
        store.SaveProfile(new Profile { Id = "id-1", Name = "Layout", CreatedAt = "", UpdatedAt = "" });
        store.SaveProfile(new Profile { Id = "id-2", Name = "Layout (2)", CreatedAt = "", UpdatedAt = "" });
        store.SaveProfile(new Profile { Id = "id-3", Name = "Other", CreatedAt = "", UpdatedAt = "" });

        var result = store.RenameProfile("id-3", "Layout");

        Assert.Equal("Layout (3)", result);
    }

    [Fact]
    public void RenameProfile_NoSuffix_WhenSameProfileKeepsSameName()
    {
        var store = CreateStore();
        store.SaveProfile(new Profile { Id = "id-1", Name = "Layout", CreatedAt = "", UpdatedAt = "" });

        // Renaming to the same name should not add suffix
        var result = store.RenameProfile("id-1", "Layout");

        Assert.Equal("Layout", result);
    }

    [Fact]
    public void RenameProfile_UpdatesTimestamp()
    {
        var store = CreateStore();
        store.SaveProfile(new Profile { Id = "id-1", Name = "Old", CreatedAt = "2020-01-01T00:00:00", UpdatedAt = "2020-01-01T00:00:00" });

        store.RenameProfile("id-1", "New");

        var profile = store.FindById("id-1")!;
        Assert.NotEqual("2020-01-01T00:00:00", profile.UpdatedAt);
    }

    // ── ResolveUniqueName ──

    [Fact]
    public void ResolveUniqueName_ReturnsOriginal_WhenNoConflict()
    {
        var store = CreateStore();
        Assert.Equal("Unique", store.ResolveUniqueName("Unique"));
    }

    [Fact]
    public void ResolveUniqueName_AddsSuffix_WhenConflicts()
    {
        var store = CreateStore();
        store.SaveProfile(new Profile { Id = "id-1", Name = "Name", CreatedAt = "", UpdatedAt = "" });

        Assert.Equal("Name (2)", store.ResolveUniqueName("Name"));
    }

    [Fact]
    public void ResolveUniqueName_ExcludesOwnId()
    {
        var store = CreateStore();
        store.SaveProfile(new Profile { Id = "id-1", Name = "Name", CreatedAt = "", UpdatedAt = "" });

        Assert.Equal("Name", store.ResolveUniqueName("Name", "id-1"));
    }
}
