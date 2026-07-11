using GPM.Core.Registry;

namespace GPM.Core.Priority;

public sealed class PriorityService(IRegistryRoot ifeo) : IPriorityService
{
    public PriorityOverride Read(string exeName)
    {
        var perf = $@"{exeName}\{PriorityValues.PerfOptions}";
        return new PriorityOverride(
            exeName.ToLowerInvariant(),
            ifeo.GetDword(perf, PriorityValues.CpuValueName),
            ifeo.GetDword(perf, PriorityValues.IoValueName),
            HasDebugger: ifeo.GetValueNames(exeName)
                .Contains("Debugger", StringComparer.OrdinalIgnoreCase));
    }

    public IReadOnlyList<PriorityOverride> ReadAll() =>
        ifeo.GetSubKeyNames()
            .Select(Read)
            .Where(o => o.Cpu is not null || o.Io is not null)
            .ToList();

    public void Write(string exeName, int? cpu, int? io)
    {
        if (cpu is int c && !PriorityValues.ValidCpu.Contains(c))
            throw new ArgumentOutOfRangeException(nameof(cpu), c, "value not allowed");
        if (io is int i && !PriorityValues.ValidIo.Contains(i))
            throw new ArgumentOutOfRangeException(nameof(io), i, "value not allowed");

        var perf = $@"{exeName}\{PriorityValues.PerfOptions}";
        Apply(perf, PriorityValues.CpuValueName, cpu);
        Apply(perf, PriorityValues.IoValueName, io);
        ifeo.DeleteSubKeyIfEmpty(perf);      // leave the registry as found
        ifeo.DeleteSubKeyIfEmpty(exeName);
    }

    private void Apply(string perfKey, string valueName, int? value)
    {
        if (value is null) ifeo.DeleteValue(perfKey, valueName);
        else ifeo.SetDword(perfKey, valueName, value.Value);
    }
}
