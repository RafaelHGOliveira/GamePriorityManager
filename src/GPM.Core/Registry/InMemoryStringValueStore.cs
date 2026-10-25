namespace GPM.Core.Registry;

/// Registry-free IStringValueStore for design data and tests.
public sealed class InMemoryStringValueStore : IStringValueStore
{
    private readonly Dictionary<string, string> _data = new(StringComparer.OrdinalIgnoreCase);

    public string? Get(string name) => _data.GetValueOrDefault(name);
    public void Set(string name, string value) => _data[name] = value;
    public void Delete(string name) => _data.Remove(name);
    public IReadOnlyList<string> GetValueNames() => _data.Keys.ToList();
}
