using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using AuraToolsExp.Dll.Infrastructure;
using Newtonsoft.Json;

namespace AuraToolsExp.Dll.Features.PixelEmoji;

public static class PixelEmojiCodec
{
    public const int SourceSize = 24;
    public const int NativeSize = 192;
    public const int Scale = NativeSize / SourceSize;
    public const int PixelCount = SourceSize * SourceSize;
    public const int PaletteVersion = 1;

    // RGBA, palette index 0 is transparent.
    public static readonly uint[] PaletteRgba =
    {
        0x00000000, 0x171820FF, 0xFFFFFFFF, 0xD9D6CFFF,
        0x9B9A94FF, 0x606168FF, 0xB82E35FF, 0xE85A44FF,
        0xF38B3BFF, 0xF2B544FF, 0xF6DD6AFF, 0x9B5A37FF,
        0x6A3C2AFF, 0xD6A06FFF, 0xECE0C5FF, 0x66762CFF,
        0x8FAA45FF, 0xBBD273FF, 0x2F6A50FF, 0x47A878FF,
        0x78CFB1FF, 0x28506FFF, 0x3579A8FF, 0x62A9D1FF,
        0x9DD7E8FF, 0x313C87FF, 0x4D57B7FF, 0x777DDAFF,
        0x59347CFF, 0x8652A1FF, 0xB879B4FF, 0xE7A9C5FF
    };

    public static byte[] Blank()
    {
        return new byte[PixelCount];
    }

    public static string Encode(byte[] pixels)
    {
        return Convert.ToBase64String(RequireValid(pixels));
    }

    public static bool TryDecode(string? encoded, out byte[] pixels)
    {
        pixels = Array.Empty<byte>();
        if (encoded == null || encoded.Length != ((PixelCount + 2) / 3) * 4)
        {
            return false;
        }
        try
        {
            var decoded = Convert.FromBase64String(encoded);
            if (!IsValid(decoded))
            {
                return false;
            }

            pixels = decoded;
            return true;
        }
        catch
        {
            return false;
        }
    }

    public static bool IsValid(byte[]? pixels)
    {
        return pixels != null
               && pixels.Length == PixelCount
               && pixels.All(index => index < PaletteRgba.Length);
    }

    public static string Sha256(byte[] pixels)
    {
        using var sha = SHA256.Create();
        return string.Concat(sha.ComputeHash(RequireValid(pixels)).Select(value => value.ToString("x2")));
    }

    public static byte[] ExpandToNativeRgba(byte[] pixels)
    {
        var source = RequireValid(pixels);
        var rgba = new byte[NativeSize * NativeSize * 4];
        for (var sourceY = 0; sourceY < SourceSize; sourceY++)
        {
            for (var sourceX = 0; sourceX < SourceSize; sourceX++)
            {
                var packed = PaletteRgba[source[sourceY * SourceSize + sourceX]];
                var r = (byte)(packed >> 24);
                var g = (byte)(packed >> 16);
                var b = (byte)(packed >> 8);
                var a = (byte)packed;
                for (var dy = 0; dy < Scale; dy++)
                {
                    var targetY = sourceY * Scale + dy;
                    for (var dx = 0; dx < Scale; dx++)
                    {
                        var offset = (targetY * NativeSize + sourceX * Scale + dx) * 4;
                        rgba[offset] = r;
                        rgba[offset + 1] = g;
                        rgba[offset + 2] = b;
                        rgba[offset + 3] = a;
                    }
                }
            }
        }

        return rgba;
    }

    public static void DrawLine(byte[] pixels, int x0, int y0, int x1, int y1, byte color)
    {
        RequireValid(pixels);
        if (color >= PaletteRgba.Length)
        {
            throw new ArgumentOutOfRangeException(nameof(color));
        }

        var dx = Math.Abs(x1 - x0);
        var sx = x0 < x1 ? 1 : -1;
        var dy = -Math.Abs(y1 - y0);
        var sy = y0 < y1 ? 1 : -1;
        var error = dx + dy;
        while (true)
        {
            SetIfInside(pixels, x0, y0, color);
            if (x0 == x1 && y0 == y1)
            {
                break;
            }

            var doubled = error * 2;
            if (doubled >= dy)
            {
                error += dy;
                x0 += sx;
            }
            if (doubled <= dx)
            {
                error += dx;
                y0 += sy;
            }
        }
    }

    public static bool FloodFill(byte[] pixels, int x, int y, byte replacement)
    {
        RequireValid(pixels);
        if (!Inside(x, y) || replacement >= PaletteRgba.Length)
        {
            return false;
        }

        var target = pixels[y * SourceSize + x];
        if (target == replacement)
        {
            return false;
        }

        var pending = new Queue<int>();
        pending.Enqueue(y * SourceSize + x);
        pixels[y * SourceSize + x] = replacement;
        while (pending.Count > 0)
        {
            var index = pending.Dequeue();
            var currentX = index % SourceSize;
            var currentY = index / SourceSize;
            TryFill(pixels, currentX - 1, currentY, target, replacement, pending);
            TryFill(pixels, currentX + 1, currentY, target, replacement, pending);
            TryFill(pixels, currentX, currentY - 1, target, replacement, pending);
            TryFill(pixels, currentX, currentY + 1, target, replacement, pending);
        }

        return true;
    }

    private static void TryFill(byte[] pixels, int x, int y, byte target, byte replacement, Queue<int> pending)
    {
        if (!Inside(x, y))
        {
            return;
        }

        var index = y * SourceSize + x;
        if (pixels[index] != target)
        {
            return;
        }

        pixels[index] = replacement;
        pending.Enqueue(index);
    }

    private static void SetIfInside(byte[] pixels, int x, int y, byte color)
    {
        if (Inside(x, y))
        {
            pixels[y * SourceSize + x] = color;
        }
    }

    private static bool Inside(int x, int y)
    {
        return x >= 0 && x < SourceSize && y >= 0 && y < SourceSize;
    }

    private static byte[] RequireValid(byte[]? pixels)
    {
        if (!IsValid(pixels))
        {
            throw new ArgumentException("Pixel emoji must contain exactly 576 valid palette indices.", nameof(pixels));
        }

        return pixels!;
    }
}

public enum PixelEmojiPlaybackMode
{
    Once = 0,
    Loop = 1
}

public static class PixelEmojiAnimationCodec
{
    public const int MinimumFrames = 1;
    public const int MaximumFrames = 8;
    public const int FrameDurationMilliseconds = 200;
    public const float FrameDurationSeconds = FrameDurationMilliseconds / 1000f;

    public static bool IsValidPlaybackMode(PixelEmojiPlaybackMode mode)
    {
        return mode == PixelEmojiPlaybackMode.Once || mode == PixelEmojiPlaybackMode.Loop;
    }

    public static bool IsValidFrames(IReadOnlyList<byte[]>? frames)
    {
        return frames != null
               && frames.Count >= MinimumFrames
               && frames.Count <= MaximumFrames
               && frames.All(PixelEmojiCodec.IsValid);
    }

    public static List<byte[]> CloneFrames(IReadOnlyList<byte[]> frames)
    {
        if (!IsValidFrames(frames))
        {
            throw new ArgumentException("Pixel emoji animation must contain between one and eight valid frames.", nameof(frames));
        }

        return frames.Select(frame => (byte[])frame.Clone()).ToList();
    }

    public static List<string> EncodeFrames(IReadOnlyList<byte[]> frames)
    {
        return CloneFrames(frames).Select(PixelEmojiCodec.Encode).ToList();
    }

    public static bool TryDecodeFrames(IReadOnlyList<string>? encodedFrames, out List<byte[]> frames)
    {
        frames = new List<byte[]>();
        if (encodedFrames == null
            || encodedFrames.Count < MinimumFrames
            || encodedFrames.Count > MaximumFrames)
        {
            return false;
        }

        foreach (var encoded in encodedFrames)
        {
            if (!PixelEmojiCodec.TryDecode(encoded, out var frame))
            {
                frames.Clear();
                return false;
            }
            frames.Add(frame);
        }

        return true;
    }

    public static bool CanSwapAdjacent(
        IReadOnlyList<byte[]>? frames,
        int selectedFrameIndex,
        int direction)
    {
        if (!IsValidFrames(frames) || selectedFrameIndex < 0 || selectedFrameIndex >= frames!.Count)
        {
            return false;
        }

        var offset = Math.Sign(direction);
        var targetFrameIndex = selectedFrameIndex + offset;
        return offset != 0 && targetFrameIndex >= 0 && targetFrameIndex < frames.Count;
    }

    public static bool TrySwapAdjacent(
        List<byte[]>? frames,
        int selectedFrameIndex,
        int direction,
        out int movedFrameIndex)
    {
        movedFrameIndex = selectedFrameIndex;
        if (!CanSwapAdjacent(frames, selectedFrameIndex, direction))
        {
            return false;
        }

        var targetFrameIndex = selectedFrameIndex + Math.Sign(direction);
        var selectedFrame = frames![selectedFrameIndex];
        frames[selectedFrameIndex] = frames[targetFrameIndex];
        frames[targetFrameIndex] = selectedFrame;
        movedFrameIndex = targetFrameIndex;
        return true;
    }

    public static string Sha256(IReadOnlyList<byte[]> frames, PixelEmojiPlaybackMode playbackMode)
    {
        if (!IsValidFrames(frames))
        {
            throw new ArgumentException("Pixel emoji animation must contain between one and eight valid frames.", nameof(frames));
        }
        if (!IsValidPlaybackMode(playbackMode))
        {
            throw new ArgumentOutOfRangeException(nameof(playbackMode));
        }

        var payload = new byte[4 + frames.Count * PixelEmojiCodec.PixelCount];
        payload[0] = 2;
        payload[1] = (byte)frames.Count;
        payload[2] = (byte)playbackMode;
        payload[3] = FrameDurationMilliseconds / 10;
        for (var index = 0; index < frames.Count; index++)
        {
            Buffer.BlockCopy(frames[index], 0, payload, 4 + index * PixelEmojiCodec.PixelCount, PixelEmojiCodec.PixelCount);
        }

        using var sha = SHA256.Create();
        return string.Concat(sha.ComputeHash(payload).Select(value => value.ToString("x2")));
    }
}

public static class PixelEmojiExportPolicy
{
    private static readonly HashSet<char> InvalidFileNameCharacters = new(
        Path.GetInvalidFileNameChars().Concat(new[] { '<', '>', ':', '"', '/', '\\', '|', '?', '*' }));

    private static readonly HashSet<string> ReservedFileNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON", "PRN", "AUX", "NUL",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9"
    };

    public static string SafeFileName(string? name)
    {
        var safe = new string((name ?? "")
                .Select(value => InvalidFileNameCharacters.Contains(value) || char.IsControl(value) ? '_' : value)
                .ToArray())
            .Trim()
            .TrimEnd('.');
        if (safe.Length == 0)
        {
            safe = "未命名表情";
        }
        if (safe.Length > 64)
        {
            safe = safe.Substring(0, 64).TrimEnd(' ', '.');
        }

        return ReservedFileNames.Contains(safe) ? "_" + safe : safe;
    }

    public static string FrameFileName(string? name, int frameNumber)
    {
        return SafeFileName(name) + "_" + Math.Max(1, frameNumber) + ".png";
    }
}

public static class PixelEmojiReferencePolicy
{
    public const int MinimumScalePercent = 1;
    public const int MaximumScalePercent = 100;
    public const int DefaultScalePercent = 100;
    public const int MinimumOpacityPercent = 10;
    public const int MaximumOpacityPercent = 80;
    public const int DefaultOpacityPercent = 45;
    public const long MaximumSourceBytes = 32L * 1024L * 1024L;
    public const int MaximumDimension = 8192;

    public static int ClampScalePercent(int value)
    {
        return Math.Max(MinimumScalePercent, Math.Min(MaximumScalePercent, value));
    }

    public static int ClampOpacityPercent(int value)
    {
        return Math.Max(MinimumOpacityPercent, Math.Min(MaximumOpacityPercent, value));
    }

    public static void MapToLogicalCanvas(
        int imageWidth,
        int imageHeight,
        float viewportWidth,
        float viewportHeight,
        int logicalCellCount,
        out float width,
        out float height)
    {
        if (imageWidth <= 0
            || imageHeight <= 0
            || viewportWidth <= 0f
            || viewportHeight <= 0f
            || logicalCellCount <= 0)
        {
            width = 0f;
            height = 0f;
            return;
        }

        var logicalPixelSize = Math.Min(viewportWidth, viewportHeight) / logicalCellCount;
        width = imageWidth * logicalPixelSize;
        height = imageHeight * logicalPixelSize;
    }

    public static bool ShouldUsePointFiltering(int imageWidth, int imageHeight)
    {
        return imageWidth > 0
               && imageHeight > 0
               && imageWidth <= PixelEmojiCodec.NativeSize
               && imageHeight <= PixelEmojiCodec.NativeSize;
    }

    public static bool IsSupportedSource(long fileLength, int imageWidth, int imageHeight)
    {
        return fileLength > 0
               && fileLength <= MaximumSourceBytes
               && imageWidth > 0
               && imageHeight > 0
               && imageWidth <= MaximumDimension
               && imageHeight <= MaximumDimension;
    }
}

public sealed class PixelEmojiDocument
{
    [JsonProperty("id")]
    public string Id { get; set; } = "";

    [JsonProperty("name")]
    public string Name { get; set; } = "";

    [JsonProperty("paletteVersion")]
    public int PaletteVersion { get; set; } = PixelEmojiCodec.PaletteVersion;

    [JsonProperty("pixelsBase64")]
    public string PixelsBase64 { get; set; } = "";

    [JsonProperty("framesBase64")]
    public List<string> FramesBase64 { get; set; } = new();

    [JsonProperty("playbackMode")]
    public PixelEmojiPlaybackMode PlaybackMode { get; set; } = PixelEmojiPlaybackMode.Loop;

    [JsonProperty("createdUtcTicks")]
    public long CreatedUtcTicks { get; set; }

    [JsonProperty("modifiedUtcTicks")]
    public long ModifiedUtcTicks { get; set; }

    public bool TryNormalize()
    {
        Id = (Id ?? "").Trim();
        if (Id.Length == 0)
        {
            Id = Guid.NewGuid().ToString("N");
        }

        Name = (Name ?? "").Trim();
        if (Name.Length == 0)
        {
            Name = "未命名表情";
        }
        if (Name.Length > 32)
        {
            Name = Name.Substring(0, 32);
        }

        if (!PixelEmojiAnimationCodec.TryDecodeFrames(FramesBase64, out var frames))
        {
            if (FramesBase64 != null && FramesBase64.Count > 0)
            {
                return false;
            }
            if (!PixelEmojiCodec.TryDecode(PixelsBase64, out var legacyFrame))
            {
                return false;
            }
            frames = new List<byte[]> { legacyFrame };
        }

        if (!PixelEmojiAnimationCodec.IsValidPlaybackMode(PlaybackMode))
        {
            PlaybackMode = PixelEmojiPlaybackMode.Loop;
        }
        FramesBase64 = PixelEmojiAnimationCodec.EncodeFrames(frames);
        PixelsBase64 = FramesBase64[0];

        var now = DateTime.UtcNow.Ticks;
        CreatedUtcTicks = CreatedUtcTicks <= 0 ? now : CreatedUtcTicks;
        ModifiedUtcTicks = ModifiedUtcTicks <= 0 ? CreatedUtcTicks : ModifiedUtcTicks;
        return PaletteVersion == PixelEmojiCodec.PaletteVersion;
    }

    public bool TryReadFrames(out List<byte[]> frames)
    {
        frames = new List<byte[]>();
        return PaletteVersion == PixelEmojiCodec.PaletteVersion
               && PixelEmojiAnimationCodec.TryDecodeFrames(FramesBase64, out frames);
    }
}

public sealed class PixelEmojiLibrary
{
    public const int CurrentSchemaVersion = 2;
    public const int MaximumItems = 256;

    [JsonProperty("schemaVersion")]
    public int SchemaVersion { get; set; } = CurrentSchemaVersion;

    [JsonProperty("items")]
    public List<PixelEmojiDocument> Items { get; set; } = new();

    public void Normalize()
    {
        SchemaVersion = Math.Max(CurrentSchemaVersion, SchemaVersion);
        Items = (Items ?? new List<PixelEmojiDocument>())
            .Where(item => item != null && item.TryNormalize())
            .GroupBy(item => item.Id, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.OrderByDescending(item => item.ModifiedUtcTicks).First())
            .OrderByDescending(item => item.ModifiedUtcTicks)
            .Take(MaximumItems)
            .ToList();
    }
}

[Serializable]
public sealed class PixelEmojiPresentation
{
    public const int CurrentProtocolVersion = 2;

    public int ProtocolVersion { get; set; } = CurrentProtocolVersion;
    public string EventId { get; set; } = "";
    // Requests carry the client's local time for diagnostics only. The server
    // overwrites this value with its authoritative acceptance time before relay.
    public long CreatedUtcTicks { get; set; }
    public string IssuerPlayerId { get; set; } = "";
    public string IssuerPlayerName { get; set; } = "";
    public int PaletteVersion { get; set; } = PixelEmojiCodec.PaletteVersion;
    public int FrameDurationMilliseconds { get; set; } = PixelEmojiAnimationCodec.FrameDurationMilliseconds;
    public PixelEmojiPlaybackMode PlaybackMode { get; set; } = PixelEmojiPlaybackMode.Loop;
    public List<string> FramesBase64 { get; set; } = new();
    public string ContentHash { get; set; } = "";
    public string RejectionReason { get; set; } = "";

    public bool TryReadFrames(out List<byte[]> frames, out string rejection)
    {
        frames = new List<byte[]>();
        rejection = "";
        if (ProtocolVersion != CurrentProtocolVersion || PaletteVersion != PixelEmojiCodec.PaletteVersion)
        {
            rejection = "协议或色板版本不匹配";
            return false;
        }
        if (string.IsNullOrWhiteSpace(EventId) || EventId.Length > 80)
        {
            rejection = "事件编号无效";
            return false;
        }
        if (FrameDurationMilliseconds != PixelEmojiAnimationCodec.FrameDurationMilliseconds)
        {
            rejection = "动画帧间隔无效";
            return false;
        }
        if (!PixelEmojiAnimationCodec.IsValidPlaybackMode(PlaybackMode))
        {
            rejection = "动画播放模式无效";
            return false;
        }
        if (!PixelEmojiAnimationCodec.TryDecodeFrames(FramesBase64, out frames))
        {
            rejection = "动画帧数据无效";
            return false;
        }
        var actualHash = PixelEmojiAnimationCodec.Sha256(frames, PlaybackMode);
        if (!string.Equals(actualHash, (ContentHash ?? "").Trim(), StringComparison.OrdinalIgnoreCase))
        {
            rejection = "内容校验失败";
            return false;
        }

        return true;
    }
}

internal sealed class PixelEmojiServerAcceptancePolicy
{
    internal const long SendCooldownMilliseconds = 1000L;
    internal const long EventRetentionMilliseconds = 2L * 60L * 1000L;
    internal const int MaximumTrackedEvents = 512;
    internal const int MaximumTrackedPlayers = 128;

    private readonly object sync = new();
    private readonly Dictionary<string, long> seenRequests = new(StringComparer.Ordinal);
    private readonly Dictionary<string, long> lastAcceptedByPlayer = new(StringComparer.Ordinal);

    internal bool TryAccept(
        AuraToolsRpcSender? sender,
        PixelEmojiPresentation? presentation,
        long serverUtcTicks,
        long monotonicMilliseconds,
        out string rejection)
    {
        rejection = "";
        if (sender == null || !sender.IsAvailable || !sender.IsLobbyMember)
        {
            rejection = "发送者不是当前房间成员";
            return false;
        }
        if (presentation == null)
        {
            rejection = "表情事件为空";
            return false;
        }
        if (serverUtcTicks < DateTime.MinValue.Ticks || serverUtcTicks > DateTime.MaxValue.Ticks)
        {
            rejection = "服务端时间无效";
            return false;
        }
        if (!presentation.TryReadFrames(out _, out rejection))
        {
            return false;
        }

        var now = Math.Max(0L, monotonicMilliseconds);
        var playerId = sender.PlayerId;
        var requestKey = playerId.Length.ToString() + ":" + playerId + ":" + presentation.EventId;
        lock (sync)
        {
            Prune(seenRequests, now, EventRetentionMilliseconds);
            Prune(lastAcceptedByPlayer, now, EventRetentionMilliseconds);
            if (seenRequests.ContainsKey(requestKey))
            {
                rejection = "表情事件重复";
                return false;
            }

            // Consume every authenticated, structurally valid request id even
            // when rate limiting rejects it, so a delayed retry cannot bypass
            // the original rate decision.
            seenRequests[requestKey] = now;
            TrimOldest(seenRequests, MaximumTrackedEvents);

            if (lastAcceptedByPlayer.TryGetValue(playerId, out var lastAccepted)
                && ElapsedMilliseconds(lastAccepted, now) < SendCooldownMilliseconds)
            {
                rejection = "表情发送过于频繁";
                return false;
            }

            lastAcceptedByPlayer[playerId] = now;
            TrimOldest(lastAcceptedByPlayer, MaximumTrackedPlayers);
        }

        presentation.CreatedUtcTicks = serverUtcTicks;
        presentation.IssuerPlayerId = playerId;
        presentation.IssuerPlayerName = sender.PlayerName;
        presentation.RejectionReason = "";
        return true;
    }

    private static long ElapsedMilliseconds(long started, long current)
    {
        return current <= started ? 0L : current - started;
    }

    private static void Prune(Dictionary<string, long> values, long now, long ttlMilliseconds)
    {
        foreach (var key in values
                     .Where(pair => ElapsedMilliseconds(pair.Value, now) >= ttlMilliseconds)
                     .Select(pair => pair.Key)
                     .ToList())
        {
            values.Remove(key);
        }
    }

    private static void TrimOldest(Dictionary<string, long> values, int maximumCount)
    {
        var overflow = values.Count - Math.Max(1, maximumCount);
        if (overflow <= 0)
        {
            return;
        }

        foreach (var key in values
                     .OrderBy(pair => pair.Value)
                     .ThenBy(pair => pair.Key, StringComparer.Ordinal)
                     .Take(overflow)
                     .Select(pair => pair.Key)
                     .ToList())
        {
            values.Remove(key);
        }
    }
}
