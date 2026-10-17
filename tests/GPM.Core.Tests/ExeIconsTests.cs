using GPM.App;

namespace GPM.Core.Tests;

public class ExeIconsTests
{
    private static readonly string Notepad =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "notepad.exe");

    [Fact]
    public async Task extracts_off_thread_a_frozen_icon_and_reuses_it()
    {
        var first = await ExeIcons.GetAsync(Notepad);

        Assert.NotNull(first);
        Assert.True(first.IsFrozen); // built on a pool thread, must be shareable with the UI thread
        Assert.Same(first, await ExeIcons.GetAsync(Notepad.ToUpperInvariant())); // one extraction per path
    }

    [Fact]
    public async Task missing_or_null_path_yields_no_icon()
    {
        Assert.Null(await ExeIcons.GetAsync(null));
        Assert.Null(await ExeIcons.GetAsync(@"C:\definitely\not\here.exe"));
    }
}
