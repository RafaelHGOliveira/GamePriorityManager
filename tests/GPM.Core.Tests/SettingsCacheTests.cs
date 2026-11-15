using GPM.App.ViewModels;
using GPM.Core.Changes;
using GPM.Core.Library;
using GPM.Core.Models;
using GPM.Core.Priority;
using GPM.Core.Resolve;
using GPM.Core.Worker;

namespace GPM.Core.Tests;

/// UI settings round-trip through games.json. The detected-game cache is GameLibraryTests.
public class SettingsCacheTests
{
    private sealed class PassthroughResolver : IExeResolver
    {
        public ExeResolution Resolve(Game game) => new(game.ExePath, false, []);
    }

    private static MainViewModel Make(MemoryConfigStore cfg) =>
        new(new GameLibrary(() => Task.FromResult<IReadOnlyList<Game>>([]), new PassthroughResolver(), cfg), cfg,
            new PendingChanges(new InMemoryPriorityService(), TestGraphics.New(), new InProcessElevatedWorker(new InMemoryPriorityService()), cfg));

    [Fact]
    public async Task settings_load_from_config_and_persist_on_change()
    {
        var cfg = new MemoryConfigStore();
        cfg.Cfg.ShowIoColumn = true;
        cfg.Cfg.Theme = "dark";
        var vm = Make(cfg);

        await vm.LoadAsync();
        Assert.True(vm.ShowIoColumn);
        Assert.Equal("dark", vm.Theme);

        vm.BetaSourcesEnabled = true;
        vm.Theme = "light";
        Assert.True(cfg.Cfg.BetaSourcesEnabled);
        Assert.Equal("light", cfg.Cfg.Theme);
        Assert.True(cfg.Cfg.ShowIoColumn); // untouched settings survive
    }

    [Fact]
    public async Task show_windowed_column_setting_is_persisted()
    {
        var cfg = new MemoryConfigStore();
        var vm = Make(cfg);
        await vm.LoadAsync();

        vm.ShowWindowedColumn = true;

        Assert.True(cfg.Cfg.ShowWindowedColumn);
    }
}
