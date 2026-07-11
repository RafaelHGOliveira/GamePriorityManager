namespace GPM.Core.Models;

public sealed record Game
{
    public required string Name { get; init; }
    public required GameStore Store { get; init; }
    public string? StoreId { get; init; }           // Steam appid, Epic AppName, GOG id…
    public string? InstallDir { get; init; }
    public string? ExePath { get; init; }           // absolute; null until resolved
    public bool NeedsExeConfirmation { get; init; } // heuristic wasn't confident
    public IReadOnlyList<string> ExeCandidates { get; init; } = [];

    /// IFEO key name — lowercase exe filename; null while unresolved.
    public string? ExeName => ExePath is null ? null : Path.GetFileName(ExePath).ToLowerInvariant();
}
