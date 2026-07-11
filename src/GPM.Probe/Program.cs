using System.Diagnostics;
using System.Runtime.InteropServices;

// Prints this process's EFFECTIVE priorities. Used to verify that IFEO
// PerfOptions overrides are actually honored by Windows (the product's core
// promise) — including the IoPriority=3 implementation checkpoint from the spec.

var process = Process.GetCurrentProcess();
Console.WriteLine($"Process:            {Path.GetFileName(Environment.ProcessPath)}");
Console.WriteLine($"CPU priority class: {process.PriorityClass}");

int ioPriority = -1;
int status = NtQueryInformationProcess(process.Handle, ProcessIoPriority, ref ioPriority, sizeof(int), out _);
Console.WriteLine(status == 0
    ? $"IO priority:        {ioPriority} (0=VeryLow 1=Low 2=Normal 3=High)"
    : $"IO priority:        query failed (NTSTATUS 0x{status:X8})");

Console.WriteLine();
Console.WriteLine("Press Enter to exit.");
Console.ReadLine();

partial class Program
{
    private const int ProcessIoPriority = 33;

    [DllImport("ntdll.dll")]
    private static extern int NtQueryInformationProcess(
        nint processHandle, int processInformationClass,
        ref int processInformation, int processInformationLength, out int returnLength);
}
