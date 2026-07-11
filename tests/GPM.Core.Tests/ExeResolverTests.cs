using GPM.Core.Models;
using GPM.Core.Resolve;

namespace GPM.Core.Tests;

public class ExeResolverTests
{
    private static readonly KnownGames Known =
        KnownGames.FromJson("""{"1245620":"Game\\eldenring.exe"}""");

    private static string MakeDir(params (string RelPath, int Size)[] files)
    {
        var dir = Directory.CreateTempSubdirectory("gpm-resolve-").FullName;
        foreach (var (rel, size) in files)
        {
            var full = Path.Combine(dir, rel);
            Directory.CreateDirectory(Path.GetDirectoryName(full)!);
            File.WriteAllBytes(full, new byte[size]);
        }
        return dir;
    }

    [Fact]
    public void game_with_exepath_already_set_passes_through()
    {
        var r = new ExeResolver(Known).Resolve(new Game
        { Name = "RL", Store = GameStore.Epic, ExePath = @"C:\e\RocketLeague.exe" });
        Assert.Equal(@"C:\e\RocketLeague.exe", r.ExePath);
        Assert.False(r.NeedsConfirmation);
    }

    [Fact]
    public void curated_hit_wins_over_heuristic_when_file_exists()
    {
        var dir = MakeDir((@"Game\eldenring.exe", 10), (@"start_protected_game.exe", 999));
        var r = new ExeResolver(Known).Resolve(new Game
        { Name = "ELDEN RING", Store = GameStore.Steam, StoreId = "1245620", InstallDir = dir });
        Assert.Equal(Path.Combine(dir, @"Game\eldenring.exe"), r.ExePath);
        Assert.False(r.NeedsConfirmation);
    }

    [Fact]
    public void curated_miss_on_disk_falls_through_to_heuristic()
    {
        var dir = MakeDir((@"OnlyGame.exe", 10)); // curated path doesn't exist here
        var r = new ExeResolver(Known).Resolve(new Game
        { Name = "ELDEN RING", Store = GameStore.Steam, StoreId = "1245620", InstallDir = dir });
        Assert.Equal(Path.Combine(dir, "OnlyGame.exe"), r.ExePath);
    }

    [Fact]
    public void non_steam_game_without_exe_uses_heuristic()
    {
        var dir = MakeDir((@"bf.exe", 100), (@"EAAntiCheat.Installer.exe", 999)); // "anticheat" not blacklisted term; use setup instead
        var r = new ExeResolver(Known).Resolve(new Game
        { Name = "BF", Store = GameStore.Ea, InstallDir = dir });
        Assert.NotNull(r.ExePath);
    }

    [Fact]
    public void missing_installdir_needs_human()
    {
        var r = new ExeResolver(Known).Resolve(new Game { Name = "X", Store = GameStore.Steam });
        Assert.Null(r.ExePath);
        Assert.True(r.NeedsConfirmation);
    }

    [Fact]
    public void nonexistent_installdir_needs_human()
    {
        var r = new ExeResolver(Known).Resolve(new Game
        { Name = "X", Store = GameStore.Steam, InstallDir = @"C:\gpm-gone-" + Guid.NewGuid().ToString("N") });
        Assert.Null(r.ExePath);
        Assert.True(r.NeedsConfirmation);
    }
}
