using System.ComponentModel;
using System.Diagnostics;

namespace GPM.Core.Worker;

/// Production adapter: relaunches this same exe with "--apply <ops.json>" under
/// runas, so the UI itself never elevates (ADR-0003).
public sealed class RunasElevatedWorker : IElevatedWorker
{
    private const int ErrorCancelled = 1223; // user declined the UAC prompt

    public async Task<ApplyReport?> ApplyAsync(IReadOnlyList<ApplyOp> ops)
    {
        var opsPath = await OpsFile.WriteAsync(ops);
        var psi = new ProcessStartInfo(Environment.ProcessPath!, $"--apply \"{opsPath}\"")
        {
            UseShellExecute = true,
            Verb = "runas",
        };
        int exitCode;
        try
        {
            using var process = Process.Start(psi)!;
            await process.WaitForExitAsync();
            exitCode = process.ExitCode;
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == ErrorCancelled)
        {
            return null;
        }
        return await OpsFile.ReadReportAsync(opsPath, exitCode);
    }
}
