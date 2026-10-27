using GPM.Core.Priority;

namespace GPM.Core.Worker;

/// Runs ApplyWorker in this process, through the same ops.json/result.json files
/// and the same OpsValidator as the elevated one, minus the UAC prompt. For tests
/// and design-time data: it never declines, so it never returns null.
public sealed class InProcessElevatedWorker(IPriorityService priorities) : IElevatedWorker
{
    public async Task<ApplyReport?> ApplyAsync(IReadOnlyList<ApplyOp> ops)
    {
        var opsPath = await OpsFile.WriteAsync(ops);
        var exitCode = ApplyWorker.Run(opsPath, priorities);
        return await OpsFile.ReadReportAsync(opsPath, exitCode);
    }
}
