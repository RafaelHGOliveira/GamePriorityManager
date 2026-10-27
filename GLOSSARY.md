# GPM

Per-game Windows performance settings, applied through the registry so they take effect every time the game starts.

## Language

**Priority Override**:
The CPU priority and/or IO priority Windows applies to every process with a given exe filename, regardless of folder.
_Avoid_: Override (alone), perf options, priority setting

**Graphics Preference**:
The GPU choice and windowed-optimization choice Windows applies to one exe at one exact path.
_Avoid_: GPU override, GPU priority, graphics setting

**GPU Preference**:
The part of a Graphics Preference that picks which GPU runs the game: Windows default, power saving, or high performance.
_Avoid_: GPU priority, adapter choice

**Windowed Optimizations**:
The part of a Graphics Preference that lets Windows upgrade a windowed DirectX game to a faster presentation model: Windows default, on, or off.
_Avoid_: Swap effect upgrade, flip model

**Linked Rows**:
Game rows that share one exe filename and therefore one Priority Override, while each keeps its own Graphics Preference.

**Generic Exe Name**:
An exe filename common enough (`game.exe`, `launcher.exe`) that its Priority Override also hits unrelated programs.
_Avoid_: shared name, common exe

**Store**:
A game launcher GPM scans for installed games: Steam, Epic, GOG, EA or Ubisoft.
_Avoid_: launcher, platform

**Beta Sources**:
The Stores whose detection is fragile (EA, Ubisoft) and are scanned only when the user opts in.
_Avoid_: experimental stores

**Manual Game**:
A game the user added by picking its exe, rather than one found by scanning a Store.
_Avoid_: custom game

**Pending Change**:
An edit the user made that Windows doesn't have yet; Discard drops it, Apply writes it.
_Avoid_: dirty row, unsaved change

**Apply**:
Writing every Pending Change at once, behind a single elevation prompt.
_Avoid_: save, commit

**Elevated Worker**:
The copy of GPM that Apply relaunches with admin rights to write Priority Overrides; it re-checks every one itself, rejects the whole batch if any is invalid, and writes nothing else.
_Avoid_: launcher, helper process

**Other Overrides**:
Priority Overrides present in Windows that match no detected game, whoever created them.
_Avoid_: unknown overrides, extra overrides

**Orphaned Override**:
One of the Other Overrides that GPM itself created for a game that is no longer detected.
_Avoid_: stale override, leftover
