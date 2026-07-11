using GPM.Core.Detection;
using GPM.Core.Models;

namespace GPM.Core.Tests;

public class GameScannerTests
{
    private sealed class StubSource(GameStore store, bool isBeta, Func<IReadOnlyList<Game>> scan) : IGameSource
    {
        public GameStore Store => store;
        public bool IsBeta => isBeta;
        public Task<IReadOnlyList<Game>> ScanAsync(CancellationToken ct = default) =>
            Task.FromResult(scan());
    }

    private static Game G(string name, GameStore store) => new() { Name = name, Store = store };

    [Fact]
    public async Task merges_results_from_all_sources()
    {
        var scanner = new GameScanner([
            new StubSource(GameStore.Steam, false, () => [G("A", GameStore.Steam)]),
            new StubSource(GameStore.Epic, false, () => [G("B", GameStore.Epic)]),
        ]);
        Assert.Equal(2, (await scanner.ScanAsync(includeBeta: true)).Count);
    }

    [Fact]
    public async Task a_throwing_source_never_breaks_the_scan()
    {
        var scanner = new GameScanner([
            new StubSource(GameStore.Steam, false, () => throw new IOException("corrupt manifest")),
            new StubSource(GameStore.Epic, false, () => [G("B", GameStore.Epic)]),
        ]);
        var games = await scanner.ScanAsync(includeBeta: true);
        Assert.Equal("B", Assert.Single(games).Name);
    }

    [Fact]
    public async Task beta_sources_are_skipped_unless_enabled()
    {
        var scanner = new GameScanner([
            new StubSource(GameStore.Steam, false, () => [G("A", GameStore.Steam)]),
            new StubSource(GameStore.Ea, true, () => [G("C", GameStore.Ea)]),
        ]);
        Assert.Single(await scanner.ScanAsync(includeBeta: false));
        Assert.Equal(2, (await scanner.ScanAsync(includeBeta: true)).Count);
    }
}
