using GPM.Core.Resolve;

namespace GPM.Core.Tests;

public class ExeHeuristicsTests
{
    private static ExeCandidate C(string path, long size = 1_000) => new(path, size);

    [Fact]
    public void single_candidate_after_blacklist_is_confident()
    {
        var r = ExeHeuristics.Pick([
            C(@"C:\g\Game.exe", 50_000_000),
            C(@"C:\g\UnrealCrashHandler.exe", 5_000_000),
            C(@"C:\g\EasyAntiCheat_Setup.exe"),
            C(@"C:\g\UEPrereqSetup_x64.exe"),
            C(@"C:\g\dxsetup.exe"),
        ]);
        Assert.Equal(@"C:\g\Game.exe", r.ExePath);
        Assert.False(r.NeedsConfirmation);
    }

    [Fact]
    public void unique_win64_shipping_wins_even_when_smaller()
    {
        var r = ExeHeuristics.Pick([
            C(@"C:\g\ReadyOrNot\Binaries\Win64\ReadyOrNotSteam-Win64-Shipping.exe", 100),
            C(@"C:\g\Launcher.exe", 900_000_000),
        ]);
        Assert.Equal(@"C:\g\ReadyOrNot\Binaries\Win64\ReadyOrNotSteam-Win64-Shipping.exe", r.ExePath);
        Assert.False(r.NeedsConfirmation);
    }

    [Fact]
    public void ambiguous_set_picks_largest_but_needs_confirmation()
    {
        // "No Rest For The Wicked" case: main exe smaller than its crash handler
        // (crash handler is blacklisted, but two legit exes remain -> ambiguous)
        var r = ExeHeuristics.Pick([
            C(@"C:\g\wicked.exe", 80_000_000),
            C(@"C:\g\tools.exe", 10_000_000),
        ]);
        Assert.Equal(@"C:\g\wicked.exe", r.ExePath);
        Assert.True(r.NeedsConfirmation);
        Assert.Equal([@"C:\g\wicked.exe", @"C:\g\tools.exe"], r.Candidates); // largest first
    }

    [Fact]
    public void everything_blacklisted_needs_human_with_original_candidates()
    {
        var all = new[] { C(@"C:\g\setup.exe"), C(@"C:\g\redist.exe") };
        var r = ExeHeuristics.Pick(all);
        Assert.Null(r.ExePath);
        Assert.True(r.NeedsConfirmation);
        Assert.Equal(2, r.Candidates.Count);
    }

    [Fact]
    public void empty_input_needs_human()
    {
        var r = ExeHeuristics.Pick([]);
        Assert.Null(r.ExePath);
        Assert.True(r.NeedsConfirmation);
    }

    [Theory]
    [InlineData(@"C:\g\GameCrashHandler64.exe")]
    [InlineData(@"C:\g\Setup.exe")]
    [InlineData(@"C:\g\UEPrereqSetup.exe")]
    [InlineData(@"C:\g\EasyAntiCheat.exe")]
    [InlineData(@"C:\g\vconsole2.exe")]
    [InlineData(@"C:\g\vc_redist.x64.exe")]
    [InlineData(@"C:\g\dxsetup.exe")]
    public void blacklisted_names_are_never_picked(string junk)
    {
        var r = ExeHeuristics.Pick([C(junk, 999_999_999), C(@"C:\g\real.exe", 1)]);
        Assert.Equal(@"C:\g\real.exe", r.ExePath);
    }
}
