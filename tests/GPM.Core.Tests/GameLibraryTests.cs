using GPM.Core.Library;
using GPM.Core.Models;
using GPM.Core.Resolve;

namespace GPM.Core.Tests;

public class GameLibraryTests
{
    /// Resolves every game to <see cref="Exe"/> and counts how often it was asked.
    private sealed class CountingResolver : IExeResolver
    {
        public string? Exe { get; set; }
        public int Calls { get; private set; }

        public ExeResolution Resolve(Game game)
        {
            Calls++;
            return new(Exe, Exe is null, []);
        }
    }

    private sealed class PassthroughResolver : IExeResolver
    {
        public ExeResolution Resolve(Game game) => new(game.ExePath, game.ExePath is null, []);
    }

    private sealed class AmbiguousResolver(string exe) : IExeResolver
    {
        public ExeResolution Resolve(Game game) => new(exe, true, [exe, "other.exe"]);
    }

    private static string TempExe(string name)
    {
        var path = Path.Combine(Directory.CreateTempSubdirectory("gpm-library-").FullName, name);
        File.WriteAllBytes(path, [0]);
        return path;
    }

    private static readonly Game Unresolved = new() { Name = "CS2", Store = GameStore.Steam, StoreId = "730", InstallDir = @"C:\s" };

    private static GameLibrary Make(MemoryConfigStore cfg, IExeResolver resolver, params Game[] detected) =>
        new(() => Task.FromResult<IReadOnlyList<Game>>(detected), resolver, cfg);

    [Fact]
    public async Task first_load_scans_and_caches_second_load_uses_cache()
    {
        var cfg = new MemoryConfigStore();
        var scans = 0;
        var library = new GameLibrary(
            () => { scans++; return Task.FromResult<IReadOnlyList<Game>>([new() { Name = "cs2", Store = GameStore.Steam, ExePath = @"C:\g\cs2.exe" }]); },
            new PassthroughResolver(), cfg);

        await library.LoadAsync();
        Assert.Equal(1, scans);
        Assert.Single(cfg.Cfg.DetectedGames);

        Assert.Single((await library.LoadAsync())!); // cache hit
        Assert.Equal(1, scans);

        await library.LoadAsync(rescan: true);
        Assert.Equal(2, scans);
    }

    [Fact]
    public async Task scanning_callback_runs_only_when_the_stores_are_scanned()
    {
        var cfg = new MemoryConfigStore();
        var library = Make(cfg, new PassthroughResolver(), new Game { Name = "cs2", Store = GameStore.Steam, ExePath = @"C:\g\cs2.exe" });
        var scanning = 0;

        await library.LoadAsync(scanning: () => scanning++);
        await library.LoadAsync(scanning: () => scanning++); // cache hit

        Assert.Equal(1, scanning);
    }

    [Fact]
    public async Task resolved_exe_is_cached_so_reload_skips_the_resolver()
    {
        var cfg = new MemoryConfigStore();
        var resolver = new CountingResolver { Exe = TempExe("cs2.exe") };
        var library = Make(cfg, resolver, Unresolved);

        await library.LoadAsync();
        Assert.Equal(1, resolver.Calls);
        Assert.Equal(resolver.Exe, Assert.Single(cfg.Cfg.DetectedGames).ExePath);

        var games = await library.LoadAsync();
        Assert.Equal(1, resolver.Calls); // no install-dir walk on a cache hit
        Assert.Equal("cs2.exe", Assert.Single(games!).ExeName);

        await library.LoadAsync(rescan: true); // rescan re-resolves
        Assert.Equal(2, resolver.Calls);
    }

    [Fact]
    public async Task cached_exe_that_vanished_is_resolved_again()
    {
        var cfg = new MemoryConfigStore();
        var resolver = new CountingResolver { Exe = TempExe("old.exe") };
        var library = Make(cfg, resolver, Unresolved);
        await library.LoadAsync();

        File.Delete(resolver.Exe);
        resolver.Exe = TempExe("new.exe");
        var games = await library.LoadAsync();

        Assert.Equal(2, resolver.Calls);
        Assert.Equal("new.exe", Assert.Single(games!).ExeName);
        Assert.Equal(resolver.Exe, Assert.Single(cfg.Cfg.DetectedGames).ExePath);
    }

    [Fact]
    public async Task ambiguous_resolution_still_asks_for_confirmation_after_a_cache_hit()
    {
        var cfg = new MemoryConfigStore();
        var exe = TempExe("big.exe");
        var library = Make(cfg, new AmbiguousResolver(exe), Unresolved);

        await library.LoadAsync();
        var game = Assert.Single((await library.LoadAsync())!); // from cache

        Assert.True(game.NeedsExeConfirmation);
        Assert.Equal([exe, "other.exe"], game.ExeCandidates);
    }

    [Fact]
    public async Task steam_tools_in_an_old_cache_are_dropped_and_the_cache_healed()
    {
        var cfg = new MemoryConfigStore();
        cfg.Cfg.DetectedGames =
        [
            new() { Name = "Steamworks Common Redistributables", Store = GameStore.Steam, ExePath = @"C:\s\redist.exe" },
            new() { Name = "CS2", Store = GameStore.Steam, ExePath = @"C:\s\cs2.exe" },
        ];
        var library = Make(cfg, new PassthroughResolver());

        var games = await library.LoadAsync();

        Assert.Equal("CS2", Assert.Single(games!).Name);
        Assert.Equal("CS2", Assert.Single(cfg.Cfg.DetectedGames).Name);
    }

    [Fact]
    public async Task a_superseded_load_returns_null_and_leaves_the_cache_to_the_newer_one()
    {
        var cfg = new MemoryConfigStore();
        var slowScan = new TaskCompletionSource<IReadOnlyList<Game>>();
        var scans = 0;
        var library = new GameLibrary(
            () => ++scans == 1 ? slowScan.Task : Task.FromResult<IReadOnlyList<Game>>([new() { Name = "New", Store = GameStore.Steam, ExePath = @"C:\s\new.exe" }]),
            new PassthroughResolver(), cfg);

        var older = library.LoadAsync(rescan: true);
        var newer = await library.LoadAsync(rescan: true);
        slowScan.SetResult([new() { Name = "Old", Store = GameStore.Steam, ExePath = @"C:\s\old.exe" }]);

        Assert.Null(await older);
        Assert.Equal("New", Assert.Single(newer!).Name);
        Assert.Equal("New", Assert.Single(cfg.Cfg.DetectedGames).Name);
    }

    [Fact]
    public async Task manual_game_is_persisted_and_listed_after_the_store_games()
    {
        var cfg = new MemoryConfigStore();
        var library = Make(cfg, new PassthroughResolver());

        library.AddManual(@"C:\tools\voicemeeterpro.exe");
        var game = Assert.Single((await library.LoadAsync())!);

        Assert.Equal("voicemeeterpro", game.Name);
        Assert.Equal(GameStore.Manual, game.Store);
        Assert.Single(cfg.Cfg.ManualGames);
    }

    [Fact]
    public async Task remove_manual_deletes_it_from_config()
    {
        var cfg = new MemoryConfigStore();
        var library = Make(cfg, new PassthroughResolver());
        library.AddManual(@"C:\tools\a.exe");
        var game = Assert.Single((await library.LoadAsync())!);

        Assert.True(library.RemoveManual(game));

        Assert.Empty(cfg.Cfg.ManualGames);
        Assert.Empty((await library.LoadAsync())!);
    }

    [Fact]
    public void remove_manual_is_a_noop_for_store_games()
    {
        var cfg = new MemoryConfigStore();
        var library = Make(cfg, new PassthroughResolver());

        Assert.False(library.RemoveManual(new Game { Name = "CS2", Store = GameStore.Steam, ExePath = @"C:\s\cs2.exe" }));
    }

    [Fact]
    public async Task chosen_exe_persists_and_wins_over_resolver()
    {
        // ExeChoices only applies when the chosen file exists -> use a real temp file
        var chosen = TempExe("real_game.exe");
        var cfg = new MemoryConfigStore();
        var game = new Game { Name = "Ambiguous", Store = GameStore.Steam, StoreId = "42", InstallDir = Path.GetDirectoryName(chosen) };
        var library = Make(cfg, new AmbiguousResolver(@"C:\nope\other.exe"), game);
        await library.LoadAsync();

        library.ChooseExe(game, chosen);
        var loaded = Assert.Single((await library.LoadAsync())!);

        Assert.Equal(chosen, cfg.Cfg.ExeChoices["Steam:42"]);
        Assert.Equal("real_game.exe", loaded.ExeName);
        Assert.False(loaded.NeedsExeConfirmation);
    }
}
