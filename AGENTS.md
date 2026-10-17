# GPM (Game Priority Manager)

Windows WPF app that sets per-exe CPU/IO priority via the registry (IFEO `PerfOptions`). Solution: `GamePriorityManager.sln` — `GPM.Core` (logic), `GPM.App` (WPF UI), `GPM.Probe` (standalone effectiveness probe), `GPM.Core.Tests` (xUnit).

## Current state
See `handoff.md` at repo root for live status. Implementation plans (if similar
work recurs): `docs/superpowers/plans/2026-07-10-gpm-{0,a,b,c,d,e}-*.md`.

## Build/test
- `dotnet --list-sdks` - verify the SDK (not just runtimes) is present before assuming `dotnet build`/`test` work; targets `net10.0-windows`.
- `dotnet build GamePriorityManager.sln -c Release`
- `dotnet test tests/GPM.Core.Tests -c Release --no-build`
- Registry tests run without admin: unique key per test class under `HKCU\Software\GPM-Test\...`, deleted on Dispose.

## Gotchas
- `GPM.Core.Registry` is a namespace in this codebase, so an unqualified `Registry.CurrentUser` inside `GPM.Core.*` resolves to that sibling namespace instead of `Microsoft.Win32.Registry` (CS0234). Always fully-qualify: `Microsoft.Win32.Registry.CurrentUser`.
- `git worktree remove` under `.worktrees/` can fail with "Device or resource busy" after `dotnet build`/`test` — leftover MSBuild node-reuse `dotnet.exe` server processes hold locks in that worktree's `bin`/`obj`. Check `Get-CimInstance Win32_Process` for stale `dotnet.exe` before assuming it's stuck.
- Never background a whole-filesystem `find /` on this machine — it can run indefinitely and locks whatever directory it's currently traversing, which can later block unrelated `git worktree remove`/deletes. Use Glob instead.

## Domain invariants
- CPU priority dwords: Idle=1, BelowNormal=5, Normal=2, AboveNormal=6, High=3. **Realtime (4) is forbidden everywhere.** IO: 0-3 (Very Low/Low/Normal/High).
- Registry writes touch ONLY `CpuPriorityClass`/`IoPriority` under `<exe>\PerfOptions` — never any other value or key.
- Overrides are keyed by lowercase exe filename (IFEO is path-blind).
- UI: WPF-UI (`FluentWindow`, `SystemThemeWatcher` for OS dark/light). No tray, no background process.

## Conventions
- Commits: conventional prefixes (`feat:`/`test:`/`chore:`), never a `Co-Authored-By` trailer.
