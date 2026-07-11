namespace GPM.App.ViewModels;

public sealed record PriorityOption(string Label, int? Value);

public static class PriorityOptions
{
    public static readonly IReadOnlyList<PriorityOption> Cpu =
    [
        new(Strings.Priority_Default, null),
        new(Strings.Priority_Idle, 1),
        new(Strings.Priority_BelowNormal, 5),
        new(Strings.Priority_Normal, 2),
        new(Strings.Priority_AboveNormal, 6),
        new(Strings.Priority_High, 3),
    ];

    public static readonly IReadOnlyList<PriorityOption> Io =
    [
        new(Strings.Priority_Default, null),
        new(Strings.Priority_VeryLow, 0),
        new(Strings.Priority_Low, 1),
        new(Strings.Priority_Normal, 2),
        new(Strings.Priority_High, 3),
    ];
}
