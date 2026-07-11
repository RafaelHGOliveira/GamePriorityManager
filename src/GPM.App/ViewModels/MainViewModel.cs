using System.Collections.ObjectModel;
using System.IO;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GPM.App.Services;
using GPM.Core.Models;
using GPM.Core.Persistence;
using GPM.Core.Priority;
using GPM.Core.Resolve;
using GPM.Core.Worker;

namespace GPM.App.ViewModels;

public sealed partial class MainViewModel(
    Func<Task<IReadOnlyList<Game>>> scanGames,
    IExeResolver resolver,
    IPriorityService priorities,
    IElevationLauncher elevation,
    IConfigStore config) : ObservableObject
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly Dictionary<string, ExeOverrideState> _states = new(StringComparer.OrdinalIgnoreCase);

    public ObservableCollection<GameRowViewModel> Rows { get; } = [];
    public ObservableCollection<OverrideRowViewModel> OtherOverrides { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(VisibleRows))]
    private string searchText = "";

    [ObservableProperty] private int pendingCount;
    [ObservableProperty] private string? statusMessage;

    public IReadOnlyList<GameRowViewModel> VisibleRows =>
        string.IsNullOrWhiteSpace(SearchText)
            ? Rows.ToList()
            : Rows.Where(r => r.Game.Name.Contains(SearchText, StringComparison.OrdinalIgnoreCase)).ToList();

    public async Task LoadAsync()
    {
        var cfg = config.Load();
        var games = (await scanGames()).Concat(cfg.ManualGames).ToList();

        // resolve exes, then group rows by filename so linked rows share ONE state
        var resolved = games.Select(g =>
        {
            var r = resolver.Resolve(g);
            return g with
            {
                ExePath = r.ExePath ?? g.ExePath,
                NeedsExeConfirmation = r.NeedsConfirmation,
                ExeCandidates = r.Candidates,
            };
        }).ToList();

        var linkCounts = resolved.Where(g => g.ExeName is not null)
            .GroupBy(g => g.ExeName!, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.Count(), StringComparer.OrdinalIgnoreCase);

        Rows.Clear();
        _states.Clear();
        foreach (var game in resolved.OrderBy(g => g.Name, StringComparer.OrdinalIgnoreCase))
        {
            ExeOverrideState? state = null;
            if (game.ExeName is string exe)
            {
                state = GetOrCreateState(exe);
            }
            Rows.Add(new GameRowViewModel(game, state, state is null ? 0 : linkCounts[game.ExeName!]));
        }

        // Other overrides: everything in IFEO with an override that isn't a detected game
        OtherOverrides.Clear();
        var gameExes = new HashSet<string>(_states.Keys, StringComparer.OrdinalIgnoreCase);
        foreach (var o in priorities.ReadAll().Where(o => !gameExes.Contains(o.ExeName)))
        {
            var state = GetOrCreateState(o.ExeName);
            OtherOverrides.Add(new OverrideRowViewModel(state,
                isOrphaned: cfg.ManagedExes.Contains(o.ExeName),
                hasDebugger: o.HasDebugger));
        }

        RecountPending();
    }

    private ExeOverrideState GetOrCreateState(string exeName)
    {
        if (_states.TryGetValue(exeName, out var existing)) return existing;
        var actual = priorities.Read(exeName);
        var state = new ExeOverrideState(exeName, actual.Cpu, actual.Io) { HasDebugger = actual.HasDebugger };
        state.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(ExeOverrideState.IsDirty)) RecountPending();
        };
        _states[state.ExeName] = state;
        return state;
    }

    private void RecountPending() => PendingCount = _states.Values.Count(s => s.IsDirty);

    [RelayCommand]
    private void Discard()
    {
        foreach (var s in _states.Values) s.DiscardPending();
        StatusMessage = null;
    }

    [RelayCommand]
    private async Task ApplyAsync()
    {
        var dirty = _states.Values.Where(s => s.IsDirty).ToList();
        if (dirty.Count == 0) return;

        var ops = dirty.Select(s => new ApplyOp(s.ExeName, s.PendingCpu, s.PendingIo)).ToList();
        var opsPath = Path.Combine(Path.GetTempPath(), $"gpm-ops-{Guid.NewGuid():N}.json");
        await File.WriteAllTextAsync(opsPath, JsonSerializer.Serialize(ops, Json));

        var exit = await elevation.RunApplyAsync(opsPath);
        if (exit is null)
        {
            StatusMessage = Strings.ApplyCancelled; // changes stay pending (spec "Error handling")
            return;
        }

        // attach per-item errors from result.json when present
        var resultPath = Path.ChangeExtension(opsPath, ".result.json");
        if (File.Exists(resultPath) &&
            JsonSerializer.Deserialize<ApplyReport>(await File.ReadAllTextAsync(resultPath), Json)
                is { BatchRejected: false } report)
        {
            foreach (var r in report.Results.Where(r => !r.Success))
                if (_states.TryGetValue(r.Exe, out var s)) s.LastError = r.Error;
        }

        // registry is the source of truth: re-read; failed rows stay dirty naturally
        foreach (var s in dirty)
        {
            var actual = priorities.Read(s.ExeName);
            s.SetRegistry(actual.Cpu, actual.Io);
        }
        RecountPending();
        StatusMessage = null;

        // provenance: remember every exe GPM has managed (for orphan detection)
        var cfg = config.Load();
        foreach (var op in ops) cfg.ManagedExes.Add(op.Exe);
        config.Save(cfg);
    }
}
