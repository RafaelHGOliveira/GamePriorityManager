using System.IO;
using GPM.App.Services;
using GPM.App.ViewModels;
using GPM.Core.Graphics;
using GPM.Core.Models;
using GPM.Core.Persistence;
using GPM.Core.Priority;
using GPM.Core.Registry;
using GPM.Core.Resolve;

namespace GPM.App.DesignData;

/// Fake wiring so the UI shell runs before plans A/B/C are merged.
/// Plan E replaces this with the real composition root.
public static class MockFactory
{
    private sealed class PassthroughResolver : IExeResolver
    {
        public ExeResolution Resolve(Game game) => new(game.ExePath, game.ExePath is null, []);
    }

    private sealed class MockLauncher(InMemoryPriorityService svc) : IElevationLauncher
    {
        public async Task<int?> RunApplyAsync(string opsPath)
        {
            var ops = System.Text.Json.JsonSerializer.Deserialize<List<GPM.Core.Worker.ApplyOp>>(
                await File.ReadAllTextAsync(opsPath),
                new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web))!;
            foreach (var op in ops) svc.Write(op.Exe, op.Cpu, op.Io);
            return 0;
        }
    }

    private sealed class MemoryConfigStore : IConfigStore
    {
        private GpmConfig _cfg = new();
        public string FilePath => "mock://games.json";
        public GpmConfig Load() => _cfg;
        public void Save(GpmConfig config) => _cfg = config;
    }

    public static MainViewModel CreateMainViewModel()
    {
        var svc = new InMemoryPriorityService();
        svc.Write("cs2.exe", 3, null);
        svc.Write("voicemeeterpro.exe", 6, null);
        IReadOnlyList<Game> games =
        [
            new() { Name = "Counter-Strike 2", Store = GameStore.Steam, ExePath = @"C:\mock\cs2.exe" },
            new() { Name = "ELDEN RING", Store = GameStore.Steam, ExePath = @"C:\mock\eldenring.exe" },
            new() { Name = "Rocket League", Store = GameStore.Epic, ExePath = @"C:\mock\RocketLeague.exe" },
            new() { Name = "Some UE Game", Store = GameStore.Gog, ExePath = @"C:\mock\game.exe" }, // generic-name warning
        ];
        var vm = new MainViewModel(() => Task.FromResult(games),
            new PassthroughResolver(), svc, new MockLauncher(svc), new MemoryConfigStore(),
            new GraphicsPreferenceService(new InMemoryStringValueStore()));
        _ = vm.LoadAsync();
        return vm;
    }
}
