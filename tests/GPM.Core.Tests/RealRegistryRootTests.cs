using GPM.Core.Registry;
using Microsoft.Win32;

namespace GPM.Core.Tests;

public sealed class RealRegistryRootTests : IDisposable
{
    private readonly string _path = $@"Software\GPM-Test\{Guid.NewGuid():N}";
    private readonly RealRegistryRoot _root;

    public RealRegistryRootTests()
    {
        Microsoft.Win32.Registry.CurrentUser.CreateSubKey(_path);
        _root = new RealRegistryRoot(Microsoft.Win32.Registry.CurrentUser, _path);
    }

    public void Dispose() =>
        Microsoft.Win32.Registry.CurrentUser.DeleteSubKeyTree(_path, throwOnMissingSubKey: false);

    [Fact]
    public void set_get_delete_dword_roundtrip()
    {
        _root.SetDword(@"a.exe\PerfOptions", "CpuPriorityClass", 3);
        Assert.Equal(3, _root.GetDword(@"a.exe\PerfOptions", "CpuPriorityClass"));
        _root.DeleteValue(@"a.exe\PerfOptions", "CpuPriorityClass");
        Assert.Null(_root.GetDword(@"a.exe\PerfOptions", "CpuPriorityClass"));
    }

    [Fact]
    public void get_on_missing_key_returns_null_not_throw()
    {
        Assert.Null(_root.GetDword(@"nope.exe\PerfOptions", "CpuPriorityClass"));
        Assert.Empty(_root.GetValueNames("nope.exe"));
        Assert.False(_root.KeyExists("nope.exe"));
    }

    [Fact]
    public void delete_subkey_if_empty_only_deletes_empty_keys()
    {
        _root.SetDword(@"b.exe\PerfOptions", "IoPriority", 1);
        _root.DeleteSubKeyIfEmpty(@"b.exe\PerfOptions"); // not empty -> stays
        Assert.True(_root.KeyExists(@"b.exe\PerfOptions"));

        _root.DeleteValue(@"b.exe\PerfOptions", "IoPriority");
        _root.DeleteSubKeyIfEmpty(@"b.exe\PerfOptions"); // now empty -> gone
        _root.DeleteSubKeyIfEmpty("b.exe");
        Assert.False(_root.KeyExists("b.exe"));
    }

    [Fact]
    public void enumerates_subkeys_and_values()
    {
        _root.SetDword(@"c.exe\PerfOptions", "CpuPriorityClass", 1);
        Assert.Contains("c.exe", _root.GetSubKeyNames());
        Assert.Contains("CpuPriorityClass", _root.GetValueNames(@"c.exe\PerfOptions"));
    }
}
