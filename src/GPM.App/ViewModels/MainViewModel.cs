using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Windows.Data;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GPM.Core.Changes;
using GPM.Core.Detection.Steam;
using GPM.Core.Models;
using GPM.Core.Persistence;
using GPM.Core.Resolve;

namespace GPM.App.ViewModels;

public sealed partial class MainViewModel : ObservableObject
{
    private readonly Func<Task<IReadOnlyList<Game>>> _scanGames;
    private readonly IExeResolver _resolver;
    private readonly IConfigStore _config;
    private readonly PendingChanges _changes;

    public MainViewModel(
        Func<Task<IReadOnlyList<Game>>> scanGames,
        IExeResolver resolver,
        IConfigStore config,
        PendingChanges changes)
    {
        _scanGames = scanGames;
        _resolver = resolver;
        _config = config;
        _changes = changes;
        changes.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(PendingChanges.Count)) OnPropertyChanged(nameof(PendingCount));
        };
    }

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

    public int PendingCount => _changes.Count;
    [ObservableProperty] private string? statusMessage;

    [ObservableProperty] private bool showIoColumn;
    [ObservableProperty] private bool showWindowedColumn;
    [ObservableProperty] private bool betaSourcesEnabled;
    [ObservableProperty] private string theme = "auto";

    public string ConfigPath => _config.FilePath;

    private bool _loadingSettings;

    partial void OnShowIoColumnChanged(bool value) => SaveSettings();
    partial void OnShowWindowedColumnChanged(bool value) => SaveSettings();
    partial void OnBetaSourcesEnabledChanged(bool value) => SaveSettings();
    partial void OnThemeChanged(string value) => SaveSettings();

    private void SaveSettings()
    {
        if (_loadingSettings) return;
        var cfg = _config.Load();
        cfg.ShowIoColumn = ShowIoColumn;
        cfg.ShowWindowedColumn = ShowWindowedColumn;
        cfg.BetaSourcesEnabled = BetaSourcesEnabled;
        cfg.Theme = Theme;
        _config.Save(cfg);
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
        var cfg = _config.Load();
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
            detected = (await _scanGames()).ToList();
            StatusMessage = null;
        }

        // Exe resolution walks every install dir and the IFEO reads hit the registry:
        // both are far too slow for the UI thread.
        var snapshot = await Task.Run(() => BuildSnapshot(cfg, detected, fromCache));
        if (version != _loadVersion) return; // a newer load superseded this one

        if (snapshot.CacheChanged)
        {
            var latest = _config.Load(); // settings may have changed while we were off-thread
            latest.DetectedGames = snapshot.Detected;
            _config.Save(latest);
        }

        // rows sharing a filename are Linked Rows (PendingChanges hands them ONE state)
        var linkCounts = snapshot.Games.Where(g => g.ExeName is not null)
            .GroupBy(g => g.ExeName!, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.Count(), StringComparer.OrdinalIgnoreCase);

        _changes.Load(snapshot.Changes);
        _rows.Clear();
        foreach (var game in snapshot.Games.OrderBy(g => g.Name, StringComparer.OrdinalIgnoreCase))
        {
            var state = game.ExeName is string exe ? _changes.PriorityOverride(exe) : null;
            var gfx = game.ExePath is string path ? _changes.GraphicsPreference(path) : null;
            var row = new GameRowViewModel(game, state, state is null ? 0 : linkCounts[game.ExeName!], gfx);
            if (state is not null) state.PropertyChanged += (_, _) => row.NotifyDirtyChanged();
            if (gfx is not null) gfx.PropertyChanged += (_, _) => row.NotifyDirtyChanged();
            _rows.Add(row);
        }

        OtherOverrides.Clear();
        foreach (var o in _changes.OtherOverrides)
            OtherOverrides.Add(new OverrideRowViewModel(o.State, o.IsOrphaned, o.State.HasDebugger));

        VisibleRows.Refresh(); // _rows is a plain list; the view has to be told it changed
    }

    private sealed record LoadSnapshot(
        List<Game> Detected,
        bool CacheChanged,
        List<Game> Games,
        PendingChanges.Snapshot Changes);

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

        return new LoadSnapshot(cached, cacheChanged, games, _changes.Read(games));
    }

    private static Game? TryApplyChoice(GpmConfig cfg, Game g) =>
        cfg.ExeChoices.TryGetValue(ExeChoiceKey(g), out var chosen) && File.Exists(chosen)
            ? g with { ExePath = chosen, NeedsExeConfirmation = false, ExeCandidates = [] }
            : null;

    private Game ApplyResolution(Game g)
    {
        var r = _resolver.Resolve(g);
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
        var cfg = _config.Load();
        cfg.ManualGames.Add(new Game
        {
            Name = Path.GetFileNameWithoutExtension(exePath),
            Store = GameStore.Manual,
            InstallDir = Path.GetDirectoryName(exePath),
            ExePath = exePath,
        });
        _config.Save(cfg);
        await LoadAsync();
    }

    public async Task RemoveManualGameAsync(GameRowViewModel row)
    {
        if (row.Game.Store != GameStore.Manual) return;
        var cfg = _config.Load();
        cfg.ManualGames.RemoveAll(g => g.ExePath == row.Game.ExePath);
        _config.Save(cfg);
        await LoadAsync();
    }

    public async Task ConfirmExeAsync(GameRowViewModel row, string exePath)
    {
        var cfg = _config.Load();
        cfg.ExeChoices[ExeChoiceKey(row.Game)] = exePath;
        _config.Save(cfg);
        await LoadAsync();
    }

    [RelayCommand]
    private void Discard()
    {
        _changes.Discard();
        StatusMessage = null;
    }

    [RelayCommand]
    private async Task ApplyAsync()
    {
        var outcome = await _changes.ApplyAsync();
        StatusMessage = outcome.Status switch
        {
            ApplyStatus.NothingToApply => StatusMessage,
            // changes stay pending (spec "Error handling")
            ApplyStatus.UacDeclined => outcome.GraphicsPreferencesWritten
                ? Strings.ApplyCancelledGpuApplied
                : Strings.ApplyCancelled,
            _ => null,
        };
    }
}
