namespace GPM.Core.Registry;

/// Wraps ONE registry key. Production: the IFEO root under HKLM.
/// Tests: HKCU\Software\GPM-Test\<unique>. All paths are relative subkeys
/// (nested paths like "eldenring.exe\PerfOptions" are allowed).
public interface IRegistryRoot
{
    IReadOnlyList<string> GetSubKeyNames();
    IReadOnlyList<string> GetValueNames(string subKey);
    int? GetDword(string subKey, string valueName);
    void SetDword(string subKey, string valueName, int value);
    void DeleteValue(string subKey, string valueName);
    /// Deletes subKey only if it has no values and no subkeys.
    void DeleteSubKeyIfEmpty(string subKey);
    bool KeyExists(string subKey);
}
