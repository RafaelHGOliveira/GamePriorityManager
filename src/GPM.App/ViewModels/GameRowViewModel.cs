using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using GPM.Core.Models;

namespace GPM.App.ViewModels;

public sealed class GameRowViewModel(Game game, ExeOverrideState? state, int linkedCount) : ObservableObject
{
    private static readonly HashSet<string> GenericNames = new(StringComparer.OrdinalIgnoreCase)
        { "game.exe", "launcher.exe", "start.exe", "main.exe", "app.exe", "run.exe" };

    private ImageSource? _icon;
    private bool _iconRequested;

    public Game Game { get; } = game;
    /// null while the exe is unresolved (NeedsExeConfirmation without candidates).
    public ExeOverrideState? State { get; } = state;
    public bool IsLinked { get; } = linkedCount > 1;
    public bool IsManual => Game.Store == GameStore.Manual;
    public bool NeedsExeConfirmation => Game.NeedsExeConfirmation;
    public bool HasGenericExeName { get; } =
        state is not null && GenericNames.Contains(state.ExeName);
    public string GenericExeWarning =>
        State is null ? "" : string.Format(Strings.GenericExeWarning, State.ExeName);

    /// Loaded on first read, i.e. only once the (virtualized) list realizes this row.
    public ImageSource? Icon
    {
        get
        {
            if (!_iconRequested)
            {
                _iconRequested = true;
                var task = ExeIcons.GetAsync(Game.ExePath);
                if (task.IsCompletedSuccessfully) _icon = task.Result;
                else _ = LoadIconAsync(task);
            }
            return _icon;
        }
    }

    private async Task LoadIconAsync(Task<ImageSource?> task)
    {
        _icon = await task;
        if (_icon is not null) OnPropertyChanged(nameof(Icon));
    }
}
