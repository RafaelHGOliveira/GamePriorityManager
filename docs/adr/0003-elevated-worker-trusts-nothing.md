# The UI never elevates; a same-exe worker applies, and trusts nothing from the UI

The main window always runs unelevated (scanning stores and reading HKLM need no admin). Apply hands the batch to `GPM.exe --apply <ops.json>`, the same exe relaunched with `runas`, which writes and exits. `ops.json` sits in `%TEMP%`, writable by any medium-integrity process while the UAC prompt is up, so the worker re-validates every op itself (bare `.exe` filename, allowed priority values, no duplicates; a bad op rejects the whole batch) and writes nothing outside `PerfOptions` regardless of input.
