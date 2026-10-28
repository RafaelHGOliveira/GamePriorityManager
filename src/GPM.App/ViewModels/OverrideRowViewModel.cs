using GPM.Core.Changes;

namespace GPM.App.ViewModels;

/// A row in the "Other overrides" section (spec "UI").
public sealed class OverrideRowViewModel(ExeOverrideState state, bool isOrphaned, bool hasDebugger)
{
    public ExeOverrideState State { get; } = state;
    /// GPM created it (provenance in games.json) but the game is gone -> cleanup suggested.
    public bool IsOrphaned { get; } = isOrphaned;
    /// Informational only — GPM never touches Debugger values.
    public bool HasDebugger { get; } = hasDebugger;
}
