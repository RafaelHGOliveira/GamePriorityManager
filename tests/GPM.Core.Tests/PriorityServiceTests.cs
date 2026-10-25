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

    [Theory]
    [InlineData(@"..\..\evil.exe")]
    [InlineData(@"foo\Debugger")]
    [InlineData("foo/bar.exe")]
    [InlineData("")]
    [InlineData("   ")]
    public void path_separator_in_exename_is_rejected(string exe) =>
        Assert.Throws<ArgumentException>(() => _svc.Write(exe, 3, null));

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

public class PriorityServiceUnreadableKeyTests
{
    /// One subkey ("locked.exe") throws like an IFEO key the current user can't read.
    private sealed class PartlyLockedRoot : IRegistryRoot
    {
        public IReadOnlyList<string> GetSubKeyNames() => ["locked.exe", "game.exe"];
        public IReadOnlyList<string> GetValueNames(string subKey) =>
            subKey.StartsWith("locked") ? throw new System.Security.SecurityException() : [];
        public int? GetDword(string subKey, string valueName) =>
            subKey.StartsWith("locked") ? throw new UnauthorizedAccessException()
            : valueName == PriorityValues.CpuValueName ? 3 : null;
        public void SetDword(string subKey, string valueName, int value) => throw new NotSupportedException();
        public void DeleteValue(string subKey, string valueName) => throw new NotSupportedException();
        public void DeleteSubKeyIfEmpty(string subKey) => throw new NotSupportedException();
        public bool KeyExists(string subKey) => true;
    }

    [Fact]
    public void read_all_skips_keys_the_user_cannot_read()
    {
        var all = new PriorityService(new PartlyLockedRoot()).ReadAll();

        Assert.Equal("game.exe", Assert.Single(all).ExeName);
    }
}

/// Same scenario through the real RegistryKey stack: a subkey whose ACL denies read
/// to the current user, as some HKLM IFEO keys do (crashed the app on 2026-10-16).
public sealed class PriorityServiceDeniedKeyTests : IDisposable
{
    private readonly string _path = $@"Software\GPM-Test\{Guid.NewGuid():N}";
    private readonly System.Security.AccessControl.RegistryAccessRule _deny = new(
        System.Security.Principal.WindowsIdentity.GetCurrent().User!,
        System.Security.AccessControl.RegistryRights.QueryValues,
        System.Security.AccessControl.AccessControlType.Deny);

    public PriorityServiceDeniedKeyTests()
    {
        using var root = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(_path);
        using (var game = root.CreateSubKey(@"game.exe\PerfOptions"))
            game.SetValue(PriorityValues.CpuValueName, 3, RegistryValueKind.DWord);
        using var locked = root.CreateSubKey("locked.exe");
        var acl = locked.GetAccessControl();
        acl.AddAccessRule(_deny);
        locked.SetAccessControl(acl);
    }

    public void Dispose()
    {
        using (var locked = Microsoft.Win32.Registry.CurrentUser.OpenSubKey($@"{_path}\locked.exe",
            RegistryKeyPermissionCheck.ReadWriteSubTree, System.Security.AccessControl.RegistryRights.ChangePermissions))
        {
            var acl = new System.Security.AccessControl.RegistrySecurity();
            acl.RemoveAccessRule(_deny); // empty explicit ACL; inheritance restores access
            locked?.SetAccessControl(acl);
        }
        Microsoft.Win32.Registry.CurrentUser.DeleteSubKeyTree(_path, throwOnMissingSubKey: false);
    }

    [Fact]
    public void read_all_skips_a_key_whose_acl_denies_read()
    {
        var svc = new PriorityService(new RealRegistryRoot(Microsoft.Win32.Registry.CurrentUser, _path));

        Assert.Equal("game.exe", Assert.Single(svc.ReadAll()).ExeName);
    }
}
