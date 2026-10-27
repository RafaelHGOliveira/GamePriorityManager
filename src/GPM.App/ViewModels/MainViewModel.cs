using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Windows.Data;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GPM.Core.Detection.Steam;
using GPM.Core.Graphics;
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
    IElevatedWorker worker,
    IConfigStore config,
    GraphicsPreferenceService graphics) : ObservableObject
{
    private readonly Dictionary<string, ExeOverrideState> _states = new(StringComparer.OrdinalIgnoreCase);
    // keyed by full exe path: Graphics Preferences are per path, unlike the filename-keyed IFEO states
    private readonly Dictionary<string, GraphicsPreferenceState> _graphics = new(StringComparer.OrdinalIgnoreCase);

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
    [ObservableProperty] private bool showWindowedColumn;
    [ObservableProperty] private bool betaSourcesEnabled;
    [ObservableProperty] private string theme = "auto";

    public string ConfigPath => config.FilePath;

    private bool _loadingSettings;

    partial void OnShowIoColumnChanged(bool value) => SaveSettings();
    partial void OnShowWindowedColumnChanged(bool value) => SaveSettings();
    partial void OnBetaSourcesEnabledChanged(bool value) => SaveSettings();
    partial void OnThemeChanged(string value) => SaveSettings();

    private void SaveSettings()
    {
        if (_loadingSettings) return;
        var cfg = config.Load();
        cfg.ShowIoColumn = ShowIoColumn;
        cfg.ShowWindowedColumn = ShowWindowedColumn;
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

    // The startup load is fire-and-forget (CompositionRoot) and rescan rethrows into
    // the dispatcher, so a failure here must surface in the status bar, not escape.
    public async Task LoadAsync(bool forceScan = false)
    {
        try { await LoadCoreAsync(forceScan); }
        catch (Exception e)
        {
            StatusMessage = string.Format(Strings.LoadFailed, e.Message);
        }
    }

    private async Task LoadCoreAsync(bool forceScan)
    {
        var version = ++_loadVersion;
        var cfg = config.Load();
        _loadingSettings = true;
        ShowIoColumn = cfg.ShowIoColumn;
        ShowWindowedColumn = cfg.ShowWindowedColumn;
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
        _graphics.Clear();
        foreach (var game in snapshot.Games.OrderBy(g => g.Name, StringComparer.OrdinalIgnoreCase))
        {
            ExeOverrideState? state = null;
            if (game.ExeName is string exe)
            {
                state = GetOrCreateState(snapshot.GameOverrides[exe]);
            }
            var gfx = game.ExePath is string path ? GetOrCreateGraphicsState(path, snapshot.GamePrefs[path]) : null;
            var row = new GameRowViewModel(game, state, state is null ? 0 : linkCounts[game.ExeName!], gfx);
            if (state is not null) state.PropertyChanged += (_, _) => row.NotifyDirtyChanged();
            if (gfx is not null) gfx.PropertyChanged += (_, _) => row.NotifyDirtyChanged();
            _rows.Add(row);
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
        Dictionary<string, GraphicsPreference> GamePrefs,
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

        var gamePrefs = games.Select(g => g.ExePath).OfType<string>()
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToDictionary(p => p, graphics.Read, StringComparer.OrdinalIgnoreCase);

        // Other overrides: everything in IFEO with an override that isn't a detected game
        var others = priorities.ReadAll().Where(o => !gameOverrides.ContainsKey(o.ExeName)).ToList();

        return new LoadSnapshot(cached, cacheChanged, games, gameOverrides, gamePrefs, others);
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

    private GraphicsPreferenceState GetOrCreateGraphicsState(string exePath, GraphicsPreference actual)
    {
        if (_graphics.TryGetValue(exePath, out var existing)) return existing;
        var state = new GraphicsPreferenceState(exePath, actual);
        state.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(GraphicsPreferenceState.IsDirty)) RecountPending();
        };
        _graphics[exePath] = state;
        return state;
    }

    private void RecountPending() =>
        PendingCount = _states.Values.Count(s => s.IsDirty) + _graphics.Values.Count(s => s.IsDirty);

    [RelayCommand]
    private void Discard()
    {
        foreach (var s in _states.Values) s.DiscardPending();
        foreach (var s in _graphics.Values) s.DiscardPending();
        StatusMessage = null;
    }

    /// HKCU, so it needs no elevation: written by the app itself, never by the elevated worker.
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
            // registry is the source of truth: re-read; a failed write stays dirty naturally
            try { s.SetActual(graphics.Read(s.ExePath)); }
            catch (Exception e) { s.LastError ??= e.Message; }
        }
        RecountPending();
    }

    [RelayCommand]
    private async Task ApplyAsync()
    {
        var dirtyGraphics = _graphics.Values.Where(s => s.IsDirty).ToList();
        var dirty = _states.Values.Where(s => s.IsDirty).ToList();
        if (dirty.Count == 0 && dirtyGraphics.Count == 0) return;

        ApplyGraphics(dirtyGraphics);
        if (dirty.Count == 0)
        {
            StatusMessage = null; // no CPU/IO change, so no UAC prompt at all
            return;
        }

        var ops = dirty.Select(s => new ApplyOp(s.ExeName, s.PendingCpu, s.PendingIo)).ToList();
        var report = await worker.ApplyAsync(ops);
        if (report is null)
        {
            // changes stay pending (spec "Error handling"); GPU changes were already applied
            StatusMessage = dirtyGraphics.Count > 0 ? Strings.ApplyCancelledGpuApplied : Strings.ApplyCancelled;
            return;
        }

        // attach per-item errors; a rejected batch carries none
        if (!report.BatchRejected)
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
