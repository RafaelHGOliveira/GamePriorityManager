using GPM.Core.Detection;
using Microsoft.Win32;

namespace GPM.Core.Tests;

/// Keys addressed as "<hive>\<path>" (e.g. "LocalMachine\SOFTWARE\...").
public sealed class FakeStoreRegistry : IStoreRegistry
{
    public Dictionary<string, Dictionary<string, string>> Keys { get; } =
        new(StringComparer.OrdinalIgnoreCase);

    public void Set(RegistryHive hive, string keyPath, string valueName, string value)
    {
        var k = $@"{hive}\{keyPath}";
        if (!Keys.TryGetValue(k, out var vals)) Keys[k] = vals = new(StringComparer.OrdinalIgnoreCase);
        vals[valueName] = value;
    }

    public string? GetString(RegistryHive hive, string keyPath, string valueName) =>
        Keys.TryGetValue($@"{hive}\{keyPath}", out var vals) &&
        vals.TryGetValue(valueName, out var v) ? v : null;

    public IReadOnlyList<string> GetSubKeyNames(RegistryHive hive, string keyPath)
    {
        var prefix = $@"{hive}\{keyPath}\";
        return Keys.Keys
            .Where(k => k.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            .Select(k => k[prefix.Length..].Split('\\')[0])
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }
}
