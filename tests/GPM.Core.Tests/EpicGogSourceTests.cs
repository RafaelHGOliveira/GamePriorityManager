using GPM.Core.Detection;
using GPM.Core.Models;
using Microsoft.Win32;

namespace GPM.Core.Tests;

public class EpicGogSourceTests
{
    [Fact]
    public async Task epic_parses_item_manifests_from_directory()
    {
        var dir = Directory.CreateTempSubdirectory("gpm-epic-").FullName;
        File.Copy(Path.Combine(AppContext.BaseDirectory, "Fixtures", "epic", "RocketLeague.item"),
            Path.Combine(dir, "RocketLeague.item"));
        File.WriteAllText(Path.Combine(dir, "broken.item"), "{ not json");

        var games = await new EpicSource(dir).ScanAsync();

        var g = Assert.Single(games); // broken manifest skipped, not fatal
        Assert.Equal("Rocket League", g.Name);
        Assert.Equal(GameStore.Epic, g.Store);
        Assert.Equal("Sugar", g.StoreId);
        Assert.Equal(@"C:\Program Files\Epic Games\rocketleague\Binaries\Win64\RocketLeague.exe", g.ExePath);
        Assert.Equal("rocketleague.exe", g.ExeName);
    }

    [Fact]
    public async Task epic_returns_empty_when_manifest_dir_missing() =>
        Assert.Empty(await new EpicSource(@"C:\gpm-does-not-exist-" + Guid.NewGuid().ToString("N")).ScanAsync());

    [Fact]
    public async Task gog_reads_games_from_registry()
    {
        var reg = new FakeStoreRegistry();
        const string root = @"SOFTWARE\WOW6432Node\GOG.com\Games";
        reg.Set(RegistryHive.LocalMachine, $@"{root}\1207658924", "gameName", "Cyberpunk 2077");
        reg.Set(RegistryHive.LocalMachine, $@"{root}\1207658924", "path", @"D:\GOG\Cyberpunk 2077");
        reg.Set(RegistryHive.LocalMachine, $@"{root}\1207658924", "exe", @"D:\GOG\Cyberpunk 2077\bin\x64\Cyberpunk2077.exe");
        reg.Set(RegistryHive.LocalMachine, $@"{root}\999", "path", @"D:\GOG\Broken"); // no gameName -> skipped

        var games = await new GogSource(reg).ScanAsync();

        var g = Assert.Single(games);
        Assert.Equal("Cyberpunk 2077", g.Name);
        Assert.Equal(GameStore.Gog, g.Store);
        Assert.Equal("cyberpunk2077.exe", g.ExeName);
    }

    [Fact]
    public async Task gog_returns_empty_when_not_installed() =>
        Assert.Empty(await new GogSource(new FakeStoreRegistry()).ScanAsync());
}
