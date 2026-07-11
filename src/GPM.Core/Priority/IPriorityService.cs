namespace GPM.Core.Priority;

/// One IFEO override, keyed by lowercase exe filename.
public sealed record PriorityOverride(string ExeName, int? Cpu, int? Io, bool HasDebugger);

public interface IPriorityService
{
    PriorityOverride Read(string exeName);
    /// All IFEO subkeys that currently carry a CPU or IO override.
    IReadOnlyList<PriorityOverride> ReadAll();
    /// null cpu/io = delete that value. Cleans up empty PerfOptions/exe keys.
    /// Throws ArgumentOutOfRangeException for values outside PriorityValues.
    void Write(string exeName, int? cpu, int? io);
}
