# Release procedure

1. `git tag vX.Y.Z && git push origin vX.Y.Z` (semver; check `git tag --sort=-v:refname | head -1` for the last one).
2. `.github/workflows/release.yml` builds, tests, publishes the self-contained
   single-file exe and creates the GitHub Release with `GPM-vX.Y.Z.exe`, size and SHA256.
   Follow it with `gh run watch`.
3. The manual smoke checklist (`docs/manual-smoke-test.md`) needs a human with admin;
   no agent can run it.

Known expectations: single exe ~70–150 MB (self-contained WPF; no trimming/AOT
support). AV vendors may flag unsigned new exes — the release body explains the
HKLM write.
