using GPM.Core.Priority;

namespace GPM.Core.Worker;

/// The elevated worker trusts nothing from the UI (ops.json sits in %TEMP% —
/// TOCTOU window while the UAC prompt is up). ANY invalid op rejects the batch.
public static class OpsValidator
{
    public static string? Validate(IReadOnlyList<ApplyOp>? ops)
    {
        if (ops is null || ops.Count == 0) return "empty batch";
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var op in ops)
        {
            if (string.IsNullOrWhiteSpace(op.Exe)) return "missing exe name";
            if (op.Exe.Length > 255) return "exe name too long";
            if (op.Exe.Contains('\\') || op.Exe.Contains('/'))
                return $"path separator in exe name: {op.Exe}";
            if (!op.Exe.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                return $"not an .exe filename: {op.Exe}";
            if (op.Exe.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
                return $"invalid characters in exe name: {op.Exe}";
            if (!seen.Add(op.Exe)) return $"duplicate exe in batch: {op.Exe}";
            if (op.Cpu is int c && !PriorityValues.ValidCpu.Contains(c))
                return $"invalid cpu value {c} for {op.Exe}";
            if (op.Io is int i && !PriorityValues.ValidIo.Contains(i))
                return $"invalid io value {i} for {op.Exe}";
        }
        return null;
    }
}
