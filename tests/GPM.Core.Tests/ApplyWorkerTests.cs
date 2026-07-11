using System.Text.Json;
using GPM.Core.Priority;
using GPM.Core.Worker;

namespace GPM.Core.Tests;

public class ApplyWorkerTests
{
    private static string TempOps(string json)
    {
        var path = Path.Combine(Path.GetTempPath(), $"gpm-test-{Guid.NewGuid():N}.json");
        File.WriteAllText(path, json);
        return path;
    }

    private static ApplyReport ReadReport(string opsPath) =>
        JsonSerializer.Deserialize<ApplyReport>(
            File.ReadAllText(Path.ChangeExtension(opsPath, ".result.json")),
            ApplyWorker.JsonOpts)!;

    [Fact]
    public void valid_batch_applies_and_exits_0()
    {
        var svc = new InMemoryPriorityService();
        var ops = TempOps("""[{"exe":"eldenring.exe","cpu":3,"io":2},{"exe":"cs2.exe","cpu":null,"io":null}]""");
        svc.Write("cs2.exe", 3, null); // pre-existing override that the second op removes

        Assert.Equal(ApplyWorker.ExitOk, ApplyWorker.Run(ops, svc));
        Assert.Equal((3, 2), svc.Data["eldenring.exe"]);
        Assert.False(svc.Data.ContainsKey("cs2.exe"));

        var report = ReadReport(ops);
        Assert.False(report.BatchRejected);
        Assert.All(report.Results, r => Assert.True(r.Success));
    }

    [Fact]
    public void invalid_batch_is_rejected_applies_nothing_exits_3()
    {
        var svc = new InMemoryPriorityService();
        var ops = TempOps("""[{"exe":"good.exe","cpu":3,"io":null},{"exe":"..\\evil.exe","cpu":3,"io":null}]""");

        Assert.Equal(ApplyWorker.ExitRejected, ApplyWorker.Run(ops, svc));
        Assert.Empty(svc.Data);                       // nothing applied
        Assert.True(ReadReport(ops).BatchRejected);
    }

    [Fact]
    public void partial_failure_exits_2_and_reports_per_item()
    {
        var svc = new ThrowOnSecondWriteService();
        var ops = TempOps("""[{"exe":"a.exe","cpu":3,"io":null},{"exe":"b.exe","cpu":3,"io":null}]""");

        Assert.Equal(ApplyWorker.ExitPartial, ApplyWorker.Run(ops, svc));
        var report = ReadReport(ops);
        Assert.True(report.Results.Single(r => r.Exe == "a.exe").Success);
        var failed = report.Results.Single(r => r.Exe == "b.exe");
        Assert.False(failed.Success);
        Assert.NotNull(failed.Error);
    }

    [Fact]
    public void garbage_json_exits_4_with_rejected_report()
    {
        var ops = TempOps("{not json[");
        Assert.Equal(ApplyWorker.ExitCrash, ApplyWorker.Run(ops, new InMemoryPriorityService()));
        Assert.True(ReadReport(ops).BatchRejected);
    }

    private sealed class ThrowOnSecondWriteService : IPriorityService
    {
        private int _writes;
        public PriorityOverride Read(string exeName) => new(exeName, null, null, false);
        public IReadOnlyList<PriorityOverride> ReadAll() => [];
        public void Write(string exeName, int? cpu, int? io)
        {
            if (++_writes > 1) throw new UnauthorizedAccessException("access denied");
        }
    }
}
