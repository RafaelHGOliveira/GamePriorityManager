using Microsoft.Win32;

namespace GPM.Core.Registry;

public sealed class RealStringValueStore(RegistryKey hive, string keyPath) : IStringValueStore
{
    public string? Get(string name)
    {
        using var key = hive.OpenSubKey(keyPath);
        return key?.GetValue(name) as string;
    }

    public void Set(string name, string value)
    {
        using var key = hive.CreateSubKey(keyPath);
        key.SetValue(name, value, RegistryValueKind.String);
    }

    public void Delete(string name)
    {
        using var key = hive.OpenSubKey(keyPath, writable: true);
        key?.DeleteValue(name, throwOnMissingValue: false);
    }

    public IReadOnlyList<string> GetValueNames()
    {
        using var key = hive.OpenSubKey(keyPath);
        return key?.GetValueNames() ?? [];
    }
}
