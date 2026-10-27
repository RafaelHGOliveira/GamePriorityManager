using GPM.Core.Worker;

namespace GPM.Core.Tests;

internal static class TestWorkers
{
    /// What RunasElevatedWorker returns when the user says No to the UAC prompt.
    public static IElevatedWorker DeclinedUac { get; } = new Declined();

    private sealed class Declined : IElevatedWorker
    {
        public Task<ApplyReport?> ApplyAsync(IReadOnlyList<ApplyOp> ops) => Task.FromResult<ApplyReport?>(null);
    }
}
