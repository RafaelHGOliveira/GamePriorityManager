using Microsoft.Win32;

namespace GPM.Core.Detection;

public interface IStoreRegistry
{
    string? GetString(RegistryHive hive, string keyPath, string valueName);
    IReadOnlyList<string> GetSubKeyNames(RegistryHive hive, string keyPath);
}
