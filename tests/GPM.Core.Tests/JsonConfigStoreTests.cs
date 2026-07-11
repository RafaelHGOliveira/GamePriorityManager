using GPM.Core.Models;
using GPM.Core.Persistence;

namespace GPM.Core.Tests;

public class JsonConfigStoreTests
{
    [Fact]
    public void save_load_roundtrips()
    {
        var dir = Directory.CreateTempSubdirectory("gpm-cfg-").FullName;
        var store = new JsonConfigStore(dir, fallbackDir: dir);
        var cfg = new GpmConfig { ShowIoColumn = true, Theme = "dark" };
        cfg.ManualGames.Add(new Game { Name = "X", Store = GameStore.Manual, ExePath = @"C:\x\x.exe" });
        cfg.ManagedExes.Add("x.exe");
        cfg.ExeChoices["Steam:1245620"] = @"C:\g\eldenring.exe";

        store.Save(cfg);
        var loaded = new JsonConfigStore(dir, dir).Load();

        Assert.True(loaded.ShowIoColumn);
        Assert.Equal("dark", loaded.Theme);
        Assert.Equal("X", Assert.Single(loaded.ManualGames).Name);
        Assert.Contains("x.exe", loaded.ManagedExes);
        Assert.Equal(@"C:\g\eldenring.exe", loaded.ExeChoices["Steam:1245620"]);
    }

    [Fact]
    public void load_returns_defaults_when_file_missing()
    {
        var dir = Directory.CreateTempSubdirectory("gpm-cfg-").FullName;
        var cfg = new JsonConfigStore(dir, dir).Load();
        Assert.Empty(cfg.ManualGames);
        Assert.Equal("auto", cfg.Theme);
    }

    [Fact]
    public void unwritable_preferred_dir_falls_back()
    {
        var missing = @"Q:\gpm-not-writable-" + Guid.NewGuid().ToString("N"); // drive doesn't exist
        var fallback = Directory.CreateTempSubdirectory("gpm-fallback-").FullName;
        var store = new JsonConfigStore(missing, fallback);

        Assert.StartsWith(fallback, store.FilePath);
        store.Save(new GpmConfig()); // must not throw
        Assert.True(File.Exists(store.FilePath));
    }
}
