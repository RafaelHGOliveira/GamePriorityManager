using GPM.App.Services;
using GPM.App.ViewModels;
using GPM.Core.Models;
using GPM.Core.Persistence;
using GPM.Core.Priority;
using GPM.Core.Resolve;

namespace GPM.Core.Tests;

public class MainViewModelTests
{
    private sealed class PassthroughResolver : IExeResolver
    {
        public ExeResolution Resolve(Game game) => new(game.ExePath, false, []);
    }

    private sealed class FakeLauncher(InMemoryPriorityService svc, bool declineUac = false) : IElevationLauncher
    {
        public Task<int?> RunApplyAsync(string opsPath)
        {
            if (declineUac) return Task.FromResult<int?>(null);
            // simulate the elevated worker: apply ops straight to the fake service
            var ops = System.Text.Json.JsonSerializer.Deserialize<List<GPM.Core.Worker.ApplyOp>>(
                File.ReadAllText(opsPath), new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web))!;
            foreach (var op in ops) svc.Write(op.Exe, op.Cpu, op.Io);
            return Task.FromResult<int?>(0);
        }
    }

    private sealed class TempConfigStore : IConfigStore
    {
        private GpmConfig _cfg = new();
        public string FilePath => "test://games.json";
        public GpmConfig Load() => _cfg;
        public void Save(GpmConfig config) => _cfg = config;
    }

    private static Game G(string name, string exe, GameStore store = GameStore.Steam) =>
        new() { Name = name, Store = store, ExePath = exe };

    private static MainViewModel Make(InMemoryPriorityService svc, bool declineUac = false, params Game[] games) =>
        new(() => Task.FromResult<IReadOnlyList<Game>>(games),
            new PassthroughResolver(), svc, new FakeLauncher(svc, declineUac), new TempConfigStore());

    [Fact]
    public async Task load_builds_rows_and_reads_registry_state()
    {
        var svc = new InMemoryPriorityService();
        svc.Write("cs2.exe", 3, null);
        var vm = Make(svc, false, G("Counter-Strike 2", @"C:\s\cs2.exe"));

        await vm.LoadAsync();

        var row = Assert.Single(vm.Rows);
        Assert.Equal(3, row.State!.PendingCpu);
        Assert.Equal(0, vm.PendingCount);
    }

    [Fact]
    public async Task same_filename_across_stores_shares_one_state()
    {
        var svc = new InMemoryPriorityService();
        var vm = Make(svc, false,
            G("Game A", @"C:\steam\cs2.exe", GameStore.Steam),
            G("Game A", @"D:\other\CS2.exe", GameStore.Gog));

        await vm.LoadAsync();

        Assert.Same(vm.Rows[0].State, vm.Rows[1].State); // linked by construction
        Assert.True(vm.Rows[0].IsLinked);

        vm.Rows[0].State!.PendingCpu = 3;
        Assert.Equal(1, vm.PendingCount); // one pending change, not two
    }

    [Fact]
    public async Task apply_writes_ops_and_refresh_clears_dirty()
    {
        var svc = new InMemoryPriorityService();
        var vm = Make(svc, false, G("ELDEN RING", @"C:\g\eldenring.exe"));
        await vm.LoadAsync();

        vm.Rows[0].State!.PendingCpu = 3;
        vm.Rows[0].State!.PendingIo = 2;
        await vm.ApplyCommand.ExecuteAsync(null);

        Assert.Equal((3, 2), svc.Data["eldenring.exe"]);
        Assert.Equal(0, vm.PendingCount);
        Assert.False(vm.Rows[0].State!.IsDirty);
    }

    [Fact]
    public async Task uac_declined_keeps_changes_pending()
    {
        var svc = new InMemoryPriorityService();
        var vm = Make(svc, declineUac: true, G("ELDEN RING", @"C:\g\eldenring.exe"));
        await vm.LoadAsync();

        vm.Rows[0].State!.PendingCpu = 3;
        await vm.ApplyCommand.ExecuteAsync(null);

        Assert.Empty(svc.Data);                         // nothing written
        Assert.Equal(1, vm.PendingCount);               // still pending
        Assert.Equal(GPM.App.Strings.ApplyCancelled, vm.StatusMessage);
    }

    [Fact]
    public async Task discard_reverts_all_pending()
    {
        var svc = new InMemoryPriorityService();
        var vm = Make(svc, false, G("A", @"C:\a\a.exe"), G("B", @"C:\b\b.exe"));
        await vm.LoadAsync();

        vm.Rows[0].State!.PendingCpu = 3;
        vm.Rows[1].State!.PendingIo = 1;
        vm.DiscardCommand.Execute(null);

        Assert.Equal(0, vm.PendingCount);
    }

    [Fact]
    public async Task load_notifies_visible_rows_so_ui_binding_refreshes()
    {
        var svc = new InMemoryPriorityService();
        var vm = Make(svc, false, G("ELDEN RING", @"C:\g\er.exe"));

        var raised = false;
        vm.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(vm.VisibleRows)) raised = true; };
        await vm.LoadAsync();

        Assert.True(raised); // WPF's ListView binds VisibleRows; without this it never re-queries Rows
    }

    [Fact]
    public async Task search_filters_visible_rows_by_name()
    {
        var svc = new InMemoryPriorityService();
        var vm = Make(svc, false, G("ELDEN RING", @"C:\g\er.exe"), G("Counter-Strike 2", @"C:\g\cs2.exe"));
        await vm.LoadAsync();

        vm.SearchText = "elden";
        Assert.Equal("ELDEN RING", Assert.Single(vm.VisibleRows).Game.Name);
    }

    [Fact]
    public async Task overrides_not_matching_games_land_in_other_overrides_with_orphan_flag()
    {
        var svc = new InMemoryPriorityService();
        svc.Write("voicemeeterpro.exe", 6, null);   // intentional non-game override
        svc.Write("uninstalled.exe", 3, null);      // was managed by GPM, game gone
        var cfgStore = new TempConfigStore();
        var cfg = cfgStore.Load();
        cfg.ManagedExes.Add("uninstalled.exe");
        cfgStore.Save(cfg);

        var vm = new MainViewModel(
            () => Task.FromResult<IReadOnlyList<Game>>([G("CS2", @"C:\g\cs2.exe")]),
            new PassthroughResolver(), svc, new FakeLauncher(svc), cfgStore);
        await vm.LoadAsync();

        Assert.Equal(2, vm.OtherOverrides.Count);
        Assert.False(vm.OtherOverrides.Single(o => o.State.ExeName == "voicemeeterpro.exe").IsOrphaned);
        Assert.True(vm.OtherOverrides.Single(o => o.State.ExeName == "uninstalled.exe").IsOrphaned);
    }
}
