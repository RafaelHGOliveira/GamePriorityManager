using CommunityToolkit.Mvvm.ComponentModel;

namespace GPM.Core.Changes;

/// The single shared pending-state object for one exe filename.
/// Every row with this filename holds the SAME instance (spec "IFEO semantics").
public sealed partial class ExeOverrideState : ObservableObject
{
    public ExeOverrideState(string exeName, int? registryCpu, int? registryIo)
    {
        ExeName = exeName.ToLowerInvariant();
        RegistryCpu = registryCpu;
        RegistryIo = registryIo;
        pendingCpu = registryCpu;
        pendingIo = registryIo;
    }

    public string ExeName { get; }
    public int? RegistryCpu { get; private set; }
    public int? RegistryIo { get; private set; }

    /// Informational: this exe's IFEO key carries a Debugger value (not GPM's — never touched).
    public bool HasDebugger { get; init; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsDirty))]
    private int? pendingCpu;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsDirty))]
    private int? pendingIo;

    [ObservableProperty]
    private string? lastError;

    public bool IsDirty => PendingCpu != RegistryCpu || PendingIo != RegistryIo;

    /// Called after re-reading the registry (source of truth). A failed write
    /// leaves the registry unchanged, so the row naturally stays dirty.
    public void SetRegistry(int? cpu, int? io)
    {
        RegistryCpu = cpu;
        RegistryIo = io;
        OnPropertyChanged(nameof(RegistryCpu));
        OnPropertyChanged(nameof(RegistryIo));
        OnPropertyChanged(nameof(IsDirty));
    }

    public void DiscardPending()
    {
        PendingCpu = RegistryCpu;
        PendingIo = RegistryIo;
        LastError = null;
    }
}
