using GPM.Core.Detection.Steam;
using GPM.Core.Models;
using GPM.Core.Persistence;
using GPM.Core.Resolve;

namespace GPM.Core.Library;

/// Every game GPM lists: the detected Store games, cached in games.json together with
/// their resolved exe, plus the Manual Games, with the user's exe choices applied.
/// Owns the game-list part of games.json (DetectedGames, ManualGames, ExeChoices).
/// Call it from one thread (the UI thread): it saves games.json on the caller's
/// context, the same one that saves settings.
public sealed class GameLibrary(
    Func<Task<IReadOnlyList<Game>>> scanStores,
    IExeResolver resolver,
    IConfigStore config)
{
    private int _loadVersion;

    /// Uses the detected-game cache unless <paramref name="rescan"/> is set or there is no
    /// cache yet; <paramref name="scanning"/> runs just before such a Store scan starts.
    /// Exe resolution runs off the calling thread. Returns null, saving nothing, when a
    /// later LoadAsync superseded this one.
    public async Task<IReadOnlyList<Game>?> LoadAsync(bool rescan = false, Action? scanning = null)
    {
        var version = ++_loadVersion;
        var cfg = config.Load();
        var fromCache = !rescan && cfg.DetectedGames.Count > 0;
        var detected = cfg.DetectedGames;
        if (!fromCache)
        {
            scanning?.Invoke();
            detected = (await scanStores()).ToList();
        }

        // Exe resolution walks every install dir: far too slow for the UI thread.
        var result = await Task.Run(() => Resolve(cfg, detected, fromCache));
        if (version != _loadVersion) return null;

        if (result.CacheChanged)
        {
            var latest = config.Load(); // settings may have changed while we were off-thread
            latest.DetectedGames = result.Detected;
            config.Save(latest);
        }
        return result.Games;
    }

    public void AddManual(string exePath)
    {
        var cfg = config.Load();
        cfg.ManualGames.Add(new Game
        {
            Name = Path.GetFileNameWithoutExtension(exePath),
            Store = GameStore.Manual,
            InstallDir = Path.GetDirectoryName(exePath),
            ExePath = exePath,
        });
        config.Save(cfg);
    }

    /// False, changing nothing, for a Store game: only Manual Games can be removed.
    public bool RemoveManual(Game game)
    {
        if (game.Store != GameStore.Manual) return false;
        var cfg = config.Load();
        cfg.ManualGames.RemoveAll(g => g.ExePath == game.ExePath);
        config.Save(cfg);
        return true;
    }

    /// The user's exe for this game; it wins over the resolver while the file exists.
    public void ChooseExe(Game game, string exePath)
    {
        var cfg = config.Load();
        cfg.ExeChoices[ExeChoiceKey(game)] = exePath;
        config.Save(cfg);
    }

    private sealed record Resolution(List<Game> Detected, bool CacheChanged, List<Game> Games);

    /// Runs off the calling thread: touches only the file system and its arguments.
    private Resolution Resolve(GpmConfig cfg, List<Game> detected, bool fromCache)
    {
        var cacheChanged = !fromCache;

        // Non-game Steam tools (redistributables, runtimes, Proton) may still sit in an
        // older games.json cache that was written before they were filtered at scan time.
        // Drop them here too so they never appear regardless of cache vs. fresh scan, and
        // heal the cache so it only happens once.
        var withoutTools = detected.Where(g => !SteamManifests.IsNonGameApp(g.Name)).ToList();
        if (withoutTools.Count != detected.Count) cacheChanged = true;

        // The resolved exe is cached in games.json, so the install-dir walk only runs
        // after a rescan, or when the cached exe is gone (game updated, moved, uninstalled).
        var cached = new List<Game>();
        var games = new List<Game>();
        foreach (var original in withoutTools)
        {
            var g = original;
            if (fromCache && g.ExePath is not null && !File.Exists(g.ExePath))
            {
                g = g with { ExePath = null, NeedsExeConfirmation = false, ExeCandidates = [] };
                cacheChanged = true;
            }
            if (TryApplyChoice(cfg, g) is { } chosen)
            {
                cached.Add(g);
                games.Add(chosen);
                continue;
            }
            if (g.ExePath is null)
            {
                g = ApplyResolution(g);
                cacheChanged |= g.ExePath is not null;
            }
            cached.Add(g);
            games.Add(g);
        }
        games.AddRange(cfg.ManualGames.Select(g => TryApplyChoice(cfg, g) ?? ApplyResolution(g)));

        return new Resolution(cached, cacheChanged, games);
    }

    private static Game? TryApplyChoice(GpmConfig cfg, Game g) =>
        cfg.ExeChoices.TryGetValue(ExeChoiceKey(g), out var chosen) && File.Exists(chosen)
            ? g with { ExePath = chosen, NeedsExeConfirmation = false, ExeCandidates = [] }
            : null;

    private Game ApplyResolution(Game g)
    {
        var r = resolver.Resolve(g);
        return g with
        {
            ExePath = r.ExePath ?? g.ExePath,
            NeedsExeConfirmation = r.NeedsConfirmation,
            ExeCandidates = r.Candidates,
        };
    }

    private static string ExeChoiceKey(Game g) => $"{g.Store}:{g.StoreId ?? g.InstallDir ?? g.Name}";
}
