# GPM manual smoke test — YYYY-MM-DD

> **Status: NOT YET RUN.** This procedure requires an interactive, elevated (admin)
> Windows session — launching a real UAC prompt, eyeballing process priority in
> Task Manager/Process Explorer, and clicking through the UI. It cannot be executed
> by a non-interactive automated agent. A human must run Steps 1-3 below and fill in
> the results, then commit the completed checklist (Step 4).

## Step 1: Build the probe and add it as a manual game

```powershell
dotnet publish src/GPM.Probe -c Release -o out/probe
dotnet run --project src/GPM.App
```

In GPM: "+ Add game" → pick `out/probe/GPM.Probe.exe`. Enable "Show I/O column" in
⚙. Set CPU = High, IO = High. Apply, accept UAC.

## Step 2: Launch the probe from a NEW terminal and read its output

Run: `out/probe/GPM.Probe.exe`

Expected: `CPU priority class: High`. Record what IO priority prints:

- If `IO priority: 3` → Windows honors High via IFEO. Keep the UI as is.
- If `IO priority: 2` (write ignored) → **spec checkpoint triggered**: remove the
  `High` entry from `PriorityOptions.Io` in
  `src/GPM.App/ViewModels/PriorityOptions.cs`, update
  `PriorityOptionsTests.io_options_match_spec` (expected values become
  `[null, 0, 1, 2]`), and note the finding below.

Cross-check both values in Process Explorer (process properties) or Task Manager
(Details → Base priority column).

**Result (fill in after running):**

- CPU priority class printed: `__`
- IO priority printed: `__`
- IoPriority=3 checkpoint: honored / not honored (circle one)
- If not honored: confirm `PriorityOptions.Io` and
  `PriorityOptionsTests.io_options_match_spec` were updated to drop `High` — yes/no

## Step 3: Full manual smoke checklist

- [ ] First run: auto-scan lists installed games with store badges and icons
- [ ] Second run: instant list from cache; ⟳ rescans
- [ ] Search filters the list
- [ ] CPU dropdown shows: Default, Idle, Below Normal, Normal, Above Normal, High (NO Realtime)
- [ ] I/O column hidden by default; ⚙ toggle shows it
- [ ] Dirty row shows ●; bottom bar counts pending
- [ ] Discard reverts all pending changes
- [ ] Apply: single UAC prompt for a multi-row batch
- [ ] UAC declined: "Apply cancelled", changes stay pending
- [ ] Applied override visible in regedit under IFEO\<exe>\PerfOptions
- [ ] Default+Default then Apply: exe key removed from IFEO (regedit)
- [ ] GPM.Probe launched with override prints CPU = High
- [ ] IoPriority=3 checkpoint result: honored / not honored (value printed: __)
- [ ] Manual game: add via picker, remove via ✕
- [ ] "Verify exe" opens picker at the game's install dir; choice survives rescan
- [ ] Other overrides section lists non-game overrides; orphan flagged (with cleanup) after removing a manual game that had an override
- [ ] Rows sharing an exe filename show 🔗 and edit together
- [ ] Theme auto/dark/light works and persists
- [ ] games.json created next to the exe; when exe dir is read-only, falls back to %APPDATA%\GPM

## Notes / findings

_(record anything unexpected here, e.g. the IoPriority checkpoint outcome and any
follow-up code changes it required)_
