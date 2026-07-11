using GPM.Core.Models;

namespace GPM.Core.Resolve;

/// 3-step pipeline (spec "ExeResolver"): curated -> heuristic -> human.
public sealed class ExeResolver(KnownGames known) : IExeResolver
{
    public ExeResolution Resolve(Game game)
    {
        if (game.ExePath is not null)
            return new(game.ExePath, false, []);

        if (game.InstallDir is null || !Directory.Exists(game.InstallDir))
            return new(null, true, []);

        // Step 1: curated list (Steam appids)
        if (game.Store == GameStore.Steam && game.StoreId is not null &&
            known.TryGetPath(game.StoreId, out var rel))
        {
            var full = Path.Combine(game.InstallDir, rel);
            if (File.Exists(full)) return new(full, false, []);
        }

        // Step 2: heuristic over every exe in the install dir
        var candidates = Directory
            .EnumerateFiles(game.InstallDir, "*.exe", SearchOption.AllDirectories)
            .Select(p => new ExeCandidate(p, new FileInfo(p).Length))
            .ToList();
        return ExeHeuristics.Pick(candidates); // step 3 = NeedsConfirmation flag for the UI
    }
}
