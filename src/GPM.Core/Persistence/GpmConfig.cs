using GPM.Core.Models;

namespace GPM.Core.Persistence;

public sealed class GpmConfig
{
    public List<Game> ManualGames { get; set; } = [];
    /// Detected-game cache (spec "UI": first-run scan cached; rescan via the toolbar button).
    public List<Game> DetectedGames { get; set; } = [];
    /// "<store>:<storeId>" -> absolute exe path chosen by the user
    public Dictionary<string, string> ExeChoices { get; set; } = [];
    /// Provenance: lowercase exe names GPM has written overrides for
    public HashSet<string> ManagedExes { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public bool ShowIoColumn { get; set; }
    public bool BetaSourcesEnabled { get; set; }
    public string Theme { get; set; } = "auto";
}

public interface IConfigStore
{
    string FilePath { get; }
    GpmConfig Load();
    void Save(GpmConfig config);
}
