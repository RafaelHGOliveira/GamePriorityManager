using System.Text.Json;

namespace GPM.Core.Worker;

/// The file transport between Apply and the Elevated Worker: ops.json in %TEMP%,
/// its report written next to it as "<name>.result.json".
internal static class OpsFile
{
    public static async Task<string> WriteAsync(IReadOnlyList<ApplyOp> ops)
    {
        var opsPath = Path.Combine(Path.GetTempPath(), $"gpm-ops-{Guid.NewGuid():N}.json");
        await File.WriteAllTextAsync(opsPath, JsonSerializer.Serialize(ops, ApplyWorker.JsonOpts));
        return opsPath;
    }

    public static string ResultPath(string opsPath) => Path.ChangeExtension(opsPath, ".result.json");

    /// A worker that died before writing its report counts as a rejected batch.
    public static async Task<ApplyReport> ReadReportAsync(string opsPath, int exitCode)
    {
        var resultPath = ResultPath(opsPath);
        return File.Exists(resultPath) &&
               JsonSerializer.Deserialize<ApplyReport>(await File.ReadAllTextAsync(resultPath), ApplyWorker.JsonOpts)
                   is { } report
            ? report
            : new ApplyReport(true, $"worker exited with code {exitCode} without a report", []);
    }
}
