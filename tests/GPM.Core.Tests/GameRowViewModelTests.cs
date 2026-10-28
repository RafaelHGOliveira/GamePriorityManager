using GPM.App.ViewModels;
using GPM.Core.Changes;
using GPM.Core.Models;

namespace GPM.Core.Tests;

public class GameRowViewModelTests
{
    [Fact]
    public void game_row_flags_linked_and_generic_names()
    {
        var game = new Game { Name = "G", Store = GameStore.Manual, ExePath = @"C:\g\game.exe" };
        var row = new GameRowViewModel(game, new ExeOverrideState("game.exe", null, null), linkedCount: 2);
        Assert.True(row.IsLinked);
        Assert.True(row.HasGenericExeName);

        var row2 = new GameRowViewModel(game with { ExePath = @"C:\g\eldenring.exe" },
            new ExeOverrideState("eldenring.exe", null, null), linkedCount: 1);
        Assert.False(row2.IsLinked);
        Assert.False(row2.HasGenericExeName);
    }
}
