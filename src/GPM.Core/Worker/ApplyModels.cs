namespace GPM.Core.Worker;

/// ops.json is a JSON array of ApplyOp (camelCase): [{"exe":"x.exe","cpu":3,"io":null}]
public sealed record ApplyOp(string Exe, int? Cpu, int? Io);

public sealed record ApplyOpResult(string Exe, bool Success, string? Error);

/// result.json — written next to ops.json as "<name>.result.json".
public sealed record ApplyReport(bool BatchRejected, string? BatchError, IReadOnlyList<ApplyOpResult> Results);
