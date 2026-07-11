using GPM.Core.Models;
using GPM.Core.Priority;

namespace GPM.Core.Tests;

public class ContractsTests
{
    [Fact]
    public void game_exename_is_lowercase_filename()
    {
        var g = new Game { Name = "ELDEN RING", Store = GameStore.Steam, ExePath = @"C:\Games\ELDEN RING\Game\ELDENRING.exe" };
        Assert.Equal("eldenring.exe", g.ExeName);
    }

    [Fact]
    public void game_exename_is_null_when_unresolved()
    {
        var g = new Game { Name = "X", Store = GameStore.Manual };
        Assert.Null(g.ExeName);
    }

    [Fact]
    public void inmemory_priority_service_rejects_realtime()
    {
        var svc = new InMemoryPriorityService();
        Assert.Throws<ArgumentOutOfRangeException>(() => svc.Write("x.exe", 4, null));
    }

    [Fact]
    public void inmemory_priority_service_roundtrips_and_deletes()
    {
        var svc = new InMemoryPriorityService();
        svc.Write("x.exe", 3, 2);
        Assert.Equal(3, svc.Read("x.exe").Cpu);
        svc.Write("x.exe", null, null);
        Assert.Null(svc.Read("x.exe").Cpu);
        Assert.Empty(svc.ReadAll());
    }
}
