using GPM.Core.Resolve;

namespace GPM.Core.Tests;

public class KnownGamesTests
{
    [Fact]
    public void embedded_list_loads_and_contains_seed_games()
    {
        var known = KnownGames.LoadEmbedded();
        Assert.True(known.TryGetPath("1245620", out var eldenRing)); // Elden Ring
        Assert.Equal(@"Game\eldenring.exe", eldenRing);              // NOT start_protected_game.exe
        Assert.True(known.TryGetPath("730", out var cs2));
        Assert.Equal(@"game\bin\win64\cs2.exe", cs2);
        Assert.False(known.TryGetPath("999999999", out _));
    }

    [Fact]
    public void fromjson_parses_custom_map()
    {
        var known = KnownGames.FromJson("""{"42":"bin\\game.exe"}""");
        Assert.True(known.TryGetPath("42", out var path));
        Assert.Equal(@"bin\game.exe", path);
    }
}
