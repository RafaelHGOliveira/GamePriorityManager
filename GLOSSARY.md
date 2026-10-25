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
