using GPM.App.ViewModels;
using GPM.Core.Changes;
using GPM.Core.Models;
using GPM.Core.Persistence;
using GPM.Core.Priority;
using GPM.Core.Resolve;
using GPM.Core.Worker;

namespace GPM.Core.Tests;

/// What the view model owns: rows, status text, search. Pending Change behaviour
/// itself is tested through PendingChanges (PendingChangesTests).
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
    public async Task same_filename_across_stores_makes_linked_rows()
    {
        var vm = Make(new InMemoryPriorityService(), false,
            G("Game A", @"C:\steam\cs2.exe", GameStore.Steam),
            G("Game A", @"D:\other\CS2.exe", GameStore.Gog));

        await vm.LoadAsync();

        Assert.Same(vm.Rows[0].State, vm.Rows[1].State);
        Assert.True(vm.Rows[0].IsLinked);
        Assert.True(vm.Rows[1].IsLinked);
    }

    [Fact]
    public async Task pending_count_follows_every_edit()
    {
        var vm = Make(new InMemoryPriorityService(), false, G("ELDEN RING", @"C:\g\eldenring.exe"));
        await vm.LoadAsync();
        var raised = new List<string?>();
        vm.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        vm.Rows[0].State!.PendingCpu = 3;

        Assert.Equal(1, vm.PendingCount);
        Assert.Contains(nameof(MainViewModel.PendingCount), raised);
    }

    [Fact]
    public async Task row_dirty_flag_follows_either_half_of_the_row()
    {
        var vm = Make(new InMemoryPriorityService(), false, G("CS2", @"C:\s\cs2.exe"));
        await vm.LoadAsync();
        var row = vm.Rows[0];
        var raised = new List<string?>();
        row.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        row.Graphics!.PendingGpu = 1;

        Assert.True(row.IsDirty);
        Assert.Contains(nameof(GameRowViewModel.IsDirty), raised);
    }

    [Fact]
    public async Task other_overrides_become_rows()
    {
        var svc = new InMemoryPriorityService();
        svc.Write("voicemeeterpro.exe", 6, null);
        var vm = Make(svc, false, G("CS2", @"C:\g\cs2.exe"));

        await vm.LoadAsync();

        Assert.Equal("voicemeeterpro.exe", Assert.Single(vm.OtherOverrides).State.ExeName);
    }

    [Fact]
    public async Task uac_declined_says_the_apply_was_cancelled()
    {
        var vm = Make(new InMemoryPriorityService(), declineUac: true, G("ELDEN RING", @"C:\g\eldenring.exe"));
        await vm.LoadAsync();

        vm.Rows[0].State!.PendingCpu = 3;
        await vm.ApplyCommand.ExecuteAsync(null);

        Assert.Equal(GPM.App.Strings.ApplyCancelled, vm.StatusMessage);
    }

    [Fact]
    public async Task uac_declined_after_graphics_preferences_says_those_were_applied()
    {
        var vm = Make(new InMemoryPriorityService(), declineUac: true, G("CS2", @"C:\s\cs2.exe"));
        await vm.LoadAsync();

        vm.Rows[0].Graphics!.PendingGpu = 2;
        vm.Rows[0].State!.PendingCpu = 3;
        await vm.ApplyCommand.ExecuteAsync(null);

        Assert.Equal(GPM.App.Strings.ApplyCancelledGpuApplied, vm.StatusMessage);
    }

    [Fact]
    public async Task discard_clears_the_status()
    {
        var vm = Make(new InMemoryPriorityService(), declineUac: true, G("ELDEN RING", @"C:\g\eldenring.exe"));
        await vm.LoadAsync();
        vm.Rows[0].State!.PendingCpu = 3;
        await vm.ApplyCommand.ExecuteAsync(null);

        vm.DiscardCommand.Execute(null);

        Assert.Null(vm.StatusMessage);
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

    // The UI never offers Realtime (4), so forcing it is the simplest batch the
    // Elevated Worker's OpsValidator rejects (ADR-0003).
    private static async Task<MainViewModel> ApplyRejectedBatch()
    {
        var vm = Make(new InMemoryPriorityService(), false, G("ELDEN RING", @"C:\g\eldenring.exe"));
        await vm.LoadAsync();

        vm.Rows[0].State!.PendingCpu = 4;
        await vm.ApplyCommand.ExecuteAsync(null);
        return vm;
    }

    // Pins today's behaviour (known bug, architecture review 2026-10-26): the user is
    // told nothing about a rejected batch. Replace with the skipped test below once
    // fixed (.scratch/apply-feedback/issues/01).
    [Fact]
    public async Task apply_rejected_by_the_worker_is_silent()
    {
        var vm = await ApplyRejectedBatch();

        Assert.Null(vm.StatusMessage);
    }

    [Fact(Skip = "Known bug: a batch the Elevated Worker rejects is silent")]
    public async Task apply_rejected_by_the_worker_tells_the_user()
    {
        var vm = await ApplyRejectedBatch();

        Assert.NotNull(vm.StatusMessage);
    }
}
