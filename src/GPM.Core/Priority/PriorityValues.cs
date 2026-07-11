namespace GPM.Core.Priority;

public static class PriorityValues
{
    public static readonly int[] ValidCpu = [1, 2, 3, 5, 6]; // Realtime (4) NEVER
    public static readonly int[] ValidIo = [0, 1, 2, 3];
    public const string PerfOptions = "PerfOptions";
    public const string CpuValueName = "CpuPriorityClass";
    public const string IoValueName = "IoPriority";
    public const string IfeoPath =
        @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Image File Execution Options";
}
