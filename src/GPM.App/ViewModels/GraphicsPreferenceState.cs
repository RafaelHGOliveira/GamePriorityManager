using CommunityToolkit.Mvvm.ComponentModel;
using GPM.Core.Graphics;

namespace GPM.App.ViewModels;

/// Pending-state object for ONE exe path. Unlike ExeOverrideState (shared by filename),
/// Graphics Preferences are per path, so linked rows keep independent instances.
public sealed partial class GraphicsPreferenceState : ObservableObject
{
    public GraphicsPreferenceState(string exePath, GraphicsPreference actual)
    {
        ExePath = exePath;
        Actual = actual;
        pendingGpu = actual.Gpu;
        pendingWindowed = actual.WindowedOptimizations;
    }

    public string ExePath { get; }
    public GraphicsPreference Actual { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsDirty))]
    private int? pendingGpu;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsDirty))]
    private bool? pendingWindowed;

    [ObservableProperty]
    private string? lastError;

    public bool IsDirty => PendingGpu != Actual.Gpu || PendingWindowed != Actual.WindowedOptimizations;

    public GraphicsPreference Pending => new(PendingGpu, PendingWindowed);

    /// Called after re-reading the registry (source of truth); a failed write leaves it dirty.
    public void SetActual(GraphicsPreference actual)
    {
        Actual = actual;
        OnPropertyChanged(nameof(Actual));
        OnPropertyChanged(nameof(IsDirty));
    }

    public void DiscardPending()
    {
        PendingGpu = Actual.Gpu;
        PendingWindowed = Actual.WindowedOptimizations;
        LastError = null;
    }
}
