# Game Priority Manager (GPM) — MVP Design

**Date:** 2026-07-10
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
| Detection scope | All major stores: Steam, Epic, GOG, EA, Ubisoft, Xbox (limited) + manual add |
| Interaction | Dropdown per game + single Apply button (one UAC prompt per batch) |
| Distribution | Portable single .exe, GitHub Releases |
| What it adjusts | `CpuPriorityClass` + `IoPriority` (IFEO `PerfOptions`) — nothing else |
| UI language | English (strings in .resx, i18n-ready) |
| Stack | C# / .NET 8, WPF + [WPF-UI](https://github.com/lepoco/wpfui) (Fluent), MVVM |

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
   portable config). Detected-game cache and per-game exe choices live there
   too.

## Game detection

| Store | Method | Notes |
|---|---|---|
| Steam | `HKLM\SOFTWARE\WOW6432Node\Valve\Steam` → `libraryfolders.vdf` → `appmanifest_*.acf` | Name, appid, install dir. Stable format. |
| Epic | `*.item` JSONs in `C:\ProgramData\Epic\EpicGamesLauncher\Data\Manifests` | Provides `LaunchExecutable` directly. |
| GOG | `HKLM\SOFTWARE\WOW6432Node\GOG.com\Games\<id>` | Provides `exe` directly. |
| EA | `HKLM\SOFTWARE\WOW6432Node\Electronic Arts\*` | Fragile; ships behind a "beta sources" toggle if unstable during implementation. |
| Ubisoft | `HKLM\SOFTWARE\WOW6432Node\Ubisoft\Launcher\Installs\<id>` | Same beta-toggle policy as EA. |
| Xbox/UWP | AppX package enumeration | Detected but marked `Unsupported` (IFEO unreliable for UWP). Grayed out with tooltip. |
| Manual | File picker | Any exe; removable. |

Duplicates across stores are kept (store badge distinguishes them).

## ExeResolver

For stores that don't name the exe (mainly Steam), a 4-step pipeline — first
hit wins:

1. **Curated list** (`known-games.json`, embedded): Steam appid → relative exe
   path. Seeds: Elden Ring (`Game\eldenring.exe`, NOT
   `start_protected_game.exe`), Nightreign, CS2/Dota (`game\bin\win64\`),
   Ready or Not (`ReadyOrNotSteam-Win64-Shipping.exe`, renamed mid-2025), No
   Rest For The Wicked (main exe smaller than its crash handler).
2. **Steam launch config** (appinfo/manifest launch entry) when available.
3. **Heuristic:** enumerate exes; drop blacklist (`*CrashHandler*`, `*Setup*`,
   `UEPrereq*`, `EasyAntiCheat*`, `vconsole*`, `*redist*`, `dxsetup*`); prefer
   `*-Win64-Shipping.exe` (UE convention); else largest remaining exe.
4. **Human fallback:** ambiguous games show a "verify exe" warning; clicking
   opens a candidate picker; the choice persists in `games.json`.

## PriorityService

Key: `HKLM\...\Image File Execution Options\<exe>\PerfOptions`

| Value | dword meanings |
|---|---|
| `CpuPriorityClass` | 1 Idle, 5 Below Normal, 2 Normal, 6 Above Normal, 3 High. **Realtime (4) is never exposed.** |
| `IoPriority` | 0 Very Low, 1 Low, 2 Normal, 3 High |

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
- Validates input, applies op-by-op, writes `result.json` (per-item
  success/error), returns aggregate exit code. No window.

## Error handling

| Scenario | Behavior |
|---|---|
| UAC declined | Changes stay pending; toast "Apply cancelled". |
| Partial failure | Succeeded rows confirm; failed rows return to dirty with the specific error. |
| Corrupt store manifest | Source logs and returns empty; never breaks the scan (sources run in parallel, each with its own try/catch). |
| Game uninstalled but IFEO override remains | Listed under "Orphaned overrides" with cleanup option. |
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
│ ▸ Orphaned overrides (2)                                 │
├──────────────────────────────────────────────────────────┤
│  ● 2 pending changes      [Discard]      [ 🛡 Apply ]    │
└──────────────────────────────────────────────────────────┘
```

- CPU dropdown: `Default | Idle | Below Normal | Above Normal | High`
  ("Default" grayed = no override). CPU is the product.
- I/O column hidden behind a settings toggle ("Show I/O column").
- Dirty rows get `●`; bottom bar counts pending; `Discard` reverts to
  registry state; Apply button carries the UAC shield icon.
- First run: auto-scan with per-store progress; cached in `games.json`;
  manual rescan via `⟳`.
- Orphaned overrides section (collapsed): any `PerfOptions` override in the
  registry not matching a detected game (e.g. `voicemeeterpro.exe`) — listed,
  editable, cleanable. GPM manages ANY override, not just games.
- Settings: theme (auto/dark/light), Show I/O column, beta sources (EA/
  Ubisoft) on/off, repo link.
- Game icons extracted from the exe. All strings in `Strings.resx`.

## Testing

- **GPM.Core:** unit tests, no admin needed — injected registry root (HKCU
  test hive); store parsers tested against fixture manifests (`.acf`, `.vdf`,
  `.item` samples committed to the repo); ExeResolver table-driven tests for
  the known hard cases.
- **Worker:** integration test against the HKCU test hive.
- **UI:** manual smoke test for MVP (WPF UI automation not worth the cost
  yet).

## Out of scope (v2+)

Core affinity (impossible via IFEO — would require a background process,
against the app's philosophy), Realtime priority (unsafe), presets/batch
profiles, PT-BR and other locales, Microsoft Store/winget distribution,
community-contributed curated exe list sync, auto-update.
