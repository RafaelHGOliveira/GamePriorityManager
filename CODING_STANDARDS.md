# Coding standards (review)

Judgement calls a reviewer checks on the diff. Mechanical rules live in tests/CI.

- **Off-thread work stays pure.** Code inside `Task.Run` (e.g. `MainViewModel.BuildSnapshot`, `PendingChanges.Read`) returns data; it never touches `Rows`, `OtherOverrides`, `ExeOverrideState` or anything bound to the UI. It never calls `IConfigStore` either (the UI thread saves settings into `games.json` at any moment): a `GpmConfig` it needs is loaded on the UI thread and passed in. The UI thread applies the result.
- **Apply's outcome is the re-read registry.** Anything decided after the Elevated Worker returns (still pending, `ManagedExes`, status text) comes from re-reading the registry, never from `ApplyReport` alone. `docs/adr/0004-apply-trusts-the-registry.md`.
- **`VisibleRows` is a view over a plain list.** Any change to `_rows` is followed by `VisibleRows.Refresh()`.
- **Registry reads tolerate the real hive.** Enumerating HKLM keys must skip what the current user can't read (`SecurityException`/`UnauthorizedAccessException`), never fail the whole listing.
