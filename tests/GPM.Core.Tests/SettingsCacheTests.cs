using GPM.App.Services;
using GPM.App.ViewModels;
using GPM.Core.Models;
using GPM.Core.Persistence;
using GPM.Core.Priority;
using GPM.Core.Resolve;

namespace GPM.Core.Tests;

public class SettingsCacheTests
{
    private sealed class PassthroughResolver : IExeResolver
    {
        public ExeResolution Resolve(Game game) => new(game.ExePath, false, []);
    }

    private sealed class NoopLauncher : IElevationLauncher
    {
        public Task<int?> RunApplyAsync(string opsPath) => Task.FromResult<int?>(0);
    }

    private sealed class MemoryConfigStore : IConfigStore
    {
        public GpmConfig Cfg { get; private set; } = new();
        public string FilePath => "test://games.json";
        public GpmConfig Load() => Cfg;
        public void Save(GpmConfig config) => Cfg = config;
    }

    private static Game G(string name) =>
        new() { Name = name, Store = GameStore.Steam, ExePath = $@"C:\g\{name}.exe" };

    [Fact]
    public async Task first_load_scans_and_caches_second_load_uses_cache()
    {
        var cfg = new MemoryConfigStore();
        var scans = 0;
        var vm = new MainViewModel(
            () => { scans++; return Task.FromResult<IReadOnlyList<Game>>([G("cs2")]); },
            new PassthroughResolver(), new InMemoryPriorityService(), new NoopLauncher(), cfg, TestGraphics.New());

        await vm.LoadAsync();
        Assert.Equal(1, scans);
        Assert.Single(cfg.Cfg.DetectedGames);

        await vm.LoadAsync();          // cache hit
        Assert.Equal(1, scans);
        Assert.Single(vm.Rows);

        await vm.LoadAsync(forceScan: true); // rescan button
        Assert.Equal(2, scans);
    }

    [Fact]
    public async Task settings_load_from_config_and_persist_on_change()
    {
        var cfg = new MemoryConfigStore();
        cfg.Cfg.ShowIoColumn = true;
        cfg.Cfg.Theme = "dark";
        var vm = new MainViewModel(
            () => Task.FromResult<IReadOnlyList<Game>>([]),
            new PassthroughResolver(), new InMemoryPriorityService(), new NoopLauncher(), cfg, TestGraphics.New());

        await vm.LoadAsync();
        Assert.True(vm.ShowIoColumn);
        Assert.Equal("dark", vm.Theme);

        vm.BetaSourcesEnabled = true;
        vm.Theme = "light";
        Assert.True(cfg.Cfg.BetaSourcesEnabled);
        Assert.Equal("light", cfg.Cfg.Theme);
        Assert.True(cfg.Cfg.ShowIoColumn); // untouched settings survive
    }
}
