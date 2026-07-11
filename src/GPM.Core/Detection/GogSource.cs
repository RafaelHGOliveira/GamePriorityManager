using GPM.Core.Models;
using Microsoft.Win32;

namespace GPM.Core.Detection;

public sealed class GogSource(IStoreRegistry registry) : IGameSource
{
    private const string Root = @"SOFTWARE\WOW6432Node\GOG.com\Games";

    public GameStore Store => GameStore.Gog;
    public bool IsBeta => false;

    public Task<IReadOnlyList<Game>> ScanAsync(CancellationToken ct = default) =>
        Task.Run<IReadOnlyList<Game>>(() =>
            registry.GetSubKeyNames(RegistryHive.LocalMachine, Root)
                .Select(id =>
                {
                    var key = $@"{Root}\{id}";
                    var name = registry.GetString(RegistryHive.LocalMachine, key, "gameName");
                    if (name is null) return null;
                    return new Game
                    {
                        Name = name,
                        Store = GameStore.Gog,
                        StoreId = id,
                        InstallDir = registry.GetString(RegistryHive.LocalMachine, key, "path"),
                        ExePath = registry.GetString(RegistryHive.LocalMachine, key, "exe"),
                    };
                })
                .Where(g => g is not null)
                .Cast<Game>()
                .ToList(), ct);
}
