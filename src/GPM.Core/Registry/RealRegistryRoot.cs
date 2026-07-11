using Microsoft.Win32;

namespace GPM.Core.Registry;

public sealed class RealRegistryRoot(RegistryKey hive, string rootPath) : IRegistryRoot
{
    private RegistryKey? OpenRoot(bool writable = false) => hive.OpenSubKey(rootPath, writable);

    public IReadOnlyList<string> GetSubKeyNames()
    {
        using var root = OpenRoot();
        return root?.GetSubKeyNames() ?? [];
    }

    public IReadOnlyList<string> GetValueNames(string subKey)
    {
        using var root = OpenRoot();
        using var key = root?.OpenSubKey(subKey);
        return key?.GetValueNames() ?? [];
    }

    public int? GetDword(string subKey, string valueName)
    {
        using var root = OpenRoot();
        using var key = root?.OpenSubKey(subKey);
        return key?.GetValue(valueName) as int?;
    }

    public void SetDword(string subKey, string valueName, int value)
    {
        using var root = hive.CreateSubKey(rootPath);
        using var key = root.CreateSubKey(subKey);
        key.SetValue(valueName, value, RegistryValueKind.DWord);
    }

    public void DeleteValue(string subKey, string valueName)
    {
        using var root = OpenRoot(writable: true);
        using var key = root?.OpenSubKey(subKey, writable: true);
        key?.DeleteValue(valueName, throwOnMissingValue: false);
    }

    public void DeleteSubKeyIfEmpty(string subKey)
    {
        using var root = OpenRoot(writable: true);
        if (root is null) return;
        using (var key = root.OpenSubKey(subKey))
        {
            if (key is null || key.ValueCount > 0 || key.SubKeyCount > 0) return;
        }
        root.DeleteSubKey(subKey, throwOnMissingSubKey: false);
    }

    public bool KeyExists(string subKey)
    {
        using var root = OpenRoot();
        using var key = root?.OpenSubKey(subKey);
        return key is not null;
    }
}
