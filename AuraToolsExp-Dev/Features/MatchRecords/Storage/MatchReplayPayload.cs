using System;
using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using AuraShared.Core;

namespace AuraToolsExp.Dll.Features.MatchRecords.Storage;

internal static class MatchReplayPayload
{
    internal static byte[] Encode<T>(T value)
    {
        var input = Encoding.UTF8.GetBytes(AuraSharedJson.SerializeCompact(value));
        using var output = new MemoryStream();
        using (var gzip = new GZipStream(output, CompressionLevel.Optimal, leaveOpen: true))
        {
            gzip.Write(input, 0, input.Length);
        }

        return output.ToArray();
    }

    internal static T? Decode<T>(byte[] payload)
    {
        if (payload == null || payload.Length == 0)
        {
            return default;
        }

        using var input = new MemoryStream(payload, writable: false);
        using var gzip = new GZipStream(input, CompressionMode.Decompress);
        using var reader = new StreamReader(gzip, Encoding.UTF8);
        return AuraSharedJson.Deserialize<T>(reader.ReadToEnd());
    }

    internal static string Sha256(byte[] payload)
    {
        using var sha = SHA256.Create();
        var hash = sha.ComputeHash(payload ?? Array.Empty<byte>());
        var builder = new StringBuilder(hash.Length * 2);
        foreach (var value in hash)
        {
            builder.Append(value.ToString("x2"));
        }

        return builder.ToString();
    }
}
