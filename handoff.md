# GPM implementation — handoff

This file is kept up to date by Claude Code while the automated implementation
pipeline for the plans in `docs/superpowers/plans/` runs. If you're picking
this up cold, read this file first, then `git log`/`git worktree list` to
verify it against reality (this file can lag actual state between refreshes).

## Status: RESUMED — E3 hand-carried to approved, E4-E6+merge+final-review running

E3's resume caveat (below, kept for history) was resolved by option (a): a
fresh reviewer agent was pointed at commit `f9daddf` directly against the
Task E3 spec (`docs/superpowers/plans/2026-07-10-gpm-e-integration.md` lines
395-702). Verdict: 6/8 required behaviors cleanly delivered, 2 delivered with
real bugs inherited from the plan's own sample code (not implementer
deviations) — `ExeIconConverter` didn't guard `Icon.ExtractAssociatedIcon`
against exceptions (could crash the app on a locked/corrupt exe), and
`MainWindow.ApplyTheme` never called `SystemThemeWatcher.UnWatch` before
applying an explicit dark/light choice (a later OS theme change would
silently revert it). Independently confirmed build clean + 98/98 tests
passing (not just trusting the commit message). Both bugs fixed directly and
committed: `00aa6d3` on `ws-e-integration` ("fix: guard icon extraction
against exceptions and stop theme watcher fighting manual theme choice").
**E3 is now genuinely reviewed and approved**, no re-run needed.

Because the original workflow's E3 `agent()` call was killed mid-flight (no
cached result), resuming it via `resumeFromRunId` would have unconditionally
re-dispatched a fresh E3 implementer regardless of any script edits — so
instead of resuming `wf_db33b133-d57`, a **new** workflow script was
authored/launched that continues from E4 through E5, E6, the merge into
`gpm-mvp`, and the final 4-lens review + fix pass, reusing the original
pipeline's exact task/review/fix prompts and schemas. See the "Pipeline
mechanics" section below for its script path and how to resume *that* one if
it gets interrupted.

<details>
<summary>Original pause note (2026-07-11, kept for history)</summary>

The user asked for a pause. The background Workflow task (`w28o5g9cx`, run ID
`wf_db33b133-d57`) was stopped via `TaskStop`, everything reachable was swept
for uncommitted changes and committed, and no wakeup is scheduled — nothing
will resume on its own. Resume only when explicitly asked.

**Progress at pause time:** Foundation and all of A/B/C/D are complete,
reviewed, and merged into `gpm-mvp`. Plan E is underway: E1 and E2 are
complete/reviewed/approved. **E3 was actively being implemented when
stopped** — its agent never returned a result (no review happened), but the
in-flight work built cleanly and passed the full suite (98/98), so it was
committed as-is directly (not through the pipeline's normal implement→review
flow): commit `f9daddf` on `ws-e-integration`, message flagged
`(WIP, paused)`. E4, E5, E6, the E-branch merge, and the final whole-branch
review have not started.

Watch what actually happened via
`C:\Users\Rafael\.claude\projects\C--Users-Rafael-Documents-Game-Priority-Manager--GPM-\9a992ccd-fde0-4643-9892-caf99b10939a\subagents\workflows\wf_db33b133-d57\journal.jsonl`
(one `"started"` entry with no matching `"result"` = the killed E3 attempt).
</details>

## What's being built

`docs/superpowers/plans/2026-07-10-gpm-{0,a,b,c,d,e}-*.md` — six implementation
plans (25 tasks total) for GPM (Game Priority Manager), a Windows WPF app that
sets per-exe CPU/IO priority via the registry (IFEO `PerfOptions`).

Dependency order (each plan states this explicitly):
`0 (Foundation)` → `A (registry/worker)`, `B (detection)`, `C (exe resolver)`,
`D (UI shell)` in parallel, independent of each other → `E (integration)`,
serial, needs 0+A+B+C+D all merged first.

## Branch / worktree layout

Nothing here touches `main` directly — a dedicated integration branch is used,
and landing to `main` is a decision for the user at the very end
(via `finishing-a-development-branch`, not automatic).

- `main` — untouched, at `7fc9856`.
- `gpm-mvp` — integration branch, branched from `main` at `7fc9856`. Each
  plan's branch merges into this one as it's approved. Checked out at
  `.worktrees/gpm-mvp`.
- `ws-0-foundation` — plan 0, off `gpm-mvp`. Checked out at
  `.worktrees/ws-0-foundation`.
- `ws-a-registry-worker`, `ws-b-detection`, `ws-c-exe-resolver`,
  `ws-d-ui-shell` — plans A–D, each off `gpm-mvp` (created only after plan 0
  merges in). Checked out at `.worktrees/ws-<name>`.
- `ws-e-integration` — plan E, off `gpm-mvp` (created only after A–D all
  merge in). Checked out at `.worktrees/ws-e-integration`.

`.worktrees/` is gitignored (commit `7fc9856`).

## Pipeline mechanics (for resuming or re-running)

One Workflow script (`gpm-execute-plans`) runs the whole thing:
Foundation (3 tasks, sequential) → A/B/C/D (15 tasks across 4 parallel
worktree tracks) → merge each into `gpm-mvp` sequentially → E (6 tasks,
sequential) → merge → 4-lens final review (correctness, registry-write
safety, spec-compliance vs. all 6 plans' Global Constraints, cross-track
consistency) → one fix pass for Critical/Important findings.

Per task: implementer agent (TDD: failing test → implement → `dotnet build`
+ `dotnet test` → commit, conventional prefix, no `Co-Authored-By` trailer)
→ reviewer agent (spec-compliance verdict + code-quality verdict) → up to 2
fix/re-review rounds if the reviewer isn't satisfied. A track that can't get
a task approved after that stops itself and is reported as blocked rather
than pushing forward broken.

The original run (`wf_db33b133-d57`, script `gpm-execute-plans-wf_db33b133-d57.js`)
covered Foundation through E3 and is superseded — do not resume it (see
Status section: its E3 `agent()` call has no cached result and would
re-dispatch a redundant implementer). E4 onward runs from a **new** workflow,
`gpm-execute-e4-onward`, launched 2026-07-11, task ID `w771ykh73`, run ID
`wf_ae8069ce-6f7`, script at
`C:\Users\Rafael\AppData\Local\Temp\claude\C--Users-Rafael-Documents-Game-Priority-Manager--GPM-\a0c1828f-c4ba-4fb0-8f38-9845f8558bb1\scratchpad\gpm-execute-e4-onward.js`
(note: under the session-scratchpad temp dir, not the repo — may not survive
a machine-wide temp cleanup; the script content is also embedded in this
conversation's history if it needs to be recreated). It reuses the same
per-task implement→review→fix mechanics and the same final-review lenses as
the original script, running E4 → E5 → E6 → merge `ws-e-integration` into
`gpm-mvp` → 4-lens final review → one fix pass for Critical/Important
findings. To resume after an interruption:
`Workflow({scriptPath: "<path above>", resumeFromRunId: "wf_ae8069ce-6f7"})`.

## Task status

Legend: ⬜ not started · 🔄 in progress · ✅ approved+merged · ⚠️ blocked (see notes)

### Phase 0 — Foundation (`ws-0-foundation`)
- ✅ Task 1: Solution skeleton + CI
- ✅ Task 2: GPM.Core contracts
- ✅ Task 3: Store fixtures
- ✅ Merge into `gpm-mvp` (commit `3a5e51f`)

### Phase A — Registry & elevated worker (`ws-a-registry-worker`)
- ✅ A1 RealRegistryRoot · ✅ A2 PriorityService · ✅ A3 OpsValidator · ✅ A4 ApplyWorker
- ✅ Merge into `gpm-mvp` (commit `def0fa8`)

### Phase B — Detection (`ws-b-detection`)
- ✅ B1 VDF parser + Steam manifests · ✅ B2 SteamSource+registries · ✅ B3 Epic+GOG · ✅ B4 EA+Ubisoft (beta) · ✅ B5 GameScanner+JsonConfigStore
- ✅ Merge into `gpm-mvp` (commit `84dd71c`)

### Phase C — Exe resolver (`ws-c-exe-resolver`)
- ✅ C1 KnownGames · ✅ C2 ExeHeuristics · ✅ C3 ExeResolver pipeline
- ✅ Merge into `gpm-mvp` (commit `e0909a1`)

### Phase D — UI shell (`ws-d-ui-shell`)
- ✅ D1 Strings.resx+options · ✅ D2 ExeOverrideState+row VMs · ✅ D3 MainViewModel · ✅ D4 MainWindow XAML (mock data)
- ✅ Merge into `gpm-mvp` (commit `d42960d`)

### Phase E — Integration (`ws-e-integration`, all of A–D now merged in)
- ✅ E1 ElevationLauncher+`--apply`+composition root
- ✅ E2 Manual games+exe verification
- ✅ E3 Settings/IO column/rescan/icons — hand-reviewed against spec (commit `f9daddf`), 2 real bugs found and fixed (commit `00aa6d3`: icon-extraction exception guard, theme-watcher/manual-theme conflict). Build clean, 98/98 tests pass. Approved.
- 🔄 E4 GPM.Probe
- ⬜ E5 manual-smoke-test.md (doc only, not executed by an agent)
- ⬜ E6 Portable release build
- ⬜ Merge into `gpm-mvp`

### Final
- ⬜ 4-lens final review (correctness / registry-write safety / spec-compliance / cross-track consistency)
- ⬜ Fix pass for Critical/Important findings (if any)
- ⬜ Present `finishing-a-development-branch` options to the user (main is never touched automatically)

## Notable findings so far

- **Fixed:** Task 1's `dotnet new gitignore` scaffolding step overwrote the
  repo's custom `.gitignore`, silently dropping the `.agents/`, `.claude/`,
  `.worktrees/` entries (per-task reviewer caught this as an Important
  finding but it didn't block that task's approval, and it had already been
  merged into `gpm-mvp`). Confirmed no working-tree files were actually at
  risk yet (`.agents/`/`.claude/` only exist in the main repo checkout, not
  inside worktrees), but the loss would have resurfaced wherever this branch
  eventually lands. Restored the three entries directly on `gpm-mvp`
  (commit `5603136`) rather than waiting for the final-review fix pass.
- **Recurring, correctly-handled gotcha:** the plan's literal test code uses
  unqualified `Registry.CurrentUser` (`Microsoft.Win32.Registry`), but since
  `GPM.Core.Registry` is also a namespace in this codebase, C# simple-name
  lookup resolves the bare `Registry` identifier to that sibling namespace
  first, causing CS0234. Both A1 (`RealRegistryRootTests`) and A2
  (`PriorityServiceTests`) implementers hit this, fully-qualified to
  `Microsoft.Win32.Registry.CurrentUser` (no behavior change), flagged it as
  a concern, and reviewers independently reproduced and confirmed the fix
  was necessary — both approved. Worth knowing if later tasks (D/E, which
  also touch registry-adjacent code) hit the same thing.
- Two plan test-count comments are off by one vs. actual xUnit `Theory`
  expansion (A2: "9 tests" vs actual 10; A3: "13 tests" vs actual 14) —
  cosmetic plan-doc inaccuracies, not code defects, noted by both
  implementers and reviewers.

## Known accepted quirks (from plan pre-flight scan, not blockers)

- Several test-double classes (`PassthroughResolver`, `MemoryConfigStore`/
  `TempConfigStore`, `NoopLauncher`) are independently redefined with slightly
  different behavior across tasks D3/D4/E2/E3 in the plans as written. The
  final review's "cross-track-consistency" lens checks this explicitly.
- `Realtime = 4` (forbidden CPU priority value) has no shared named constant
  in plan 0's contracts — it's hardcoded with comments in several test files
  across plans. Flagged to the same review lens.
- Plan D1 defines `Strings.PendingChanges`, which stays unused per the plan's
  own text ("wired in plan E polish if desired" — plan E never revisits it).
  Not a bug, just a known dead string.
- Tasks E1/E4/E5/E6 have little-to-no automated test coverage by design
  (UAC elevation, HKLM, and `dotnet publish` output can't run unattended) —
  the plans themselves call this out.

## Environment notes

- .NET 10 SDK was missing on this machine (only runtimes were installed) and
  was installed via `winget install Microsoft.DotNet.SDK.10` before this run
  started. `dotnet --version` → `10.0.301`.
