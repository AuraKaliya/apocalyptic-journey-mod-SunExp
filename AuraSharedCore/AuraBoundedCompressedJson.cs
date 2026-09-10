using System;
using System.IO;
using System.IO.Compression;
using System.Text;

namespace AuraShared.Core;

public static class AuraBoundedCompressedJson
{
    public static string Encode(string json, int maximumEncodedBytes)
    {
        using var buffer = new MemoryStream();
        using (var gzip = new GZipStream(buffer, CompressionMode.Compress, true))
        {
            var bytes = Encoding.UTF8.GetBytes(json ?? "");
            gzip.Write(bytes, 0, bytes.Length);
        }
        var value = Convert.ToBase64String(buffer.ToArray());
        if (value.Length > maximumEncodedBytes) throw new InvalidDataException("Compressed payload exceeds the wire budget.");
        return value;
    }

    public static string Decode(string value, int maximumDecodedBytes)
    {
        if (maximumDecodedBytes <= 0 || value == null || value.Length > Math.Max(65536, maximumDecodedBytes * 2L)) throw new InvalidDataException("Compressed payload is oversized.");
        using var input = new MemoryStream(Convert.FromBase64String(value));
        using var gzip = new GZipStream(input, CompressionMode.Decompress);
        using var output = new MemoryStream();
        var block = new byte[8192];
        int count;
        while ((count = gzip.Read(block, 0, block.Length)) != 0)
        {
            if (output.Length + count > maximumDecodedBytes) throw new InvalidDataException("Decoded payload exceeds its budget.");
            output.Write(block, 0, count);
        }
        return new UTF8Encoding(false, true).GetString(output.ToArray());
    }
}
