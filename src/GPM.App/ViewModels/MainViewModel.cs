using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Data;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GPM.Core.Changes;
using GPM.Core.Library;
using GPM.Core.Persistence;

namespace GPM.App.ViewModels;

public sealed partial class MainViewModel : ObservableObject
{
    private readonly GameLibrary _library;
    private readonly IConfigStore _config;
    private readonly PendingChanges _changes;

    public MainViewModel(
        GameLibrary library,
        IConfigStore config,
        PendingChanges changes)
    {
        _library = library;
        _config = config;
        _changes = changes;
        changes.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(PendingChanges.Count)) OnPropertyChanged(nameof(PendingCount));
        };
    }

    private readonly List<GameRowViewModel> _rows = [];
    private ListCollectionView? _visibleRows;

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
        var cfg = _config.Load();
        _loadingSettings = true;
        ShowIoColumn = cfg.ShowIoColumn;
        ShowWindowedColumn = cfg.ShowWindowedColumn;
        BetaSourcesEnabled = cfg.BetaSourcesEnabled;
        Theme = cfg.Theme;
        _loadingSettings = false;

        var scanned = false;
        var games = await _library.LoadAsync(forceScan, () =>
        {
            scanned = true;
            StatusMessage = string.Format(Strings.ScanProgress, "stores"); // simplified per-store progress
        });
        if (scanned) StatusMessage = null;
        // null / false: a newer load superseded this one
        if (games is null || !await _changes.LoadAsync(games)) return;

        // rows sharing a filename are Linked Rows (PendingChanges hands them ONE state)
        var linkCounts = games.Where(g => g.ExeName is not null)
            .GroupBy(g => g.ExeName!, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.Count(), StringComparer.OrdinalIgnoreCase);

        _rows.Clear();
        foreach (var game in games.OrderBy(g => g.Name, StringComparer.OrdinalIgnoreCase))
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

    public async Task AddManualGameAsync(string exePath)
    {
        _library.AddManual(exePath);
        await LoadAsync();
    }

    public async Task RemoveManualGameAsync(GameRowViewModel row)
    {
        if (_library.RemoveManual(row.Game)) await LoadAsync();
    }

    public async Task ConfirmExeAsync(GameRowViewModel row, string exePath)
    {
        _library.ChooseExe(row.Game, exePath);
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
            ApplyStatus.Rejected => string.Format(outcome.GraphicsPreferencesWritten
                ? Strings.ApplyRejectedGpuApplied
                : Strings.ApplyRejected, outcome.RejectionReason),
            _ => null,
        };
    }
}
