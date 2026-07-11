using System.Text.Json;
using GPM.Core.Priority;

namespace GPM.Core.Worker;

public static class ApplyWorker
{
    public const int ExitOk = 0;
    public const int ExitPartial = 2;   // some ops failed
    public const int ExitRejected = 3;  // batch failed validation
    public const int ExitCrash = 4;     // unreadable input / unexpected error

    public static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);

    public static int Run(string opsPath, IPriorityService priorities)
    {
        var resultPath = Path.ChangeExtension(opsPath, ".result.json");
        try
        {
            var ops = JsonSerializer.Deserialize<List<ApplyOp>>(File.ReadAllText(opsPath), JsonOpts);
            if (OpsValidator.Validate(ops) is string batchError)
            {
                WriteReport(resultPath, new ApplyReport(true, batchError, []));
                return ExitRejected;
            }

            var results = new List<ApplyOpResult>();
            foreach (var op in ops!)
            {
                try
                {
                    priorities.Write(op.Exe.ToLowerInvariant(), op.Cpu, op.Io);
                    results.Add(new(op.Exe, true, null));
                }
                catch (Exception ex)
                {
                    results.Add(new(op.Exe, false, ex.Message));
                }
            }
            WriteReport(resultPath, new ApplyReport(false, null, results));
            return results.All(r => r.Success) ? ExitOk : ExitPartial;
        }
        catch (Exception ex)
        {
            try { WriteReport(resultPath, new ApplyReport(true, ex.Message, [])); } catch { /* best effort */ }
            return ExitCrash;
        }
    }

    private static void WriteReport(string path, ApplyReport report) =>
        File.WriteAllText(path, JsonSerializer.Serialize(report, JsonOpts));
}
