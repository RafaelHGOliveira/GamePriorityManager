using System.IO;
using GPM.App.ViewModels;
using GPM.Core.Detection;
using GPM.Core.Detection.Steam;
using GPM.Core.Graphics;
using GPM.Core.Persistence;
using GPM.Core.Priority;
using GPM.Core.Registry;
using GPM.Core.Resolve;
using GPM.Core.Worker;
using Microsoft.Win32;

namespace GPM.App;

public static class CompositionRoot
{
    public static IPriorityService CreatePriorityService() =>
        new PriorityService(new RealRegistryRoot(
            RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64),
            PriorityValues.IfeoPath));

    public static MainViewModel CreateMainViewModel()
    {
        var storeRegistry = new WindowsStoreRegistry();
        var scanner = new GameScanner(
        [
            new SteamSource(storeRegistry),
            new EpicSource(),
            new GogSource(storeRegistry),
            new EaSource(storeRegistry),
            new UbisoftSource(storeRegistry),
        ]);
        var config = new JsonConfigStore(
            preferredDir: AppContext.BaseDirectory,
            fallbackDir: Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "GPM"));

        var vm = new MainViewModel(
            scanGames: () => scanner.ScanAsync(config.Load().BetaSourcesEnabled),
            resolver: new ExeResolver(KnownGames.LoadEmbedded()),
            priorities: CreatePriorityService(),
            worker: new RunasElevatedWorker(),
            config: config,
            graphics: new GraphicsPreferenceService(new RealStringValueStore(
                RegistryKey.OpenBaseKey(RegistryHive.CurrentUser, RegistryView.Registry64),
                @"Software\Microsoft\DirectX\UserGpuPreferences")));
        _ = vm.LoadAsync(); // kick off the first scan; UI binds to Rows as it populates
        return vm;
    }
}
