using GPM.Core.Detection.Steam;
using GPM.Core.Models;
using Microsoft.Win32;

namespace GPM.Core.Tests;

public class SteamSourceTests
{
    private static string MakeSteamTree(out string libDir)
    {
        var root = Directory.CreateTempSubdirectory("gpm-steam-").FullName;
        libDir = Directory.CreateTempSubdirectory("gpm-steamlib-").FullName;
        Directory.CreateDirectory(Path.Combine(root, "steamapps"));
        Directory.CreateDirectory(Path.Combine(libDir, "steamapps"));

        File.WriteAllText(Path.Combine(root, "steamapps", "libraryfolders.vdf"), $$"""
            "libraryfolders"
            {
                "0" { "path" "{{root.Replace(@"\", @"\\")}}" }
                "1" { "path" "{{libDir.Replace(@"\", @"\\")}}" }
            }
            """);
        File.WriteAllText(Path.Combine(root, "steamapps", "appmanifest_1245620.acf"),
            "\"AppState\"\n{\n\t\"appid\"\t\"1245620\"\n\t\"name\"\t\"ELDEN RING\"\n\t\"installdir\"\t\"ELDEN RING\"\n}");
        File.WriteAllText(Path.Combine(libDir, "steamapps", "appmanifest_730.acf"),
            "\"AppState\"\n{\n\t\"appid\"\t\"730\"\n\t\"name\"\t\"Counter-Strike 2\"\n\t\"installdir\"\t\"Counter-Strike Global Offensive\"\n}");
        return root;
    }

    [Fact]
    public void scanroot_finds_games_across_libraries()
    {
        var root = MakeSteamTree(out var lib);
        var games = SteamSource.ScanRoot(root);

        Assert.Equal(2, games.Count);
        var er = games.Single(g => g.StoreId == "1245620");
        Assert.Equal("ELDEN RING", er.Name);
        Assert.Equal(GameStore.Steam, er.Store);
        Assert.Equal(Path.Combine(root, "steamapps", "common", "ELDEN RING"), er.InstallDir);
        Assert.Null(er.ExePath); // Steam never names the exe -> plan C resolves it
        Assert.Equal(Path.Combine(lib, "steamapps", "common", "Counter-Strike Global Offensive"),
            games.Single(g => g.StoreId == "730").InstallDir);
    }

    [Fact]
    public void scanroot_returns_empty_when_vdf_missing() =>
        Assert.Empty(SteamSource.ScanRoot(Directory.CreateTempSubdirectory("gpm-empty-").FullName));

    [Fact]
    public async Task scanasync_uses_hklm_then_hkcu_fallback()
    {
        var root = MakeSteamTree(out _);
        var reg = new FakeStoreRegistry();
        reg.Set(RegistryHive.CurrentUser, @"Software\Valve\Steam", "SteamPath", root);

        var games = await new SteamSource(reg).ScanAsync(); // HKLM absent -> HKCU
        Assert.Equal(2, games.Count);
    }

    [Fact]
    public async Task scanasync_returns_empty_when_steam_not_installed() =>
        Assert.Empty(await new SteamSource(new FakeStoreRegistry()).ScanAsync());
}
