namespace GPM.Core.Priority;

public sealed class InMemoryPriorityService : IPriorityService
{
    public Dictionary<string, (int? Cpu, int? Io)> Data { get; } = new(StringComparer.OrdinalIgnoreCase);

    public PriorityOverride Read(string exeName) =>
        Data.TryGetValue(exeName, out var v)
            ? new(exeName.ToLowerInvariant(), v.Cpu, v.Io, false)
            : new(exeName.ToLowerInvariant(), null, null, false);

    public IReadOnlyList<PriorityOverride> ReadAll() =>
        Data.Where(kv => kv.Value.Cpu is not null || kv.Value.Io is not null)
            .Select(kv => new PriorityOverride(kv.Key.ToLowerInvariant(), kv.Value.Cpu, kv.Value.Io, false))
            .ToList();

    public void Write(string exeName, int? cpu, int? io)
    {
        if (cpu is int c && !PriorityValues.ValidCpu.Contains(c))
            throw new ArgumentOutOfRangeException(nameof(cpu), c, "value not allowed");
        if (io is int i && !PriorityValues.ValidIo.Contains(i))
            throw new ArgumentOutOfRangeException(nameof(io), i, "value not allowed");
        if (cpu is null && io is null) Data.Remove(exeName);
        else Data[exeName] = (cpu, io);
    }
}
