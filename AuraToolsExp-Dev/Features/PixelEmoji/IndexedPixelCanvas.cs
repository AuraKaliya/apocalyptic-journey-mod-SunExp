using System;
using System.Collections.Generic;

namespace AuraToolsExp.Dll.Features.PixelEmoji;

/// <summary>Dimension-independent indexed raster operations shared by emoji and card artwork.</summary>
public static class IndexedPixelCanvas
{
    public static void DrawLine(byte[] pixels, int size, int x0, int y0, int x1, int y1, byte color)
    {
        Require(pixels, size);
        if (Math.Abs((long)x0) > 4096 || Math.Abs((long)y0) > 4096 || Math.Abs((long)x1) > 4096 || Math.Abs((long)y1) > 4096) throw new ArgumentOutOfRangeException("Line coordinates are out of range.");
        int dx = Math.Abs(x1 - x0), sx = x0 < x1 ? 1 : -1, dy = -Math.Abs(y1 - y0), sy = y0 < y1 ? 1 : -1, error = dx + dy;
        while (true)
        {
            if (Inside(x0, y0, size)) pixels[y0 * size + x0] = color;
            if (x0 == x1 && y0 == y1) break;
            int doubled = error * 2;
            if (doubled >= dy) { error += dy; x0 += sx; }
            if (doubled <= dx) { error += dx; y0 += sy; }
        }
    }
    public static bool Fill(byte[] pixels, int size, int x, int y, byte replacement)
    {
        Require(pixels, size);
        if (!Inside(x, y, size)) return false;
        byte target = pixels[y * size + x];
        if (target == replacement) return false;
        var q = new Queue<int>();
        void Add(int px, int py)
        {
            if (!Inside(px, py, size) || pixels[py * size + px] != target) return;
            pixels[py * size + px] = replacement; q.Enqueue(py * size + px);
        }
        Add(x, y);
        while (q.Count > 0)
        {
            int i = q.Dequeue(), px = i % size, py = i / size;
            Add(px - 1, py); Add(px + 1, py); Add(px, py - 1); Add(px, py + 1);
        }
        return true;
    }
    public static byte[] Resize(byte[] pixels, int from, int to)
    {
        Require(pixels, from);
        if (to < 1 || to > 256) throw new ArgumentOutOfRangeException(nameof(to));
        var output = new byte[to * to];
        for (int y = 0; y < to; y++) for (int x = 0; x < to; x++) output[y * to + x] = pixels[(y * from / to) * from + x * from / to];
        return output;
    }
    private static bool Inside(int x, int y, int size) => x >= 0 && y >= 0 && x < size && y < size;
    private static void Require(byte[] pixels, int size) { if (size < 1 || size > 256 || pixels == null || pixels.Length != size * size) throw new ArgumentException("Invalid indexed pixel canvas."); }
}
