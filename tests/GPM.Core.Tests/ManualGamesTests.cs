using GPM.App.Services;
using GPM.App.ViewModels;
using GPM.Core.Models;
using GPM.Core.Persistence;
using GPM.Core.Priority;
using GPM.Core.Resolve;

namespace GPM.Core.Tests;

public class ManualGamesTests
{
    private sealed class PassthroughResolver : IExeResolver
    {
        public ExeResolution Resolve(Game game) => new(game.ExePath, game.ExePath is null, []);
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

    private static MainViewModel Make(MemoryConfigStore cfg, params Game[] detected) =>
        new(() => Task.FromResult<IReadOnlyList<Game>>(detected),
            new PassthroughResolver(), new InMemoryPriorityService(), new NoopLauncher(), cfg);

    [Fact]
    public async Task add_manual_game_persists_and_shows_as_removable_row()
    {
        var cfg = new MemoryConfigStore();
        var vm = Make(cfg);
        await vm.LoadAsync();

        await vm.AddManualGameAsync(@"C:\tools\voicemeeterpro.exe");

        var row = Assert.Single(vm.Rows);
        Assert.Equal("voicemeeterpro", row.Game.Name);
        Assert.Equal(GameStore.Manual, row.Game.Store);
        Assert.True(row.IsManual);
        Assert.Single(cfg.Cfg.ManualGames);
    }

    [Fact]
    public async Task remove_manual_game_deletes_it_from_config_and_rows()
    {
        var cfg = new MemoryConfigStore();
        var vm = Make(cfg);
        await vm.LoadAsync();
        await vm.AddManualGameAsync(@"C:\tools\a.exe");

        await vm.RemoveManualGameAsync(vm.Rows.Single());

        Assert.Empty(vm.Rows);
        Assert.Empty(cfg.Cfg.ManualGames);
    }

    [Fact]
    public async Task remove_is_a_noop_for_store_games()
    {
        var cfg = new MemoryConfigStore();
        var vm = Make(cfg, new Game { Name = "CS2", Store = GameStore.Steam, ExePath = @"C:\s\cs2.exe" });
        await vm.LoadAsync();

        await vm.RemoveManualGameAsync(vm.Rows.Single());
        Assert.Single(vm.Rows);
    }

    [Fact]
    public async Task confirmed_exe_choice_persists_and_wins_over_resolver()
    {
        // ExeChoices only applies when the chosen file exists -> use a real temp file
        var dir = Directory.CreateTempSubdirectory("gpm-choice-").FullName;
        var chosen = Path.Combine(dir, "real_game.exe");
        File.WriteAllBytes(chosen, [0]);

        var cfg = new MemoryConfigStore();
        var game = new Game { Name = "Ambiguous", Store = GameStore.Steam, StoreId = "42", InstallDir = dir };
        var vm = Make(cfg, game);
        await vm.LoadAsync();

        await vm.ConfirmExeAsync(vm.Rows.Single(), chosen);

        Assert.Equal(chosen, cfg.Cfg.ExeChoices["Steam:42"]);
        var row = vm.Rows.Single();
        Assert.Equal("real_game.exe", row.State!.ExeName);
        Assert.False(row.NeedsExeConfirmation);
    }
}
