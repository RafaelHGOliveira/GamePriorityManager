using GPM.Core.Models;
using Microsoft.Win32;

namespace GPM.Core.Detection;

/// Same beta-toggle policy as EA (spec).
public sealed class UbisoftSource(IStoreRegistry registry) : IGameSource
{
    private const string Root = @"SOFTWARE\WOW6432Node\Ubisoft\Launcher\Installs";

    public GameStore Store => GameStore.Ubisoft;
    public bool IsBeta => true;

    public Task<IReadOnlyList<Game>> ScanAsync(CancellationToken ct = default) =>
        Task.Run<IReadOnlyList<Game>>(() =>
            registry.GetSubKeyNames(RegistryHive.LocalMachine, Root)
                .Select(id =>
                {
                    var dir = registry.GetString(RegistryHive.LocalMachine, $@"{Root}\{id}", "InstallDir");
                    if (dir is null) return null;
                    var normalized = Path.TrimEndingDirectorySeparator(
                        dir.Replace('/', Path.DirectorySeparatorChar));
                    return new Game
                    {
                        Name = Path.GetFileName(normalized),
                        Store = GameStore.Ubisoft,
                        StoreId = id,
                        InstallDir = normalized,
                    };
                })
                .Where(g => g is not null)
                .Cast<Game>()
                .ToList(), ct);
}
