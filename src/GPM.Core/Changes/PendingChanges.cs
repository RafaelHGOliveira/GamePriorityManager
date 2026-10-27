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
            List<PriorityOverride> others,
            HashSet<string> managedExes)
        {
            GameOverrides = gameOverrides;
            GamePrefs = gamePrefs;
            Others = others;
            ManagedExes = managedExes;
        }

        internal Dictionary<string, PriorityOverride> GameOverrides { get; }
        internal Dictionary<string, GraphicsPreference> GamePrefs { get; }
        internal List<PriorityOverride> Others { get; }
        internal HashSet<string> ManagedExes { get; }
    }

    /// Touches only the registry and games.json, so it is safe (and meant) to run off the UI thread.
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
        return new Snapshot(gameOverrides, gamePrefs, others, config.Load().ManagedExes);
    }

    /// Replaces every tracked state (pending edits included) with the snapshot.
    public void Load(Snapshot snapshot)
    {
        _overrides.Clear();
        _graphics.Clear();
        foreach (var o in snapshot.GameOverrides.Values) Track(o);
        foreach (var (path, pref) in snapshot.GamePrefs) Track(path, pref);
        OtherOverrides = snapshot.Others
            .Select(o => new OtherOverride(Track(o), snapshot.ManagedExes.Contains(o.ExeName)))
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

        ApplyGraphics(dirtyGraphics);
        var wroteGraphics = dirtyGraphics.Count > 0;
        if (dirty.Count == 0) return new(ApplyStatus.Applied, wroteGraphics); // no UAC prompt at all

        var ops = dirty.Select(s => new ApplyOp(s.ExeName, s.PendingCpu, s.PendingIo)).ToList();
        var report = await worker.ApplyAsync(ops);
        if (report is null) return new(ApplyStatus.UacDeclined, wroteGraphics); // changes stay pending

        // attach per-item errors; a rejected batch carries none
        if (!report.BatchRejected)
        {
            foreach (var r in report.Results.Where(r => !r.Success))
                if (_overrides.TryGetValue(r.Exe, out var s)) s.LastError = r.Error;
        }

        foreach (var s in dirty)
        {
            var actual = priorities.Read(s.ExeName);
            s.SetRegistry(actual.Cpu, actual.Io);
        }
        Recount();

        // provenance: remember every exe GPM has managed (for orphan detection)
        var cfg = config.Load();
        foreach (var op in ops) cfg.ManagedExes.Add(op.Exe);
        config.Save(cfg);

        return report.BatchRejected
            ? new(ApplyStatus.Rejected, wroteGraphics, report.BatchError)
            : new(ApplyStatus.Applied, wroteGraphics);
    }

    private void ApplyGraphics(List<GraphicsPreferenceState> dirty)
    {
        foreach (var s in dirty)
        {
            try
            {
                graphics.Write(s.ExePath, s.Pending);
                s.LastError = null;
            }
            catch (Exception e)
            {
                s.LastError = e.Message;
            }
            try { s.SetActual(graphics.Read(s.ExePath)); }
            catch (Exception e) { s.LastError ??= e.Message; }
        }
        Recount();
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
