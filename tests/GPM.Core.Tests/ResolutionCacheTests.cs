using GPM.App.ViewModels;
using GPM.Core.Changes;
using GPM.Core.Models;
using GPM.Core.Persistence;
using GPM.Core.Priority;
using GPM.Core.Resolve;
using GPM.Core.Worker;

namespace GPM.Core.Tests;

public class ResolutionCacheTests
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

    private sealed class MemoryConfigStore : IConfigStore
    {
        public GpmConfig Cfg { get; private set; } = new();
        public string FilePath => "test://games.json";
        public GpmConfig Load() => Cfg;
        public void Save(GpmConfig config) => Cfg = config;
    }

    private static string TempExe(string name)
    {
        var path = Path.Combine(Directory.CreateTempSubdirectory("gpm-resolve-").FullName, name);
        File.WriteAllBytes(path, [0]);
        return path;
    }

    private static readonly Game Unresolved = new() { Name = "CS2", Store = GameStore.Steam, StoreId = "730", InstallDir = @"C:\s" };

    private static MainViewModel Make(MemoryConfigStore cfg, CountingResolver resolver) =>
        new(() => Task.FromResult<IReadOnlyList<Game>>([Unresolved]),
            resolver, cfg, new PendingChanges(new InMemoryPriorityService(), TestGraphics.New(), new InProcessElevatedWorker(new InMemoryPriorityService()), cfg));

    [Fact]
    public async Task resolved_exe_is_cached_so_reload_skips_the_resolver()
    {
        var cfg = new MemoryConfigStore();
        var resolver = new CountingResolver { Exe = TempExe("cs2.exe") };
        var vm = Make(cfg, resolver);

        await vm.LoadAsync();
        Assert.Equal(1, resolver.Calls);
        Assert.Equal(resolver.Exe, Assert.Single(cfg.Cfg.DetectedGames).ExePath);

        await vm.LoadAsync();
        Assert.Equal(1, resolver.Calls); // no install-dir walk on a cache hit
        Assert.Equal("cs2.exe", Assert.Single(vm.Rows).State!.ExeName);

        await vm.LoadAsync(forceScan: true); // rescan re-resolves
        Assert.Equal(2, resolver.Calls);
    }

    [Fact]
    public async Task cached_exe_that_vanished_is_resolved_again()
    {
        var cfg = new MemoryConfigStore();
        var resolver = new CountingResolver { Exe = TempExe("old.exe") };
        var vm = Make(cfg, resolver);
        await vm.LoadAsync();

        File.Delete(resolver.Exe);
        resolver.Exe = TempExe("new.exe");
        await vm.LoadAsync();

        Assert.Equal(2, resolver.Calls);
        Assert.Equal("new.exe", Assert.Single(vm.Rows).State!.ExeName);
        Assert.Equal(resolver.Exe, Assert.Single(cfg.Cfg.DetectedGames).ExePath);
    }

    [Fact]
    public async Task ambiguous_resolution_still_asks_for_confirmation_after_a_cache_hit()
    {
        var cfg = new MemoryConfigStore();
        var exe = TempExe("big.exe");
        var vm = new MainViewModel(() => Task.FromResult<IReadOnlyList<Game>>([Unresolved]),
            new AmbiguousResolver(exe), cfg, new PendingChanges(new InMemoryPriorityService(), TestGraphics.New(), new InProcessElevatedWorker(new InMemoryPriorityService()), cfg));

        await vm.LoadAsync();
        await vm.LoadAsync(); // from cache

        var row = Assert.Single(vm.Rows);
        Assert.True(row.NeedsExeConfirmation);
        Assert.Equal([exe, "other.exe"], row.Game.ExeCandidates);
    }

    private sealed class AmbiguousResolver(string exe) : IExeResolver
    {
        public ExeResolution Resolve(Game game) => new(exe, true, [exe, "other.exe"]);
    }
}
