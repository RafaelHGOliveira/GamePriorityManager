using System.Text.Json;
using GPM.Core.Models;

namespace GPM.Core.Detection;

public sealed class EpicSource(string? manifestDir = null) : IGameSource
{
    private readonly string _dir =
        manifestDir ?? @"C:\ProgramData\Epic\EpicGamesLauncher\Data\Manifests";

    public GameStore Store => GameStore.Epic;
    public bool IsBeta => false;

    public Task<IReadOnlyList<Game>> ScanAsync(CancellationToken ct = default) =>
        Task.Run<IReadOnlyList<Game>>(() =>
        {
            if (!Directory.Exists(_dir)) return [];
            var games = new List<Game>();
            foreach (var file in Directory.EnumerateFiles(_dir, "*.item"))
            {
                try
                {
                    using var doc = JsonDocument.Parse(File.ReadAllText(file));
                    var r = doc.RootElement;
                    var name = GetString(r, "DisplayName");
                    var loc = GetString(r, "InstallLocation");
                    if (name is null || loc is null) continue;
                    var exe = GetString(r, "LaunchExecutable");
                    games.Add(new Game
                    {
                        Name = name,
                        Store = GameStore.Epic,
                        StoreId = GetString(r, "AppName"),
                        InstallDir = loc,
                        ExePath = exe is null ? null : Path.Combine(loc, exe),
                    });
                }
                catch (JsonException) { /* one corrupt manifest never breaks the scan */ }
            }
            return games;
        }, ct);

    private static string? GetString(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String
            ? v.GetString() : null;
}
