using CommunityToolkit.Mvvm.ComponentModel;
using GPM.Core.Graphics;
using GPM.Core.Models;
using GPM.Core.Persistence;
using GPM.Core.Priority;
using GPM.Core.Worker;

namespace GPM.Core.Changes;

/// One of the Other Overrides; Orphaned when GPM created it for a game no longer detected.
public sealed record OtherOverride(ExeOverrideState State, bool IsOrphaned);

public enum ApplyStatus { NothingToApply, Applied, UacDeclined, Rejected }

/// GraphicsPreferencesWritten: HKCU writes happen before the UAC prompt, so they
/// stand even when the Priority Override batch is declined or rejected.
public sealed record ApplyOutcome(ApplyStatus Status, bool GraphicsPreferencesWritten, string? RejectionReason = null);

/// Every Pending Change GPM tracks, and Apply. Priority Overrides are keyed by exe
/// filename (linked rows share ONE state), Graphics Preferences by full exe path (ADR-0001).
/// Owns every read and write of both, and the provenance behind Orphaned Overrides.
public sealed partial class PendingChanges(
    IPriorityService priorities,
    GraphicsPreferenceService graphics,
    IElevatedWorker worker,
    IConfigStore config) : ObservableObject
{
    private readonly Dictionary<string, ExeOverrideState> _overrides = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, GraphicsPreferenceState> _graphics = new(StringComparer.OrdinalIgnoreCase);

    [ObservableProperty] private int count;

    public IReadOnlyList<OtherOverride> OtherOverrides { get; private set; } = [];

    /// What Windows has now for these games, plus the Other Overrides.
    public sealed class Snapshot
    {
        internal Snapshot(
            Dictionary<string, PriorityOverride> gameOverrides,
            Dictionary<string, GraphicsPreference> gamePrefs,
            List<PriorityOverride> others)
        {
            GameOverrides = gameOverrides;
            GamePrefs = gamePrefs;
            Others = others;
        }

        internal Dictionary<string, PriorityOverride> GameOverrides { get; }
        internal Dictionary<string, GraphicsPreference> GamePrefs { get; }
        internal List<PriorityOverride> Others { get; }
    }

    /// Touches only the registry, so it is safe (and meant) to run off the UI thread.
    /// games.json stays out of it: the UI thread may be saving it at the same moment.
    public Snapshot Read(IEnumerable<Game> games)
    {
        var list = games.ToList();
        var gameOverrides = list.Select(g => g.ExeName).OfType<string>()
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToDictionary(exe => exe, priorities.Read, StringComparer.OrdinalIgnoreCase);
        var gamePrefs = list.Select(g => g.ExePath).OfType<string>()
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToDictionary(p => p, graphics.Read, StringComparer.OrdinalIgnoreCase);
        // Other overrides: everything in IFEO with an override that isn't a detected game
        var others = priorities.ReadAll().Where(o => !gameOverrides.ContainsKey(o.ExeName)).ToList();
        return new Snapshot(gameOverrides, gamePrefs, others);
    }

    /// Replaces every tracked state (pending edits included) with the snapshot. UI thread.
    public void Load(Snapshot snapshot)
    {
        var managed = config.Load().ManagedExes;
        _overrides.Clear();
        _graphics.Clear();
        foreach (var o in snapshot.GameOverrides.Values) Track(o);
        foreach (var (path, pref) in snapshot.GamePrefs) Track(path, pref);
        OtherOverrides = snapshot.Others
            .Select(o => new OtherOverride(Track(o), managed.Contains(o.ExeName)))
            .ToList();
        Recount();
    }

    /// The state every row with this exe filename shares; the exe must be in the loaded snapshot.
    public ExeOverrideState PriorityOverride(string exeName) => _overrides[exeName];

    /// The state of this one exe path; the path must be in the loaded snapshot.
    public GraphicsPreferenceState GraphicsPreference(string exePath) => _graphics[exePath];

    public void Discard()
    {
        foreach (var s in _overrides.Values) s.DiscardPending();
        foreach (var s in _graphics.Values) s.DiscardPending();
    }

    /// Graphics Preferences first (HKCU, no elevation, never the Elevated Worker), then
    /// every pending Priority Override in one batch behind a single UAC prompt.
    /// The registry is the source of truth: everything applied is re-read, so a failed
    /// write simply stays pending.
    public async Task<ApplyOutcome> ApplyAsync()
    {
        var dirtyGraphics = _graphics.Values.Where(s => s.IsDirty).ToList();
        var dirty = _overrides.Values.Where(s => s.IsDirty).ToList();
        if (dirty.Count == 0 && dirtyGraphics.Count == 0) return new(ApplyStatus.NothingToApply, false);

        var wroteGraphics = ApplyGraphics(dirtyGraphics);
        if (dirty.Count == 0) return new(ApplyStatus.Applied, wroteGraphics); // no UAC prompt at all

        var ops = dirty.Select(s => new ApplyOp(s.ExeName, s.PendingCpu, s.PendingIo)).ToList();
        var before = dirty.ToDictionary(s => s.ExeName, s => (s.RegistryCpu, s.RegistryIo));
        ApplyReport? report;
        try { report = await worker.ApplyAsync(ops); }
        catch (Exception e) { report = new ApplyReport(true, e.Message, []); } // ops.json, result.json, process start
        if (report is null) return new(ApplyStatus.UacDeclined, wroteGraphics); // changes stay pending

        foreach (var s in dirty) s.LastError = null;
        // a rejected batch carries no per-item errors: its reason is batch-wide
        if (!report.BatchRejected)
        {
            foreach (var r in report.Results.Where(r => !r.Success))
                if (_overrides.TryGetValue(r.Exe, out var s)) s.LastError = r.Error;
        }

        // Re-read even a rejected batch: a worker that died without its report may
        // have written some ops already, and only the registry knows which.
        foreach (var s in dirty)
        {
            try
            {
                var actual = priorities.Read(s.ExeName);
                s.SetRegistry(actual.Cpu, actual.Io);
            }
            catch (Exception e) { s.LastError ??= e.Message; }
        }
        Recount();

        // provenance (for orphan detection): every exe whose override this Apply
        // changed, including a write that failed halfway
        var changed = dirty.Where(s => (s.RegistryCpu, s.RegistryIo) != before[s.ExeName]).ToList();
        if (changed.Count > 0)
        {
            var cfg = config.Load();
            foreach (var s in changed) cfg.ManagedExes.Add(s.ExeName);
            config.Save(cfg);
        }

        return report.BatchRejected
            ? new(ApplyStatus.Rejected, wroteGraphics, report.BatchError)
            : new(ApplyStatus.Applied, wroteGraphics);
    }

    /// True when at least one Graphics Preference was written.
    private bool ApplyGraphics(List<GraphicsPreferenceState> dirty)
    {
        var wrote = false;
        foreach (var s in dirty)
        {
            try
            {
                graphics.Write(s.ExePath, s.Pending);
                s.LastError = null;
                wrote = true;
            }
            catch (Exception e)
            {
                s.LastError = e.Message;
            }
            try { s.SetActual(graphics.Read(s.ExePath)); }
            catch (Exception e) { s.LastError ??= e.Message; }
        }
        Recount();
        return wrote;
    }

    private ExeOverrideState Track(PriorityOverride actual)
    {
        var state = new ExeOverrideState(actual.ExeName, actual.Cpu, actual.Io) { HasDebugger = actual.HasDebugger };
        state.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(ExeOverrideState.IsDirty)) Recount();
        };
        _overrides[state.ExeName] = state;
        return state;
    }

    private void Track(string exePath, GraphicsPreference actual)
    {
        var state = new GraphicsPreferenceState(exePath, actual);
        state.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(GraphicsPreferenceState.IsDirty)) Recount();
        };
        _graphics[exePath] = state;
    }

    private void Recount() =>
        Count = _overrides.Values.Count(s => s.IsDirty) + _graphics.Values.Count(s => s.IsDirty);
}
