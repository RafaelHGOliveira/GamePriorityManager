using System.ComponentModel;
using System.Diagnostics;

namespace GPM.App.Services;

public sealed class ElevationLauncher : IElevationLauncher
{
    private const int ErrorCancelled = 1223; // user declined the UAC prompt

    public async Task<int?> RunApplyAsync(string opsPath)
    {
        var psi = new ProcessStartInfo(Environment.ProcessPath!, $"--apply \"{opsPath}\"")
        {
            UseShellExecute = true,
            Verb = "runas",
        };
        try
        {
            using var process = Process.Start(psi)!;
            await process.WaitForExitAsync();
            return process.ExitCode;
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == ErrorCancelled)
        {
            return null;
        }
    }
}
