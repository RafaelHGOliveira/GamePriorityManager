using GPM.App.ViewModels;
using GPM.Core.Changes;
using GPM.Core.Graphics;
using GPM.Core.Models;
using GPM.Core.Persistence;
using GPM.Core.Priority;
using GPM.Core.Registry;
using GPM.Core.Resolve;
using GPM.Core.Worker;

namespace GPM.Core.Tests;

public class GraphicsPreferenceViewModelTests
{
    private const string CsPath = @"C:\s\cs2.exe";

    private sealed class PassthroughResolver : IExeResolver
    {
        public ExeResolution Resolve(Game game) => new(game.ExePath, false, []);
    }

    private sealed class CountingWorker(IElevatedWorker inner) : IElevatedWorker
    {
        public int Calls { get; private set; }
        public Task<ApplyReport?> ApplyAsync(IReadOnlyList<ApplyOp> ops)
        {
            Calls++;
            return inner.ApplyAsync(ops);
        }
    }

    private sealed class MemoryConfig : IConfigStore
    {
        private GpmConfig _cfg = new();
        public string FilePath => "test://games.json";
        public GpmConfig Load() => _cfg;
        public void Save(GpmConfig config) => _cfg = config;
    }

    private static Game G(string name, string exe, GameStore store = GameStore.Steam) =>
        new() { Name = name, Store = store, ExePath = exe };

    private static MainViewModel Make(
        InMemoryStringValueStore store, IElevatedWorker? worker, params Game[] games)
    {
        var priorities = new InMemoryPriorityService();
        var config = new MemoryConfig();
        return new(() => Task.FromResult<IReadOnlyList<Game>>(games),
            new PassthroughResolver(), config,
            new PendingChanges(priorities, new GraphicsPreferenceService(store),
                worker ?? new InProcessElevatedWorker(priorities), config));
    }

    [Fact]
    public async Task load_reads_the_preference_of_each_exe_path()
    {
        var store = new InMemoryStringValueStore();
        store.Set(CsPath, "GpuPreference=2;");

        var vm = Make(store, null, G("CS2", CsPath));
        await vm.LoadAsync();

        Assert.Equal(2, vm.Rows[0].Graphics!.PendingGpu);
        Assert.Equal(0, vm.PendingCount);
    }

    [Fact]
    public async Task linked_rows_share_cpu_state_but_not_graphics_state()
    {
        var vm = Make(new InMemoryStringValueStore(), null,
            G("A", @"C:\steam\cs2.exe"), G("B", @"D:\other\cs2.exe", GameStore.Gog));
        await vm.LoadAsync();

        Assert.Same(vm.Rows[0].State, vm.Rows[1].State);
        Assert.NotSame(vm.Rows[0].Graphics, vm.Rows[1].Graphics);

        vm.Rows[0].Graphics!.PendingGpu = 2;
        Assert.Null(vm.Rows[1].Graphics!.PendingGpu);
        Assert.Equal(1, vm.PendingCount);
    }

    [Fact]
    public async Task gpu_only_apply_writes_the_registry_without_elevating()
    {
        var store = new InMemoryStringValueStore();
        var worker = new CountingWorker(TestWorkers.DeclinedUac);
        var vm = Make(store, worker, G("CS2", CsPath));
        await vm.LoadAsync();

        vm.Rows[0].Graphics!.PendingGpu = 2;
        await vm.ApplyCommand.ExecuteAsync(null);

        Assert.Equal("GpuPreference=2;", store.Get(CsPath));
        Assert.Equal(0, worker.Calls);
        Assert.Equal(0, vm.PendingCount);
        Assert.False(vm.Rows[0].IsDirty);
    }

    [Fact]
    public async Task declined_uac_keeps_cpu_pending_but_gpu_stays_applied()
    {
        var store = new InMemoryStringValueStore();
        var vm = Make(store, TestWorkers.DeclinedUac, G("CS2", CsPath));
        await vm.LoadAsync();

        vm.Rows[0].Graphics!.PendingGpu = 2;
        vm.Rows[0].State!.PendingCpu = 3;
        await vm.ApplyCommand.ExecuteAsync(null);

        Assert.Equal("GpuPreference=2;", store.Get(CsPath));
        Assert.Equal(1, vm.PendingCount); // only CPU is still pending
        Assert.Equal(GPM.App.Strings.ApplyCancelledGpuApplied, vm.StatusMessage);
    }

    [Fact]
    public async Task apply_keeps_foreign_tokens_in_the_value()
    {
        var store = new InMemoryStringValueStore();
        store.Set(CsPath, "AutoHDREnable=2097;GpuPreference=1;");
        var vm = Make(store, null, G("CS2", CsPath));
        await vm.LoadAsync();

        vm.Rows[0].Graphics!.PendingGpu = 2;
        vm.Rows[0].Graphics!.PendingWindowed = false;
        await vm.ApplyCommand.ExecuteAsync(null);

        Assert.Equal("AutoHDREnable=2097;GpuPreference=2;SwapEffectUpgradeEnable=0;", store.Get(CsPath));
    }

    [Fact]
    public async Task discard_reverts_pending_graphics_changes()
    {
        var store = new InMemoryStringValueStore();
        var vm = Make(store, null, G("CS2", CsPath));
        await vm.LoadAsync();

        vm.Rows[0].Graphics!.PendingGpu = 2;
        vm.DiscardCommand.Execute(null);

        Assert.Equal(0, vm.PendingCount);
        Assert.Null(vm.Rows[0].Graphics!.PendingGpu);
        Assert.Null(store.Get(CsPath));
    }

    [Fact]
    public async Task row_dirty_flag_follows_either_half_of_the_row()
    {
        var vm = Make(new InMemoryStringValueStore(), null, G("CS2", CsPath));
        await vm.LoadAsync();
        var row = vm.Rows[0];
        var raised = new List<string?>();
        row.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        row.Graphics!.PendingGpu = 1;

        Assert.True(row.IsDirty);
        Assert.Contains(nameof(GameRowViewModel.IsDirty), raised);
    }

    [Fact]
    public async Task show_windowed_column_setting_is_persisted()
    {
        var cfg = new MemoryConfig();
        var vm = new MainViewModel(
            () => Task.FromResult<IReadOnlyList<Game>>([]),
            new PassthroughResolver(), cfg,
            new PendingChanges(new InMemoryPriorityService(), TestGraphics.New(),
                new InProcessElevatedWorker(new InMemoryPriorityService()), cfg));
        await vm.LoadAsync();

        vm.ShowWindowedColumn = true;

        Assert.True(cfg.Load().ShowWindowedColumn);
    }
}
