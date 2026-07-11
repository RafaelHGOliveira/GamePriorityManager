# Game Priority Manager (GPM) — MVP Design

**Date:** 2026-07-10 (rev. 2 — design review applied)
**Status:** Approved
**Repo:** https://github.com/RafaelHGOliveira/GamePriorityManager

## Purpose

A portable Windows app that auto-detects installed games, shows each game's
current permanent CPU/IO priority (read from the IFEO registry), and lets the
user change it and apply in one click. Inspired by the DLSS Swapper /
OptiScaler client UX.

**The gap it fills:** Process Lasso needs a 24/7 background service; manual
`.reg` files need registry knowledge. GPM is passive (writes IFEO once, no
background process — safest approach for games with kernel anti-cheat like
EAC) with a scan → pick → apply UI.

## Decisions (locked)

| Topic | Decision |
|---|---|
| Detection scope | Steam, Epic, GOG, EA, Ubisoft + manual add. Xbox/UWP moved to v2 (see Out of scope) |
| Interaction | Dropdown per game + single Apply button (one UAC prompt per batch) |
| Distribution | Portable single .exe (self-contained; expect ~70–150 MB — WPF supports neither trimming nor Native AOT), GitHub Releases |
| What it adjusts | `CpuPriorityClass` + `IoPriority` (IFEO `PerfOptions`) — nothing else |
| UI language | English (strings in .resx, i18n-ready) |
| Stack | C# / .NET 10 (LTS to Nov 2028; .NET 8 leaves support Nov 2026), WPF + [WPF-UI](https://github.com/lepoco/wpfui) 4.x (Fluent), MVVM via CommunityToolkit.Mvvm |

## Architecture

Three layers, each independently testable:

```
┌─────────────────────────────────────────────┐
│ GPM.App — WPF + WPF-UI, MVVM                │
│   MainWindow: list, search, dropdowns, Apply│
├─────────────────────────────────────────────┤
│ GPM.Core — class library, no UI deps        │
│   Detection: IGameSource (one per store)    │
│   ExeResolver: pick the game's real exe     │
│   PriorityService: read/write IFEO          │
├─────────────────────────────────────────────┤
│ Elevated worker — SAME exe, `--apply` flag  │
└─────────────────────────────────────────────┘
```

Principles:

1. **Main app never runs elevated.** Reading HKLM and scanning stores works
   unelevated. Only writes elevate.
2. **Apply flow:** UI collects pending changes → writes `ops.json` to %TEMP% →
   relaunches itself elevated (`runas`) with `--apply <path>` → worker writes
   keys, writes `result.json`, exits → UI re-reads the registry (source of
   truth) and refreshes.
3. **One `IGameSource` per store**, single interface (`ScanAsync() →
   List<Game>`). A failing source never breaks the overall scan.
4. Manual games persist in `games.json` next to the exe (portable app →
   portable config). Detected-game cache, per-game exe choices, and
   override provenance (which overrides GPM created) live there too.
   **If the app folder isn't writable** (e.g. the exe was dropped into
   `Program Files`), fall back to `%APPDATA%\GPM\games.json`; the active
   config path is shown in Settings.
5. **The unit of an override is the exe *filename*, not the game.** IFEO
   keys by name only — path-blind. See "IFEO semantics" below; this drives
   UI row linking and worker validation.

## Game detection

| Store | Method | Notes |
|---|---|---|
| Steam | `HKLM\SOFTWARE\WOW6432Node\Valve\Steam` (fallback: `HKCU\Software\Valve\Steam` → `SteamPath`) → `libraryfolders.vdf` → `appmanifest_*.acf` | Name, appid, install dir. Stable format. |
| Epic | `*.item` JSONs in `C:\ProgramData\Epic\EpicGamesLauncher\Data\Manifests` | Provides `LaunchExecutable` directly. |
| GOG | `HKLM\SOFTWARE\WOW6432Node\GOG.com\Games\<id>` | Provides `exe` directly. |
| EA | `HKLM\SOFTWARE\WOW6432Node\Electronic Arts\*` | Fragile; ships behind a "beta sources" toggle if unstable during implementation. |
| Ubisoft | `HKLM\SOFTWARE\WOW6432Node\Ubisoft\Launcher\Installs\<id>` | Same beta-toggle policy as EA. |
| Manual | File picker | Any exe; removable. |

Duplicates across stores are kept (store badge distinguishes them) — but rows
that resolve to the same exe filename share one IFEO key and therefore one
pending state (see IFEO semantics).

## ExeResolver

For stores that don't name the exe (mainly Steam), a 3-step pipeline — first
hit wins:

1. **Curated list** (`known-games.json`, embedded): Steam appid → relative exe
   path. Seeds: Elden Ring (`Game\eldenring.exe`, NOT
   `start_protected_game.exe`), Nightreign, CS2/Dota (`game\bin\win64\`),
   Ready or Not (`ReadyOrNotSteam-Win64-Shipping.exe`, renamed mid-2025), No
   Rest For The Wicked (main exe smaller than its crash handler).
2. **Heuristic:** enumerate exes; drop blacklist (`*CrashHandler*`, `*Setup*`,
   `UEPrereq*`, `EasyAntiCheat*`, `vconsole*`, `*redist*`, `dxsetup*`); prefer
   `*-Win64-Shipping.exe` (UE convention); else largest remaining exe.
3. **Human fallback:** ambiguous games show a "verify exe" warning; clicking
   opens a candidate picker; the choice persists in `games.json`.

> Steam launch-config (`appinfo.vdf`) parsing was cut from the MVP pipeline:
> it's a binary format with version churn, and steps 1+2+3 already cover the
> hard cases. Revisit in v2.

## PriorityService

Key: `HKLM\...\Image File Execution Options\<exe>\PerfOptions`

| Value | dword meanings |
|---|---|
| `CpuPriorityClass` | 1 Idle, 5 Below Normal, 2 Normal, 6 Above Normal, 3 High. **Realtime (4) is never exposed.** |
| `IoPriority` | 0 Very Low, 1 Low, 2 Normal, 3 High |

> **Implementation checkpoint:** verify that Windows actually honors
> `IoPriority = 3 (High)` for processes launched with an IFEO override —
> user-mode APIs cap I/O priority at Normal (High is normally reserved for
> the system), and the kernel may or may not apply it from `PerfOptions`.
> If not honored, the UI exposes only Very Low / Low / Normal for I/O.

### IFEO semantics — filename, not path

- An IFEO key applies to **every process with that exe filename**, from any
  path — not to one game.
- Pending changes are keyed internally by lowercased exe filename. Rows that
  resolve to the same filename (same game in two stores, or two games sharing
  a name) are visually linked (🔗 badge + tooltip) and always display/edit
  the same values — conflicting entries in one Apply batch are impossible by
  construction.
- Generic filenames (`game.exe`, `launcher.exe`, `start.exe`, …) get a
  warning tooltip: "this override applies to every process with this name on
  the system."

Safety rules:

- Writes ONLY these two values. Never touches other IFEO values (e.g.
  `Debugger`). If a suspicious `Debugger` value exists on a game's key, show
  an informational warning; don't modify it.
- "Default" = **delete** the value. If `PerfOptions` and the exe key become
  empty, delete the orphaned keys — leave the registry as found.
- Registry root is injected (tests run against `HKCU\Software\GPM-Test`).

## Elevated worker protocol

- `GPM.exe --apply <ops.json>`: `[{exe, cpu: int|null, io: int|null}, ...]`
  (`null` = remove override).
- **The worker trusts nothing from the UI.** `ops.json` sits in %TEMP%,
  writable by any medium-integrity process — a classic TOCTOU window while
  the UAC prompt is up. The worker re-validates every op itself:
  - `exe`: bare filename only — must end in `.exe`, no path separators
    (`\` or `/`), no registry-reserved characters, length ≤ 255;
  - `cpu` ∈ {1, 2, 3, 5, 6} or null; `io` ∈ {0, 1, 2, 3} or null;
  - duplicate `exe` entries → the whole batch is rejected;
  - writes only `CpuPriorityClass`/`IoPriority` under `PerfOptions` — never
    any other value or key, regardless of input.
- Applies op-by-op, writes `result.json` (per-item success/error), returns
  aggregate exit code. No window.

## Error handling

| Scenario | Behavior |
|---|---|
| UAC declined | Changes stay pending; toast "Apply cancelled". |
| Partial failure | Succeeded rows confirm; failed rows return to dirty with the specific error. |
| Corrupt store manifest | Source logs and returns empty; never breaks the scan (sources run in parallel, each with its own try/catch). |
| Game uninstalled but IFEO override remains | Flagged as **orphaned** inside "Other overrides", with one-click cleanup. |
| AV blocks HKLM write | Clear error linking to docs explaining why GPM writes there. |

## UI

Single window, Fluent dark/light following the OS. No tray, no background.

```
┌──────────────────────────────────────────────────────────┐
│  GPM — Game Priority Manager                    ⚙  ⟳     │
├──────────────────────────────────────────────────────────┤
│  🔍 Search...        [Store: All ▾]        [+ Add game]  │
├──────────────────────────────────────────────────────────┤
│ ICON  GAME             STORE   EXE            CPU    I/O │
│ 🎮 Counter-Strike 2    Steam   cs2.exe      [High ▾][─ ▾]│
│ 🎮 ELDEN RING          Steam   eldenring…   [Norm ▾][─ ▾]│ ●
├──────────────────────────────────────────────────────────┤
│ ▸ Other overrides (2)                                    │
├──────────────────────────────────────────────────────────┤
│  ● 2 pending changes      [Discard]      [ 🛡 Apply ]    │
└──────────────────────────────────────────────────────────┘
```

- CPU dropdown: `Default | Idle | Below Normal | Normal | Above Normal |
  High` ("Default" grayed = no override). CPU is the product.
- I/O column hidden behind a settings toggle ("Show I/O column").
- Dirty rows get `●`; bottom bar counts pending; `Discard` reverts to
  registry state; Apply button carries the UAC shield icon.
- Rows sharing an exe filename show the 🔗 linked badge; generic exe names
  show the system-wide warning (see IFEO semantics).
- First run: auto-scan with per-store progress; cached in `games.json`;
  manual rescan via `⟳`.
- **Other overrides** section (collapsed): any `PerfOptions` override in the
  registry not matching a detected game — listed, editable, cleanable. GPM
  manages ANY override, not just games. Provenance (tracked in `games.json`)
  splits it: overrides GPM created whose game is gone are flagged
  **orphaned** (cleanup suggested); the rest (e.g. `voicemeeterpro.exe`) are
  intentional non-game overrides — editable, never nagged.
- Settings: theme (auto/dark/light), Show I/O column, beta sources (EA/
  Ubisoft) on/off, config file path, repo link.
- Game icons extracted from the exe. All strings in `Strings.resx`.

## Testing

- **GPM.Core:** unit tests, no admin needed — injected registry root (HKCU
  test hive); store parsers tested against fixture manifests (`.acf`, `.vdf`,
  `.item` samples committed to the repo); ExeResolver table-driven tests for
  the known hard cases.
- **Worker:** integration test against the HKCU test hive.
- **Effectiveness probe:** the repo ships `GPM.Probe`, a tiny console exe
  that prints its own effective CPU/IO priority. Local (admin) smoke test:
  apply an override to `GPM.Probe.exe`, launch it, confirm the values;
  cross-check in Task Manager/Process Explorer. Registry writes alone prove
  nothing — this validates the product's core promise. Also the definitive
  answer to the `IoPriority = 3` checkpoint.
- **UI:** manual smoke test for MVP (WPF UI automation not worth the cost
  yet).

## Build plan — parallel workstreams

Structured so several tasks/agents run simultaneously. Each workstream owns
disjoint projects/folders — merge conflicts should be near zero (use branches
or git worktrees per workstream).

**Phase 0 — Contracts (serial, small).** Everything else codes against this:

- Solution skeleton (`GPM.App`, `GPM.Core`, `GPM.Core.Tests`, `GPM.Probe`) +
  CI (build + tests).
- GPM.Core contracts: `Game`, `IGameSource`, `IRegistryRoot`,
  `IPriorityService`, `PendingChange`.
- `ops.json` / `result.json` schemas; `Strings.resx` skeleton.
- Store fixture files committed (`.acf`, `.vdf`, `.item` samples).

**Phase 1 — parallel workstreams (no shared files):**

| WS | Scope | Depends on | Done when |
|---|---|---|---|
| A — Registry & worker | `PriorityService` (IFEO read/write/delete, IFEO-semantics keying), `--apply` worker + input validation | Contracts | HKCU-hive integration tests green |
| B — Detection | One sub-task per store (Steam, Epic, GOG, EA, Ubisoft — each independently parallelizable) + `games.json` persistence/fallback | Contracts + fixtures | Fixture-driven parser tests green |
| C — ExeResolver | Curated `known-games.json`, heuristic + blacklist, candidate enumeration | Contracts | Table-driven tests for the known hard cases green |
| D — UI shell | Window, list/search/dropdowns, dirty tracking, linked rows, Apply flow — against fake `IGameSource`/`IPriorityService` | Contracts | Manual walkthrough with mock data |

**Phase 2 — Integration (serial).** Wire real services into the UI;
end-to-end elevation flow; Other-overrides scan; effectiveness probe + full
manual smoke test (including the `IoPriority = 3` checkpoint); first portable
release build.

## Out of scope (v2+)

Core affinity (impossible via IFEO — would require a background process,
against the app's philosophy), Realtime priority (unsafe), presets/batch
profiles, PT-BR and other locales, Microsoft Store/winget distribution,
community-contributed curated exe list sync, auto-update, **Xbox/UWP
detection** (AppX enumeration forces a `net10.0-windows10.0.x` TFM + CsWinRT
for a store where IFEO is unreliable anyway), **Steam `appinfo.vdf`
launch-config parsing** (binary format, version churn).
