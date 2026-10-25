using GPM.Core.Registry;

namespace GPM.Core.Graphics;

/// Reads/writes ONLY the GpuPreference and SwapEffectUpgradeEnable tokens of the
/// "<exe path>" value; every other token (and every other value) is left exactly as found.
public sealed class GraphicsPreferenceService(IStringValueStore store)
{
    private const string GpuToken = "GpuPreference";
    private const string WindowedToken = "SwapEffectUpgradeEnable";

    public GraphicsPreference Read(string exePath)
    {
        var tokens = Parse(store.Get(exePath));
        var gpu = GetValue(tokens, GpuToken);
        return new GraphicsPreference(
            gpu is 1 or 2 ? gpu : null,
            GetValue(tokens, WindowedToken) switch { 1 => true, 0 => false, _ => null });
    }

    public void Write(string exePath, GraphicsPreference pref)
    {
        if (!Path.IsPathFullyQualified(exePath) || !exePath.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("exe path must be absolute and end in .exe", nameof(exePath));
        if (pref.Gpu is int g && g is < 0 or > 2)
            throw new ArgumentOutOfRangeException(nameof(pref), g, "gpu value not allowed");

        var tokens = Parse(store.Get(exePath));
        SetToken(tokens, GpuToken, pref.Gpu is 1 or 2 ? pref.Gpu.ToString() : null);
        SetToken(tokens, WindowedToken, pref.WindowedOptimizations switch { true => "1", false => "0", null => null });

        if (tokens.Count == 0) store.Delete(exePath);
        else store.Set(exePath, string.Concat(tokens.Select(t => t + ";")));
    }

    /// Tokens kept verbatim (trimmed), in original order. Empty segments are dropped.
    private static List<string> Parse(string? raw) =>
        (raw ?? "").Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();

    private static int? GetValue(List<string> tokens, string name)
    {
        var i = IndexOf(tokens, name);
        return i >= 0 && int.TryParse(tokens[i][(name.Length + 1)..].Trim(), out var v) ? v : null;
    }

    private static void SetToken(List<string> tokens, string name, string? value)
    {
        var i = IndexOf(tokens, name);
        if (value is null) { if (i >= 0) tokens.RemoveAt(i); }
        else if (i >= 0) tokens[i] = $"{name}={value}";
        else tokens.Add($"{name}={value}");
    }

    private static int IndexOf(List<string> tokens, string name) =>
        tokens.FindIndex(t => t.StartsWith(name + "=", StringComparison.OrdinalIgnoreCase));
}
