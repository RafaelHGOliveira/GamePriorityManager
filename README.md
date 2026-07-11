# GPM — Game Priority Manager

A Windows desktop app that sets **per-executable CPU and I/O priority** for your
games, so a background process can't starve the game you're actually playing.
It works by writing Windows' [Image File Execution Options (IFEO)](https://learn.microsoft.com/en-us/windows-hardware/drivers/debugger/image-file-execution-options)
`PerfOptions` keys — the same mechanism the OS itself uses — so the priority is
applied by Windows every time the exe launches, with **no background process, no
tray icon, and nothing running while you game.**

GPM scans your installed games automatically (Steam, Epic, GOG, EA, Ubisoft),
lets you pick a priority per game from a simple list, and applies it with a
single elevation prompt.

## How it works

Windows lets you attach performance options to any executable via a registry key
under:

```
HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Image File Execution Options\<exe>\PerfOptions
```

GPM writes **only** two values there — `CpuPriorityClass` and `IoPriority` — for
the exes you choose. Because IFEO is keyed by the *filename* (path-blind), the
override follows the game regardless of where it's installed. Windows reads and
honors these values on process start, so once set, GPM does not need to run.

Writing under `HKLM` requires administrator rights, so GPM launches a short-lived
elevated worker (`--apply`) only at the moment you apply changes — the main UI
runs unelevated.

### Priority values

| CPU class     | Value | | I/O priority | Value |
|---------------|-------|-|--------------|-------|
| Idle          | 1     | | Very Low     | 0     |
| Below Normal  | 5     | | Low          | 1     |
| Normal        | 2     | | Normal       | 2     |
| Above Normal  | 6     | | High         | 3     |
| High          | 3     | | | |

> **Realtime (CPU value 4) is deliberately unsupported** — it can starve system
> processes and destabilize Windows.

## Features

- **Automatic game detection** across Steam, Epic Games, GOG, EA app, and Ubisoft
  Connect (the last two are best-effort/beta).
- **Executable resolution** — finds the real game exe (not the launcher/crash
  handler) using a known-games list plus heuristics.
- **Manual games** — add any exe by hand for titles the scanner misses.
- **Per-game CPU + I/O priority** chosen from a plain dropdown.
- **Single elevation prompt** — changes are batched and applied by one short-lived
  elevated worker; nothing stays resident.
- **Native Windows look** — Fluent (WPF-UI) window that follows the OS light/dark
  theme automatically.

## Project layout

The solution is `GamePriorityManager.sln`, targeting `net10.0-windows`:

| Project            | What it is                                                            |
|--------------------|-----------------------------------------------------------------------|
| `GPM.Core`         | All logic: game detection, exe resolution, registry writes, worker.   |
| `GPM.App`          | The WPF UI (FluentWindow, view models, elevation launcher).           |
| `GPM.Probe`        | Standalone tool that prints a process's *effective* CPU/IO priority — used to verify Windows actually honors the overrides. |
| `GPM.Core.Tests`   | xUnit test suite (100+ tests).                                        |

## Build & test

Requires the **.NET 10 SDK** (not just the runtime — verify with
`dotnet --list-sdks`).

```powershell
# Build
dotnet build GamePriorityManager.sln -c Release

# Test
dotnet test tests/GPM.Core.Tests -c Release --no-build
```

Registry tests run without admin — each test class uses a unique key under
`HKCU\Software\GPM-Test\...` and deletes it on disposal.

## Building a release

See [`docs/release.md`](docs/release.md) for the full procedure. In short:

```powershell
dotnet publish src/GPM.App -c Release -r win-x64 --self-contained true \
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true \
  -o out/release
```

This produces a single self-contained `.exe` (~70–150 MB; no trimming/AOT for
WPF). Since it's unsigned and writes to `HKLM`, some antivirus vendors may flag
it — the release notes document the rationale.

## Verifying it actually works

Setting a registry value is one thing; getting Windows to *honor* it is another.
`GPM.Probe` exists to confirm the effect: run it via an IFEO override and it
prints the process's real, effective CPU and I/O priority. See
[`docs/manual-smoke-test.md`](docs/manual-smoke-test.md) for the admin-gated
smoke test that validates the end-to-end behavior on a real machine.

## Safety notes

- GPM only ever touches `CpuPriorityClass` and `IoPriority` under an exe's
  `PerfOptions` subkey — never any other registry value or key.
- Overrides are keyed by the lowercase exe filename; removing an override deletes
  exactly those values.
- The elevated worker is short-lived and runs only when you apply changes.

## Status

Core functionality is implemented, reviewed, and covered by tests. See
[`handoff.md`](handoff.md) for detailed live status and known caveats.
