using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Text.Json;
using System.Windows.Data;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GPM.App.Services;
using GPM.Core.Detection.Steam;
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

    private readonly List<GameRowViewModel> _rows = [];
    private ListCollectionView? _visibleRows;
    private int _loadVersion;

    public IReadOnlyList<GameRowViewModel> Rows => _rows;
    public ObservableCollection<OverrideRowViewModel> OtherOverrides { get; } = [];

    [ObservableProperty]
    private string searchText = "";

    // Filtering a view keeps the ListView's containers; swapping in a new list per
    // keystroke made it rebuild every row.
    partial void OnSearchTextChanged(string value) => VisibleRows.Refresh();

    [ObservableProperty] private int pendingCount;
    [ObservableProperty] private string? statusMessage;

    [ObservableProperty] private bool showIoColumn;
    [ObservableProperty] private bool betaSourcesEnabled;
    [ObservableProperty] private string theme = "auto";

    public string ConfigPath => config.FilePath;

    private bool _loadingSettings;

    partial void OnShowIoColumnChanged(bool value) => SaveSettings();
    partial void OnBetaSourcesEnabledChanged(bool value) => SaveSettings();
    partial void OnThemeChanged(string value) => SaveSettings();

    private void SaveSettings()
    {
        if (_loadingSettings) return;
        var cfg = config.Load();
        cfg.ShowIoColumn = ShowIoColumn;
        cfg.BetaSourcesEnabled = BetaSourcesEnabled;
        cfg.Theme = Theme;
        config.Save(cfg);
    }

    [RelayCommand]
    private Task RescanAsync() => LoadAsync(forceScan: true);

    public ICollectionView VisibleRows => _visibleRows ??= new ListCollectionView(_rows)
    {
        Filter = o => string.IsNullOrWhiteSpace(SearchText) ||
                      ((GameRowViewModel)o).Game.Name.Contains(SearchText, StringComparison.OrdinalIgnoreCase),
    };

    public async Task LoadAsync(bool forceScan = false)
    {
        var version = ++_loadVersion;
        var cfg = config.Load();
        _loadingSettings = true;
        ShowIoColumn = cfg.ShowIoColumn;
        BetaSourcesEnabled = cfg.BetaSourcesEnabled;
        Theme = cfg.Theme;
        _loadingSettings = false;

        var fromCache = !forceScan && cfg.DetectedGames.Count > 0; // spec: cached in games.json
        var detected = cfg.DetectedGames;
        if (!fromCache)
        {
            StatusMessage = string.Format(Strings.ScanProgress, "stores"); // simplified per-store progress
            detected = (await scanGames()).ToList();
            StatusMessage = null;
        }

        // Exe resolution walks every install dir and the IFEO reads hit the registry:
        // both are far too slow for the UI thread.
        var snapshot = await Task.Run(() => BuildSnapshot(cfg, detected, fromCache));
        if (version != _loadVersion) return; // a newer load superseded this one

        if (snapshot.CacheChanged)
        {
            var latest = config.Load(); // settings may have changed while we were off-thread
            latest.DetectedGames = snapshot.Detected;
            config.Save(latest);
        }

        // group rows by filename so linked rows share ONE state
        var linkCounts = snapshot.Games.Where(g => g.ExeName is not null)
            .GroupBy(g => g.ExeName!, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.Count(), StringComparer.OrdinalIgnoreCase);

        _rows.Clear();
        _states.Clear();
        foreach (var game in snapshot.Games.OrderBy(g => g.Name, StringComparer.OrdinalIgnoreCase))
        {
            ExeOverrideState? state = null;
            if (game.ExeName is string exe)
            {
                state = GetOrCreateState(snapshot.GameOverrides[exe]);
            }
            _rows.Add(new GameRowViewModel(game, state, state is null ? 0 : linkCounts[game.ExeName!]));
        }

        OtherOverrides.Clear();
        foreach (var o in snapshot.OtherOverrides)
        {
            var state = GetOrCreateState(o);
            OtherOverrides.Add(new OverrideRowViewModel(state,
                isOrphaned: cfg.ManagedExes.Contains(o.ExeName),
                hasDebugger: o.HasDebugger));
        }

        RecountPending();
        VisibleRows.Refresh(); // _rows is a plain list; the view has to be told it changed
    }

    private sealed record LoadSnapshot(
        List<Game> Detected,
        bool CacheChanged,
        List<Game> Games,
        Dictionary<string, PriorityOverride> GameOverrides,
        List<PriorityOverride> OtherOverrides);

    /// Runs off the UI thread: touches only the file system, the registry and its arguments.
    private LoadSnapshot BuildSnapshot(GpmConfig cfg, List<Game> detected, bool fromCache)
    {
        var cacheChanged = !fromCache;

        // Non-game Steam tools (redistributables, runtimes, Proton) may still sit in an
        // older games.json cache that was written before they were filtered at scan time.
        // Drop them here too so they never appear regardless of cache vs. fresh scan, and
        // heal the cache so it only happens once.
        var withoutTools = detected.Where(g => !SteamManifests.IsNonGameApp(g.Name)).ToList();
        if (withoutTools.Count != detected.Count) cacheChanged = true;

        // The resolved exe is cached in games.json, so the install-dir walk only runs
        // after a rescan, or when the cached exe is gone (game updated, moved, uninstalled).
        var cached = new List<Game>();
        var games = new List<Game>();
        foreach (var original in withoutTools)
        {
            var g = original;
            if (fromCache && g.ExePath is not null && !File.Exists(g.ExePath))
            {
                g = g with { ExePath = null, NeedsExeConfirmation = false, ExeCandidates = [] };
                cacheChanged = true;
            }
            if (TryApplyChoice(cfg, g) is { } chosen)
            {
                cached.Add(g);
                games.Add(chosen);
                continue;
            }
            if (g.ExePath is null)
            {
                g = ApplyResolution(g);
                cacheChanged |= g.ExePath is not null;
            }
            cached.Add(g);
            games.Add(g);
        }
        games.AddRange(cfg.ManualGames.Select(g => TryApplyChoice(cfg, g) ?? ApplyResolution(g)));

        var gameOverrides = games.Select(g => g.ExeName).OfType<string>()
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToDictionary(exe => exe, priorities.Read, StringComparer.OrdinalIgnoreCase);

        // Other overrides: everything in IFEO with an override that isn't a detected game
        var others = priorities.ReadAll().Where(o => !gameOverrides.ContainsKey(o.ExeName)).ToList();

        return new LoadSnapshot(cached, cacheChanged, games, gameOverrides, others);
    }

    private static Game? TryApplyChoice(GpmConfig cfg, Game g) =>
        cfg.ExeChoices.TryGetValue(ExeChoiceKey(g), out var chosen) && File.Exists(chosen)
            ? g with { ExePath = chosen, NeedsExeConfirmation = false, ExeCandidates = [] }
            : null;

    private Game ApplyResolution(Game g)
    {
        var r = resolver.Resolve(g);
        return g with
        {
            ExePath = r.ExePath ?? g.ExePath,
            NeedsExeConfirmation = r.NeedsConfirmation,
            ExeCandidates = r.Candidates,
        };
    }

    private static string ExeChoiceKey(Game g) => $"{g.Store}:{g.StoreId ?? g.InstallDir ?? g.Name}";

    public async Task AddManualGameAsync(string exePath)
    {
        var cfg = config.Load();
        cfg.ManualGames.Add(new Game
        {
            Name = Path.GetFileNameWithoutExtension(exePath),
            Store = GameStore.Manual,
            InstallDir = Path.GetDirectoryName(exePath),
            ExePath = exePath,
        });
        config.Save(cfg);
        await LoadAsync();
    }

    public async Task RemoveManualGameAsync(GameRowViewModel row)
    {
        if (row.Game.Store != GameStore.Manual) return;
        var cfg = config.Load();
        cfg.ManualGames.RemoveAll(g => g.ExePath == row.Game.ExePath);
        config.Save(cfg);
        await LoadAsync();
    }

    public async Task ConfirmExeAsync(GameRowViewModel row, string exePath)
    {
        var cfg = config.Load();
        cfg.ExeChoices[ExeChoiceKey(row.Game)] = exePath;
        config.Save(cfg);
        await LoadAsync();
    }

    private ExeOverrideState GetOrCreateState(PriorityOverride actual)
    {
        if (_states.TryGetValue(actual.ExeName, out var existing)) return existing;
        var state = new ExeOverrideState(actual.ExeName, actual.Cpu, actual.Io) { HasDebugger = actual.HasDebugger };
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
