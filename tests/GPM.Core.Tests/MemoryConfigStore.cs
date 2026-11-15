using GPM.Core.Persistence;

namespace GPM.Core.Tests;

/// games.json in memory. Load returns the same instance every time, unlike JsonConfigStore.
internal sealed class MemoryConfigStore : IConfigStore
{
    public GpmConfig Cfg { get; private set; } = new();
    public string FilePath => "test://games.json";
    public GpmConfig Load() => Cfg;
    public void Save(GpmConfig config) => Cfg = config;
}
