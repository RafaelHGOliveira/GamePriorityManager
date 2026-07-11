using System.Text.Json;

namespace GPM.Core.Persistence;

public sealed class JsonConfigStore : IConfigStore
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    public string FilePath { get; }

    /// preferredDir: next to the exe (portable). fallbackDir: %APPDATA%\GPM.
    public JsonConfigStore(string preferredDir, string fallbackDir)
    {
        FilePath = Path.Combine(IsWritable(preferredDir) ? preferredDir : fallbackDir, "games.json");
    }

    public GpmConfig Load()
    {
        if (!File.Exists(FilePath)) return new GpmConfig();
        try
        {
            return JsonSerializer.Deserialize<GpmConfig>(File.ReadAllText(FilePath), Json) ?? new GpmConfig();
        }
        catch (JsonException)
        {
            return new GpmConfig(); // corrupt config falls back to defaults, never crashes startup
        }
    }

    public void Save(GpmConfig config)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        File.WriteAllText(FilePath, JsonSerializer.Serialize(config, Json));
    }

    private static bool IsWritable(string dir)
    {
        try
        {
            Directory.CreateDirectory(dir);
            var probe = Path.Combine(dir, $".gpm-write-{Guid.NewGuid():N}");
            File.WriteAllText(probe, "");
            File.Delete(probe);
            return true;
        }
        catch { return false; }
    }
}
