using System.Text.Json;

namespace GPM.Core.Resolve;

/// Curated Steam appid -> relative exe path map (step 1 of the resolver pipeline).
public sealed class KnownGames
{
    private readonly Dictionary<string, string> _map;

    private KnownGames(Dictionary<string, string> map) => _map = map;

    public static KnownGames LoadEmbedded()
    {
        using var stream = typeof(KnownGames).Assembly
            .GetManifestResourceStream("GPM.Core.Resolve.known-games.json")
            ?? throw new InvalidOperationException("known-games.json not embedded");
        return new KnownGames(JsonSerializer.Deserialize<Dictionary<string, string>>(stream)!);
    }

    public static KnownGames FromJson(string json) =>
        new(JsonSerializer.Deserialize<Dictionary<string, string>>(json)!);

    public bool TryGetPath(string appId, out string relativeExePath) =>
        _map.TryGetValue(appId, out relativeExePath!);
}
