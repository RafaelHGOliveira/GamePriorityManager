using GPM.Core.Worker;

namespace GPM.Core.Tests;

public class OpsValidatorTests
{
    private static ApplyOp Op(string exe, int? cpu = 3, int? io = null) => new(exe, cpu, io);

    [Fact]
    public void valid_batch_returns_null()
    {
        Assert.Null(OpsValidator.Validate([Op("eldenring.exe"), Op("cs2.exe", null, 2), Op("a.exe", null, null)]));
    }

    [Fact]
    public void empty_or_null_batch_rejected()
    {
        Assert.NotNull(OpsValidator.Validate(null));
        Assert.NotNull(OpsValidator.Validate([]));
    }

    [Theory]
    [InlineData(@"..\evil.exe")]          // path separator
    [InlineData("dir/evil.exe")]          // path separator
    [InlineData("evil.dll")]              // not .exe
    [InlineData("evil")]                  // not .exe
    [InlineData("")]                      // empty
    [InlineData("bad|name.exe")]          // invalid filename char
    public void bad_exe_rejects_batch(string exe) =>
        Assert.NotNull(OpsValidator.Validate([Op(exe)]));

    [Fact]
    public void exe_longer_than_255_chars_rejected() =>
        Assert.NotNull(OpsValidator.Validate([Op(new string('a', 252) + ".exe")]));

    [Theory]
    [InlineData(4)]   // Realtime
    [InlineData(0)]
    public void invalid_cpu_rejects_batch(int cpu) =>
        Assert.NotNull(OpsValidator.Validate([Op("a.exe", cpu)]));

    [Theory]
    [InlineData(-1)]
    [InlineData(4)]
    public void invalid_io_rejects_batch(int io) =>
        Assert.NotNull(OpsValidator.Validate([Op("a.exe", null, io)]));

    [Fact]
    public void duplicate_exe_rejects_whole_batch_case_insensitive() =>
        Assert.NotNull(OpsValidator.Validate([Op("a.exe"), Op("A.EXE")]));
}
