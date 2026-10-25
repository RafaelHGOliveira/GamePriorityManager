namespace GPM.Core.Graphics;

/// One exe's Windows Graphics Preference (keyed by full exe path).
/// Gpu: 1 = power saving, 2 = high performance, null = Windows default.
/// WindowedOptimizations: true/false = forced on/off, null = follow the Windows global setting.
public sealed record GraphicsPreference(int? Gpu, bool? WindowedOptimizations)
{
    public static readonly GraphicsPreference Default = new(null, null);
}
