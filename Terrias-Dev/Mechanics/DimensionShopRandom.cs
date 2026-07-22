using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;

namespace Terrias.Dll.Mechanics;

public static class DimensionShopRandom
{
    public static List<T> Sample<T>(
        IReadOnlyList<T> values,
        string seed,
        string stream,
        int counter,
        int count)
    {
        var remaining = values == null ? new List<T>() : new List<T>(values);
        var result = new List<T>(Math.Min(Math.Max(0, count), remaining.Count));
        while (result.Count < count && remaining.Count > 0)
        {
            var index = Index(seed, stream + "." + result.Count, counter, remaining.Count);
            result.Add(remaining[index]);
            remaining.RemoveAt(index);
        }

        return result;
    }

    public static int Index(string seed, string stream, int counter, int count)
    {
        if (count <= 0)
        {
            return -1;
        }

        var payload = (seed ?? "") + "|" + (stream ?? "") + "|" + Math.Max(0, counter);
        using var sha = SHA256.Create();
        var hash = sha.ComputeHash(Encoding.UTF8.GetBytes(payload));
        var value = ((uint)hash[0] << 24)
                    | ((uint)hash[1] << 16)
                    | ((uint)hash[2] << 8)
                    | hash[3];
        return (int)(value % (uint)count);
    }
}
