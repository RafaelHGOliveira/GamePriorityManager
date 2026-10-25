using GPM.Core.Graphics;
using GPM.Core.Registry;

namespace GPM.Core.Tests;

public sealed class GraphicsPreferenceServiceTests : IDisposable
{
    private const string Exe = @"F:\Games\Foo\foo.exe";
    private readonly string _path = $@"Software\GPM-Test\{Guid.NewGuid():N}";
    private readonly RealStringValueStore _store;
    private readonly GraphicsPreferenceService _svc;

    public GraphicsPreferenceServiceTests()
    {
        _store = new RealStringValueStore(Microsoft.Win32.Registry.CurrentUser, _path);
        _svc = new GraphicsPreferenceService(_store);
    }

    public void Dispose() =>
        Microsoft.Win32.Registry.CurrentUser.DeleteSubKeyTree(_path, throwOnMissingSubKey: false);

    [Fact]
    public void read_of_unknown_exe_is_all_default()
    {
        Assert.Equal(GraphicsPreference.Default, _svc.Read(Exe));
    }

    [Fact]
    public void write_then_read_roundtrips()
    {
        _svc.Write(Exe, new GraphicsPreference(2, false));
        Assert.Equal(new GraphicsPreference(2, false), _svc.Read(Exe));
    }

    [Fact]
    public void write_uses_the_format_windows_settings_uses()
    {
        _svc.Write(Exe, new GraphicsPreference(2, true));
        Assert.Equal("GpuPreference=2;SwapEffectUpgradeEnable=1;", _store.Get(Exe));
    }

    [Fact]
    public void gpu_zero_reads_as_default()
    {
        _store.Set(Exe, "GpuPreference=0;");
        Assert.Null(_svc.Read(Exe).Gpu);
    }

    [Fact]
    public void write_preserves_tokens_gpm_does_not_own()
    {
        _store.Set(Exe, "AutoHDREnable=2097;GpuPreference=1;VRROptimizeEnable=0;");
        _svc.Write(Exe, new GraphicsPreference(2, null));
        Assert.Equal("AutoHDREnable=2097;GpuPreference=2;VRROptimizeEnable=0;", _store.Get(Exe));
    }

    [Fact]
    public void default_removes_token_and_keeps_foreign_ones()
    {
        _store.Set(Exe, "AutoHDREnable=2097;GpuPreference=2;SwapEffectUpgradeEnable=1;");
        _svc.Write(Exe, GraphicsPreference.Default);
        Assert.Equal("AutoHDREnable=2097;", _store.Get(Exe));
    }

    [Fact]
    public void default_on_last_token_deletes_the_value()
    {
        _svc.Write(Exe, new GraphicsPreference(2, null));
        _svc.Write(Exe, GraphicsPreference.Default);
        Assert.Null(_store.Get(Exe));
    }

    [Fact]
    public void default_on_missing_value_does_not_create_one()
    {
        _svc.Write(Exe, GraphicsPreference.Default);
        Assert.Empty(_store.GetValueNames());
    }

    [Fact]
    public void write_never_touches_other_exes()
    {
        const string other = @"D:\Other\other.exe";
        _store.Set(other, "GpuPreference=1;");
        _svc.Write(Exe, new GraphicsPreference(2, true));
        Assert.Equal("GpuPreference=1;", _store.Get(other));
    }

    [Fact]
    public void tolerates_whitespace_and_missing_trailing_semicolon()
    {
        _store.Set(Exe, " GpuPreference=2 ; SwapEffectUpgradeEnable=0");
        Assert.Equal(new GraphicsPreference(2, false), _svc.Read(Exe));
    }

    [Fact]
    public void malformed_values_read_as_default_and_survive_a_write()
    {
        _store.Set(Exe, "GpuPreference=banana;junk;Foo=bar;");
        Assert.Equal(GraphicsPreference.Default, _svc.Read(Exe));
        _svc.Write(Exe, new GraphicsPreference(1, null));
        Assert.Equal("GpuPreference=1;junk;Foo=bar;", _store.Get(Exe));
    }

    [Theory]
    [InlineData(3)]
    [InlineData(-1)]
    public void rejects_gpu_values_outside_0_to_2(int gpu)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            _svc.Write(Exe, new GraphicsPreference(gpu, null)));
    }

    [Theory]
    [InlineData("")]
    [InlineData("foo.exe")]
    [InlineData(@"..\foo.exe")]
    [InlineData(@"F:\Games\Foo\")]
    public void rejects_non_absolute_or_non_exe_paths(string path)
    {
        Assert.Throws<ArgumentException>(() => _svc.Write(path, new GraphicsPreference(2, null)));
    }
}
