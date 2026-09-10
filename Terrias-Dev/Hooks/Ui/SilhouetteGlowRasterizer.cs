using System;

namespace Terrias.Dll.Hooks.Ui;

// Produces a white alpha-only outer halo. Flood filling from the padded exterior
// prevents enclosed transparent holes from acquiring their own selection rings.
public static class SilhouetteGlowRasterizer
{
    public static byte[] Create(byte[] alpha, int width, int height, int padding, float coreWidth, float feather)
    {
        if (width <= 0 || height <= 0 || alpha.Length != width * height || padding < 1 || coreWidth < 0f || feather <= 0f)
            throw new ArgumentException("Invalid silhouette dimensions or glow profile.");
        var w = width + padding * 2;
        var h = height + padding * 2;
        var occupied = new bool[w * h];
        var hasShape = false;
        for (var y = 0; y < height; y++)
        for (var x = 0; x < width; x++)
        {
            var solid = alpha[y * width + x] >= 32;
            occupied[(y + padding) * w + x + padding] = solid;
            hasShape |= solid;
        }
        var result = new byte[w * h];
        if (!hasShape) return result;

        var exterior = new bool[w * h];
        var queue = new int[w * h];
        var head = 0;
        var tail = 1;
        queue[0] = 0;
        exterior[0] = true;
        void Visit(int index)
        {
            if (occupied[index] || exterior[index]) return;
            exterior[index] = true;
            queue[tail++] = index;
        }
        while (head < tail)
        {
            var p = queue[head++];
            var x = p % w;
            var y = p / w;
            if (x > 0) Visit(p - 1);
            if (x + 1 < w) Visit(p + 1);
            if (y > 0) Visit(p - w);
            if (y + 1 < h) Visit(p + w);
        }

        var distance = new float[w * h];
        var limit = w + h;
        for (var x = 0; x < w; x++)
        {
            var nearest = limit;
            for (var y = 0; y < h; y++)
            {
                var p = y * w + x;
                nearest = !exterior[p] ? 0 : Math.Min(limit, nearest + 1);
                distance[p] = nearest;
            }
            nearest = limit;
            for (var y = h - 1; y >= 0; y--)
            {
                var p = y * w + x;
                nearest = !exterior[p] ? 0 : Math.Min(limit, nearest + 1);
                var value = Math.Min(distance[p], nearest);
                distance[p] = value * value;
            }
        }

        var sites = new int[w];
        var crossings = new double[w + 1];
        for (var y = 0; y < h; y++)
        {
            var start = y * w;
            var k = 0;
            sites[0] = 0;
            crossings[0] = double.NegativeInfinity;
            crossings[1] = double.PositiveInfinity;
            for (var q = 1; q < w; q++)
            {
                double intersection;
                do
                {
                    var v = sites[k];
                    intersection = ((double)distance[start + q] + q * q - distance[start + v] - v * v) / (2d * (q - v));
                    if (intersection > crossings[k]) break;
                    k--;
                } while (k >= 0);
                sites[++k] = q;
                crossings[k] = intersection;
                crossings[k + 1] = double.PositiveInfinity;
            }
            k = 0;
            for (var x = 0; x < w; x++)
            {
                if (!exterior[start + x]) continue;
                while (crossings[k + 1] < x) k++;
                var dx = x - sites[k];
                var d = Math.Sqrt(dx * dx + distance[start + sites[k]]) - 0.5;
                var t = Math.Max(0d, Math.Min(1d, (coreWidth + feather - d) / feather));
                var core = Math.Max(0d, Math.Min(1d, coreWidth + 0.5d - d));
                var soft = t * t * (3d - 2d * t);
                result[start + x] = (byte)Math.Round(255d * Math.Max(core, 0.38d * soft * soft));
            }
        }
        return result;
    }
}
