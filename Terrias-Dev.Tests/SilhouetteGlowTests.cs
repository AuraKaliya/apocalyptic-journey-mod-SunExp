using System;
using System.Linq;
using Terrias.Dll.Hooks.Ui;

internal static partial class Program
{
    private static void TestSilhouetteGlow()
    {
        const int size = 31;
        const int pad = 9;
        var input = new byte[size * size];
        for (var y = 8; y <= 22; y++)
        for (var x = 8; x <= 22; x++) input[y * size + x] = 255;
        input[15 * size + 15] = 0;
        for (var y = 4; y < 8; y++) input[y * size + 15] = 255;
        var saved = input.ToArray();
        var halo = SilhouetteGlowRasterizer.Create(input, size, size, pad, 2f, 5f);
        byte At(int x, int y) => halo[(y + pad) * (size + pad * 2) + x + pad];
        True(input.SequenceEqual(saved), "Selection rasterization leaves the source alpha intact");
        Equal((byte)0, At(15, 15), "Enclosed transparent holes do not gain inner rings");
        Equal((byte)0, At(12, 12), "The actual card interior remains transparent in the glow layer");
        True(At(15, 3) > 230, "The selection follows a protruding tip");
        Equal((byte)0, At(0, 0), "The outer bounding rectangle does not become a selection frame");
        True(At(7, 15) > At(3, 15), "Soft glow fades outward from a bright silhouette edge");
        True(input.Select((value, index) => value < 32 || At(index % size, index / size) == 0).All(value => value),
            "Selection does not tint any opaque source pixel");
        True(SilhouetteGlowRasterizer.Create(new byte[25], 5, 5, 4, 1f, 2f).All(value => value == 0),
            "An empty sprite produces no rectangular fallback");
        var full = Enumerable.Repeat((byte)255, 25).ToArray();
        var fullHalo = SilhouetteGlowRasterizer.Create(full, 5, 5, 4, 1f, 2f);
        True(fullHalo[4 * 13 + 3] > 0, "Padding retains a contour where artwork touches the source texture edge");
        Equal((byte)0, fullHalo[0], "The generated texture reaches zero alpha at its outside corners");
    }
}
