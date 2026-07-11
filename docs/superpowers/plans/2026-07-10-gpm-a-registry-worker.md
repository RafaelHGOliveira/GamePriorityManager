# GPM Plan A — Registry & Elevated Worker

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Implement the IFEO registry layer (`RealRegistryRoot`, `PriorityService`) and the elevated worker (`OpsValidator`, `ApplyWorker`) — everything that actually reads and writes priorities.

**Architecture:** `PriorityService` talks to the registry only through the injected `IRegistryRoot` (tests point it at an HKCU key, production at the HKLM IFEO root). The worker is pure library code here — the `--apply` exe wiring happens in plan E.

**Tech Stack:** .NET 10, `Microsoft.Win32` registry APIs, `System.Text.Json`, xUnit.

**Prerequisites:** Plan 0 (foundation) merged to `main`. Work on branch/worktree `ws-a-registry-worker`. Only touch the files listed below — no other plan touches them.

**Spec:** `docs/superpowers/specs/2026-07-10-gpm-mvp-design.md` — sections "PriorityService", "IFEO semantics", "Elevated worker protocol".

## Global Constraints

- CPU dwords: `1` Idle, `5` Below Normal, `2` Normal, `6` Above Normal, `3` High. **Realtime (4) always rejected.** IO: `0`–`3`.
- Writes touch ONLY `CpuPriorityClass` and `IoPriority` under `<exe>\PerfOptions`. Never any other value (e.g. `Debugger`) or key.
- "Default" = delete the value; delete `PerfOptions` and the exe key when they become empty — leave the registry as found.
- The worker trusts nothing from the UI: it re-validates every op (ops.json sits in %TEMP%, TOCTOU window during the UAC prompt).
- Registry tests use a unique key per test class under `HKCU\Software\GPM-Test\` and delete it on dispose. No admin required.
- Commits: conventional prefixes, **never add a `Co-Authored-By` trailer**.

---

### Task A1: RealRegistryRoot

**Files:**
- Create: `src/GPM.Core/Registry/RealRegistryRoot.cs`
- Test: `tests/GPM.Core.Tests/RealRegistryRootTests.cs`

**Interfaces:**
- Consumes: `IRegistryRoot` (plan 0, `GPM.Core.Registry`).
- Produces: `RealRegistryRoot(RegistryKey hive, string rootPath) : IRegistryRoot` — used by plan E's composition root as `new RealRegistryRoot(RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64), PriorityValues.IfeoPath)`.

- [ ] **Step 1: Write the failing tests**

`tests/GPM.Core.Tests/RealRegistryRootTests.cs`:
```csharp
using GPM.Core.Registry;
using Microsoft.Win32;

namespace GPM.Core.Tests;

public sealed class RealRegistryRootTests : IDisposable
{
    private readonly string _path = $@"Software\GPM-Test\{Guid.NewGuid():N}";
    private readonly RealRegistryRoot _root;

    public RealRegistryRootTests()
    {
        Registry.CurrentUser.CreateSubKey(_path);
        _root = new RealRegistryRoot(Registry.CurrentUser, _path);
    }

    public void Dispose() =>
        Registry.CurrentUser.DeleteSubKeyTree(_path, throwOnMissingSubKey: false);

    [Fact]
    public void set_get_delete_dword_roundtrip()
    {
        _root.SetDword(@"a.exe\PerfOptions", "CpuPriorityClass", 3);
        Assert.Equal(3, _root.GetDword(@"a.exe\PerfOptions", "CpuPriorityClass"));
        _root.DeleteValue(@"a.exe\PerfOptions", "CpuPriorityClass");
        Assert.Null(_root.GetDword(@"a.exe\PerfOptions", "CpuPriorityClass"));
    }

    [Fact]
    public void get_on_missing_key_returns_null_not_throw()
    {
        Assert.Null(_root.GetDword(@"nope.exe\PerfOptions", "CpuPriorityClass"));
        Assert.Empty(_root.GetValueNames("nope.exe"));
        Assert.False(_root.KeyExists("nope.exe"));
    }

    [Fact]
    public void delete_subkey_if_empty_only_deletes_empty_keys()
    {
        _root.SetDword(@"b.exe\PerfOptions", "IoPriority", 1);
        _root.DeleteSubKeyIfEmpty(@"b.exe\PerfOptions"); // not empty -> stays
        Assert.True(_root.KeyExists(@"b.exe\PerfOptions"));

        _root.DeleteValue(@"b.exe\PerfOptions", "IoPriority");
        _root.DeleteSubKeyIfEmpty(@"b.exe\PerfOptions"); // now empty -> gone
        _root.DeleteSubKeyIfEmpty("b.exe");
        Assert.False(_root.KeyExists("b.exe"));
    }

    [Fact]
    public void enumerates_subkeys_and_values()
    {
        _root.SetDword(@"c.exe\PerfOptions", "CpuPriorityClass", 1);
        Assert.Contains("c.exe", _root.GetSubKeyNames());
        Assert.Contains("CpuPriorityClass", _root.GetValueNames(@"c.exe\PerfOptions"));
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/GPM.Core.Tests --filter RealRegistryRootTests`
Expected: FAIL — `RealRegistryRoot` does not exist (compile error).

- [ ] **Step 3: Implement**

`src/GPM.Core/Registry/RealRegistryRoot.cs`:
```csharp
using Microsoft.Win32;

namespace GPM.Core.Registry;

public sealed class RealRegistryRoot(RegistryKey hive, string rootPath) : IRegistryRoot
{
    private RegistryKey? OpenRoot(bool writable = false) => hive.OpenSubKey(rootPath, writable);

    public IReadOnlyList<string> GetSubKeyNames()
    {
        using var root = OpenRoot();
        return root?.GetSubKeyNames() ?? [];
    }

    public IReadOnlyList<string> GetValueNames(string subKey)
    {
        using var root = OpenRoot();
        using var key = root?.OpenSubKey(subKey);
        return key?.GetValueNames() ?? [];
    }

    public int? GetDword(string subKey, string valueName)
    {
        using var root = OpenRoot();
        using var key = root?.OpenSubKey(subKey);
        return key?.GetValue(valueName) as int?;
    }

    public void SetDword(string subKey, string valueName, int value)
    {
        using var root = hive.CreateSubKey(rootPath);
        using var key = root.CreateSubKey(subKey);
        key.SetValue(valueName, value, RegistryValueKind.DWord);
    }

    public void DeleteValue(string subKey, string valueName)
    {
        using var root = OpenRoot(writable: true);
        using var key = root?.OpenSubKey(subKey, writable: true);
        key?.DeleteValue(valueName, throwOnMissingValue: false);
    }

    public void DeleteSubKeyIfEmpty(string subKey)
    {
        using var root = OpenRoot(writable: true);
        if (root is null) return;
        using (var key = root.OpenSubKey(subKey))
        {
            if (key is null || key.ValueCount > 0 || key.SubKeyCount > 0) return;
        }
        root.DeleteSubKey(subKey, throwOnMissingSubKey: false);
    }

    public bool KeyExists(string subKey)
    {
        using var root = OpenRoot();
        using var key = root?.OpenSubKey(subKey);
        return key is not null;
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test tests/GPM.Core.Tests --filter RealRegistryRootTests`
Expected: PASS (4 tests).

- [ ] **Step 5: Commit**

```powershell
git add src/GPM.Core/Registry/RealRegistryRoot.cs tests/GPM.Core.Tests/RealRegistryRootTests.cs
git commit -m "feat: RealRegistryRoot IRegistryRoot implementation"
```

---

### Task A2: PriorityService

**Files:**
- Create: `src/GPM.Core/Priority/PriorityService.cs`
- Test: `tests/GPM.Core.Tests/PriorityServiceTests.cs`

**Interfaces:**
- Consumes: `IRegistryRoot`, `IPriorityService`, `PriorityOverride`, `PriorityValues` (plan 0); `RealRegistryRoot` (A1, tests only).
- Produces: `PriorityService(IRegistryRoot ifeo) : IPriorityService` — consumed by `ApplyWorker` (A4) and plan E.

- [ ] **Step 1: Write the failing tests**

`tests/GPM.Core.Tests/PriorityServiceTests.cs`:
```csharp
using GPM.Core.Priority;
using GPM.Core.Registry;
using Microsoft.Win32;

namespace GPM.Core.Tests;

public sealed class PriorityServiceTests : IDisposable
{
    private readonly string _path = $@"Software\GPM-Test\{Guid.NewGuid():N}";
    private readonly RealRegistryRoot _root;
    private readonly PriorityService _svc;

    public PriorityServiceTests()
    {
        Registry.CurrentUser.CreateSubKey(_path);
        _root = new RealRegistryRoot(Registry.CurrentUser, _path);
        _svc = new PriorityService(_root);
    }

    public void Dispose() =>
        Registry.CurrentUser.DeleteSubKeyTree(_path, throwOnMissingSubKey: false);

    [Fact]
    public void write_then_read_roundtrips()
    {
        _svc.Write("eldenring.exe", 3, 2);
        var o = _svc.Read("eldenring.exe");
        Assert.Equal(3, o.Cpu);
        Assert.Equal(2, o.Io);
        Assert.False(o.HasDebugger);
    }

    [Fact]
    public void read_normalizes_exename_to_lowercase()
    {
        _svc.Write("CS2.exe", 3, null);
        Assert.Equal("cs2.exe", _svc.Read("CS2.exe").ExeName);
    }

    [Fact]
    public void null_deletes_value_and_cleans_empty_keys()
    {
        _svc.Write("cs2.exe", 3, 1);
        _svc.Write("cs2.exe", null, 1);          // cpu removed, io stays
        Assert.Null(_svc.Read("cs2.exe").Cpu);
        Assert.Equal(1, _svc.Read("cs2.exe").Io);

        _svc.Write("cs2.exe", null, null);       // all gone -> keys cleaned
        Assert.False(_root.KeyExists("cs2.exe"));
    }

    [Fact]
    public void write_never_touches_other_values()
    {
        _root.SetDword(@"game.exe\PerfOptions", "SomethingElse", 42);
        _svc.Write("game.exe", 3, null);
        _svc.Write("game.exe", null, null);
        Assert.Equal(42, _root.GetDword(@"game.exe\PerfOptions", "SomethingElse"));
        Assert.True(_root.KeyExists(@"game.exe\PerfOptions")); // not empty -> kept
    }

    [Theory]
    [InlineData(4)]   // Realtime
    [InlineData(0)]
    [InlineData(7)]
    public void invalid_cpu_is_rejected(int cpu) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => _svc.Write("x.exe", cpu, null));

    [Theory]
    [InlineData(-1)]
    [InlineData(4)]
    public void invalid_io_is_rejected(int io) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => _svc.Write("x.exe", null, io));

    [Fact]
    public void readall_lists_only_keys_with_overrides_and_flags_debugger()
    {
        _svc.Write("a.exe", 3, null);
        _root.SetDword(@"b.exe\PerfOptions", "IoPriority", 0);
        _root.SetDword("c.exe", "GlobalFlag", 1);              // no override -> excluded
        using (var key = Registry.CurrentUser.CreateSubKey($@"{_path}\a.exe"))
            key.SetValue("Debugger", @"C:\evil.exe");

        var all = _svc.ReadAll();
        Assert.Equal(2, all.Count);
        Assert.True(all.Single(o => o.ExeName == "a.exe").HasDebugger);
        Assert.False(all.Single(o => o.ExeName == "b.exe").HasDebugger);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/GPM.Core.Tests --filter PriorityServiceTests`
Expected: FAIL — `PriorityService` does not exist.

- [ ] **Step 3: Implement**

`src/GPM.Core/Priority/PriorityService.cs`:
```csharp
using GPM.Core.Registry;

namespace GPM.Core.Priority;

public sealed class PriorityService(IRegistryRoot ifeo) : IPriorityService
{
    public PriorityOverride Read(string exeName)
    {
        var perf = $@"{exeName}\{PriorityValues.PerfOptions}";
        return new PriorityOverride(
            exeName.ToLowerInvariant(),
            ifeo.GetDword(perf, PriorityValues.CpuValueName),
            ifeo.GetDword(perf, PriorityValues.IoValueName),
            HasDebugger: ifeo.GetValueNames(exeName)
                .Contains("Debugger", StringComparer.OrdinalIgnoreCase));
    }

    public IReadOnlyList<PriorityOverride> ReadAll() =>
        ifeo.GetSubKeyNames()
            .Select(Read)
            .Where(o => o.Cpu is not null || o.Io is not null)
            .ToList();

    public void Write(string exeName, int? cpu, int? io)
    {
        if (cpu is int c && !PriorityValues.ValidCpu.Contains(c))
            throw new ArgumentOutOfRangeException(nameof(cpu), c, "value not allowed");
        if (io is int i && !PriorityValues.ValidIo.Contains(i))
            throw new ArgumentOutOfRangeException(nameof(io), i, "value not allowed");

        var perf = $@"{exeName}\{PriorityValues.PerfOptions}";
        Apply(perf, PriorityValues.CpuValueName, cpu);
        Apply(perf, PriorityValues.IoValueName, io);
        ifeo.DeleteSubKeyIfEmpty(perf);      // leave the registry as found
        ifeo.DeleteSubKeyIfEmpty(exeName);
    }

    private void Apply(string perfKey, string valueName, int? value)
    {
        if (value is null) ifeo.DeleteValue(perfKey, valueName);
        else ifeo.SetDword(perfKey, valueName, value.Value);
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test tests/GPM.Core.Tests --filter PriorityServiceTests`
Expected: PASS (9 tests).

- [ ] **Step 5: Commit**

```powershell
git add src/GPM.Core/Priority/PriorityService.cs tests/GPM.Core.Tests/PriorityServiceTests.cs
git commit -m "feat: PriorityService IFEO read/write with key cleanup and Debugger flag"
```

---

### Task A3: OpsValidator

**Files:**
- Create: `src/GPM.Core/Worker/OpsValidator.cs`
- Test: `tests/GPM.Core.Tests/OpsValidatorTests.cs`

**Interfaces:**
- Consumes: `ApplyOp`, `PriorityValues` (plan 0).
- Produces: `static string? OpsValidator.Validate(IReadOnlyList<ApplyOp>? ops)` — returns `null` when valid, else the batch-rejection reason. Consumed by A4.

- [ ] **Step 1: Write the failing tests**

`tests/GPM.Core.Tests/OpsValidatorTests.cs`:
```csharp
using GPM.Core.Worker;

namespace GPM.Core.Tests;

public class OpsValidatorTests
{
    private static ApplyOp Op(string exe, int? cpu = 3, int? io = null) => new(exe, cpu, io);

    [Fact]
    public void valid_batch_returns_null()
    {
        Assert.Null(OpsValidator.Validate([Op("eldenring.exe"), Op("cs2.exe", null, 2), Op("a.exe", null, null)]));
    }

    [Fact]
    public void empty_or_null_batch_rejected()
    {
        Assert.NotNull(OpsValidator.Validate(null));
        Assert.NotNull(OpsValidator.Validate([]));
    }

    [Theory]
    [InlineData(@"..\evil.exe")]          // path separator
    [InlineData("dir/evil.exe")]          // path separator
    [InlineData("evil.dll")]              // not .exe
    [InlineData("evil")]                  // not .exe
    [InlineData("")]                      // empty
    [InlineData("bad|name.exe")]          // invalid filename char
    public void bad_exe_rejects_batch(string exe) =>
        Assert.NotNull(OpsValidator.Validate([Op(exe)]));

    [Fact]
    public void exe_longer_than_255_chars_rejected() =>
        Assert.NotNull(OpsValidator.Validate([Op(new string('a', 252) + ".exe")]));

    [Theory]
    [InlineData(4)]   // Realtime
    [InlineData(0)]
    public void invalid_cpu_rejects_batch(int cpu) =>
        Assert.NotNull(OpsValidator.Validate([Op("a.exe", cpu)]));

    [Theory]
    [InlineData(-1)]
    [InlineData(4)]
    public void invalid_io_rejects_batch(int io) =>
        Assert.NotNull(OpsValidator.Validate([Op("a.exe", null, io)]));

    [Fact]
    public void duplicate_exe_rejects_whole_batch_case_insensitive() =>
        Assert.NotNull(OpsValidator.Validate([Op("a.exe"), Op("A.EXE")]));
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/GPM.Core.Tests --filter OpsValidatorTests`
Expected: FAIL — `OpsValidator` does not exist.

- [ ] **Step 3: Implement**

`src/GPM.Core/Worker/OpsValidator.cs`:
```csharp
using GPM.Core.Priority;

namespace GPM.Core.Worker;

/// The elevated worker trusts nothing from the UI (ops.json sits in %TEMP% —
/// TOCTOU window while the UAC prompt is up). ANY invalid op rejects the batch.
public static class OpsValidator
{
    public static string? Validate(IReadOnlyList<ApplyOp>? ops)
    {
        if (ops is null || ops.Count == 0) return "empty batch";
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var op in ops)
        {
            if (string.IsNullOrWhiteSpace(op.Exe)) return "missing exe name";
            if (op.Exe.Length > 255) return "exe name too long";
            if (op.Exe.Contains('\\') || op.Exe.Contains('/'))
                return $"path separator in exe name: {op.Exe}";
            if (!op.Exe.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                return $"not an .exe filename: {op.Exe}";
            if (op.Exe.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
                return $"invalid characters in exe name: {op.Exe}";
            if (!seen.Add(op.Exe)) return $"duplicate exe in batch: {op.Exe}";
            if (op.Cpu is int c && !PriorityValues.ValidCpu.Contains(c))
                return $"invalid cpu value {c} for {op.Exe}";
            if (op.Io is int i && !PriorityValues.ValidIo.Contains(i))
                return $"invalid io value {i} for {op.Exe}";
        }
        return null;
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test tests/GPM.Core.Tests --filter OpsValidatorTests`
Expected: PASS (13 tests).

- [ ] **Step 5: Commit**

```powershell
git add src/GPM.Core/Worker/OpsValidator.cs tests/GPM.Core.Tests/OpsValidatorTests.cs
git commit -m "feat: OpsValidator batch validation for the elevated worker"
```

---

### Task A4: ApplyWorker

**Files:**
- Create: `src/GPM.Core/Worker/ApplyWorker.cs`
- Test: `tests/GPM.Core.Tests/ApplyWorkerTests.cs`

**Interfaces:**
- Consumes: `OpsValidator` (A3), `IPriorityService`/`InMemoryPriorityService` (plan 0), `ApplyOp`/`ApplyReport`/`ApplyOpResult` (plan 0).
- Produces: `static int ApplyWorker.Run(string opsPath, IPriorityService priorities)` + exit-code constants + `static readonly JsonSerializerOptions ApplyWorker.JsonOpts` (camelCase). result.json path = `Path.ChangeExtension(opsPath, ".result.json")`. Consumed by plan E (App `--apply` entry) and plan D (result reading).

- [ ] **Step 1: Write the failing tests**

`tests/GPM.Core.Tests/ApplyWorkerTests.cs`:
```csharp
using System.Text.Json;
using GPM.Core.Priority;
using GPM.Core.Worker;

namespace GPM.Core.Tests;

public class ApplyWorkerTests
{
    private static string TempOps(string json)
    {
        var path = Path.Combine(Path.GetTempPath(), $"gpm-test-{Guid.NewGuid():N}.json");
        File.WriteAllText(path, json);
        return path;
    }

    private static ApplyReport ReadReport(string opsPath) =>
        JsonSerializer.Deserialize<ApplyReport>(
            File.ReadAllText(Path.ChangeExtension(opsPath, ".result.json")),
            ApplyWorker.JsonOpts)!;

    [Fact]
    public void valid_batch_applies_and_exits_0()
    {
        var svc = new InMemoryPriorityService();
        var ops = TempOps("""[{"exe":"eldenring.exe","cpu":3,"io":2},{"exe":"cs2.exe","cpu":null,"io":null}]""");
        svc.Write("cs2.exe", 3, null); // pre-existing override that the second op removes

        Assert.Equal(ApplyWorker.ExitOk, ApplyWorker.Run(ops, svc));
        Assert.Equal((3, 2), svc.Data["eldenring.exe"]);
        Assert.False(svc.Data.ContainsKey("cs2.exe"));

        var report = ReadReport(ops);
        Assert.False(report.BatchRejected);
        Assert.All(report.Results, r => Assert.True(r.Success));
    }

    [Fact]
    public void invalid_batch_is_rejected_applies_nothing_exits_3()
    {
        var svc = new InMemoryPriorityService();
        var ops = TempOps("""[{"exe":"good.exe","cpu":3,"io":null},{"exe":"..\\evil.exe","cpu":3,"io":null}]""");

        Assert.Equal(ApplyWorker.ExitRejected, ApplyWorker.Run(ops, svc));
        Assert.Empty(svc.Data);                       // nothing applied
        Assert.True(ReadReport(ops).BatchRejected);
    }

    [Fact]
    public void partial_failure_exits_2_and_reports_per_item()
    {
        var svc = new ThrowOnSecondWriteService();
        var ops = TempOps("""[{"exe":"a.exe","cpu":3,"io":null},{"exe":"b.exe","cpu":3,"io":null}]""");

        Assert.Equal(ApplyWorker.ExitPartial, ApplyWorker.Run(ops, svc));
        var report = ReadReport(ops);
        Assert.True(report.Results.Single(r => r.Exe == "a.exe").Success);
        var failed = report.Results.Single(r => r.Exe == "b.exe");
        Assert.False(failed.Success);
        Assert.NotNull(failed.Error);
    }

    [Fact]
    public void garbage_json_exits_4_with_rejected_report()
    {
        var ops = TempOps("{not json[");
        Assert.Equal(ApplyWorker.ExitCrash, ApplyWorker.Run(ops, new InMemoryPriorityService()));
        Assert.True(ReadReport(ops).BatchRejected);
    }

    private sealed class ThrowOnSecondWriteService : IPriorityService
    {
        private int _writes;
        public PriorityOverride Read(string exeName) => new(exeName, null, null, false);
        public IReadOnlyList<PriorityOverride> ReadAll() => [];
        public void Write(string exeName, int? cpu, int? io)
        {
            if (++_writes > 1) throw new UnauthorizedAccessException("access denied");
        }
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/GPM.Core.Tests --filter ApplyWorkerTests`
Expected: FAIL — `ApplyWorker` does not exist.

- [ ] **Step 3: Implement**

`src/GPM.Core/Worker/ApplyWorker.cs`:
```csharp
using System.Text.Json;
using GPM.Core.Priority;

namespace GPM.Core.Worker;

public static class ApplyWorker
{
    public const int ExitOk = 0;
    public const int ExitPartial = 2;   // some ops failed
    public const int ExitRejected = 3;  // batch failed validation
    public const int ExitCrash = 4;     // unreadable input / unexpected error

    public static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);

    public static int Run(string opsPath, IPriorityService priorities)
    {
        var resultPath = Path.ChangeExtension(opsPath, ".result.json");
        try
        {
            var ops = JsonSerializer.Deserialize<List<ApplyOp>>(File.ReadAllText(opsPath), JsonOpts);
            if (OpsValidator.Validate(ops) is string batchError)
            {
                WriteReport(resultPath, new ApplyReport(true, batchError, []));
                return ExitRejected;
            }

            var results = new List<ApplyOpResult>();
            foreach (var op in ops!)
            {
                try
                {
                    priorities.Write(op.Exe.ToLowerInvariant(), op.Cpu, op.Io);
                    results.Add(new(op.Exe, true, null));
                }
                catch (Exception ex)
                {
                    results.Add(new(op.Exe, false, ex.Message));
                }
            }
            WriteReport(resultPath, new ApplyReport(false, null, results));
            return results.All(r => r.Success) ? ExitOk : ExitPartial;
        }
        catch (Exception ex)
        {
            try { WriteReport(resultPath, new ApplyReport(true, ex.Message, [])); } catch { /* best effort */ }
            return ExitCrash;
        }
    }

    private static void WriteReport(string path, ApplyReport report) =>
        File.WriteAllText(path, JsonSerializer.Serialize(report, JsonOpts));
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test tests/GPM.Core.Tests --filter ApplyWorkerTests`
Expected: PASS (4 tests). Then run the whole suite: `dotnet test tests/GPM.Core.Tests` — all green.

- [ ] **Step 5: Commit**

```powershell
git add src/GPM.Core/Worker/ApplyWorker.cs tests/GPM.Core.Tests/ApplyWorkerTests.cs
git commit -m "feat: ApplyWorker with validation, per-op results and exit codes"
```

Plan A complete — ready for review/merge per superpowers:finishing-a-development-branch.
