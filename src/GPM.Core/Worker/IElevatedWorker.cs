namespace GPM.Core.Worker;

/// The Elevated Worker: the only writer of Priority Overrides. Apply hands it the
/// whole batch; it re-validates every op itself (ADR-0003), so a bad op rejects
/// the batch and nothing is written.
public interface IElevatedWorker
{
    /// Returns the worker's report, or null when the user declined the UAC prompt
    /// (nothing ran). A rejected batch and a worker crash both come back as a report
    /// with BatchRejected set; only the per-op Results of an accepted batch are meaningful.
    Task<ApplyReport?> ApplyAsync(IReadOnlyList<ApplyOp> ops);
}
