# GPM Plan C — ExeResolver (Curated List + Heuristics)

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Implement the 3-step exe resolution pipeline: curated `known-games.json` → filename heuristics → flag for human confirmation.

**Architecture:** `KnownGames` wraps the embedded curated JSON; `ExeHeuristics.Pick` is a pure function over `(path, size)` candidates; `ExeResolver` chains them behind `IExeResolver`. Steam `appinfo.vdf` parsing is out of scope (v2, per spec).

**Tech Stack:** .NET 10, `System.Text.Json` (embedded resource), xUnit.

**Prerequisites:** Plan 0 merged to `main`. Work on branch/worktree `ws-c-exe-resolver`. Independent of plans A/B/D.

**Spec:** `docs/superpowers/specs/2026-07-10-gpm-mvp-design.md` — section "ExeResolver".

## Global Constraints

- Pipeline order: curated list → heuristic → human fallback. First confident hit wins.
- Heuristic: blacklist junk exes; prefer a unique `*-Win64-Shipping.exe`; else pick the largest remaining exe but mark `NeedsConfirmation = true` with all candidates listed (largest first).
- The curated hit only counts when the file actually exists on disk.
- Commits: conventional prefixes, **never add a `Co-Authored-By` trailer**.

---

### Task C1: KnownGames (embedded curated list)

**Files:**
- Create: `src/GPM.Core/Resolve/known-games.json`
- Create: `src/GPM.Core/Resolve/KnownGames.cs`
- Modify: `src/GPM.Core/GPM.Core.csproj` (embed the JSON)
- Test: `tests/GPM.Core.Tests/KnownGamesTests.cs`

**Interfaces:**
- Produces: `KnownGames.LoadEmbedded()`, `KnownGames.FromJson(string)`, `bool TryGetPath(string appId, out string relativeExePath)`. Consumed by C3 and plan E.

- [ ] **Step 1: Write the failing test**

`tests/GPM.Core.Tests/KnownGamesTests.cs`:
```csharp
using GPM.Core.Resolve;

namespace GPM.Core.Tests;

public class KnownGamesTests
{
    [Fact]
    public void embedded_list_loads_and_contains_seed_games()
    {
        var known = KnownGames.LoadEmbedded();
        Assert.True(known.TryGetPath("1245620", out var eldenRing)); // Elden Ring
        Assert.Equal(@"Game\eldenring.exe", eldenRing);              // NOT start_protected_game.exe
        Assert.True(known.TryGetPath("730", out var cs2));
        Assert.Equal(@"game\bin\win64\cs2.exe", cs2);
        Assert.False(known.TryGetPath("999999999", out _));
    }

    [Fact]
    public void fromjson_parses_custom_map()
    {
        var known = KnownGames.FromJson("""{"42":"bin\\game.exe"}""");
        Assert.True(known.TryGetPath("42", out var path));
        Assert.Equal(@"bin\game.exe", path);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/GPM.Core.Tests --filter KnownGamesTests`
Expected: FAIL — `KnownGames` does not exist.

- [ ] **Step 3: Implement**

`src/GPM.Core/Resolve/known-games.json` (seed entries from the spec; grows over time):
```json
{
  "1245620": "Game\\eldenring.exe",
  "2622380": "Game\\nightreign.exe",
  "730": "game\\bin\\win64\\cs2.exe",
  "570": "game\\bin\\win64\\dota2.exe",
  "1144200": "ReadyOrNot\\Binaries\\Win64\\ReadyOrNotSteam-Win64-Shipping.exe",
  "1371980": "NoRestForTheWicked.exe"
}
```

Add to `src/GPM.Core/GPM.Core.csproj` (inside `<Project>`):
```xml
  <ItemGroup>
    <EmbeddedResource Include="Resolve\known-games.json" />
  </ItemGroup>
```

`src/GPM.Core/Resolve/KnownGames.cs`:
```csharp
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
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test tests/GPM.Core.Tests --filter KnownGamesTests`
Expected: PASS (2 tests).

- [ ] **Step 5: Commit**

```powershell
git add src/GPM.Core/Resolve src/GPM.Core/GPM.Core.csproj tests/GPM.Core.Tests/KnownGamesTests.cs
git commit -m "feat: embedded curated known-games list"
```

---

### Task C2: ExeHeuristics

**Files:**
- Create: `src/GPM.Core/Resolve/ExeHeuristics.cs`
- Test: `tests/GPM.Core.Tests/ExeHeuristicsTests.cs`

**Interfaces:**
- Consumes: `ExeResolution` (plan 0).
- Produces: `record ExeCandidate(string Path, long Size)`; `static ExeResolution ExeHeuristics.Pick(IReadOnlyList<ExeCandidate> all)`. Consumed by C3.

- [ ] **Step 1: Write the failing tests** (table-driven over the spec's known hard cases)

`tests/GPM.Core.Tests/ExeHeuristicsTests.cs`:
```csharp
using GPM.Core.Resolve;

namespace GPM.Core.Tests;

public class ExeHeuristicsTests
{
    private static ExeCandidate C(string path, long size = 1_000) => new(path, size);

    [Fact]
    public void single_candidate_after_blacklist_is_confident()
    {
        var r = ExeHeuristics.Pick([
            C(@"C:\g\Game.exe", 50_000_000),
            C(@"C:\g\UnrealCrashHandler.exe", 5_000_000),
            C(@"C:\g\EasyAntiCheat_Setup.exe"),
            C(@"C:\g\UEPrereqSetup_x64.exe"),
            C(@"C:\g\dxsetup.exe"),
        ]);
        Assert.Equal(@"C:\g\Game.exe", r.ExePath);
        Assert.False(r.NeedsConfirmation);
    }

    [Fact]
    public void unique_win64_shipping_wins_even_when_smaller()
    {
        var r = ExeHeuristics.Pick([
            C(@"C:\g\ReadyOrNot\Binaries\Win64\ReadyOrNotSteam-Win64-Shipping.exe", 100),
            C(@"C:\g\Launcher.exe", 900_000_000),
        ]);
        Assert.Equal(@"C:\g\ReadyOrNot\Binaries\Win64\ReadyOrNotSteam-Win64-Shipping.exe", r.ExePath);
        Assert.False(r.NeedsConfirmation);
    }

    [Fact]
    public void ambiguous_set_picks_largest_but_needs_confirmation()
    {
        // "No Rest For The Wicked" case: main exe smaller than its crash handler
        // (crash handler is blacklisted, but two legit exes remain -> ambiguous)
        var r = ExeHeuristics.Pick([
            C(@"C:\g\wicked.exe", 80_000_000),
            C(@"C:\g\tools.exe", 10_000_000),
        ]);
        Assert.Equal(@"C:\g\wicked.exe", r.ExePath);
        Assert.True(r.NeedsConfirmation);
        Assert.Equal([@"C:\g\wicked.exe", @"C:\g\tools.exe"], r.Candidates); // largest first
    }

    [Fact]
    public void everything_blacklisted_needs_human_with_original_candidates()
    {
        var all = new[] { C(@"C:\g\setup.exe"), C(@"C:\g\redist.exe") };
        var r = ExeHeuristics.Pick(all);
        Assert.Null(r.ExePath);
        Assert.True(r.NeedsConfirmation);
        Assert.Equal(2, r.Candidates.Count);
    }

    [Fact]
    public void empty_input_needs_human()
    {
        var r = ExeHeuristics.Pick([]);
        Assert.Null(r.ExePath);
        Assert.True(r.NeedsConfirmation);
    }

    [Theory]
    [InlineData(@"C:\g\GameCrashHandler64.exe")]
    [InlineData(@"C:\g\Setup.exe")]
    [InlineData(@"C:\g\UEPrereqSetup.exe")]
    [InlineData(@"C:\g\EasyAntiCheat.exe")]
    [InlineData(@"C:\g\vconsole2.exe")]
    [InlineData(@"C:\g\vc_redist.x64.exe")]
    [InlineData(@"C:\g\dxsetup.exe")]
    public void blacklisted_names_are_never_picked(string junk)
    {
        var r = ExeHeuristics.Pick([C(junk, 999_999_999), C(@"C:\g\real.exe", 1)]);
        Assert.Equal(@"C:\g\real.exe", r.ExePath);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/GPM.Core.Tests --filter ExeHeuristicsTests`
Expected: FAIL — types don't exist.

- [ ] **Step 3: Implement**

`src/GPM.Core/Resolve/ExeHeuristics.cs`:
```csharp
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
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test tests/GPM.Core.Tests --filter ExeHeuristicsTests`
Expected: PASS (12 tests).

- [ ] **Step 5: Commit**

```powershell
git add src/GPM.Core/Resolve/ExeHeuristics.cs tests/GPM.Core.Tests/ExeHeuristicsTests.cs
git commit -m "feat: exe pick heuristics with blacklist and UE shipping preference"
```

---

### Task C3: ExeResolver pipeline

**Files:**
- Create: `src/GPM.Core/Resolve/ExeResolver.cs`
- Test: `tests/GPM.Core.Tests/ExeResolverTests.cs`

**Interfaces:**
- Consumes: `IExeResolver`, `ExeResolution`, `Game`, `GameStore` (plan 0); `KnownGames` (C1); `ExeHeuristics`, `ExeCandidate` (C2).
- Produces: `ExeResolver(KnownGames known) : IExeResolver` — consumed by plan E's composition root. Behavior: game already has `ExePath` → passthrough; Steam + curated hit that exists on disk → confident; else enumerate `*.exe` recursively under `InstallDir` and delegate to `ExeHeuristics.Pick`; missing/absent `InstallDir` → `(null, true, [])`.

- [ ] **Step 1: Write the failing tests**

`tests/GPM.Core.Tests/ExeResolverTests.cs`:
```csharp
using GPM.Core.Models;
using GPM.Core.Resolve;

namespace GPM.Core.Tests;

public class ExeResolverTests
{
    private static readonly KnownGames Known =
        KnownGames.FromJson("""{"1245620":"Game\\eldenring.exe"}""");

    private static string MakeDir(params (string RelPath, int Size)[] files)
    {
        var dir = Directory.CreateTempSubdirectory("gpm-resolve-").FullName;
        foreach (var (rel, size) in files)
        {
            var full = Path.Combine(dir, rel);
            Directory.CreateDirectory(Path.GetDirectoryName(full)!);
            File.WriteAllBytes(full, new byte[size]);
        }
        return dir;
    }

    [Fact]
    public void game_with_exepath_already_set_passes_through()
    {
        var r = new ExeResolver(Known).Resolve(new Game
        { Name = "RL", Store = GameStore.Epic, ExePath = @"C:\e\RocketLeague.exe" });
        Assert.Equal(@"C:\e\RocketLeague.exe", r.ExePath);
        Assert.False(r.NeedsConfirmation);
    }

    [Fact]
    public void curated_hit_wins_over_heuristic_when_file_exists()
    {
        var dir = MakeDir((@"Game\eldenring.exe", 10), (@"start_protected_game.exe", 999));
        var r = new ExeResolver(Known).Resolve(new Game
        { Name = "ELDEN RING", Store = GameStore.Steam, StoreId = "1245620", InstallDir = dir });
        Assert.Equal(Path.Combine(dir, @"Game\eldenring.exe"), r.ExePath);
        Assert.False(r.NeedsConfirmation);
    }

    [Fact]
    public void curated_miss_on_disk_falls_through_to_heuristic()
    {
        var dir = MakeDir((@"OnlyGame.exe", 10)); // curated path doesn't exist here
        var r = new ExeResolver(Known).Resolve(new Game
        { Name = "ELDEN RING", Store = GameStore.Steam, StoreId = "1245620", InstallDir = dir });
        Assert.Equal(Path.Combine(dir, "OnlyGame.exe"), r.ExePath);
    }

    [Fact]
    public void non_steam_game_without_exe_uses_heuristic()
    {
        var dir = MakeDir((@"bf.exe", 100), (@"EAAntiCheat.Installer.exe", 999)); // "anticheat" not blacklisted term; use setup instead
        var r = new ExeResolver(Known).Resolve(new Game
        { Name = "BF", Store = GameStore.Ea, InstallDir = dir });
        Assert.NotNull(r.ExePath);
    }

    [Fact]
    public void missing_installdir_needs_human()
    {
        var r = new ExeResolver(Known).Resolve(new Game { Name = "X", Store = GameStore.Steam });
        Assert.Null(r.ExePath);
        Assert.True(r.NeedsConfirmation);
    }

    [Fact]
    public void nonexistent_installdir_needs_human()
    {
        var r = new ExeResolver(Known).Resolve(new Game
        { Name = "X", Store = GameStore.Steam, InstallDir = @"C:\gpm-gone-" + Guid.NewGuid().ToString("N") });
        Assert.Null(r.ExePath);
        Assert.True(r.NeedsConfirmation);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/GPM.Core.Tests --filter ExeResolverTests`
Expected: FAIL — `ExeResolver` does not exist.

- [ ] **Step 3: Implement**

`src/GPM.Core/Resolve/ExeResolver.cs`:
```csharp
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
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test tests/GPM.Core.Tests --filter ExeResolverTests`
Expected: PASS (6 tests). Then the whole suite: `dotnet test tests/GPM.Core.Tests` — all green.

- [ ] **Step 5: Commit**

```powershell
git add src/GPM.Core/Resolve/ExeResolver.cs tests/GPM.Core.Tests/ExeResolverTests.cs
git commit -m "feat: ExeResolver pipeline (curated -> heuristic -> human fallback)"
```

Plan C complete — ready for review/merge per superpowers:finishing-a-development-branch.
