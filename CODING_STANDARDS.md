# Coding standards (review)

Judgement calls a reviewer checks on the diff. Mechanical rules live in tests/CI.

- **Off-thread work stays pure.** Code inside `Task.Run` (e.g. `MainViewModel.BuildSnapshot`) returns data; it never touches `Rows`, `OtherOverrides`, `ExeOverrideState` or anything bound to the UI. The UI thread applies the result.
- **`VisibleRows` is a view over a plain list.** Any change to `_rows` is followed by `VisibleRows.Refresh()`.
- **Registry reads tolerate the real hive.** Enumerating HKLM keys must skip what the current user can't read (`SecurityException`/`UnauthorizedAccessException`), never fail the whole listing.
