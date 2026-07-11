using GPM.App.ViewModels;

namespace GPM.Core.Tests;

public class PriorityOptionsTests
{
    [Fact]
    public void cpu_options_match_spec_order_and_values()
    {
        Assert.Equal([null, 1, 5, 2, 6, 3], PriorityOptions.Cpu.Select(o => o.Value)); // Normal(2) included; Realtime(4) absent
        Assert.Equal("Default", PriorityOptions.Cpu[0].Label);
        Assert.Equal("Normal", PriorityOptions.Cpu[3].Label);
    }

    [Fact]
    public void io_options_match_spec()
    {
        Assert.Equal([null, 0, 1, 2, 3], PriorityOptions.Io.Select(o => o.Value));
    }
}
