# GPM Plan B — Game Detection (Stores + Scanner + Config)

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Implement all store detection sources (Steam, Epic, GOG, EA, Ubisoft), the failure-isolating `GameScanner`, and the `games.json` config store with writable-dir fallback.

**Architecture:** Each store is one `IGameSource`. Registry-based stores read through the injectable `IStoreRegistry`; file-based scanning is split into static/parametrized methods so tests point them at fixture/temp directories. Steam's text formats get a small VDF parser.

**Tech Stack:** .NET 10, `Microsoft.Win32`, `System.Text.Json`, xUnit. Fixtures from plan 0 (`tests/GPM.Core.Tests/Fixtures/`).

**Prerequisites:** Plan 0 merged to `main`. Work on branch/worktree `ws-b-detection`. Tasks B1–B5 are independent of plans A/C/D. Within this plan, B2 depends on B1; the rest are parallelizable.

**Spec:** `docs/superpowers/specs/2026-07-10-gpm-mvp-design.md` — sections "Game detection", "Architecture" (principles 3–4).

## Global Constraints

- A failing source NEVER breaks the overall scan (per-source try/catch in `GameScanner`).
- EA and Ubisoft are beta sources (`IsBeta = true`) — the UI gates them behind a toggle.
- Steam registry: `HKLM\SOFTWARE\WOW6432Node\Valve\Steam` → `InstallPath`, fallback `HKCU\Software\Valve\Steam` → `SteamPath`.
- `games.json` lives next to the exe; fall back to `%APPDATA%\GPM` when that directory is not writable.
- Commits: conventional prefixes, **never add a `Co-Authored-By` trailer**.

---

### Task B1: VDF parser + Steam manifest readers

**Files:**
- Create: `src/GPM.Core/Detection/Steam/VdfParser.cs`, `src/GPM.Core/Detection/Steam/SteamManifests.cs`
- Test: `tests/GPM.Core.Tests/SteamManifestTests.cs`

**Interfaces:**
- Consumes: fixtures `Fixtures/steam/libraryfolders.vdf`, `Fixtures/steam/appmanifest_1245620.acf` (plan 0).
- Produces: `static Dictionary<string, object> VdfParser.Parse(string text)` (values are `string` or nested `Dictionary<string, object>`); `static IReadOnlyList<string> SteamManifests.LibraryPaths(string vdfText)`; `static (string AppId, string Name, string InstallDir)? SteamManifests.ParseAppManifest(string acfText)`. Consumed by B2.

- [ ] **Step 1: Write the failing tests**

`tests/GPM.Core.Tests/SteamManifestTests.cs`:
```csharp
using GPM.Core.Detection.Steam;

namespace GPM.Core.Tests;

public class SteamManifestTests
{
    private static string Fixture(string rel) =>
        Path.Combine(AppContext.BaseDirectory, "Fixtures", rel);

    [Fact]
    public void vdf_parser_handles_nesting_and_escapes()
    {
        var root = VdfParser.Parse("""
            "outer"
            {
                "key"   "va\\lue"
                "inner" { "a" "1" }
            }
            """);
        var outer = (Dictionary<string, object>)root["outer"];
        Assert.Equal(@"va\lue", outer["key"]);
        Assert.Equal("1", ((Dictionary<string, object>)outer["inner"])["a"]);
    }

    [Fact]
    public void library_paths_come_from_fixture()
    {
        var paths = SteamManifests.LibraryPaths(File.ReadAllText(Fixture(@"steam\libraryfolders.vdf")));
        Assert.Equal([@"C:\Program Files (x86)\Steam", @"D:\SteamLibrary"], paths);
    }

    [Fact]
    public void appmanifest_parses_fixture()
    {
        var app = SteamManifests.ParseAppManifest(File.ReadAllText(Fixture(@"steam\appmanifest_1245620.acf")));
        Assert.Equal(("1245620", "ELDEN RING", "ELDEN RING"), app);
    }

    [Fact]
    public void malformed_manifest_returns_null()
    {
        Assert.Null(SteamManifests.ParseAppManifest("\"AppState\"\n{\n\t\"appid\"\t\t\"1\"\n}"));
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/GPM.Core.Tests --filter SteamManifestTests`
Expected: FAIL — types don't exist.

- [ ] **Step 3: Implement**

`src/GPM.Core/Detection/Steam/VdfParser.cs`:
```csharp
using System.Text;

namespace GPM.Core.Detection.Steam;

/// Minimal parser for Valve's text KeyValues format (.vdf/.acf).
public static class VdfParser
{
    public static Dictionary<string, object> Parse(string text)
    {
        var pos = 0;
        var root = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
        ParseInto(root, text, ref pos);
        return root;
    }

    private static void ParseInto(Dictionary<string, object> target, string text, ref int pos)
    {
        while (true)
        {
            SkipWhitespace(text, ref pos);
            if (pos >= text.Length) return;
            if (text[pos] == '}') { pos++; return; }
            var key = ReadString(text, ref pos);
            SkipWhitespace(text, ref pos);
            if (text[pos] == '{')
            {
                pos++;
                var child = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
                ParseInto(child, text, ref pos);
                target[key] = child;
            }
            else
            {
                target[key] = ReadString(text, ref pos);
            }
        }
    }

    private static void SkipWhitespace(string t, ref int pos)
    {
        while (pos < t.Length && char.IsWhiteSpace(t[pos])) pos++;
    }

    private static string ReadString(string t, ref int pos)
    {
        pos++; // opening quote
        var sb = new StringBuilder();
        while (t[pos] != '"')
        {
            if (t[pos] == '\\') pos++; // unescape \" and \\
            sb.Append(t[pos]);
            pos++;
        }
        pos++; // closing quote
        return sb.ToString();
    }
}
```

`src/GPM.Core/Detection/Steam/SteamManifests.cs`:
```csharp
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
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test tests/GPM.Core.Tests --filter SteamManifestTests`
Expected: PASS (4 tests).

- [ ] **Step 5: Commit**

```powershell
git add src/GPM.Core/Detection/Steam tests/GPM.Core.Tests/SteamManifestTests.cs
git commit -m "feat: VDF parser and Steam manifest readers"
```

---

### Task B2: SteamSource + FakeStoreRegistry + WindowsStoreRegistry

**Files:**
- Create: `src/GPM.Core/Detection/Steam/SteamSource.cs`, `src/GPM.Core/Detection/WindowsStoreRegistry.cs`
- Create: `tests/GPM.Core.Tests/FakeStoreRegistry.cs`
- Test: `tests/GPM.Core.Tests/SteamSourceTests.cs`

**Interfaces:**
- Consumes: `IGameSource`, `IStoreRegistry`, `Game`, `GameStore` (plan 0); `SteamManifests` (B1).
- Produces: `SteamSource(IStoreRegistry) : IGameSource` with testable `static IReadOnlyList<Game> SteamSource.ScanRoot(string steamPath)`; `WindowsStoreRegistry : IStoreRegistry` (production, also used by B3/B4); `FakeStoreRegistry` (tests, also used by B3/B4).

- [ ] **Step 1: Write the fake and the failing tests**

`tests/GPM.Core.Tests/FakeStoreRegistry.cs`:
```csharp
using GPM.Core.Detection;
using Microsoft.Win32;

namespace GPM.Core.Tests;

/// Keys addressed as "<hive>\<path>" (e.g. "LocalMachine\SOFTWARE\...").
public sealed class FakeStoreRegistry : IStoreRegistry
{
    public Dictionary<string, Dictionary<string, string>> Keys { get; } =
        new(StringComparer.OrdinalIgnoreCase);

    public void Set(RegistryHive hive, string keyPath, string valueName, string value)
    {
        var k = $@"{hive}\{keyPath}";
        if (!Keys.TryGetValue(k, out var vals)) Keys[k] = vals = new(StringComparer.OrdinalIgnoreCase);
        vals[valueName] = value;
    }

    public string? GetString(RegistryHive hive, string keyPath, string valueName) =>
        Keys.TryGetValue($@"{hive}\{keyPath}", out var vals) &&
        vals.TryGetValue(valueName, out var v) ? v : null;

    public IReadOnlyList<string> GetSubKeyNames(RegistryHive hive, string keyPath)
    {
        var prefix = $@"{hive}\{keyPath}\";
        return Keys.Keys
            .Where(k => k.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            .Select(k => k[prefix.Length..].Split('\\')[0])
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }
}
```

`tests/GPM.Core.Tests/SteamSourceTests.cs`:
```csharp
using GPM.Core.Detection.Steam;
using GPM.Core.Models;
using Microsoft.Win32;

namespace GPM.Core.Tests;

public class SteamSourceTests
{
    private static string MakeSteamTree(out string libDir)
    {
        var root = Directory.CreateTempSubdirectory("gpm-steam-").FullName;
        libDir = Directory.CreateTempSubdirectory("gpm-steamlib-").FullName;
        Directory.CreateDirectory(Path.Combine(root, "steamapps"));
        Directory.CreateDirectory(Path.Combine(libDir, "steamapps"));

        File.WriteAllText(Path.Combine(root, "steamapps", "libraryfolders.vdf"), $$"""
            "libraryfolders"
            {
                "0" { "path" "{{root.Replace(@"\", @"\\")}}" }
                "1" { "path" "{{libDir.Replace(@"\", @"\\")}}" }
            }
            """);
        File.WriteAllText(Path.Combine(root, "steamapps", "appmanifest_1245620.acf"),
            "\"AppState\"\n{\n\t\"appid\"\t\"1245620\"\n\t\"name\"\t\"ELDEN RING\"\n\t\"installdir\"\t\"ELDEN RING\"\n}");
        File.WriteAllText(Path.Combine(libDir, "steamapps", "appmanifest_730.acf"),
            "\"AppState\"\n{\n\t\"appid\"\t\"730\"\n\t\"name\"\t\"Counter-Strike 2\"\n\t\"installdir\"\t\"Counter-Strike Global Offensive\"\n}");
        return root;
    }

    [Fact]
    public void scanroot_finds_games_across_libraries()
    {
        var root = MakeSteamTree(out var lib);
        var games = SteamSource.ScanRoot(root);

        Assert.Equal(2, games.Count);
        var er = games.Single(g => g.StoreId == "1245620");
        Assert.Equal("ELDEN RING", er.Name);
        Assert.Equal(GameStore.Steam, er.Store);
        Assert.Equal(Path.Combine(root, "steamapps", "common", "ELDEN RING"), er.InstallDir);
        Assert.Null(er.ExePath); // Steam never names the exe -> plan C resolves it
        Assert.Equal(Path.Combine(lib, "steamapps", "common", "Counter-Strike Global Offensive"),
            games.Single(g => g.StoreId == "730").InstallDir);
    }

    [Fact]
    public void scanroot_returns_empty_when_vdf_missing() =>
        Assert.Empty(SteamSource.ScanRoot(Directory.CreateTempSubdirectory("gpm-empty-").FullName));

    [Fact]
    public async Task scanasync_uses_hklm_then_hkcu_fallback()
    {
        var root = MakeSteamTree(out _);
        var reg = new FakeStoreRegistry();
        reg.Set(RegistryHive.CurrentUser, @"Software\Valve\Steam", "SteamPath", root);

        var games = await new SteamSource(reg).ScanAsync(); // HKLM absent -> HKCU
        Assert.Equal(2, games.Count);
    }

    [Fact]
    public async Task scanasync_returns_empty_when_steam_not_installed() =>
        Assert.Empty(await new SteamSource(new FakeStoreRegistry()).ScanAsync());
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/GPM.Core.Tests --filter SteamSourceTests`
Expected: FAIL — `SteamSource` does not exist.

- [ ] **Step 3: Implement**

`src/GPM.Core/Detection/Steam/SteamSource.cs`:
```csharp
using GPM.Core.Models;
using Microsoft.Win32;

namespace GPM.Core.Detection.Steam;

public sealed class SteamSource(IStoreRegistry registry) : IGameSource
{
    public GameStore Store => GameStore.Steam;
    public bool IsBeta => false;

    public Task<IReadOnlyList<Game>> ScanAsync(CancellationToken ct = default) =>
        Task.Run<IReadOnlyList<Game>>(() =>
        {
            var steamPath =
                registry.GetString(RegistryHive.LocalMachine, @"SOFTWARE\WOW6432Node\Valve\Steam", "InstallPath")
                ?? registry.GetString(RegistryHive.CurrentUser, @"Software\Valve\Steam", "SteamPath");
            return steamPath is null ? [] : ScanRoot(steamPath);
        }, ct);

    /// Separated from registry lookup so tests can point at a fixture tree.
    public static IReadOnlyList<Game> ScanRoot(string steamPath)
    {
        var vdf = Path.Combine(steamPath, "steamapps", "libraryfolders.vdf");
        if (!File.Exists(vdf)) return [];

        var games = new List<Game>();
        foreach (var lib in SteamManifests.LibraryPaths(File.ReadAllText(vdf)))
        {
            var steamapps = Path.Combine(lib, "steamapps");
            if (!Directory.Exists(steamapps)) continue;
            foreach (var acf in Directory.EnumerateFiles(steamapps, "appmanifest_*.acf"))
            {
                var app = SteamManifests.ParseAppManifest(File.ReadAllText(acf));
                if (app is null) continue;
                games.Add(new Game
                {
                    Name = app.Value.Name,
                    Store = GameStore.Steam,
                    StoreId = app.Value.AppId,
                    InstallDir = Path.Combine(steamapps, "common", app.Value.InstallDir),
                });
            }
        }
        return games;
    }
}
```

`src/GPM.Core/Detection/WindowsStoreRegistry.cs`:
```csharp
using Microsoft.Win32;

namespace GPM.Core.Detection;

public sealed class WindowsStoreRegistry : IStoreRegistry
{
    public string? GetString(RegistryHive hive, string keyPath, string valueName)
    {
        using var baseKey = RegistryKey.OpenBaseKey(hive, RegistryView.Registry64);
        using var key = baseKey.OpenSubKey(keyPath);
        return key?.GetValue(valueName) as string;
    }

    public IReadOnlyList<string> GetSubKeyNames(RegistryHive hive, string keyPath)
    {
        using var baseKey = RegistryKey.OpenBaseKey(hive, RegistryView.Registry64);
        using var key = baseKey.OpenSubKey(keyPath);
        return key?.GetSubKeyNames() ?? [];
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test tests/GPM.Core.Tests --filter SteamSourceTests`
Expected: PASS (4 tests).

- [ ] **Step 5: Commit**

```powershell
git add src/GPM.Core/Detection tests/GPM.Core.Tests/FakeStoreRegistry.cs tests/GPM.Core.Tests/SteamSourceTests.cs
git commit -m "feat: SteamSource with HKCU fallback and store registry implementations"
```

---

### Task B3: EpicSource + GogSource

**Files:**
- Create: `src/GPM.Core/Detection/EpicSource.cs`, `src/GPM.Core/Detection/GogSource.cs`
- Test: `tests/GPM.Core.Tests/EpicGogSourceTests.cs`

**Interfaces:**
- Consumes: `IGameSource`, `IStoreRegistry`, `Game` (plan 0); `FakeStoreRegistry` (B2); fixture `Fixtures/epic/RocketLeague.item` (plan 0).
- Produces: `EpicSource(string? manifestDir = null) : IGameSource`; `GogSource(IStoreRegistry) : IGameSource`.

- [ ] **Step 1: Write the failing tests**

`tests/GPM.Core.Tests/EpicGogSourceTests.cs`:
```csharp
using GPM.Core.Detection;
using GPM.Core.Models;
using Microsoft.Win32;

namespace GPM.Core.Tests;

public class EpicGogSourceTests
{
    [Fact]
    public async Task epic_parses_item_manifests_from_directory()
    {
        var dir = Directory.CreateTempSubdirectory("gpm-epic-").FullName;
        File.Copy(Path.Combine(AppContext.BaseDirectory, "Fixtures", "epic", "RocketLeague.item"),
            Path.Combine(dir, "RocketLeague.item"));
        File.WriteAllText(Path.Combine(dir, "broken.item"), "{ not json");

        var games = await new EpicSource(dir).ScanAsync();

        var g = Assert.Single(games); // broken manifest skipped, not fatal
        Assert.Equal("Rocket League", g.Name);
        Assert.Equal(GameStore.Epic, g.Store);
        Assert.Equal("Sugar", g.StoreId);
        Assert.Equal(@"C:\Program Files\Epic Games\rocketleague\Binaries\Win64\RocketLeague.exe", g.ExePath);
        Assert.Equal("rocketleague.exe", g.ExeName);
    }

    [Fact]
    public async Task epic_returns_empty_when_manifest_dir_missing() =>
        Assert.Empty(await new EpicSource(@"C:\gpm-does-not-exist-" + Guid.NewGuid().ToString("N")).ScanAsync());

    [Fact]
    public async Task gog_reads_games_from_registry()
    {
        var reg = new FakeStoreRegistry();
        const string root = @"SOFTWARE\WOW6432Node\GOG.com\Games";
        reg.Set(RegistryHive.LocalMachine, $@"{root}\1207658924", "gameName", "Cyberpunk 2077");
        reg.Set(RegistryHive.LocalMachine, $@"{root}\1207658924", "path", @"D:\GOG\Cyberpunk 2077");
        reg.Set(RegistryHive.LocalMachine, $@"{root}\1207658924", "exe", @"D:\GOG\Cyberpunk 2077\bin\x64\Cyberpunk2077.exe");
        reg.Set(RegistryHive.LocalMachine, $@"{root}\999", "path", @"D:\GOG\Broken"); // no gameName -> skipped

        var games = await new GogSource(reg).ScanAsync();

        var g = Assert.Single(games);
        Assert.Equal("Cyberpunk 2077", g.Name);
        Assert.Equal(GameStore.Gog, g.Store);
        Assert.Equal("cyberpunk2077.exe", g.ExeName);
    }

    [Fact]
    public async Task gog_returns_empty_when_not_installed() =>
        Assert.Empty(await new GogSource(new FakeStoreRegistry()).ScanAsync());
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/GPM.Core.Tests --filter EpicGogSourceTests`
Expected: FAIL — types don't exist.

- [ ] **Step 3: Implement**

`src/GPM.Core/Detection/EpicSource.cs`:
```csharp
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
```

`src/GPM.Core/Detection/GogSource.cs`:
```csharp
using GPM.Core.Models;
using Microsoft.Win32;

namespace GPM.Core.Detection;

public sealed class GogSource(IStoreRegistry registry) : IGameSource
{
    private const string Root = @"SOFTWARE\WOW6432Node\GOG.com\Games";

    public GameStore Store => GameStore.Gog;
    public bool IsBeta => false;

    public Task<IReadOnlyList<Game>> ScanAsync(CancellationToken ct = default) =>
        Task.Run<IReadOnlyList<Game>>(() =>
            registry.GetSubKeyNames(RegistryHive.LocalMachine, Root)
                .Select(id =>
                {
                    var key = $@"{Root}\{id}";
                    var name = registry.GetString(RegistryHive.LocalMachine, key, "gameName");
                    if (name is null) return null;
                    return new Game
                    {
                        Name = name,
                        Store = GameStore.Gog,
                        StoreId = id,
                        InstallDir = registry.GetString(RegistryHive.LocalMachine, key, "path"),
                        ExePath = registry.GetString(RegistryHive.LocalMachine, key, "exe"),
                    };
                })
                .Where(g => g is not null)
                .Cast<Game>()
                .ToList(), ct);
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test tests/GPM.Core.Tests --filter EpicGogSourceTests`
Expected: PASS (4 tests).

- [ ] **Step 5: Commit**

```powershell
git add src/GPM.Core/Detection/EpicSource.cs src/GPM.Core/Detection/GogSource.cs tests/GPM.Core.Tests/EpicGogSourceTests.cs
git commit -m "feat: Epic and GOG detection sources"
```

---

### Task B4: EaSource + UbisoftSource (beta sources)

**Files:**
- Create: `src/GPM.Core/Detection/EaSource.cs`, `src/GPM.Core/Detection/UbisoftSource.cs`
- Test: `tests/GPM.Core.Tests/BetaSourceTests.cs`

**Interfaces:**
- Consumes: `IGameSource`, `IStoreRegistry`, `Game` (plan 0); `FakeStoreRegistry` (B2).
- Produces: `EaSource(IStoreRegistry) : IGameSource` and `UbisoftSource(IStoreRegistry) : IGameSource`, both `IsBeta = true`.

- [ ] **Step 1: Write the failing tests**

`tests/GPM.Core.Tests/BetaSourceTests.cs`:
```csharp
using GPM.Core.Detection;
using GPM.Core.Models;
using Microsoft.Win32;

namespace GPM.Core.Tests;

public class BetaSourceTests
{
    [Fact]
    public async Task ea_lists_subkeys_with_install_dir()
    {
        var reg = new FakeStoreRegistry();
        const string root = @"SOFTWARE\WOW6432Node\Electronic Arts";
        reg.Set(RegistryHive.LocalMachine, $@"{root}\Battlefield 2042", "Install Dir", @"D:\EA\BF2042\");
        reg.Set(RegistryHive.LocalMachine, $@"{root}\EA Desktop", "InstallSuccessful", "true"); // no Install Dir -> skipped

        var src = new EaSource(reg);
        Assert.True(src.IsBeta);

        var g = Assert.Single(await src.ScanAsync());
        Assert.Equal("Battlefield 2042", g.Name);
        Assert.Equal(GameStore.Ea, g.Store);
        Assert.Equal(@"D:\EA\BF2042", g.InstallDir);
        Assert.Null(g.ExePath); // EA doesn't name the exe -> plan C resolves
    }

    [Fact]
    public async Task ubisoft_reads_installs_and_names_from_dir()
    {
        var reg = new FakeStoreRegistry();
        const string root = @"SOFTWARE\WOW6432Node\Ubisoft\Launcher\Installs";
        reg.Set(RegistryHive.LocalMachine, $@"{root}\635", "InstallDir", "D:/Ubisoft/Anno 1800/");

        var src = new UbisoftSource(reg);
        Assert.True(src.IsBeta);

        var g = Assert.Single(await src.ScanAsync());
        Assert.Equal("Anno 1800", g.Name); // last path segment
        Assert.Equal(GameStore.Ubisoft, g.Store);
        Assert.Equal("635", g.StoreId);
        Assert.Equal(@"D:\Ubisoft\Anno 1800", g.InstallDir);
    }

    [Fact]
    public async Task both_return_empty_when_not_installed()
    {
        Assert.Empty(await new EaSource(new FakeStoreRegistry()).ScanAsync());
        Assert.Empty(await new UbisoftSource(new FakeStoreRegistry()).ScanAsync());
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/GPM.Core.Tests --filter BetaSourceTests`
Expected: FAIL — types don't exist.

- [ ] **Step 3: Implement**

`src/GPM.Core/Detection/EaSource.cs`:
```csharp
using GPM.Core.Models;
using Microsoft.Win32;

namespace GPM.Core.Detection;

/// Fragile registry layout — ships behind the "beta sources" toggle (spec).
public sealed class EaSource(IStoreRegistry registry) : IGameSource
{
    private const string Root = @"SOFTWARE\WOW6432Node\Electronic Arts";

    public GameStore Store => GameStore.Ea;
    public bool IsBeta => true;

    public Task<IReadOnlyList<Game>> ScanAsync(CancellationToken ct = default) =>
        Task.Run<IReadOnlyList<Game>>(() =>
            registry.GetSubKeyNames(RegistryHive.LocalMachine, Root)
                .Select(name =>
                {
                    var dir = registry.GetString(RegistryHive.LocalMachine, $@"{Root}\{name}", "Install Dir");
                    if (dir is null) return null;
                    return new Game
                    {
                        Name = name,
                        Store = GameStore.Ea,
                        InstallDir = Path.TrimEndingDirectorySeparator(dir),
                    };
                })
                .Where(g => g is not null)
                .Cast<Game>()
                .ToList(), ct);
}
```

`src/GPM.Core/Detection/UbisoftSource.cs`:
```csharp
using GPM.Core.Models;
using Microsoft.Win32;

namespace GPM.Core.Detection;

/// Same beta-toggle policy as EA (spec).
public sealed class UbisoftSource(IStoreRegistry registry) : IGameSource
{
    private const string Root = @"SOFTWARE\WOW6432Node\Ubisoft\Launcher\Installs";

    public GameStore Store => GameStore.Ubisoft;
    public bool IsBeta => true;

    public Task<IReadOnlyList<Game>> ScanAsync(CancellationToken ct = default) =>
        Task.Run<IReadOnlyList<Game>>(() =>
            registry.GetSubKeyNames(RegistryHive.LocalMachine, Root)
                .Select(id =>
                {
                    var dir = registry.GetString(RegistryHive.LocalMachine, $@"{Root}\{id}", "InstallDir");
                    if (dir is null) return null;
                    var normalized = Path.TrimEndingDirectorySeparator(
                        dir.Replace('/', Path.DirectorySeparatorChar));
                    return new Game
                    {
                        Name = Path.GetFileName(normalized),
                        Store = GameStore.Ubisoft,
                        StoreId = id,
                        InstallDir = normalized,
                    };
                })
                .Where(g => g is not null)
                .Cast<Game>()
                .ToList(), ct);
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test tests/GPM.Core.Tests --filter BetaSourceTests`
Expected: PASS (3 tests).

- [ ] **Step 5: Commit**

```powershell
git add src/GPM.Core/Detection/EaSource.cs src/GPM.Core/Detection/UbisoftSource.cs tests/GPM.Core.Tests/BetaSourceTests.cs
git commit -m "feat: EA and Ubisoft beta detection sources"
```

---

### Task B5: GameScanner + JsonConfigStore

**Files:**
- Create: `src/GPM.Core/Detection/GameScanner.cs`, `src/GPM.Core/Persistence/JsonConfigStore.cs`
- Test: `tests/GPM.Core.Tests/GameScannerTests.cs`, `tests/GPM.Core.Tests/JsonConfigStoreTests.cs`

**Interfaces:**
- Consumes: `IGameSource`, `Game`, `GpmConfig`, `IConfigStore` (plan 0).
- Produces: `GameScanner(IEnumerable<IGameSource> sources)` with `Task<IReadOnlyList<Game>> ScanAsync(bool includeBeta, CancellationToken ct = default)`; `JsonConfigStore(string preferredDir, string fallbackDir) : IConfigStore`. Both consumed by plan E's composition root and plan D (via interfaces).

- [ ] **Step 1: Write the failing tests**

`tests/GPM.Core.Tests/GameScannerTests.cs`:
```csharp
using GPM.Core.Detection;
using GPM.Core.Models;

namespace GPM.Core.Tests;

public class GameScannerTests
{
    private sealed class StubSource(GameStore store, bool isBeta, Func<IReadOnlyList<Game>> scan) : IGameSource
    {
        public GameStore Store => store;
        public bool IsBeta => isBeta;
        public Task<IReadOnlyList<Game>> ScanAsync(CancellationToken ct = default) =>
            Task.FromResult(scan());
    }

    private static Game G(string name, GameStore store) => new() { Name = name, Store = store };

    [Fact]
    public async Task merges_results_from_all_sources()
    {
        var scanner = new GameScanner([
            new StubSource(GameStore.Steam, false, () => [G("A", GameStore.Steam)]),
            new StubSource(GameStore.Epic, false, () => [G("B", GameStore.Epic)]),
        ]);
        Assert.Equal(2, (await scanner.ScanAsync(includeBeta: true)).Count);
    }

    [Fact]
    public async Task a_throwing_source_never_breaks_the_scan()
    {
        var scanner = new GameScanner([
            new StubSource(GameStore.Steam, false, () => throw new IOException("corrupt manifest")),
            new StubSource(GameStore.Epic, false, () => [G("B", GameStore.Epic)]),
        ]);
        var games = await scanner.ScanAsync(includeBeta: true);
        Assert.Equal("B", Assert.Single(games).Name);
    }

    [Fact]
    public async Task beta_sources_are_skipped_unless_enabled()
    {
        var scanner = new GameScanner([
            new StubSource(GameStore.Steam, false, () => [G("A", GameStore.Steam)]),
            new StubSource(GameStore.Ea, true, () => [G("C", GameStore.Ea)]),
        ]);
        Assert.Single(await scanner.ScanAsync(includeBeta: false));
        Assert.Equal(2, (await scanner.ScanAsync(includeBeta: true)).Count);
    }
}
```

`tests/GPM.Core.Tests/JsonConfigStoreTests.cs`:
```csharp
using GPM.Core.Models;
using GPM.Core.Persistence;

namespace GPM.Core.Tests;

public class JsonConfigStoreTests
{
    [Fact]
    public void save_load_roundtrips()
    {
        var dir = Directory.CreateTempSubdirectory("gpm-cfg-").FullName;
        var store = new JsonConfigStore(dir, fallbackDir: dir);
        var cfg = new GpmConfig { ShowIoColumn = true, Theme = "dark" };
        cfg.ManualGames.Add(new Game { Name = "X", Store = GameStore.Manual, ExePath = @"C:\x\x.exe" });
        cfg.ManagedExes.Add("x.exe");
        cfg.ExeChoices["Steam:1245620"] = @"C:\g\eldenring.exe";

        store.Save(cfg);
        var loaded = new JsonConfigStore(dir, dir).Load();

        Assert.True(loaded.ShowIoColumn);
        Assert.Equal("dark", loaded.Theme);
        Assert.Equal("X", Assert.Single(loaded.ManualGames).Name);
        Assert.Contains("x.exe", loaded.ManagedExes);
        Assert.Equal(@"C:\g\eldenring.exe", loaded.ExeChoices["Steam:1245620"]);
    }

    [Fact]
    public void load_returns_defaults_when_file_missing()
    {
        var dir = Directory.CreateTempSubdirectory("gpm-cfg-").FullName;
        var cfg = new JsonConfigStore(dir, dir).Load();
        Assert.Empty(cfg.ManualGames);
        Assert.Equal("auto", cfg.Theme);
    }

    [Fact]
    public void unwritable_preferred_dir_falls_back()
    {
        var missing = @"Q:\gpm-not-writable-" + Guid.NewGuid().ToString("N"); // drive doesn't exist
        var fallback = Directory.CreateTempSubdirectory("gpm-fallback-").FullName;
        var store = new JsonConfigStore(missing, fallback);

        Assert.StartsWith(fallback, store.FilePath);
        store.Save(new GpmConfig()); // must not throw
        Assert.True(File.Exists(store.FilePath));
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/GPM.Core.Tests --filter "GameScannerTests|JsonConfigStoreTests"`
Expected: FAIL — types don't exist.

- [ ] **Step 3: Implement**

`src/GPM.Core/Detection/GameScanner.cs`:
```csharp
using GPM.Core.Models;

namespace GPM.Core.Detection;

public sealed class GameScanner(IEnumerable<IGameSource> sources)
{
    public async Task<IReadOnlyList<Game>> ScanAsync(bool includeBeta, CancellationToken ct = default)
    {
        var tasks = sources
            .Where(s => includeBeta || !s.IsBeta)
            .Select(async s =>
            {
                try { return await s.ScanAsync(ct); }
                catch { return (IReadOnlyList<Game>)[]; } // a failing source never breaks the scan
            });
        return (await Task.WhenAll(tasks)).SelectMany(g => g).ToList();
    }
}
```

`src/GPM.Core/Persistence/JsonConfigStore.cs`:
```csharp
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
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test tests/GPM.Core.Tests --filter "GameScannerTests|JsonConfigStoreTests"`
Expected: PASS (6 tests). Then the whole suite: `dotnet test tests/GPM.Core.Tests` — all green.

- [ ] **Step 5: Commit**

```powershell
git add src/GPM.Core/Detection/GameScanner.cs src/GPM.Core/Persistence/JsonConfigStore.cs tests/GPM.Core.Tests/GameScannerTests.cs tests/GPM.Core.Tests/JsonConfigStoreTests.cs
git commit -m "feat: GameScanner with failure isolation and JsonConfigStore with dir fallback"
```

Plan B complete — ready for review/merge per superpowers:finishing-a-development-branch.
