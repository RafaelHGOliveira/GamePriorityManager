# GPM Plan D — UI Shell (MVVM + WPF-UI, Against Fakes)

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build the single-window UI — list, search, priority dropdowns, dirty tracking with filename-keyed linked rows, Apply/Discard flow, Other-overrides section — entirely against fakes (`InMemoryPriorityService`, stub sources). Real wiring happens in plan E.

**Architecture:** All UI state lives in ViewModels (CommunityToolkit.Mvvm) so the logic is unit-testable without WPF. The core invariant: pending changes are keyed by lowercase exe filename via a shared `ExeOverrideState` object — rows with the same filename hold the SAME instance, so conflicting edits are impossible by construction (spec "IFEO semantics").

**Tech Stack:** WPF + WPF-UI 4.x (`FluentWindow`), CommunityToolkit.Mvvm 8.x, xUnit. ViewModels live in `GPM.App`; the test project gains a reference to `GPM.App` (added in D1) — tests only instantiate ViewModels, never `Application`.

**Prerequisites:** Plan 0 merged to `main`. Work on branch/worktree `ws-d-ui-shell`. Independent of plans A/B/C (uses only plan-0 contracts + fakes defined here).

**Spec:** `docs/superpowers/specs/2026-07-10-gpm-mvp-design.md` — sections "UI", "IFEO semantics", "Error handling".

## Global Constraints

- CPU dropdown options and dword values, in this order: Default (null), Idle (1), Below Normal (5), Normal (2), Above Normal (6), High (3). IO: Default (null), Very Low (0), Low (1), Normal (2), High (3). **Realtime never appears.**
- All user-visible strings in `src/GPM.App/Strings.resx`. English. No hardcoded UI text in XAML/code-behind.
- Main window follows OS dark/light (WPF-UI `SystemThemeWatcher`). No tray, no background process.
- The ops.json contract is exactly plan 0's `ApplyOp` serialized with camelCase (`JsonSerializerDefaults.Web`); result path = `Path.ChangeExtension(opsPath, ".result.json")`.
- Generic exe names that get a system-wide warning badge: `game.exe`, `launcher.exe`, `start.exe`, `main.exe`, `app.exe`, `run.exe`.
- Commits: conventional prefixes, **never add a `Co-Authored-By` trailer**.

---

### Task D1: Strings.resx + priority options + test project reference

**Files:**
- Create: `src/GPM.App/Strings.resx`, `src/GPM.App/ViewModels/PriorityOptions.cs`
- Modify: `src/GPM.App/GPM.App.csproj` (resx codegen), `tests/GPM.Core.Tests/GPM.Core.Tests.csproj` (reference GPM.App)
- Test: `tests/GPM.Core.Tests/PriorityOptionsTests.cs`

**Interfaces:**
- Produces: `Strings` resource class (`GPM.App` namespace); `record PriorityOption(string Label, int? Value)`; `static IReadOnlyList<PriorityOption> PriorityOptions.Cpu` / `.Io`. Consumed by D2–D4.

- [ ] **Step 1: Create `src/GPM.App/Strings.resx`**

```xml
<?xml version="1.0" encoding="utf-8"?>
<root>
  <resheader name="resmimetype"><value>text/microsoft-resx</value></resheader>
  <resheader name="version"><value>2.0</value></resheader>
  <resheader name="reader"><value>System.Resources.ResXResourceReader, System.Windows.Forms, Version=4.0.0.0, Culture=neutral, PublicKeyToken=b77a5c561934e089</value></resheader>
  <resheader name="writer"><value>System.Resources.ResXResourceWriter, System.Windows.Forms, Version=4.0.0.0, Culture=neutral, PublicKeyToken=b77a5c561934e089</value></resheader>
  <data name="AppTitle" xml:space="preserve"><value>GPM — Game Priority Manager</value></data>
  <data name="SearchPlaceholder" xml:space="preserve"><value>Search...</value></data>
  <data name="AddGame" xml:space="preserve"><value>+ Add game</value></data>
  <data name="Apply" xml:space="preserve"><value>Apply</value></data>
  <data name="Discard" xml:space="preserve"><value>Discard</value></data>
  <data name="PendingChanges" xml:space="preserve"><value>{0} pending changes</value></data>
  <data name="ApplyCancelled" xml:space="preserve"><value>Apply cancelled</value></data>
  <data name="OtherOverrides" xml:space="preserve"><value>Other overrides ({0})</value></data>
  <data name="OrphanedTooltip" xml:space="preserve"><value>GPM created this override but the game is no longer detected. Safe to clean up.</value></data>
  <data name="LinkedRowsTooltip" xml:space="preserve"><value>Another entry shares this exe filename — they always change together (IFEO is keyed by filename).</value></data>
  <data name="GenericExeWarning" xml:space="preserve"><value>This override applies to EVERY process named {0} on the system.</value></data>
  <data name="DebuggerWarning" xml:space="preserve"><value>This exe has a Debugger value set in IFEO (not by GPM). GPM will not touch it.</value></data>
  <data name="VerifyExe" xml:space="preserve"><value>Verify exe</value></data>
  <data name="Priority_Default" xml:space="preserve"><value>Default</value></data>
  <data name="Priority_Idle" xml:space="preserve"><value>Idle</value></data>
  <data name="Priority_BelowNormal" xml:space="preserve"><value>Below Normal</value></data>
  <data name="Priority_Normal" xml:space="preserve"><value>Normal</value></data>
  <data name="Priority_AboveNormal" xml:space="preserve"><value>Above Normal</value></data>
  <data name="Priority_High" xml:space="preserve"><value>High</value></data>
  <data name="Priority_VeryLow" xml:space="preserve"><value>Very Low</value></data>
  <data name="Priority_Low" xml:space="preserve"><value>Low</value></data>
  <data name="Settings_ShowIo" xml:space="preserve"><value>Show I/O column</value></data>
  <data name="Settings_BetaSources" xml:space="preserve"><value>Beta sources (EA, Ubisoft)</value></data>
  <data name="Settings_Theme" xml:space="preserve"><value>Theme</value></data>
  <data name="ScanProgress" xml:space="preserve"><value>Scanning {0}...</value></data>
</root>
```

Add to `src/GPM.App/GPM.App.csproj` (inside `<Project>`):
```xml
  <ItemGroup>
    <EmbeddedResource Update="Strings.resx">
      <Generator>PublicResXFileCodeGenerator</Generator>
      <LastGenOutput>Strings.Designer.cs</LastGenOutput>
    </EmbeddedResource>
    <Compile Update="Strings.Designer.cs">
      <DesignTime>True</DesignTime>
      <AutoGen>True</AutoGen>
      <DependentUpon>Strings.resx</DependentUpon>
    </Compile>
  </ItemGroup>
```

If `Strings.Designer.cs` is not generated by the build, generate it once with: `dotnet msbuild src/GPM.App -t:CoreResGen` (or open the resx in an IDE). Verify `GPM.App.Strings.AppTitle` compiles.

Add the GPM.App reference to `tests/GPM.Core.Tests/GPM.Core.Tests.csproj`:
```xml
    <ProjectReference Include="..\..\src\GPM.App\GPM.App.csproj" />
```
Also set `<EnableWindowsTargeting>` is unnecessary (already on Windows); tests reference a WinExe — that works because tests only instantiate ViewModels, never `Application`.

- [ ] **Step 2: Write the failing test**

`tests/GPM.Core.Tests/PriorityOptionsTests.cs`:
```csharp
using GPM.App.ViewModels;

namespace GPM.Core.Tests;

public class PriorityOptionsTests
{
    [Fact]
    public void cpu_options_match_spec_order_and_values()
    {
        Assert.Equal([null, 1, 5, 2, 6, 3], PriorityOptions.Cpu.Select(o => o.Value)); // Normal(2) included; Realtime(4) absent
        Assert.Equal("Default", PriorityOptions.Cpu[0].Label);
        Assert.Equal("Normal", PriorityOptions.Cpu[3].Label);
    }

    [Fact]
    public void io_options_match_spec()
    {
        Assert.Equal([null, 0, 1, 2, 3], PriorityOptions.Io.Select(o => o.Value));
    }
}
```

- [ ] **Step 3: Run test to verify it fails**

Run: `dotnet test tests/GPM.Core.Tests --filter PriorityOptionsTests`
Expected: FAIL — `PriorityOptions` does not exist.

- [ ] **Step 4: Implement**

`src/GPM.App/ViewModels/PriorityOptions.cs`:
```csharp
namespace GPM.App.ViewModels;

public sealed record PriorityOption(string Label, int? Value);

public static class PriorityOptions
{
    public static readonly IReadOnlyList<PriorityOption> Cpu =
    [
        new(Strings.Priority_Default, null),
        new(Strings.Priority_Idle, 1),
        new(Strings.Priority_BelowNormal, 5),
        new(Strings.Priority_Normal, 2),
        new(Strings.Priority_AboveNormal, 6),
        new(Strings.Priority_High, 3),
    ];

    public static readonly IReadOnlyList<PriorityOption> Io =
    [
        new(Strings.Priority_Default, null),
        new(Strings.Priority_VeryLow, 0),
        new(Strings.Priority_Low, 1),
        new(Strings.Priority_Normal, 2),
        new(Strings.Priority_High, 3),
    ];
}
```

- [ ] **Step 5: Run test to verify it passes, then commit**

Run: `dotnet test tests/GPM.Core.Tests --filter PriorityOptionsTests`
Expected: PASS (2 tests).

```powershell
git add src/GPM.App tests/GPM.Core.Tests
git commit -m "feat: UI strings and priority dropdown options"
```

---

### Task D2: ExeOverrideState + row ViewModels

**Files:**
- Create: `src/GPM.App/ViewModels/ExeOverrideState.cs`, `src/GPM.App/ViewModels/GameRowViewModel.cs`, `src/GPM.App/ViewModels/OverrideRowViewModel.cs`
- Test: `tests/GPM.Core.Tests/ExeOverrideStateTests.cs`

**Interfaces:**
- Consumes: `Game` (plan 0), `Strings`/`PriorityOptions` (D1).
- Produces (consumed by D3/D4):
  - `ExeOverrideState(string exeName, int? registryCpu, int? registryIo)` with `string ExeName`, `int? RegistryCpu/RegistryIo`, `bool HasDebugger { get; init; }`, observable `int? PendingCpu/PendingIo`, `string? LastError`, `bool IsDirty`, `void SetRegistry(int? cpu, int? io)`, `void DiscardPending()`.
  - `GameRowViewModel(Game game, ExeOverrideState? state, int linkedCount)` with `Game Game`, `ExeOverrideState? State`, `bool IsLinked`, `bool HasGenericExeName`, `bool NeedsExeConfirmation`.
  - `OverrideRowViewModel(ExeOverrideState state, bool isOrphaned, bool hasDebugger)`.

- [ ] **Step 1: Write the failing tests**

`tests/GPM.Core.Tests/ExeOverrideStateTests.cs`:
```csharp
using GPM.App.ViewModels;
using GPM.Core.Models;

namespace GPM.Core.Tests;

public class ExeOverrideStateTests
{
    [Fact]
    public void starts_clean_matching_registry()
    {
        var s = new ExeOverrideState("Game.EXE", 3, null);
        Assert.Equal("game.exe", s.ExeName);
        Assert.False(s.IsDirty);
    }

    [Fact]
    public void changing_pending_makes_dirty_and_raises_isdirty_notification()
    {
        var s = new ExeOverrideState("a.exe", null, null);
        var raised = new List<string?>();
        s.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        s.PendingCpu = 3;
        Assert.True(s.IsDirty);
        Assert.Contains(nameof(ExeOverrideState.IsDirty), raised);
    }

    [Fact]
    public void discard_reverts_to_registry()
    {
        var s = new ExeOverrideState("a.exe", 5, 1);
        s.PendingCpu = 3; s.PendingIo = null;
        s.DiscardPending();
        Assert.False(s.IsDirty);
        Assert.Equal(5, s.PendingCpu);
        Assert.Equal(1, s.PendingIo);
    }

    [Fact]
    public void setregistry_after_successful_apply_makes_row_clean()
    {
        var s = new ExeOverrideState("a.exe", null, null);
        s.PendingCpu = 3;
        s.SetRegistry(3, null); // registry now matches pending
        Assert.False(s.IsDirty);
    }

    [Fact]
    public void setregistry_after_failed_apply_keeps_row_dirty()
    {
        var s = new ExeOverrideState("a.exe", null, null);
        s.PendingCpu = 3;
        s.SetRegistry(null, null); // write failed -> registry unchanged
        Assert.True(s.IsDirty);
    }

    [Fact]
    public void game_row_flags_linked_and_generic_names()
    {
        var game = new Game { Name = "G", Store = GameStore.Manual, ExePath = @"C:\g\game.exe" };
        var row = new GameRowViewModel(game, new ExeOverrideState("game.exe", null, null), linkedCount: 2);
        Assert.True(row.IsLinked);
        Assert.True(row.HasGenericExeName);

        var row2 = new GameRowViewModel(game with { ExePath = @"C:\g\eldenring.exe" },
            new ExeOverrideState("eldenring.exe", null, null), linkedCount: 1);
        Assert.False(row2.IsLinked);
        Assert.False(row2.HasGenericExeName);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/GPM.Core.Tests --filter ExeOverrideStateTests`
Expected: FAIL — types don't exist.

- [ ] **Step 3: Implement**

`src/GPM.App/ViewModels/ExeOverrideState.cs`:
```csharp
using CommunityToolkit.Mvvm.ComponentModel;

namespace GPM.App.ViewModels;

/// The single shared pending-state object for one exe filename.
/// Every row with this filename holds the SAME instance (spec "IFEO semantics").
public sealed partial class ExeOverrideState : ObservableObject
{
    public ExeOverrideState(string exeName, int? registryCpu, int? registryIo)
    {
        ExeName = exeName.ToLowerInvariant();
        RegistryCpu = registryCpu;
        RegistryIo = registryIo;
        pendingCpu = registryCpu;
        pendingIo = registryIo;
    }

    public string ExeName { get; }
    public int? RegistryCpu { get; private set; }
    public int? RegistryIo { get; private set; }

    /// Informational: this exe's IFEO key carries a Debugger value (not GPM's — never touched).
    public bool HasDebugger { get; init; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsDirty))]
    private int? pendingCpu;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsDirty))]
    private int? pendingIo;

    [ObservableProperty]
    private string? lastError;

    public bool IsDirty => PendingCpu != RegistryCpu || PendingIo != RegistryIo;

    /// Called after re-reading the registry (source of truth). A failed write
    /// leaves the registry unchanged, so the row naturally stays dirty.
    public void SetRegistry(int? cpu, int? io)
    {
        RegistryCpu = cpu;
        RegistryIo = io;
        OnPropertyChanged(nameof(RegistryCpu));
        OnPropertyChanged(nameof(RegistryIo));
        OnPropertyChanged(nameof(IsDirty));
    }

    public void DiscardPending()
    {
        PendingCpu = RegistryCpu;
        PendingIo = RegistryIo;
        LastError = null;
    }
}
```

`src/GPM.App/ViewModels/GameRowViewModel.cs`:
```csharp
using GPM.Core.Models;

namespace GPM.App.ViewModels;

public sealed class GameRowViewModel(Game game, ExeOverrideState? state, int linkedCount)
{
    private static readonly HashSet<string> GenericNames = new(StringComparer.OrdinalIgnoreCase)
        { "game.exe", "launcher.exe", "start.exe", "main.exe", "app.exe", "run.exe" };

    public Game Game { get; } = game;
    /// null while the exe is unresolved (NeedsExeConfirmation without candidates).
    public ExeOverrideState? State { get; } = state;
    public bool IsLinked { get; } = linkedCount > 1;
    public bool NeedsExeConfirmation => Game.NeedsExeConfirmation;
    public bool HasGenericExeName { get; } =
        state is not null && GenericNames.Contains(state.ExeName);
    public string GenericExeWarning =>
        State is null ? "" : string.Format(Strings.GenericExeWarning, State.ExeName);
}
```

`src/GPM.App/ViewModels/OverrideRowViewModel.cs`:
```csharp
namespace GPM.App.ViewModels;

/// A row in the "Other overrides" section (spec "UI").
public sealed class OverrideRowViewModel(ExeOverrideState state, bool isOrphaned, bool hasDebugger)
{
    public ExeOverrideState State { get; } = state;
    /// GPM created it (provenance in games.json) but the game is gone -> cleanup suggested.
    public bool IsOrphaned { get; } = isOrphaned;
    /// Informational only — GPM never touches Debugger values.
    public bool HasDebugger { get; } = hasDebugger;
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test tests/GPM.Core.Tests --filter ExeOverrideStateTests`
Expected: PASS (6 tests).

- [ ] **Step 5: Commit**

```powershell
git add src/GPM.App/ViewModels tests/GPM.Core.Tests/ExeOverrideStateTests.cs
git commit -m "feat: filename-keyed override state and row viewmodels"
```

---

### Task D3: MainViewModel (load, dirty count, apply, discard, search)

**Files:**
- Create: `src/GPM.App/ViewModels/MainViewModel.cs`, `src/GPM.App/Services/IElevationLauncher.cs`
- Test: `tests/GPM.Core.Tests/MainViewModelTests.cs`

**Interfaces:**
- Consumes: `GameScanner`-shape via `IGameSource` list? **No** — to stay independent of plan B, MainViewModel takes `Func<Task<IReadOnlyList<Game>>> scanGames` (plan E passes `() => scanner.ScanAsync(cfg.BetaSourcesEnabled)`); plus `IPriorityService`, `IExeResolver`, `IConfigStore` (plan 0), `ApplyOp`/`ApplyReport`/`ApplyWorker.JsonOpts` (plan 0/A — only the record types and a local `JsonSerializerOptions`, no dependency on plan A code: define `private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);`).
- Produces: `MainViewModel(Func<Task<IReadOnlyList<Game>>> scanGames, IExeResolver resolver, IPriorityService priorities, IElevationLauncher elevation, IConfigStore config)` with: `ObservableCollection<GameRowViewModel> Rows`, `ObservableCollection<OverrideRowViewModel> OtherOverrides`, `string SearchText`, `int PendingCount`, `string? StatusMessage`, `Task LoadAsync()`, `IAsyncRelayCommand ApplyCommand`, `IRelayCommand DiscardCommand`, `IReadOnlyList<GameRowViewModel> VisibleRows`.
- `IElevationLauncher`: `Task<int?> RunApplyAsync(string opsPath)` — `null` means UAC declined. Real implementation in plan E.

- [ ] **Step 1: Write the failing tests**

`tests/GPM.Core.Tests/MainViewModelTests.cs`:
```csharp
using GPM.App.Services;
using GPM.App.ViewModels;
using GPM.Core.Models;
using GPM.Core.Persistence;
using GPM.Core.Priority;
using GPM.Core.Resolve;

namespace GPM.Core.Tests;

public class MainViewModelTests
{
    private sealed class PassthroughResolver : IExeResolver
    {
        public ExeResolution Resolve(Game game) => new(game.ExePath, false, []);
    }

    private sealed class FakeLauncher(InMemoryPriorityService svc, bool declineUac = false) : IElevationLauncher
    {
        public Task<int?> RunApplyAsync(string opsPath)
        {
            if (declineUac) return Task.FromResult<int?>(null);
            // simulate the elevated worker: apply ops straight to the fake service
            var ops = System.Text.Json.JsonSerializer.Deserialize<List<GPM.Core.Worker.ApplyOp>>(
                File.ReadAllText(opsPath), new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web))!;
            foreach (var op in ops) svc.Write(op.Exe, op.Cpu, op.Io);
            return Task.FromResult<int?>(0);
        }
    }

    private sealed class TempConfigStore : IConfigStore
    {
        private GpmConfig _cfg = new();
        public string FilePath => "test://games.json";
        public GpmConfig Load() => _cfg;
        public void Save(GpmConfig config) => _cfg = config;
    }

    private static Game G(string name, string exe, GameStore store = GameStore.Steam) =>
        new() { Name = name, Store = store, ExePath = exe };

    private static MainViewModel Make(InMemoryPriorityService svc, bool declineUac = false, params Game[] games) =>
        new(() => Task.FromResult<IReadOnlyList<Game>>(games),
            new PassthroughResolver(), svc, new FakeLauncher(svc, declineUac), new TempConfigStore());

    [Fact]
    public async Task load_builds_rows_and_reads_registry_state()
    {
        var svc = new InMemoryPriorityService();
        svc.Write("cs2.exe", 3, null);
        var vm = Make(svc, false, G("Counter-Strike 2", @"C:\s\cs2.exe"));

        await vm.LoadAsync();

        var row = Assert.Single(vm.Rows);
        Assert.Equal(3, row.State!.PendingCpu);
        Assert.Equal(0, vm.PendingCount);
    }

    [Fact]
    public async Task same_filename_across_stores_shares_one_state()
    {
        var svc = new InMemoryPriorityService();
        var vm = Make(svc, false,
            G("Game A", @"C:\steam\cs2.exe", GameStore.Steam),
            G("Game A", @"D:\other\CS2.exe", GameStore.Gog));

        await vm.LoadAsync();

        Assert.Same(vm.Rows[0].State, vm.Rows[1].State); // linked by construction
        Assert.True(vm.Rows[0].IsLinked);

        vm.Rows[0].State!.PendingCpu = 3;
        Assert.Equal(1, vm.PendingCount); // one pending change, not two
    }

    [Fact]
    public async Task apply_writes_ops_and_refresh_clears_dirty()
    {
        var svc = new InMemoryPriorityService();
        var vm = Make(svc, false, G("ELDEN RING", @"C:\g\eldenring.exe"));
        await vm.LoadAsync();

        vm.Rows[0].State!.PendingCpu = 3;
        vm.Rows[0].State!.PendingIo = 2;
        await vm.ApplyCommand.ExecuteAsync(null);

        Assert.Equal((3, 2), svc.Data["eldenring.exe"]);
        Assert.Equal(0, vm.PendingCount);
        Assert.False(vm.Rows[0].State!.IsDirty);
    }

    [Fact]
    public async Task uac_declined_keeps_changes_pending()
    {
        var svc = new InMemoryPriorityService();
        var vm = Make(svc, declineUac: true, G("ELDEN RING", @"C:\g\eldenring.exe"));
        await vm.LoadAsync();

        vm.Rows[0].State!.PendingCpu = 3;
        await vm.ApplyCommand.ExecuteAsync(null);

        Assert.Empty(svc.Data);                         // nothing written
        Assert.Equal(1, vm.PendingCount);               // still pending
        Assert.Equal(GPM.App.Strings.ApplyCancelled, vm.StatusMessage);
    }

    [Fact]
    public async Task discard_reverts_all_pending()
    {
        var svc = new InMemoryPriorityService();
        var vm = Make(svc, false, G("A", @"C:\a\a.exe"), G("B", @"C:\b\b.exe"));
        await vm.LoadAsync();

        vm.Rows[0].State!.PendingCpu = 3;
        vm.Rows[1].State!.PendingIo = 1;
        vm.DiscardCommand.Execute(null);

        Assert.Equal(0, vm.PendingCount);
    }

    [Fact]
    public async Task search_filters_visible_rows_by_name()
    {
        var svc = new InMemoryPriorityService();
        var vm = Make(svc, false, G("ELDEN RING", @"C:\g\er.exe"), G("Counter-Strike 2", @"C:\g\cs2.exe"));
        await vm.LoadAsync();

        vm.SearchText = "elden";
        Assert.Equal("ELDEN RING", Assert.Single(vm.VisibleRows).Game.Name);
    }

    [Fact]
    public async Task overrides_not_matching_games_land_in_other_overrides_with_orphan_flag()
    {
        var svc = new InMemoryPriorityService();
        svc.Write("voicemeeterpro.exe", 6, null);   // intentional non-game override
        svc.Write("uninstalled.exe", 3, null);      // was managed by GPM, game gone
        var cfgStore = new TempConfigStore();
        var cfg = cfgStore.Load();
        cfg.ManagedExes.Add("uninstalled.exe");
        cfgStore.Save(cfg);

        var vm = new MainViewModel(
            () => Task.FromResult<IReadOnlyList<Game>>([G("CS2", @"C:\g\cs2.exe")]),
            new PassthroughResolver(), svc, new FakeLauncher(svc), cfgStore);
        await vm.LoadAsync();

        Assert.Equal(2, vm.OtherOverrides.Count);
        Assert.False(vm.OtherOverrides.Single(o => o.State.ExeName == "voicemeeterpro.exe").IsOrphaned);
        Assert.True(vm.OtherOverrides.Single(o => o.State.ExeName == "uninstalled.exe").IsOrphaned);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/GPM.Core.Tests --filter MainViewModelTests`
Expected: FAIL — types don't exist.

- [ ] **Step 3: Implement**

`src/GPM.App/Services/IElevationLauncher.cs`:
```csharp
namespace GPM.App.Services;

public interface IElevationLauncher
{
    /// Relaunches GPM elevated with "--apply <opsPath>". Returns the worker's
    /// exit code, or null when the user declined the UAC prompt.
    Task<int?> RunApplyAsync(string opsPath);
}
```

`src/GPM.App/ViewModels/MainViewModel.cs`:
```csharp
using System.Collections.ObjectModel;
using System.IO;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GPM.App.Services;
using GPM.Core.Models;
using GPM.Core.Persistence;
using GPM.Core.Priority;
using GPM.Core.Resolve;
using GPM.Core.Worker;

namespace GPM.App.ViewModels;

public sealed partial class MainViewModel(
    Func<Task<IReadOnlyList<Game>>> scanGames,
    IExeResolver resolver,
    IPriorityService priorities,
    IElevationLauncher elevation,
    IConfigStore config) : ObservableObject
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly Dictionary<string, ExeOverrideState> _states = new(StringComparer.OrdinalIgnoreCase);

    public ObservableCollection<GameRowViewModel> Rows { get; } = [];
    public ObservableCollection<OverrideRowViewModel> OtherOverrides { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(VisibleRows))]
    private string searchText = "";

    [ObservableProperty] private int pendingCount;
    [ObservableProperty] private string? statusMessage;

    public IReadOnlyList<GameRowViewModel> VisibleRows =>
        string.IsNullOrWhiteSpace(SearchText)
            ? Rows.ToList()
            : Rows.Where(r => r.Game.Name.Contains(SearchText, StringComparison.OrdinalIgnoreCase)).ToList();

    public async Task LoadAsync()
    {
        var cfg = config.Load();
        var games = (await scanGames()).Concat(cfg.ManualGames).ToList();

        // resolve exes, then group rows by filename so linked rows share ONE state
        var resolved = games.Select(g =>
        {
            var r = resolver.Resolve(g);
            return g with
            {
                ExePath = r.ExePath ?? g.ExePath,
                NeedsExeConfirmation = r.NeedsConfirmation,
                ExeCandidates = r.Candidates,
            };
        }).ToList();

        var linkCounts = resolved.Where(g => g.ExeName is not null)
            .GroupBy(g => g.ExeName!, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.Count(), StringComparer.OrdinalIgnoreCase);

        Rows.Clear();
        _states.Clear();
        foreach (var game in resolved.OrderBy(g => g.Name, StringComparer.OrdinalIgnoreCase))
        {
            ExeOverrideState? state = null;
            if (game.ExeName is string exe)
            {
                state = GetOrCreateState(exe);
            }
            Rows.Add(new GameRowViewModel(game, state, state is null ? 0 : linkCounts[game.ExeName!]));
        }

        // Other overrides: everything in IFEO with an override that isn't a detected game
        OtherOverrides.Clear();
        var gameExes = new HashSet<string>(_states.Keys, StringComparer.OrdinalIgnoreCase);
        foreach (var o in priorities.ReadAll().Where(o => !gameExes.Contains(o.ExeName)))
        {
            var state = GetOrCreateState(o.ExeName);
            OtherOverrides.Add(new OverrideRowViewModel(state,
                isOrphaned: cfg.ManagedExes.Contains(o.ExeName),
                hasDebugger: o.HasDebugger));
        }

        RecountPending();
    }

    private ExeOverrideState GetOrCreateState(string exeName)
    {
        if (_states.TryGetValue(exeName, out var existing)) return existing;
        var actual = priorities.Read(exeName);
        var state = new ExeOverrideState(exeName, actual.Cpu, actual.Io) { HasDebugger = actual.HasDebugger };
        state.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(ExeOverrideState.IsDirty)) RecountPending();
        };
        _states[state.ExeName] = state;
        return state;
    }

    private void RecountPending() => PendingCount = _states.Values.Count(s => s.IsDirty);

    [RelayCommand]
    private void Discard()
    {
        foreach (var s in _states.Values) s.DiscardPending();
        StatusMessage = null;
    }

    [RelayCommand]
    private async Task ApplyAsync()
    {
        var dirty = _states.Values.Where(s => s.IsDirty).ToList();
        if (dirty.Count == 0) return;

        var ops = dirty.Select(s => new ApplyOp(s.ExeName, s.PendingCpu, s.PendingIo)).ToList();
        var opsPath = Path.Combine(Path.GetTempPath(), $"gpm-ops-{Guid.NewGuid():N}.json");
        await File.WriteAllTextAsync(opsPath, JsonSerializer.Serialize(ops, Json));

        var exit = await elevation.RunApplyAsync(opsPath);
        if (exit is null)
        {
            StatusMessage = Strings.ApplyCancelled; // changes stay pending (spec "Error handling")
            return;
        }

        // attach per-item errors from result.json when present
        var resultPath = Path.ChangeExtension(opsPath, ".result.json");
        if (File.Exists(resultPath) &&
            JsonSerializer.Deserialize<ApplyReport>(await File.ReadAllTextAsync(resultPath), Json)
                is { BatchRejected: false } report)
        {
            foreach (var r in report.Results.Where(r => !r.Success))
                if (_states.TryGetValue(r.Exe, out var s)) s.LastError = r.Error;
        }

        // registry is the source of truth: re-read; failed rows stay dirty naturally
        foreach (var s in dirty)
        {
            var actual = priorities.Read(s.ExeName);
            s.SetRegistry(actual.Cpu, actual.Io);
        }
        RecountPending();
        StatusMessage = null;

        // provenance: remember every exe GPM has managed (for orphan detection)
        var cfg = config.Load();
        foreach (var op in ops) cfg.ManagedExes.Add(op.Exe);
        config.Save(cfg);
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test tests/GPM.Core.Tests --filter MainViewModelTests`
Expected: PASS (7 tests).

- [ ] **Step 5: Commit**

```powershell
git add src/GPM.App/ViewModels/MainViewModel.cs src/GPM.App/Services/IElevationLauncher.cs tests/GPM.Core.Tests/MainViewModelTests.cs
git commit -m "feat: MainViewModel with linked-row pending state, apply/discard flow and other overrides"
```

---

### Task D4: MainWindow XAML (FluentWindow shell with mock data)

**Files:**
- Create: `src/GPM.App/MainWindow.xaml`, `src/GPM.App/MainWindow.xaml.cs` (replace template files), `src/GPM.App/DesignData/MockFactory.cs`
- Modify: `src/GPM.App/App.xaml`, `src/GPM.App/App.xaml.cs`

**Interfaces:**
- Consumes: `MainViewModel` (D3), `PriorityOptions`/`Strings` (D1), `InMemoryPriorityService` (plan 0).
- Produces: runnable UI shell on mock data. Plan E swaps `MockFactory` for the real composition root.

- [ ] **Step 1: Replace `src/GPM.App/App.xaml`**

```xml
<Application x:Class="GPM.App.App"
             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             xmlns:ui="http://schemas.lepo.co/wpfui/2022/xaml">
    <Application.Resources>
        <ResourceDictionary>
            <ResourceDictionary.MergedDictionaries>
                <ui:ThemesDictionary Theme="Dark" />
                <ui:ControlsDictionary />
            </ResourceDictionary.MergedDictionaries>
        </ResourceDictionary>
    </Application.Resources>
</Application>
```

`src/GPM.App/App.xaml.cs`:
```csharp
using System.Windows;

namespace GPM.App;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        var window = new MainWindow(DesignData.MockFactory.CreateMainViewModel());
        window.Show();
    }
}
```

Remove `StartupUri` from the template's App.xaml (it's already absent in the version above).

- [ ] **Step 2: Create the mock composition root**

`src/GPM.App/DesignData/MockFactory.cs`:
```csharp
using GPM.App.Services;
using GPM.App.ViewModels;
using GPM.Core.Models;
using GPM.Core.Persistence;
using GPM.Core.Priority;
using GPM.Core.Resolve;

namespace GPM.App.DesignData;

/// Fake wiring so the UI shell runs before plans A/B/C are merged.
/// Plan E replaces this with the real composition root.
public static class MockFactory
{
    private sealed class PassthroughResolver : IExeResolver
    {
        public ExeResolution Resolve(Game game) => new(game.ExePath, game.ExePath is null, []);
    }

    private sealed class MockLauncher(InMemoryPriorityService svc) : IElevationLauncher
    {
        public async Task<int?> RunApplyAsync(string opsPath)
        {
            var ops = System.Text.Json.JsonSerializer.Deserialize<List<GPM.Core.Worker.ApplyOp>>(
                await File.ReadAllTextAsync(opsPath),
                new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web))!;
            foreach (var op in ops) svc.Write(op.Exe, op.Cpu, op.Io);
            return 0;
        }
    }

    private sealed class MemoryConfigStore : IConfigStore
    {
        private GpmConfig _cfg = new();
        public string FilePath => "mock://games.json";
        public GpmConfig Load() => _cfg;
        public void Save(GpmConfig config) => _cfg = config;
    }

    public static MainViewModel CreateMainViewModel()
    {
        var svc = new InMemoryPriorityService();
        svc.Write("cs2.exe", 3, null);
        svc.Write("voicemeeterpro.exe", 6, null);
        IReadOnlyList<Game> games =
        [
            new() { Name = "Counter-Strike 2", Store = GameStore.Steam, ExePath = @"C:\mock\cs2.exe" },
            new() { Name = "ELDEN RING", Store = GameStore.Steam, ExePath = @"C:\mock\eldenring.exe" },
            new() { Name = "Rocket League", Store = GameStore.Epic, ExePath = @"C:\mock\RocketLeague.exe" },
            new() { Name = "Some UE Game", Store = GameStore.Gog, ExePath = @"C:\mock\game.exe" }, // generic-name warning
        ];
        var vm = new MainViewModel(() => Task.FromResult(games),
            new PassthroughResolver(), svc, new MockLauncher(svc), new MemoryConfigStore());
        _ = vm.LoadAsync();
        return vm;
    }
}
```

- [ ] **Step 3: Replace `src/GPM.App/MainWindow.xaml`** (layout mirrors the spec mockup)

```xml
<ui:FluentWindow x:Class="GPM.App.MainWindow"
        xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        xmlns:ui="http://schemas.lepo.co/wpfui/2022/xaml"
        xmlns:vm="clr-namespace:GPM.App.ViewModels"
        xmlns:app="clr-namespace:GPM.App"
        Title="{x:Static app:Strings.AppTitle}"
        Width="900" Height="620" WindowStartupLocation="CenterScreen"
        ExtendsContentIntoTitleBar="True" WindowBackdropType="Mica">
    <Grid>
        <Grid.RowDefinitions>
            <RowDefinition Height="Auto" />  <!-- title bar -->
            <RowDefinition Height="Auto" />  <!-- toolbar -->
            <RowDefinition Height="*" />     <!-- game list -->
            <RowDefinition Height="Auto" />  <!-- other overrides -->
            <RowDefinition Height="Auto" />  <!-- bottom bar -->
        </Grid.RowDefinitions>

        <ui:TitleBar Grid.Row="0" Title="{x:Static app:Strings.AppTitle}" />

        <DockPanel Grid.Row="1" Margin="12,4,12,8">
            <ui:Button DockPanel.Dock="Right" Margin="8,0,0,0"
                       Content="{x:Static app:Strings.AddGame}" />
            <ui:TextBox PlaceholderText="{x:Static app:Strings.SearchPlaceholder}"
                        Icon="{ui:SymbolIcon Search24}"
                        Text="{Binding SearchText, UpdateSourceTrigger=PropertyChanged}" />
        </DockPanel>

        <ListView Grid.Row="2" Margin="12,0" ItemsSource="{Binding VisibleRows}"
                  ScrollViewer.HorizontalScrollBarVisibility="Disabled">
            <ListView.ItemTemplate>
                <DataTemplate DataType="{x:Type vm:GameRowViewModel}">
                    <Grid Margin="0,2">
                        <Grid.ColumnDefinitions>
                            <ColumnDefinition Width="*" />
                            <ColumnDefinition Width="90" />
                            <ColumnDefinition Width="170" />
                            <ColumnDefinition Width="130" />
                            <ColumnDefinition Width="24" />
                        </Grid.ColumnDefinitions>
                        <TextBlock Grid.Column="0" Text="{Binding Game.Name}" VerticalAlignment="Center" />
                        <TextBlock Grid.Column="1" Text="{Binding Game.Store}" Opacity="0.6" VerticalAlignment="Center" />
                        <StackPanel Grid.Column="2" Orientation="Horizontal" VerticalAlignment="Center">
                            <TextBlock Text="{Binding State.ExeName}" Opacity="0.6" />
                            <ui:SymbolIcon Symbol="Link24" Margin="4,0,0,0"
                                           Visibility="{Binding IsLinked, Converter={StaticResource BoolToVis}}"
                                           ToolTip="{x:Static app:Strings.LinkedRowsTooltip}" />
                            <ui:SymbolIcon Symbol="Warning24" Margin="4,0,0,0" Foreground="Orange"
                                           Visibility="{Binding HasGenericExeName, Converter={StaticResource BoolToVis}}"
                                           ToolTip="{Binding GenericExeWarning}" />
                            <ui:SymbolIcon Symbol="Bug24" Margin="4,0,0,0" Foreground="OrangeRed"
                                           Visibility="{Binding State.HasDebugger, Converter={StaticResource BoolToVis}}"
                                           ToolTip="{x:Static app:Strings.DebuggerWarning}" />
                        </StackPanel>
                        <ComboBox Grid.Column="3"
                                  ItemsSource="{x:Static vm:PriorityOptions.Cpu}"
                                  DisplayMemberPath="Label" SelectedValuePath="Value"
                                  SelectedValue="{Binding State.PendingCpu}" />
                        <TextBlock Grid.Column="4" Text="●" Foreground="#4cc2ff" VerticalAlignment="Center"
                                   HorizontalAlignment="Center"
                                   Visibility="{Binding State.IsDirty, Converter={StaticResource BoolToVis}}" />
                    </Grid>
                </DataTemplate>
            </ListView.ItemTemplate>
        </ListView>

        <Expander Grid.Row="3" Margin="12,4" IsExpanded="False">
            <Expander.Header>
                <TextBlock>
                    <Run Text="{Binding OtherOverrides.Count, Mode=OneWay, StringFormat=Other overrides ({0})}" />
                </TextBlock>
            </Expander.Header>
            <ItemsControl ItemsSource="{Binding OtherOverrides}">
                <ItemsControl.ItemTemplate>
                    <DataTemplate DataType="{x:Type vm:OverrideRowViewModel}">
                        <Grid Margin="0,2">
                            <Grid.ColumnDefinitions>
                                <ColumnDefinition Width="*" />
                                <ColumnDefinition Width="24" />
                                <ColumnDefinition Width="130" />
                            </Grid.ColumnDefinitions>
                            <TextBlock Grid.Column="0" Text="{Binding State.ExeName}" VerticalAlignment="Center" />
                            <ui:SymbolIcon Grid.Column="1" Symbol="Broom24"
                                           Visibility="{Binding IsOrphaned, Converter={StaticResource BoolToVis}}"
                                           ToolTip="{x:Static app:Strings.OrphanedTooltip}" />
                            <ComboBox Grid.Column="2"
                                      ItemsSource="{x:Static vm:PriorityOptions.Cpu}"
                                      DisplayMemberPath="Label" SelectedValuePath="Value"
                                      SelectedValue="{Binding State.PendingCpu}" />
                        </Grid>
                    </DataTemplate>
                </ItemsControl.ItemTemplate>
            </ItemsControl>
        </Expander>

        <DockPanel Grid.Row="4" Margin="12,8,12,12">
            <ui:Button DockPanel.Dock="Right" Appearance="Primary"
                       Icon="{ui:SymbolIcon ShieldCheckmark24}"
                       Content="{x:Static app:Strings.Apply}"
                       Command="{Binding ApplyCommand}" />
            <ui:Button DockPanel.Dock="Right" Margin="0,0,8,0"
                       Content="{x:Static app:Strings.Discard}"
                       Command="{Binding DiscardCommand}" />
            <TextBlock VerticalAlignment="Center">
                <Run Text="{Binding PendingCount, Mode=OneWay}" /> <Run Text="pending" />
                <Run Text="{Binding StatusMessage, Mode=OneWay}" Foreground="Orange" />
            </TextBlock>
        </DockPanel>
    </Grid>
</ui:FluentWindow>
```

Add the converter to `App.xaml` resources (inside `ResourceDictionary`, after the merged dictionaries):
```xml
            <BooleanToVisibilityConverter x:Key="BoolToVis" />
```

`src/GPM.App/MainWindow.xaml.cs`:
```csharp
using GPM.App.ViewModels;

namespace GPM.App;

public partial class MainWindow
{
    public MainWindow(MainViewModel viewModel)
    {
        DataContext = viewModel;
        InitializeComponent();
        Wpf.Ui.Appearance.SystemThemeWatcher.Watch(this); // follow OS dark/light
    }
}
```

Note: the `PendingChanges`/`OtherOverrides` counts use simplified `Run` bindings; the exact "{0} pending changes" resx format string is wired in plan E polish if desired — the resx entries already exist.

- [ ] **Step 4: Verify it builds and runs**

Run: `dotnet build src/GPM.App` — expected: `Build succeeded`.
Run: `dotnet run --project src/GPM.App` — expected: Fluent window opens with 4 mock games; changing CS2's CPU dropdown marks the row `●` and pending count = 1; Apply clears it; the "Some UE Game" row shows the generic-name warning icon; "Other overrides (1)" lists `voicemeeterpro.exe`. Close the window.

- [ ] **Step 5: Run the whole test suite and commit**

Run: `dotnet test tests/GPM.Core.Tests`
Expected: all green.

```powershell
git add src/GPM.App
git commit -m "feat: FluentWindow UI shell running on mock data"
```

Plan D complete — ready for review/merge per superpowers:finishing-a-development-branch.
