using GPM.App.ViewModels;
using GPM.Core.Changes;
using GPM.Core.Models;
using GPM.Core.Persistence;
using GPM.Core.Priority;
using GPM.Core.Resolve;
using GPM.Core.Worker;

namespace GPM.Core.Tests;

public class MainViewModelTests
{
    private sealed class PassthroughResolver : IExeResolver
    {
        public ExeResolution Resolve(Game game) => new(game.ExePath, false, []);
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

    private static MainViewModel Make(InMemoryPriorityService svc, bool declineUac = false, params Game[] games)
    {
        var cfg = new TempConfigStore();
        return new(() => Task.FromResult<IReadOnlyList<Game>>(games), new PassthroughResolver(), cfg,
            new PendingChanges(svc, TestGraphics.New(),
                declineUac ? TestWorkers.DeclinedUac : new InProcessElevatedWorker(svc), cfg));
    }

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
    public async Task load_failure_surfaces_in_status_instead_of_throwing()
    {
        var svc = new InMemoryPriorityService();
        var cfg = new TempConfigStore();
        var vm = new MainViewModel(
            () => throw new InvalidOperationException("boom"),
            new PassthroughResolver(), cfg, new PendingChanges(svc, TestGraphics.New(), new InProcessElevatedWorker(svc), cfg));

        await vm.LoadAsync();

        Assert.Contains("boom", vm.StatusMessage);
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
    public async Task visible_rows_view_reflects_each_load()
    {
        var svc = new InMemoryPriorityService();
        var vm = Make(svc, false, G("ELDEN RING", @"C:\g\er.exe"));

        Assert.Empty(vm.VisibleRows.Cast<GameRowViewModel>()); // view created before the load
        await vm.LoadAsync();

        Assert.Single(vm.VisibleRows.Cast<GameRowViewModel>()); // the plain backing list needs an explicit Refresh
    }

    [Fact]
    public async Task search_filters_visible_rows_by_name()
    {
        var svc = new InMemoryPriorityService();
        var vm = Make(svc, false, G("ELDEN RING", @"C:\g\er.exe"), G("Counter-Strike 2", @"C:\g\cs2.exe"));
        await vm.LoadAsync();

        vm.SearchText = "elden";
        Assert.Equal("ELDEN RING", Assert.Single(vm.VisibleRows.Cast<GameRowViewModel>()).Game.Name);

        vm.SearchText = "";
        Assert.Equal(2, vm.VisibleRows.Cast<GameRowViewModel>().Count());
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
            new PassthroughResolver(), cfgStore, new PendingChanges(svc, TestGraphics.New(), new InProcessElevatedWorker(svc), cfgStore));
        await vm.LoadAsync();

        Assert.Equal(2, vm.OtherOverrides.Count);
        Assert.False(vm.OtherOverrides.Single(o => o.State.ExeName == "voicemeeterpro.exe").IsOrphaned);
        Assert.True(vm.OtherOverrides.Single(o => o.State.ExeName == "uninstalled.exe").IsOrphaned);
    }

    // The UI never offers Realtime (4), so forcing it is the simplest batch the
    // Elevated Worker's OpsValidator rejects (ADR-0003).
    private static async Task<(MainViewModel vm, InMemoryPriorityService svc, TempConfigStore cfg)> ApplyRejectedBatch()
    {
        var svc = new InMemoryPriorityService();
        var cfg = new TempConfigStore();
        var vm = new MainViewModel(
            () => Task.FromResult<IReadOnlyList<Game>>([G("ELDEN RING", @"C:\g\eldenring.exe")]),
            new PassthroughResolver(), cfg, new PendingChanges(svc, TestGraphics.New(), new InProcessElevatedWorker(svc), cfg));
        await vm.LoadAsync();

        vm.Rows[0].State!.PendingCpu = 4;
        await vm.ApplyCommand.ExecuteAsync(null);
        return (vm, svc, cfg);
    }

    // Pins today's behaviour (known bug, architecture review 2026-10-26): a rejected
    // batch writes nothing and stays pending, but the user is told nothing and the exe
    // is still recorded as managed. Replace with the skipped test below once fixed.
    [Fact]
    public async Task apply_rejected_by_the_worker_is_silent_and_still_marks_exe_managed()
    {
        var (vm, svc, cfg) = await ApplyRejectedBatch();

        Assert.Empty(svc.Data);                        // the worker wrote nothing
        Assert.Equal(1, vm.PendingCount);              // the change is still pending
        Assert.Null(vm.StatusMessage);                 // ...but nothing tells the user why
        Assert.Null(vm.Rows[0].State!.LastError);
        Assert.Contains("eldenring.exe", cfg.Load().ManagedExes);
    }

    [Fact(Skip = "Known bug: a batch the Elevated Worker rejects is silent and still marks exes as managed")]
    public async Task apply_rejected_by_the_worker_tells_the_user_and_marks_nothing_managed()
    {
        var (vm, _, cfg) = await ApplyRejectedBatch();

        Assert.NotNull(vm.StatusMessage);
        Assert.DoesNotContain("eldenring.exe", cfg.Load().ManagedExes);
    }
}
