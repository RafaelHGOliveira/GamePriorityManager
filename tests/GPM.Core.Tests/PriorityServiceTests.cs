using GPM.Core.Priority;
using GPM.Core.Registry;
using Microsoft.Win32;

namespace GPM.Core.Tests;

public sealed class PriorityServiceTests : IDisposable
{
    private readonly string _path = $@"Software\GPM-Test\{Guid.NewGuid():N}";
    private readonly RealRegistryRoot _root;
    private readonly PriorityService _svc;

    public PriorityServiceTests()
    {
        Microsoft.Win32.Registry.CurrentUser.CreateSubKey(_path);
        _root = new RealRegistryRoot(Microsoft.Win32.Registry.CurrentUser, _path);
        _svc = new PriorityService(_root);
    }

    public void Dispose() =>
        Microsoft.Win32.Registry.CurrentUser.DeleteSubKeyTree(_path, throwOnMissingSubKey: false);

    [Fact]
    public void write_then_read_roundtrips()
    {
        _svc.Write("eldenring.exe", 3, 2);
        var o = _svc.Read("eldenring.exe");
        Assert.Equal(3, o.Cpu);
        Assert.Equal(2, o.Io);
        Assert.False(o.HasDebugger);
    }

    [Fact]
    public void read_normalizes_exename_to_lowercase()
    {
        _svc.Write("CS2.exe", 3, null);
        Assert.Equal("cs2.exe", _svc.Read("CS2.exe").ExeName);
    }

    [Fact]
    public void null_deletes_value_and_cleans_empty_keys()
    {
        _svc.Write("cs2.exe", 3, 1);
        _svc.Write("cs2.exe", null, 1);          // cpu removed, io stays
        Assert.Null(_svc.Read("cs2.exe").Cpu);
        Assert.Equal(1, _svc.Read("cs2.exe").Io);

        _svc.Write("cs2.exe", null, null);       // all gone -> keys cleaned
        Assert.False(_root.KeyExists("cs2.exe"));
    }

    [Fact]
    public void write_never_touches_other_values()
    {
        _root.SetDword(@"game.exe\PerfOptions", "SomethingElse", 42);
        _svc.Write("game.exe", 3, null);
        _svc.Write("game.exe", null, null);
        Assert.Equal(42, _root.GetDword(@"game.exe\PerfOptions", "SomethingElse"));
        Assert.True(_root.KeyExists(@"game.exe\PerfOptions")); // not empty -> kept
    }

    [Theory]
    [InlineData(4)]   // Realtime
    [InlineData(0)]
    [InlineData(7)]
    public void invalid_cpu_is_rejected(int cpu) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => _svc.Write("x.exe", cpu, null));

    [Theory]
    [InlineData(-1)]
    [InlineData(4)]
    public void invalid_io_is_rejected(int io) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => _svc.Write("x.exe", null, io));

    [Fact]
    public void readall_lists_only_keys_with_overrides_and_flags_debugger()
    {
        _svc.Write("a.exe", 3, null);
        _root.SetDword(@"b.exe\PerfOptions", "IoPriority", 0);
        _root.SetDword("c.exe", "GlobalFlag", 1);              // no override -> excluded
        using (var key = Microsoft.Win32.Registry.CurrentUser.CreateSubKey($@"{_path}\a.exe"))
            key.SetValue("Debugger", @"C:\evil.exe");

        var all = _svc.ReadAll();
        Assert.Equal(2, all.Count);
        Assert.True(all.Single(o => o.ExeName == "a.exe").HasDebugger);
        Assert.False(all.Single(o => o.ExeName == "b.exe").HasDebugger);
    }
}
