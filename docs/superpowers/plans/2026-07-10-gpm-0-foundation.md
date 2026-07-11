# GPM Plan 0 — Foundation (Solution Skeleton + Contracts + Fixtures)

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Create the solution skeleton, CI, all shared GPM.Core contracts, and test fixtures — the serial Phase 0 that every parallel workstream (plans A–D) codes against.

**Architecture:** Three projects (`GPM.App` WPF, `GPM.Core` class library, `GPM.Probe` console) plus one test project. This plan produces only types, schemas, and fixtures — no behavior beyond trivial derived properties. Plans A–D branch off `main` AFTER this plan is merged.

**Tech Stack:** .NET 10 (`net10.0-windows`), C# (nullable enabled), xUnit, WPF + WPF-UI 4.x + CommunityToolkit.Mvvm (referenced now, used in plan D).

**Spec:** `docs/superpowers/specs/2026-07-10-gpm-mvp-design.md`

## Global Constraints

- TargetFramework: `net10.0-windows` for ALL projects. `<Nullable>enable</Nullable>`, `<ImplicitUsings>enable</ImplicitUsings>`.
- CPU priority dwords: `1` Idle, `5` Below Normal, `2` Normal, `6` Above Normal, `3` High. **Realtime (4) is forbidden everywhere.** IO: `0` Very Low, `1` Low, `2` Normal, `3` High.
- Overrides are keyed by **lowercase exe filename** (IFEO is path-blind).
- Tests must run without admin — registry tests use `HKCU\Software\GPM-Test\...`.
- Commit messages: conventional prefixes (`feat:`, `test:`, `chore:`). **Never add a `Co-Authored-By` trailer.**
- Plan order: this plan merges to `main` first; plans A–D then run in parallel (separate branches/worktrees); plan E runs last.

---

### Task 1: Solution skeleton + CI

**Files:**
- Create: `GamePriorityManager.sln`, `.gitignore`, `.github/workflows/ci.yml`
- Create: `src/GPM.Core/GPM.Core.csproj`, `src/GPM.App/GPM.App.csproj`, `src/GPM.App/app.manifest`, `src/GPM.Probe/GPM.Probe.csproj`, `tests/GPM.Core.Tests/GPM.Core.Tests.csproj`

**Interfaces:**
- Produces: the four projects every other plan adds files to.

- [ ] **Step 1: Scaffold projects**

```powershell
dotnet new gitignore
dotnet new sln -n GamePriorityManager
dotnet new classlib -n GPM.Core -o src/GPM.Core
dotnet new wpf -n GPM.App -o src/GPM.App
dotnet new console -n GPM.Probe -o src/GPM.Probe
dotnet new xunit -n GPM.Core.Tests -o tests/GPM.Core.Tests
dotnet sln add src/GPM.Core src/GPM.App src/GPM.Probe tests/GPM.Core.Tests
Remove-Item src/GPM.Core/Class1.cs
```

- [ ] **Step 2: Overwrite the csproj files** (exact content — templates may target another TFM)

`src/GPM.Core/GPM.Core.csproj`:
```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0-windows</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <LangVersion>latest</LangVersion>
  </PropertyGroup>
</Project>
```

`src/GPM.App/GPM.App.csproj`:
```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>WinExe</OutputType>
    <TargetFramework>net10.0-windows</TargetFramework>
    <UseWPF>true</UseWPF>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <LangVersion>latest</LangVersion>
    <ApplicationManifest>app.manifest</ApplicationManifest>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="WPF-UI" Version="4.*" />
    <PackageReference Include="CommunityToolkit.Mvvm" Version="8.*" />
    <ProjectReference Include="..\GPM.Core\GPM.Core.csproj" />
  </ItemGroup>
</Project>
```

`src/GPM.App/app.manifest` (main app NEVER elevates — spec principle 1):
```xml
<?xml version="1.0" encoding="utf-8"?>
<assembly manifestVersion="1.0" xmlns="urn:schemas-microsoft-com:asm.v1">
  <trustInfo xmlns="urn:schemas-microsoft-com:asm.v2">
    <security>
      <requestedPrivileges xmlns="urn:schemas-microsoft-com:asm.v3">
        <requestedExecutionLevel level="asInvoker" uiAccess="false" />
      </requestedPrivileges>
    </security>
  </trustInfo>
</assembly>
```

`src/GPM.Probe/GPM.Probe.csproj`:
```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net10.0-windows</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
  </PropertyGroup>
</Project>
```

`tests/GPM.Core.Tests/GPM.Core.Tests.csproj` (keep the xunit PackageReferences the template generated; add the marked lines):
```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0-windows</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <IsPackable>false</IsPackable>
  </PropertyGroup>
  <ItemGroup>
    <!-- keep template packages: xunit, xunit.runner.visualstudio, Microsoft.NET.Test.Sdk, coverlet.collector -->
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="..\..\src\GPM.Core\GPM.Core.csproj" />
    <None Include="Fixtures\**" CopyToOutputDirectory="PreserveNewest" />
  </ItemGroup>
</Project>
```

- [ ] **Step 3: Create `.github/workflows/ci.yml`**

```yaml
name: ci
on: [push, pull_request]
jobs:
  build-test:
    runs-on: windows-latest
    steps:
      - uses: actions/checkout@v4
      - uses: actions/setup-dotnet@v4
        with:
          dotnet-version: 10.0.x
      - run: dotnet build GamePriorityManager.sln -c Release
      - run: dotnet test tests/GPM.Core.Tests -c Release --no-build
```

- [ ] **Step 4: Verify it builds**

Run: `dotnet build GamePriorityManager.sln`
Expected: `Build succeeded` (0 errors; NU1603-style version warnings are acceptable).

- [ ] **Step 5: Commit**

```powershell
git add -A
git commit -m "chore: solution skeleton, projects and CI"
```

---

### Task 2: GPM.Core contracts

**Files:**
- Create: `src/GPM.Core/Models/GameStore.cs`, `src/GPM.Core/Models/Game.cs`
- Create: `src/GPM.Core/Detection/IGameSource.cs`, `src/GPM.Core/Detection/IStoreRegistry.cs`
- Create: `src/GPM.Core/Registry/IRegistryRoot.cs`
- Create: `src/GPM.Core/Priority/PriorityValues.cs`, `src/GPM.Core/Priority/IPriorityService.cs`, `src/GPM.Core/Priority/InMemoryPriorityService.cs`
- Create: `src/GPM.Core/Worker/ApplyModels.cs`
- Create: `src/GPM.Core/Resolve/IExeResolver.cs`
- Create: `src/GPM.Core/Persistence/GpmConfig.cs`
- Test: `tests/GPM.Core.Tests/ContractsTests.cs`

**Interfaces:**
- Produces: every type below, consumed verbatim by plans A–E. **Do not rename anything.**

- [ ] **Step 1: Write the contract types** (all files, exact content)

`src/GPM.Core/Models/GameStore.cs`:
```csharp
namespace GPM.Core.Models;

public enum GameStore { Steam, Epic, Gog, Ea, Ubisoft, Manual }
```

`src/GPM.Core/Models/Game.cs`:
```csharp
namespace GPM.Core.Models;

public sealed record Game
{
    public required string Name { get; init; }
    public required GameStore Store { get; init; }
    public string? StoreId { get; init; }           // Steam appid, Epic AppName, GOG id…
    public string? InstallDir { get; init; }
    public string? ExePath { get; init; }           // absolute; null until resolved
    public bool NeedsExeConfirmation { get; init; } // heuristic wasn't confident
    public IReadOnlyList<string> ExeCandidates { get; init; } = [];

    /// IFEO key name — lowercase exe filename; null while unresolved.
    public string? ExeName => ExePath is null ? null : Path.GetFileName(ExePath).ToLowerInvariant();
}
```

`src/GPM.Core/Detection/IGameSource.cs`:
```csharp
using GPM.Core.Models;

namespace GPM.Core.Detection;

public interface IGameSource
{
    GameStore Store { get; }
    bool IsBeta { get; } // EA/Ubisoft ship behind the "beta sources" toggle
    Task<IReadOnlyList<Game>> ScanAsync(CancellationToken ct = default);
}
```

`src/GPM.Core/Detection/IStoreRegistry.cs` (read-only registry access for store detection; fakeable in tests):
```csharp
using Microsoft.Win32;

namespace GPM.Core.Detection;

public interface IStoreRegistry
{
    string? GetString(RegistryHive hive, string keyPath, string valueName);
    IReadOnlyList<string> GetSubKeyNames(RegistryHive hive, string keyPath);
}
```

`src/GPM.Core/Registry/IRegistryRoot.cs`:
```csharp
namespace GPM.Core.Registry;

/// Wraps ONE registry key. Production: the IFEO root under HKLM.
/// Tests: HKCU\Software\GPM-Test\<unique>. All paths are relative subkeys
/// (nested paths like "eldenring.exe\PerfOptions" are allowed).
public interface IRegistryRoot
{
    IReadOnlyList<string> GetSubKeyNames();
    IReadOnlyList<string> GetValueNames(string subKey);
    int? GetDword(string subKey, string valueName);
    void SetDword(string subKey, string valueName, int value);
    void DeleteValue(string subKey, string valueName);
    /// Deletes subKey only if it has no values and no subkeys.
    void DeleteSubKeyIfEmpty(string subKey);
    bool KeyExists(string subKey);
}
```

`src/GPM.Core/Priority/PriorityValues.cs`:
```csharp
namespace GPM.Core.Priority;

public static class PriorityValues
{
    public static readonly int[] ValidCpu = [1, 2, 3, 5, 6]; // Realtime (4) NEVER
    public static readonly int[] ValidIo = [0, 1, 2, 3];
    public const string PerfOptions = "PerfOptions";
    public const string CpuValueName = "CpuPriorityClass";
    public const string IoValueName = "IoPriority";
    public const string IfeoPath =
        @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Image File Execution Options";
}
```

`src/GPM.Core/Priority/IPriorityService.cs`:
```csharp
namespace GPM.Core.Priority;

/// One IFEO override, keyed by lowercase exe filename.
public sealed record PriorityOverride(string ExeName, int? Cpu, int? Io, bool HasDebugger);

public interface IPriorityService
{
    PriorityOverride Read(string exeName);
    /// All IFEO subkeys that currently carry a CPU or IO override.
    IReadOnlyList<PriorityOverride> ReadAll();
    /// null cpu/io = delete that value. Cleans up empty PerfOptions/exe keys.
    /// Throws ArgumentOutOfRangeException for values outside PriorityValues.
    void Write(string exeName, int? cpu, int? io);
}
```

`src/GPM.Core/Priority/InMemoryPriorityService.cs` (test double + design-time mock for the UI):
```csharp
namespace GPM.Core.Priority;

public sealed class InMemoryPriorityService : IPriorityService
{
    public Dictionary<string, (int? Cpu, int? Io)> Data { get; } = new(StringComparer.OrdinalIgnoreCase);

    public PriorityOverride Read(string exeName) =>
        Data.TryGetValue(exeName, out var v)
            ? new(exeName.ToLowerInvariant(), v.Cpu, v.Io, false)
            : new(exeName.ToLowerInvariant(), null, null, false);

    public IReadOnlyList<PriorityOverride> ReadAll() =>
        Data.Where(kv => kv.Value.Cpu is not null || kv.Value.Io is not null)
            .Select(kv => new PriorityOverride(kv.Key.ToLowerInvariant(), kv.Value.Cpu, kv.Value.Io, false))
            .ToList();

    public void Write(string exeName, int? cpu, int? io)
    {
        if (cpu is int c && !PriorityValues.ValidCpu.Contains(c))
            throw new ArgumentOutOfRangeException(nameof(cpu), c, "value not allowed");
        if (io is int i && !PriorityValues.ValidIo.Contains(i))
            throw new ArgumentOutOfRangeException(nameof(io), i, "value not allowed");
        if (cpu is null && io is null) Data.Remove(exeName);
        else Data[exeName] = (cpu, io);
    }
}
```

`src/GPM.Core/Worker/ApplyModels.cs` (the `ops.json` / `result.json` schema):
```csharp
namespace GPM.Core.Worker;

/// ops.json is a JSON array of ApplyOp (camelCase): [{"exe":"x.exe","cpu":3,"io":null}]
public sealed record ApplyOp(string Exe, int? Cpu, int? Io);

public sealed record ApplyOpResult(string Exe, bool Success, string? Error);

/// result.json — written next to ops.json as "<name>.result.json".
public sealed record ApplyReport(bool BatchRejected, string? BatchError, IReadOnlyList<ApplyOpResult> Results);
```

`src/GPM.Core/Resolve/IExeResolver.cs`:
```csharp
using GPM.Core.Models;

namespace GPM.Core.Resolve;

public sealed record ExeResolution(string? ExePath, bool NeedsConfirmation, IReadOnlyList<string> Candidates);

public interface IExeResolver
{
    ExeResolution Resolve(Game game);
}
```

`src/GPM.Core/Persistence/GpmConfig.cs`:
```csharp
using GPM.Core.Models;

namespace GPM.Core.Persistence;

public sealed class GpmConfig
{
    public List<Game> ManualGames { get; set; } = [];
    /// "<store>:<storeId>" -> absolute exe path chosen by the user
    public Dictionary<string, string> ExeChoices { get; set; } = [];
    /// Provenance: lowercase exe names GPM has written overrides for
    public HashSet<string> ManagedExes { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public bool ShowIoColumn { get; set; }
    public bool BetaSourcesEnabled { get; set; }
    public string Theme { get; set; } = "auto";
}

public interface IConfigStore
{
    string FilePath { get; }
    GpmConfig Load();
    void Save(GpmConfig config);
}
```

- [ ] **Step 2: Write the smoke test**

`tests/GPM.Core.Tests/ContractsTests.cs`:
```csharp
using GPM.Core.Models;
using GPM.Core.Priority;

namespace GPM.Core.Tests;

public class ContractsTests
{
    [Fact]
    public void game_exename_is_lowercase_filename()
    {
        var g = new Game { Name = "ELDEN RING", Store = GameStore.Steam, ExePath = @"C:\Games\ELDEN RING\Game\ELDENRING.exe" };
        Assert.Equal("eldenring.exe", g.ExeName);
    }

    [Fact]
    public void game_exename_is_null_when_unresolved()
    {
        var g = new Game { Name = "X", Store = GameStore.Manual };
        Assert.Null(g.ExeName);
    }

    [Fact]
    public void inmemory_priority_service_rejects_realtime()
    {
        var svc = new InMemoryPriorityService();
        Assert.Throws<ArgumentOutOfRangeException>(() => svc.Write("x.exe", 4, null));
    }

    [Fact]
    public void inmemory_priority_service_roundtrips_and_deletes()
    {
        var svc = new InMemoryPriorityService();
        svc.Write("x.exe", 3, 2);
        Assert.Equal(3, svc.Read("x.exe").Cpu);
        svc.Write("x.exe", null, null);
        Assert.Null(svc.Read("x.exe").Cpu);
        Assert.Empty(svc.ReadAll());
    }
}
```

Delete the template's `UnitTest1.cs`.

- [ ] **Step 3: Run tests**

Run: `dotnet test tests/GPM.Core.Tests`
Expected: PASS (4 tests).

- [ ] **Step 4: Commit**

```powershell
git add -A
git commit -m "feat: GPM.Core shared contracts (models, registry, priority, worker, resolver, config)"
```

---

### Task 3: Store fixtures

**Files:**
- Create: `tests/GPM.Core.Tests/Fixtures/steam/libraryfolders.vdf`
- Create: `tests/GPM.Core.Tests/Fixtures/steam/appmanifest_1245620.acf`
- Create: `tests/GPM.Core.Tests/Fixtures/epic/RocketLeague.item`

**Interfaces:**
- Produces: fixture files consumed by plan B's parser tests via `Path.Combine(AppContext.BaseDirectory, "Fixtures", ...)`.

- [ ] **Step 1: Write the fixtures** (exact content; values use `\\` escaping as the real formats do)

`tests/GPM.Core.Tests/Fixtures/steam/libraryfolders.vdf`:
```
"libraryfolders"
{
	"0"
	{
		"path"		"C:\\Program Files (x86)\\Steam"
	}
	"1"
	{
		"path"		"D:\\SteamLibrary"
	}
}
```

`tests/GPM.Core.Tests/Fixtures/steam/appmanifest_1245620.acf`:
```
"AppState"
{
	"appid"		"1245620"
	"name"		"ELDEN RING"
	"StateFlags"		"4"
	"installdir"		"ELDEN RING"
}
```

`tests/GPM.Core.Tests/Fixtures/epic/RocketLeague.item`:
```json
{
  "FormatVersion": 0,
  "AppName": "Sugar",
  "DisplayName": "Rocket League",
  "InstallLocation": "C:\\Program Files\\Epic Games\\rocketleague",
  "LaunchExecutable": "Binaries\\Win64\\RocketLeague.exe"
}
```

- [ ] **Step 2: Verify fixtures are copied to test output**

Run: `dotnet build tests/GPM.Core.Tests; Test-Path tests/GPM.Core.Tests/bin/Debug/net10.0-windows/Fixtures/steam/libraryfolders.vdf`
Expected: `True`

- [ ] **Step 3: Commit and merge to main**

```powershell
git add -A
git commit -m "test: store manifest fixtures (steam acf/vdf, epic item)"
```

This plan must be on `main` before plans A–D start.
