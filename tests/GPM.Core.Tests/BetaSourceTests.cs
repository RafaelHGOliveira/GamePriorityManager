using GPM.Core.Detection;
using GPM.Core.Models;
using Microsoft.Win32;

namespace GPM.Core.Tests;

public class BetaSourceTests
{
    [Fact]
    public async Task ea_lists_subkeys_with_install_dir()
    {
        var reg = new FakeStoreRegistry();
        const string root = @"SOFTWARE\WOW6432Node\Electronic Arts";
        reg.Set(RegistryHive.LocalMachine, $@"{root}\Battlefield 2042", "Install Dir", @"D:\EA\BF2042\");
        reg.Set(RegistryHive.LocalMachine, $@"{root}\EA Desktop", "InstallSuccessful", "true"); // no Install Dir -> skipped

        var src = new EaSource(reg);
        Assert.True(src.IsBeta);

        var g = Assert.Single(await src.ScanAsync());
        Assert.Equal("Battlefield 2042", g.Name);
        Assert.Equal(GameStore.Ea, g.Store);
        Assert.Equal(@"D:\EA\BF2042", g.InstallDir);
        Assert.Null(g.ExePath); // EA doesn't name the exe -> plan C resolves
    }

    [Fact]
    public async Task ubisoft_reads_installs_and_names_from_dir()
    {
        var reg = new FakeStoreRegistry();
        const string root = @"SOFTWARE\WOW6432Node\Ubisoft\Launcher\Installs";
        reg.Set(RegistryHive.LocalMachine, $@"{root}\635", "InstallDir", "D:/Ubisoft/Anno 1800/");

        var src = new UbisoftSource(reg);
        Assert.True(src.IsBeta);

        var g = Assert.Single(await src.ScanAsync());
        Assert.Equal("Anno 1800", g.Name); // last path segment
        Assert.Equal(GameStore.Ubisoft, g.Store);
        Assert.Equal("635", g.StoreId);
        Assert.Equal(@"D:\Ubisoft\Anno 1800", g.InstallDir);
    }

    [Fact]
    public async Task both_return_empty_when_not_installed()
    {
        Assert.Empty(await new EaSource(new FakeStoreRegistry()).ScanAsync());
        Assert.Empty(await new UbisoftSource(new FakeStoreRegistry()).ScanAsync());
    }
}
