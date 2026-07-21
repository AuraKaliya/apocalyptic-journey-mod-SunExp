using System;
using System.Security.Cryptography;
using System.Text;

namespace Terrias.Dll.Mechanics;

public static class DimensionShopRandom
{
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
