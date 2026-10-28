using GPM.Core.Changes;
using GPM.Core.Graphics;
using GPM.Core.Models;
using GPM.Core.Persistence;
using GPM.Core.Priority;
using GPM.Core.Registry;
using GPM.Core.Worker;

namespace GPM.Core.Tests;

public class PendingChangesTests
{
    private const string CsPath = @"C:\s\cs2.exe";

    private sealed class MemoryConfig : IConfigStore
    {
        public GpmConfig Cfg { get; private set; } = new();
        public string FilePath => "test://games.json";
        public GpmConfig Load() => Cfg;
        public void Save(GpmConfig config) => Cfg = config;
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

    /// Lets the Elevated Worker's write of one exe fail, as an ACL-protected IFEO key would.
    private sealed class FailingFor(string exe, IPriorityService inner) : IPriorityService
    {
        public PriorityOverride Read(string exeName) => inner.Read(exeName);
        public IReadOnlyList<PriorityOverride> ReadAll() => inner.ReadAll();
        public void Write(string exeName, int? cpu, int? io)
        {
            if (exeName == exe) throw new UnauthorizedAccessException("access denied");
            inner.Write(exeName, cpu, io);
        }
    }

    private readonly InMemoryPriorityService _priorities = new();
    private readonly InMemoryStringValueStore _store = new();
    private readonly MemoryConfig _config = new();

    private static Game G(string exePath, GameStore store = GameStore.Steam) =>
        new() { Name = Path.GetFileNameWithoutExtension(exePath), Store = store, ExePath = exePath };

    private PendingChanges Loaded(IElevatedWorker? worker, params Game[] games)
    {
        var changes = new PendingChanges(_priorities, new GraphicsPreferenceService(_store),
            worker ?? new InProcessElevatedWorker(_priorities), _config);
        changes.Load(changes.Read(games));
        return changes;
    }

    private PendingChanges Loaded(params Game[] games) => Loaded(null, games);

    [Fact]
    public void load_reads_what_windows_has_for_each_game()
    {
        _priorities.Write("cs2.exe", 3, null);
        _store.Set(CsPath, "GpuPreference=2;");

        var changes = Loaded(G(CsPath));

        Assert.Equal(3, changes.PriorityOverride("cs2.exe").PendingCpu);
        Assert.Equal(2, changes.GraphicsPreference(CsPath).PendingGpu);
        Assert.Equal(0, changes.Count);
    }

    [Fact]
    public void priority_override_is_keyed_by_lowercase_exe_filename()
    {
        var changes = Loaded(G(@"C:\g\Game.EXE"));

        Assert.Equal("game.exe", changes.PriorityOverride("game.exe").ExeName);
    }

    [Fact]
    public void linked_rows_share_one_priority_override_but_keep_their_own_graphics_preference()
    {
        var changes = Loaded(G(@"C:\steam\cs2.exe"), G(@"D:\other\CS2.exe", GameStore.Gog));

        Assert.Same(changes.PriorityOverride("cs2.exe"), changes.PriorityOverride("CS2.exe"));
        Assert.NotSame(changes.GraphicsPreference(@"C:\steam\cs2.exe"), changes.GraphicsPreference(@"D:\other\CS2.exe"));

        changes.PriorityOverride("cs2.exe").PendingCpu = 3;
        Assert.Equal(1, changes.Count); // one Pending Change, not two

        changes.GraphicsPreference(@"C:\steam\cs2.exe").PendingGpu = 2;
        Assert.Null(changes.GraphicsPreference(@"D:\other\CS2.exe").PendingGpu);
        Assert.Equal(2, changes.Count);
    }

    [Fact]
    public void editing_a_pending_change_raises_count()
    {
        var changes = Loaded(G(CsPath));
        var raised = new List<string?>();
        changes.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        changes.PriorityOverride("cs2.exe").PendingIo = 1;

        Assert.Equal(1, changes.Count);
        Assert.Contains(nameof(PendingChanges.Count), raised);
    }

    [Fact]
    public void discard_reverts_every_pending_change_to_what_windows_has()
    {
        _priorities.Write("a.exe", 5, 1);
        var changes = Loaded(G(@"C:\a\a.exe"), G(CsPath));
        var a = changes.PriorityOverride("a.exe");
        a.PendingCpu = 3;
        a.PendingIo = null;
        changes.GraphicsPreference(CsPath).PendingGpu = 2;

        changes.Discard();

        Assert.Equal(0, changes.Count);
        Assert.Equal((5, 1), (a.PendingCpu, a.PendingIo));
        Assert.Null(changes.GraphicsPreference(CsPath).PendingGpu);
        Assert.Null(_store.Get(CsPath)); // nothing written
    }

    [Fact]
    public async Task nothing_pending_is_nothing_to_apply()
    {
        var worker = new CountingWorker(new InProcessElevatedWorker(_priorities));
        var changes = Loaded(worker, G(CsPath));

        var outcome = await changes.ApplyAsync();

        Assert.Equal(ApplyStatus.NothingToApply, outcome.Status);
        Assert.Equal(0, worker.Calls);
    }

    [Fact]
    public async Task apply_writes_the_batch_and_nothing_stays_pending()
    {
        var changes = Loaded(G(@"C:\g\eldenring.exe"));
        var state = changes.PriorityOverride("eldenring.exe");
        state.PendingCpu = 3;
        state.PendingIo = 2;

        var outcome = await changes.ApplyAsync();

        Assert.Equal(ApplyStatus.Applied, outcome.Status);
        Assert.Equal((3, 2), _priorities.Data["eldenring.exe"]);
        Assert.False(state.IsDirty);
        Assert.Equal(0, changes.Count);
    }

    [Fact]
    public async Task apply_records_provenance_for_orphan_detection()
    {
        var changes = Loaded(G(@"C:\g\eldenring.exe"));
        changes.PriorityOverride("eldenring.exe").PendingCpu = 3;

        await changes.ApplyAsync();

        Assert.Contains("eldenring.exe", _config.Cfg.ManagedExes);
    }

    [Fact]
    public async Task a_write_that_fails_stays_pending_with_its_error()
    {
        var worker = new InProcessElevatedWorker(new FailingFor("bad.exe", _priorities));
        var changes = Loaded(worker, G(@"C:\g\bad.exe"), G(@"C:\g\good.exe"));
        changes.PriorityOverride("bad.exe").PendingCpu = 3;
        changes.PriorityOverride("good.exe").PendingCpu = 3;

        var outcome = await changes.ApplyAsync();

        Assert.Equal(ApplyStatus.Applied, outcome.Status);
        Assert.True(changes.PriorityOverride("bad.exe").IsDirty);
        Assert.Equal("access denied", changes.PriorityOverride("bad.exe").LastError);
        Assert.False(changes.PriorityOverride("good.exe").IsDirty);
        Assert.Equal(1, changes.Count);
    }

    [Fact]
    public async Task only_exes_written_successfully_are_recorded_as_managed()
    {
        var worker = new InProcessElevatedWorker(new FailingFor("bad.exe", _priorities));
        var changes = Loaded(worker, G(@"C:\g\bad.exe"), G(@"C:\g\good.exe"));
        changes.PriorityOverride("bad.exe").PendingCpu = 3;
        changes.PriorityOverride("good.exe").PendingCpu = 3;

        await changes.ApplyAsync();

        Assert.Contains("good.exe", _config.Cfg.ManagedExes);
        Assert.DoesNotContain("bad.exe", _config.Cfg.ManagedExes);
    }

    [Fact]
    public async Task uac_declined_keeps_priority_overrides_pending()
    {
        var changes = Loaded(TestWorkers.DeclinedUac, G(@"C:\g\eldenring.exe"));
        changes.PriorityOverride("eldenring.exe").PendingCpu = 3;

        var outcome = await changes.ApplyAsync();

        Assert.Equal(new ApplyOutcome(ApplyStatus.UacDeclined, GraphicsPreferencesWritten: false), outcome);
        Assert.Empty(_priorities.Data);
        Assert.Equal(1, changes.Count);
    }

    [Fact]
    public async Task graphics_preference_only_apply_never_asks_for_elevation()
    {
        var worker = new CountingWorker(TestWorkers.DeclinedUac);
        var changes = Loaded(worker, G(CsPath));
        changes.GraphicsPreference(CsPath).PendingGpu = 2;

        var outcome = await changes.ApplyAsync();

        Assert.Equal(new ApplyOutcome(ApplyStatus.Applied, GraphicsPreferencesWritten: true), outcome);
        Assert.Equal("GpuPreference=2;", _store.Get(CsPath));
        Assert.Equal(0, worker.Calls);
        Assert.Equal(0, changes.Count);
    }

    [Fact]
    public async Task declined_uac_still_leaves_graphics_preferences_applied()
    {
        var changes = Loaded(TestWorkers.DeclinedUac, G(CsPath));
        changes.GraphicsPreference(CsPath).PendingGpu = 2;
        changes.PriorityOverride("cs2.exe").PendingCpu = 3;

        var outcome = await changes.ApplyAsync();

        Assert.Equal(new ApplyOutcome(ApplyStatus.UacDeclined, GraphicsPreferencesWritten: true), outcome);
        Assert.Equal("GpuPreference=2;", _store.Get(CsPath));
        Assert.Equal(1, changes.Count); // only the Priority Override is still pending
    }

    [Fact]
    public async Task apply_keeps_foreign_tokens_in_the_graphics_preference_value()
    {
        _store.Set(CsPath, "AutoHDREnable=2097;GpuPreference=1;");
        var changes = Loaded(G(CsPath));
        changes.GraphicsPreference(CsPath).PendingGpu = 2;
        changes.GraphicsPreference(CsPath).PendingWindowed = false;

        await changes.ApplyAsync();

        Assert.Equal("AutoHDREnable=2097;GpuPreference=2;SwapEffectUpgradeEnable=0;", _store.Get(CsPath));
    }

    [Fact]
    public void overrides_matching_no_game_are_other_overrides_flagged_orphaned_by_provenance()
    {
        _priorities.Write("voicemeeterpro.exe", 6, null); // intentional non-game override
        _priorities.Write("uninstalled.exe", 3, null);    // was managed by GPM, game gone
        _config.Cfg.ManagedExes.Add("uninstalled.exe");

        var changes = Loaded(G(CsPath));

        Assert.Equal(2, changes.OtherOverrides.Count);
        Assert.False(changes.OtherOverrides.Single(o => o.State.ExeName == "voicemeeterpro.exe").IsOrphaned);
        Assert.True(changes.OtherOverrides.Single(o => o.State.ExeName == "uninstalled.exe").IsOrphaned);
    }

    [Fact]
    public async Task other_overrides_are_applied_like_any_pending_change()
    {
        _priorities.Write("voicemeeterpro.exe", 6, null);
        var changes = Loaded(G(CsPath));
        var other = changes.OtherOverrides.Single().State;
        other.PendingCpu = null; // remove the override

        await changes.ApplyAsync();

        Assert.Empty(_priorities.Data);
        Assert.False(other.IsDirty);
    }

    // The UI never offers Realtime (4), so forcing it is the simplest batch the
    // Elevated Worker's OpsValidator rejects (ADR-0003).
    private async Task<(PendingChanges changes, ApplyOutcome outcome)> ApplyRejectedBatch()
    {
        var changes = Loaded(G(@"C:\g\eldenring.exe"));
        changes.PriorityOverride("eldenring.exe").PendingCpu = 4;
        return (changes, await changes.ApplyAsync());
    }

    [Fact]
    public async Task apply_rejected_by_the_worker_writes_nothing_and_marks_nothing_managed()
    {
        var (changes, outcome) = await ApplyRejectedBatch();

        Assert.Equal(ApplyStatus.Rejected, outcome.Status);
        Assert.NotNull(outcome.RejectionReason);
        Assert.Empty(_priorities.Data);                         // the worker wrote nothing
        Assert.Equal(1, changes.Count);                         // the change is still pending
        Assert.Null(changes.PriorityOverride("eldenring.exe").LastError); // batch-wide, not per row
        Assert.DoesNotContain("eldenring.exe", _config.Cfg.ManagedExes);
    }
}
