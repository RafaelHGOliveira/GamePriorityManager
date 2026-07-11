using GPM.Core.Models;
using Microsoft.Win32;

namespace GPM.Core.Detection.Steam;

public sealed class SteamSource(IStoreRegistry registry) : IGameSource
{
    public GameStore Store => GameStore.Steam;
    public bool IsBeta => false;

    public Task<IReadOnlyList<Game>> ScanAsync(CancellationToken ct = default) =>
        Task.Run<IReadOnlyList<Game>>(() =>
        {
            var steamPath =
                registry.GetString(RegistryHive.LocalMachine, @"SOFTWARE\WOW6432Node\Valve\Steam", "InstallPath")
                ?? registry.GetString(RegistryHive.CurrentUser, @"Software\Valve\Steam", "SteamPath");
            return steamPath is null ? [] : ScanRoot(steamPath);
        }, ct);

    /// Separated from registry lookup so tests can point at a fixture tree.
    public static IReadOnlyList<Game> ScanRoot(string steamPath)
    {
        var vdf = Path.Combine(steamPath, "steamapps", "libraryfolders.vdf");
        if (!File.Exists(vdf)) return [];

        var games = new List<Game>();
        foreach (var lib in SteamManifests.LibraryPaths(File.ReadAllText(vdf)))
        {
            var steamapps = Path.Combine(lib, "steamapps");
            if (!Directory.Exists(steamapps)) continue;
            foreach (var acf in Directory.EnumerateFiles(steamapps, "appmanifest_*.acf"))
            {
                try
                {
                    var app = SteamManifests.ParseAppManifest(File.ReadAllText(acf));
                    if (app is null) continue;
                    if (SteamManifests.IsNonGameApp(app.Value.Name)) continue; // skip redists/runtimes/Proton
                    games.Add(new Game
                    {
                        Name = app.Value.Name,
                        Store = GameStore.Steam,
                        StoreId = app.Value.AppId,
                        InstallDir = Path.Combine(steamapps, "common", app.Value.InstallDir),
                    });
                }
                catch (IOException) { /* one unreadable manifest never breaks the scan */ }
            }
        }
        return games;
    }
}
