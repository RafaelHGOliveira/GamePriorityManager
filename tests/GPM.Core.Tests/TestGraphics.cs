using GPM.Core.Graphics;
using GPM.Core.Registry;

namespace GPM.Core.Tests;

internal static class TestGraphics
{
    public static GraphicsPreferenceService New(IStringValueStore? store = null) =>
        new(store ?? new InMemoryStringValueStore());
}
