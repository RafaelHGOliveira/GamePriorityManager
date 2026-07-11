using GPM.Core.Models;

namespace GPM.App.ViewModels;

public sealed class GameRowViewModel(Game game, ExeOverrideState? state, int linkedCount)
{
    private static readonly HashSet<string> GenericNames = new(StringComparer.OrdinalIgnoreCase)
        { "game.exe", "launcher.exe", "start.exe", "main.exe", "app.exe", "run.exe" };

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
}
