using GPM.Core.Models;
using Microsoft.Win32;

namespace GPM.Core.Detection;

/// Fragile registry layout — ships behind the "beta sources" toggle (spec).
public sealed class EaSource(IStoreRegistry registry) : IGameSource
{
    private const string Root = @"SOFTWARE\WOW6432Node\Electronic Arts";

    public GameStore Store => GameStore.Ea;
    public bool IsBeta => true;

    public Task<IReadOnlyList<Game>> ScanAsync(CancellationToken ct = default) =>
        Task.Run<IReadOnlyList<Game>>(() =>
            registry.GetSubKeyNames(RegistryHive.LocalMachine, Root)
                .Select(name =>
                {
                    var dir = registry.GetString(RegistryHive.LocalMachine, $@"{Root}\{name}", "Install Dir");
                    if (dir is null) return null;
                    return new Game
                    {
                        Name = name,
                        Store = GameStore.Ea,
                        InstallDir = Path.TrimEndingDirectorySeparator(dir),
                    };
                })
                .Where(g => g is not null)
                .Cast<Game>()
                .ToList(), ct);
}
