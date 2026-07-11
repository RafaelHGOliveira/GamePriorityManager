namespace GPM.Core.Resolve;

public sealed record ExeCandidate(string Path, long Size);

/// Step 2 of the resolver pipeline (spec "ExeResolver").
public static class ExeHeuristics
{
    private static readonly string[] Blacklist =
        ["crashhandler", "setup", "ueprereq", "easyanticheat", "vconsole", "redist", "dxsetup", "unins"];

    public static ExeResolution Pick(IReadOnlyList<ExeCandidate> all)
    {
        var candidates = all
            .Where(c => !Blacklist.Any(b =>
                System.IO.Path.GetFileName(c.Path).Contains(b, StringComparison.OrdinalIgnoreCase)))
            .ToList();

        if (candidates.Count == 0)
            return new(null, true, all.Select(c => c.Path).ToList());

        if (candidates.Count == 1)
            return new(candidates[0].Path, false, []);

        var shipping = candidates
            .Where(c => System.IO.Path.GetFileName(c.Path)
                .EndsWith("-Win64-Shipping.exe", StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (shipping.Count == 1)
            return new(shipping[0].Path, false, []);

        var ordered = candidates.OrderByDescending(c => c.Size).ToList();
        return new(ordered[0].Path, true, ordered.Select(c => c.Path).ToList());
    }
}
