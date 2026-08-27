using System;
using System.Collections.Generic;
using System.Linq;

namespace AuraToolsExp.Dll.Features.MatchRecords.ReplayV12.Core;

internal static class ReplayAssetContractV12
{
    internal const long MaximumSingleAssetBytes = 256L * 1024L * 1024L;
    internal const int MaximumImageDimension = 8192;

    internal static string Validate(ReplayAssetV12? asset, bool requirePayload)
    {
        if (asset == null || !IsSha256(asset.Sha256)) return "identity-invalid";
        if (!asset.Required) return "required-flag-invalid";
        if (asset.ByteLength <= 0 || asset.ByteLength > MaximumSingleAssetBytes) return "byte-length-invalid";
        if ((asset.Usage?.Length ?? 0) > ReplayLimitsV12.MaximumTextLength) return "usage-invalid";

        switch (asset.MediaType)
        {
            case "image/png":
                if (!string.Equals(asset.Extension, ".png", StringComparison.OrdinalIgnoreCase)
                    || asset.Width <= 0 || asset.Width > MaximumImageDimension
                    || asset.Height <= 0 || asset.Height > MaximumImageDimension
                    || asset.SampleRate != 0 || asset.Channels != 0 || asset.SampleFrames != 0)
                    return "image-metadata-invalid";
                break;
            case "audio/wav":
                if (!string.Equals(asset.Extension, ".wav", StringComparison.OrdinalIgnoreCase)
                    || asset.Width != 0 || asset.Height != 0
                    || asset.SampleRate is < 8_000 or > 384_000
                    || asset.Channels is < 1 or > 2
                    || asset.SampleFrames <= 0)
                    return "audio-metadata-invalid";
                break;
            default:
                return "media-type-unsupported";
        }

        var payload = asset.Payload ?? Array.Empty<byte>();
        if (payload.Length == 0) return requirePayload ? "payload-missing" : "";
        if (payload.LongLength != asset.ByteLength
            || !string.Equals(ReplayCanonicalJsonV12.Sha256(payload), asset.Sha256, StringComparison.OrdinalIgnoreCase))
            return "payload-hash-invalid";
        if (asset.MediaType == "image/png") return ValidatePng(asset, payload);
        return ValidateWave(asset, payload);
    }

    private static string ValidatePng(ReplayAssetV12 asset, byte[] payload)
    {
        var signature = new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 };
        if (payload.Length < 24) return "png-truncated";
        for (var index = 0; index < signature.Length; index++)
            if (payload[index] != signature[index]) return "png-signature-invalid";
        if (payload[12] != (byte)'I' || payload[13] != (byte)'H'
            || payload[14] != (byte)'D' || payload[15] != (byte)'R') return "png-ihdr-missing";
        var width = ReadBigEndianInt32(payload, 16);
        var height = ReadBigEndianInt32(payload, 20);
        return width == asset.Width && height == asset.Height ? "" : "png-dimensions-mismatch";
    }

    private static string ValidateWave(ReplayAssetV12 asset, byte[] payload)
    {
        if (!ReplayPcm16WaveContractV12.TryRead(payload, out var wave, out _)) return "wave-invalid";
        return wave.SampleRate == asset.SampleRate
               && wave.Channels == asset.Channels
               && wave.SampleFrames == asset.SampleFrames
            ? ""
            : "wave-metadata-mismatch";
    }

    private static int ReadBigEndianInt32(byte[] payload, int offset)
    {
        return payload[offset] << 24
               | payload[offset + 1] << 16
               | payload[offset + 2] << 8
               | payload[offset + 3];
    }

    private static bool IsSha256(string value) => value != null
        && value.Length == 64
        && value.All(character => character is >= '0' and <= '9'
                                  || character is >= 'a' and <= 'f'
                                  || character is >= 'A' and <= 'F');
}

internal sealed class ReplayAssetPayloadSetV12
{
    public List<ReplayAssetPayloadV12> Items { get; set; } = new();
}

internal sealed class ReplayAssetPayloadV12
{
    public string Sha256 { get; set; } = "";
    public byte[] Payload { get; set; } = Array.Empty<byte>();
}

internal static class ReplayAssetPayloadTransferV12
{
    internal static ReplayAssetPayloadSetV12 Capture(ReplayDocumentV12 document)
    {
        return new ReplayAssetPayloadSetV12
        {
            Items = (document?.Assets ?? new List<ReplayAssetV12>())
                .OrderBy(item => item.Sha256, StringComparer.Ordinal)
                .Select(item => new ReplayAssetPayloadV12
                {
                    Sha256 = item.Sha256,
                    Payload = item.Payload == null ? Array.Empty<byte>() : (byte[])item.Payload.Clone()
                })
                .ToList()
        };
    }

    internal static void AttachAndValidate(ReplayDocumentV12 document, ReplayAssetPayloadSetV12 payloadSet)
    {
        if (document?.Assets == null || payloadSet?.Items == null)
            throw new InvalidOperationException("replay asset transfer shape is invalid");
        var manifests = document.Assets
            .Where(item => item != null && !string.IsNullOrWhiteSpace(item.Sha256))
            .GroupBy(item => item.Sha256, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
        var blobs = payloadSet.Items
            .Where(item => item != null && !string.IsNullOrWhiteSpace(item.Sha256))
            .GroupBy(item => item.Sha256, StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (manifests.Count != document.Assets.Count
            || blobs.Count != payloadSet.Items.Count
            || blobs.Any(group => group.Count() != 1)
            || !manifests.Keys.ToHashSet(StringComparer.OrdinalIgnoreCase)
                .SetEquals(blobs.Select(group => group.Key)))
            throw new InvalidOperationException("replay asset manifest and payload set differ");
        foreach (var group in blobs)
        {
            var bytes = group.Single().Payload ?? Array.Empty<byte>();
            var asset = manifests[group.Key];
            asset.Payload = (byte[])bytes.Clone();
            var error = ReplayAssetContractV12.Validate(asset, requirePayload: true);
            if (error.Length > 0)
                throw new InvalidOperationException("replay asset invalid: " + asset.Sha256 + ", " + error);
        }
    }
}
