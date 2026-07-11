using GPM.App.ViewModels;
using GPM.Core.Models;

namespace GPM.Core.Tests;

public class ExeOverrideStateTests
{
    [Fact]
    public void starts_clean_matching_registry()
    {
        var s = new ExeOverrideState("Game.EXE", 3, null);
        Assert.Equal("game.exe", s.ExeName);
        Assert.False(s.IsDirty);
    }

    [Fact]
    public void changing_pending_makes_dirty_and_raises_isdirty_notification()
    {
        var s = new ExeOverrideState("a.exe", null, null);
        var raised = new List<string?>();
        s.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        s.PendingCpu = 3;
        Assert.True(s.IsDirty);
        Assert.Contains(nameof(ExeOverrideState.IsDirty), raised);
    }

    [Fact]
    public void discard_reverts_to_registry()
    {
        var s = new ExeOverrideState("a.exe", 5, 1);
        s.PendingCpu = 3; s.PendingIo = null;
        s.DiscardPending();
        Assert.False(s.IsDirty);
        Assert.Equal(5, s.PendingCpu);
        Assert.Equal(1, s.PendingIo);
    }

    [Fact]
    public void setregistry_after_successful_apply_makes_row_clean()
    {
        var s = new ExeOverrideState("a.exe", null, null);
        s.PendingCpu = 3;
        s.SetRegistry(3, null); // registry now matches pending
        Assert.False(s.IsDirty);
    }

    [Fact]
    public void setregistry_after_failed_apply_keeps_row_dirty()
    {
        var s = new ExeOverrideState("a.exe", null, null);
        s.PendingCpu = 3;
        s.SetRegistry(null, null); // write failed -> registry unchanged
        Assert.True(s.IsDirty);
    }

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
