using Microsoft.Win32;

namespace GPM.Core.Detection;

public sealed class WindowsStoreRegistry : IStoreRegistry
{
    public string? GetString(RegistryHive hive, string keyPath, string valueName)
    {
        using var baseKey = RegistryKey.OpenBaseKey(hive, RegistryView.Registry64);
        using var key = baseKey.OpenSubKey(keyPath);
        return key?.GetValue(valueName) as string;
    }

    public IReadOnlyList<string> GetSubKeyNames(RegistryHive hive, string keyPath)
    {
        using var baseKey = RegistryKey.OpenBaseKey(hive, RegistryView.Registry64);
        using var key = baseKey.OpenSubKey(keyPath);
        return key?.GetSubKeyNames() ?? [];
    }
}
