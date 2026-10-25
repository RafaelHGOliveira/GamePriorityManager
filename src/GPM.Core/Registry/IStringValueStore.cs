namespace GPM.Core.Registry;

/// String values directly on ONE registry key (value name = arbitrary text, e.g. an exe path).
/// Production: HKCU\Software\Microsoft\DirectX\UserGpuPreferences.
/// Tests: HKCU\Software\GPM-Test\<unique>.
public interface IStringValueStore
{
    string? Get(string name);
    void Set(string name, string value);
    void Delete(string name);
    IReadOnlyList<string> GetValueNames();
}
