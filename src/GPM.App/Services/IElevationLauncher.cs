namespace GPM.App.Services;

public interface IElevationLauncher
{
    /// Relaunches GPM elevated with "--apply <opsPath>". Returns the worker's
    /// exit code, or null when the user declined the UAC prompt.
    Task<int?> RunApplyAsync(string opsPath);
}
