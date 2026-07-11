# Release procedure

1. `dotnet test tests/GPM.Core.Tests` — must be green.
2. `dotnet publish src/GPM.App -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o out/release`
3. Rename `out/release/GPM.App.exe` to `GPM-vX.Y.Z.exe`.
4. Run the manual smoke checklist (docs/manual-smoke-test.md) against the published exe.
5. Create a GitHub Release, attach the exe, note the size and SHA256
   (`Get-FileHash out/release/GPM-vX.Y.Z.exe`).

Known expectations: single exe ~70–150 MB (self-contained WPF; no trimming/AOT
support). AV vendors may flag unsigned new exes — document the HKLM write
rationale in the release notes (spec "Error handling").
