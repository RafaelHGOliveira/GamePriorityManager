namespace GPM.Core.Detection.Steam;

public static class SteamManifests
{
    public static IReadOnlyList<string> LibraryPaths(string vdfText)
    {
        if (VdfParser.Parse(vdfText).Values.FirstOrDefault()
            is not Dictionary<string, object> folders) return [];
        return folders.Values.OfType<Dictionary<string, object>>()
            .Select(f => f.TryGetValue("path", out var p) ? p as string : null)
            .Where(p => !string.IsNullOrEmpty(p))
            .Cast<string>()
            .ToList();
    }

    /// Valve ships tools/redistributables (redist packages, Linux runtimes, Proton)
    /// through the same appmanifest mechanism as games. They clutter the list and have
    /// no game exe to prioritize. Matched by name — stable across versions, unlike the
    /// ever-growing set of appids. The trailing space on "Proton " is deliberate: it
    /// keeps real games like "Protonwar" from being filtered out.
    public static bool IsNonGameApp(string name) =>
        name.Equals("Steamworks Common Redistributables", StringComparison.OrdinalIgnoreCase)
        || name.StartsWith("Steam Linux Runtime", StringComparison.OrdinalIgnoreCase)
        || name.Equals("Proton", StringComparison.OrdinalIgnoreCase)
        || name.StartsWith("Proton ", StringComparison.OrdinalIgnoreCase);

    public static (string AppId, string Name, string InstallDir)? ParseAppManifest(string acfText)
    {
        if (VdfParser.Parse(acfText).Values.FirstOrDefault()
            is not Dictionary<string, object> state) return null;
        if (state.TryGetValue("appid", out var id) && id is string appId &&
            state.TryGetValue("name", out var n) && n is string name &&
            state.TryGetValue("installdir", out var d) && d is string dir)
            return (appId, name, dir);
        return null;
    }
}
