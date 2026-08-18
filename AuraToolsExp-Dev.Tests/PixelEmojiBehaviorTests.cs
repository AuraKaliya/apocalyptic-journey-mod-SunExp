using AuraToolsExp.Dll.Config;
using AuraToolsExp.Dll.Features.PixelEmoji;
using AuraToolsExp.Dll.Infrastructure;

internal static partial class AuraToolsTestSuite
{
    public static void TestPixelEmojiCore()
    {
        TestPixelEmojiWorkshopLayoutPolicy();
        var settings = new AuraToolsPixelEmojiSettings
        {
            SchemaVersion = 0,
            MaxFavorites = 999,
            FavoriteIds = new List<string> { " a ", "A", "", "b" }
        };
        settings.Normalize();
        Assert(settings.SchemaVersion == 1
               && settings.MaxFavorites == 64
               && settings.FavoriteIds.SequenceEqual(new[] { "a", "b" })
               && settings.IsFavorite("A"),
            "pixel emoji settings clamp limits and normalize favorite identities");

        var pixels = PixelEmojiCodec.Blank();
        PixelEmojiCodec.DrawLine(pixels, 0, 0, 23, 23, 2);
        Assert(Enumerable.Range(0, 24).All(index => pixels[index * 24 + index] == 2),
            "pixel emoji pencil fills a continuous Bresenham diagonal");

        pixels = PixelEmojiCodec.Blank();
        PixelEmojiCodec.DrawLine(pixels, 12, 0, 12, 23, 1);
        Assert(PixelEmojiCodec.FloodFill(pixels, 0, 0, 3)
               && pixels[0] == 3
               && pixels[11] == 3
               && pixels[12] == 1
               && pixels[23] == 0,
            "pixel emoji flood fill stays inside four-connected boundaries");

        var encoded = PixelEmojiCodec.Encode(pixels);
        Assert(PixelEmojiCodec.TryDecode(encoded, out var decoded)
               && decoded.SequenceEqual(pixels)
               && !PixelEmojiCodec.TryDecode(Convert.ToBase64String(new byte[575]), out _),
            "pixel emoji source codec preserves exactly 576 palette indices");

        pixels = PixelEmojiCodec.Blank();
        pixels[0] = 6;
        pixels[1] = 24;
        var rgba = PixelEmojiCodec.ExpandToNativeRgba(pixels);
        var red = PixelEmojiCodec.PaletteRgba[6];
        var blue = PixelEmojiCodec.PaletteRgba[24];
        Assert(rgba.Length == 192 * 192 * 4
               && ReadRgba(rgba, 0, 0) == red
               && ReadRgba(rgba, 7, 7) == red
               && ReadRgba(rgba, 8, 0) == blue
               && ReadRgba(rgba, 15, 7) == blue,
            "pixel emoji native export performs strict 8x nearest-neighbor replication");

        var secondFrame = (byte[])pixels.Clone();
        secondFrame[2] = 10;
        var animationFrames = new List<byte[]> { pixels, secondFrame };
        var encodedFrames = PixelEmojiAnimationCodec.EncodeFrames(animationFrames);
        Assert(PixelEmojiAnimationCodec.TryDecodeFrames(encodedFrames, out var decodedFrames)
               && decodedFrames.Count == 2
               && decodedFrames[0].SequenceEqual(pixels)
               && decodedFrames[1].SequenceEqual(secondFrame)
               && PixelEmojiAnimationCodec.FrameDurationMilliseconds == 200,
            "pixel emoji animation codec preserves ordered source frames at the fixed 0.2 second interval");
        decodedFrames[0][0] = 0;
        Assert(pixels[0] == 6
               && !PixelEmojiAnimationCodec.IsValidFrames(Array.Empty<byte[]>())
               && !PixelEmojiAnimationCodec.IsValidFrames(
                   Enumerable.Range(0, PixelEmojiAnimationCodec.MaximumFrames + 1)
                       .Select(_ => PixelEmojiCodec.Blank())
                       .ToList()),
            "pixel emoji animation frames are independently cloned and constrained to one through eight frames");
        Assert(PixelEmojiAnimationCodec.Sha256(animationFrames, PixelEmojiPlaybackMode.Loop)
               != PixelEmojiAnimationCodec.Sha256(animationFrames, PixelEmojiPlaybackMode.Once)
               && PixelEmojiAnimationCodec.Sha256(animationFrames, PixelEmojiPlaybackMode.Loop)
               != PixelEmojiAnimationCodec.Sha256(animationFrames.AsEnumerable().Reverse().ToList(), PixelEmojiPlaybackMode.Loop),
            "pixel emoji animation hash binds playback mode and frame order");

        var frameA = PixelEmojiCodec.Blank();
        var frameB = PixelEmojiCodec.Blank();
        var frameC = PixelEmojiCodec.Blank();
        frameA[0] = 1;
        frameB[0] = 2;
        frameC[0] = 3;
        var reorderedFrames = new List<byte[]> { frameA, frameB, frameC };
        Assert(PixelEmojiAnimationCodec.CanSwapAdjacent(reorderedFrames, 1, -1)
               && PixelEmojiAnimationCodec.TrySwapAdjacent(reorderedFrames, 1, -1, out var movedLeftIndex)
               && movedLeftIndex == 0
               && reorderedFrames.Select(frame => frame[0]).SequenceEqual(new byte[] { 2, 1, 3 }),
            "pixel emoji previous-frame action swaps the selected frame with its left neighbor and follows it");
        Assert(PixelEmojiAnimationCodec.TrySwapAdjacent(reorderedFrames, 0, 1, out var movedRightIndex)
               && movedRightIndex == 1
               && reorderedFrames.Select(frame => frame[0]).SequenceEqual(new byte[] { 1, 2, 3 })
               && !PixelEmojiAnimationCodec.TrySwapAdjacent(reorderedFrames, 0, -1, out _)
               && !PixelEmojiAnimationCodec.TrySwapAdjacent(reorderedFrames, 2, 1, out _)
               && reorderedFrames.Select(frame => frame[0]).SequenceEqual(new byte[] { 1, 2, 3 }),
            "pixel emoji next-frame action swaps right while boundary actions leave frame order unchanged");

        var presentation = new PixelEmojiPresentation
        {
            EventId = "event-1",
            CreatedUtcTicks = DateTime.UtcNow.Ticks,
            PlaybackMode = PixelEmojiPlaybackMode.Once,
            FramesBase64 = encodedFrames,
            ContentHash = PixelEmojiAnimationCodec.Sha256(animationFrames, PixelEmojiPlaybackMode.Once)
        };
        Assert(presentation.TryReadFrames(out var presented, out _)
               && presented.Count == 2
               && presented[0].SequenceEqual(pixels)
               && presented[1].SequenceEqual(secondFrame),
            "pixel emoji presentation accepts a matching animation hash and single-play mode");
        presentation.ContentHash = new string('0', 64);
        Assert(!presentation.TryReadFrames(out _, out var rejection)
               && rejection.Contains("校验"),
            "pixel emoji presentation rejects tampered content");
        presentation.ContentHash = PixelEmojiAnimationCodec.Sha256(animationFrames, PixelEmojiPlaybackMode.Once);
        presentation.FrameDurationMilliseconds = 100;
        Assert(presentation.TryReadFrames(out _, out rejection)
               && presentation.FrameDurationMilliseconds
               == PixelEmojiAnimationCodec.FrameDurationMilliseconds,
            "pixel emoji presentation accepts bounded legacy timing metadata and normalizes playback cadence");
        presentation.FrameDurationMilliseconds =
            PixelEmojiAnimationCodec.MinimumFrameDurationMilliseconds - 1;
        Assert(!presentation.TryReadFrames(out _, out rejection)
               && rejection.Contains("间隔"),
            "pixel emoji presentation still rejects unsafe frame timing");
        presentation.FrameDurationMilliseconds =
            PixelEmojiAnimationCodec.FrameDurationMilliseconds;
        presentation.ProtocolVersion = PixelEmojiPresentation.CurrentProtocolVersion + 1;
        presentation.MinimumProtocolVersion = PixelEmojiPresentation.CurrentProtocolVersion;
        presentation.RequiredCapabilities = new List<string>
        {
            PixelEmojiPresentation.IndexedFramesCapability,
            PixelEmojiPresentation.PaletteIndicesCapability,
            PixelEmojiPresentation.ContentHashCapability
        };
        Assert(presentation.TryReadFrames(out _, out rejection),
            "pixel emoji presentation accepts a future additive protocol that declares a compatible baseline");
        presentation.RequiredCapabilities.Add("future-required-renderer.v1");
        Assert(!presentation.TryReadFrames(out _, out rejection)
               && rejection.Contains("能力"),
            "pixel emoji presentation rejects unknown required capabilities without disabling unrelated tools");

        var maximumFrames = Enumerable.Range(0, PixelEmojiAnimationCodec.MaximumFrames)
            .Select(index =>
            {
                var frame = PixelEmojiCodec.Blank();
                frame[index] = (byte)(index + 1);
                return frame;
            })
            .ToList();
        var maximumPresentation = new PixelEmojiPresentation
        {
            EventId = "event-max",
            CreatedUtcTicks = DateTime.UtcNow.Ticks,
            FramesBase64 = PixelEmojiAnimationCodec.EncodeFrames(maximumFrames),
            ContentHash = PixelEmojiAnimationCodec.Sha256(maximumFrames, PixelEmojiPlaybackMode.Loop)
        };
        Assert(maximumPresentation.TryReadFrames(out var presentedMaximum, out _)
               && presentedMaximum.Count == PixelEmojiAnimationCodec.MaximumFrames
               && AuraToolsRpcPayloadGuard.FitsSoftLimit(
                   maximumPresentation,
                   AuraToolsRpcPayloadGuard.DefaultSoftLimitBytes,
                   out var presentationBytes,
                   out _)
               && presentationBytes > 0,
            "maximum eight-frame pixel emoji presentation remains below the RPC soft payload limit");
        maximumPresentation.PlaybackMode = (PixelEmojiPlaybackMode)99;
        Assert(!maximumPresentation.TryReadFrames(out _, out rejection)
               && rejection.Contains("模式"),
            "pixel emoji presentation rejects unknown playback modes");
        maximumPresentation.PlaybackMode = PixelEmojiPlaybackMode.Loop;
        maximumPresentation.FramesBase64.Add(PixelEmojiCodec.Encode(PixelEmojiCodec.Blank()));
        Assert(!maximumPresentation.TryReadFrames(out _, out rejection)
               && rejection.Contains("帧数据"),
            "pixel emoji presentation rejects frame counts above the eight-frame limit");

        var serverUtcTicks = new DateTime(2026, 8, 17, 0, 0, 0, DateTimeKind.Utc).Ticks;
        var sender = new AuraToolsRpcSender("client-a", "Client A", true, false, "test", true);
        var acceptance = new PixelEmojiServerAcceptancePolicy();
        var futureClockPresentation = CreatePresentation(
            "clock-future",
            serverUtcTicks + TimeSpan.FromHours(12).Ticks,
            animationFrames,
            PixelEmojiPlaybackMode.Once);
        futureClockPresentation.IssuerPlayerId = "spoofed-player";
        futureClockPresentation.IssuerPlayerName = "Spoofed Name";
        Assert(acceptance.TryAccept(sender, futureClockPresentation, serverUtcTicks, 1000L, out rejection)
               && rejection.Length == 0
               && futureClockPresentation.CreatedUtcTicks == serverUtcTicks
               && futureClockPresentation.IssuerPlayerId == sender.PlayerId
               && futureClockPresentation.IssuerPlayerName == sender.PlayerName,
            "pixel emoji server accepts client clock skew and stamps authoritative sender and receive time");

        var pastClockPresentation = CreatePresentation(
            "clock-past",
            serverUtcTicks - TimeSpan.FromHours(12).Ticks,
            animationFrames,
            PixelEmojiPlaybackMode.Once);
        Assert(acceptance.TryAccept(
                   sender,
                   pastClockPresentation,
                   serverUtcTicks + TimeSpan.TicksPerSecond,
                   2000L,
                   out rejection)
               && pastClockPresentation.CreatedUtcTicks == serverUtcTicks + TimeSpan.TicksPerSecond,
            "pixel emoji server does not authorize requests using cross-machine wall-clock age");

        var duplicatePresentation = CreatePresentation(
            "clock-past",
            long.MaxValue,
            animationFrames,
            PixelEmojiPlaybackMode.Once);
        Assert(!acceptance.TryAccept(
                   sender,
                   duplicatePresentation,
                   serverUtcTicks + TimeSpan.FromSeconds(2).Ticks,
                   3000L,
                   out rejection)
               && rejection.Contains("重复"),
            "pixel emoji server suppresses duplicate event ids independently of client timestamps");

        var secondSender = new AuraToolsRpcSender("client-b", "Client B", true, false, "test", true);
        var secondSenderPresentation = CreatePresentation(
            "clock-past",
            serverUtcTicks,
            animationFrames,
            PixelEmojiPlaybackMode.Once);
        Assert(acceptance.TryAccept(
                   secondSender,
                   secondSenderPresentation,
                   serverUtcTicks + TimeSpan.FromSeconds(2).Ticks,
                   3000L,
                   out rejection),
            "pixel emoji duplicate identity is scoped by the server-bound sender");

        var ratePolicy = new PixelEmojiServerAcceptancePolicy();
        var rateFirst = CreatePresentation("rate-1", serverUtcTicks, animationFrames, PixelEmojiPlaybackMode.Once);
        var rateRejected = CreatePresentation("rate-2", serverUtcTicks, animationFrames, PixelEmojiPlaybackMode.Once);
        var rateNext = CreatePresentation("rate-3", serverUtcTicks, animationFrames, PixelEmojiPlaybackMode.Once);
        Assert(ratePolicy.TryAccept(sender, rateFirst, serverUtcTicks, 10000L, out _)
               && !ratePolicy.TryAccept(sender, rateRejected, serverUtcTicks, 10999L, out rejection)
               && rejection.Contains("频繁")
               && !ratePolicy.TryAccept(sender, rateRejected, serverUtcTicks, 12000L, out rejection)
               && rejection.Contains("重复")
               && ratePolicy.TryAccept(sender, rateNext, serverUtcTicks, 12000L, out _),
            "pixel emoji monotonic rate limiting consumes rejected request ids and allows the next unique event after cooldown");

        var outsider = new AuraToolsRpcSender("outsider", "Outsider", false, false, "test", true);
        var outsiderPresentation = CreatePresentation(
            "outsider-event",
            serverUtcTicks,
            animationFrames,
            PixelEmojiPlaybackMode.Once);
        Assert(!new PixelEmojiServerAcceptancePolicy().TryAccept(
                   outsider,
                   outsiderPresentation,
                   serverUtcTicks,
                   1000L,
                   out rejection)
               && rejection.Contains("房间成员"),
            "pixel emoji server rejects presentations from senders outside the current lobby");

        var library = new PixelEmojiLibrary
        {
            SchemaVersion = 1,
            Items = new List<PixelEmojiDocument>
            {
                new() { Id = "ok", Name = " Good ", PixelsBase64 = encoded },
                new()
                {
                    Id = "animated",
                    Name = "Animated",
                    FramesBase64 = encodedFrames,
                    PlaybackMode = PixelEmojiPlaybackMode.Once
                },
                new() { Id = "bad", PixelsBase64 = "invalid" }
            }
        };
        library.Normalize();
        var legacyDocument = library.Items.Single(item => item.Id == "ok");
        var animatedDocument = library.Items.Single(item => item.Id == "animated");
        Assert(library.SchemaVersion == PixelEmojiLibrary.CurrentSchemaVersion
               && library.Items.Count == 2
               && legacyDocument.Name == "Good"
               && legacyDocument.PlaybackMode == PixelEmojiPlaybackMode.Loop
               && legacyDocument.TryReadFrames(out var migratedFrames)
               && migratedFrames.Count == 1
               && legacyDocument.PixelsBase64 == legacyDocument.FramesBase64[0]
               && animatedDocument.TryReadFrames(out var normalizedAnimation)
               && normalizedAnimation.Count == 2
               && animatedDocument.PlaybackMode == PixelEmojiPlaybackMode.Once,
            "pixel emoji library migrates legacy art, preserves animations, and removes invalid documents");
        Assert(PixelEmojiExportPolicy.FrameFileName(" A:B? ", 2) == "A_B__2.png"
               && PixelEmojiExportPolicy.FrameFileName("CON", 1) == "_CON_1.png"
               && PixelEmojiExportPolicy.FrameFileName("...", 0) == "未命名表情_1.png",
            "pixel emoji PNG sequence names replace invalid characters and avoid Windows reserved names");

        PixelEmojiReferencePolicy.MapToLogicalCanvas(24, 24, 424f, 424f, 24, out var gridWidth, out var gridHeight);
        PixelEmojiReferencePolicy.MapToLogicalCanvas(48, 24, 424f, 424f, 24, out var landscapeWidth, out var landscapeHeight);
        PixelEmojiReferencePolicy.MapToLogicalCanvas(12, 24, 424f, 424f, 24, out var portraitWidth, out var portraitHeight);
        PixelEmojiReferencePolicy.MapToLogicalCanvas(0, 24, 424f, 424f, 24, out var invalidWidth, out var invalidHeight);
        Assert(Math.Abs(gridWidth - 424f) < 0.001f
               && Math.Abs(gridHeight - 424f) < 0.001f
               && Math.Abs(landscapeWidth - 848f) < 0.001f
               && Math.Abs(landscapeHeight - 424f) < 0.001f
               && Math.Abs(portraitWidth - 212f) < 0.001f
               && Math.Abs(portraitHeight - 424f) < 0.001f
               && Math.Abs(invalidWidth) < 0.001f
               && Math.Abs(invalidHeight) < 0.001f,
            "pixel emoji reference art maps each source pixel to one logical canvas cell at 100 percent");
        Assert(PixelEmojiReferencePolicy.ShouldUsePointFiltering(24, 24)
               && PixelEmojiReferencePolicy.ShouldUsePointFiltering(192, 192)
               && !PixelEmojiReferencePolicy.ShouldUsePointFiltering(193, 192),
            "pixel emoji reference art keeps low-resolution pixel sources crisp");
        Assert(PixelEmojiReferencePolicy.ClampScalePercent(-10) == 1
               && PixelEmojiReferencePolicy.ClampScalePercent(150) == 100
               && PixelEmojiReferencePolicy.ClampOpacityPercent(0) == 10
               && PixelEmojiReferencePolicy.ClampOpacityPercent(100) == 80,
            "pixel emoji reference controls enforce the agreed scale and opacity ranges");
        Assert(PixelEmojiReferencePolicy.IsSupportedSource(1024, 8192, 8192)
               && !PixelEmojiReferencePolicy.IsSupportedSource(0, 100, 100)
               && !PixelEmojiReferencePolicy.IsSupportedSource(PixelEmojiReferencePolicy.MaximumSourceBytes + 1, 100, 100)
               && !PixelEmojiReferencePolicy.IsSupportedSource(1024, 8193, 100),
            "pixel emoji reference import rejects empty, oversized, and excessive-dimension sources");
    }

    private static void TestPixelEmojiWorkshopLayoutPolicy()
    {
        var wide = PixelEmojiWorkshopLayoutPolicy.Resolve(850f);
        Assert(wide.Tier == PixelEmojiWorkshopLayoutTier.Wide
               && !wide.StackVertically
               && wide.CanvasSize == 408f
               && wide.CanvasColumnWidth == 424f
               && wide.PaletteCellWidth == 38f,
            "pixel emoji wide workshop gives equal visual weight to canvas and palette tools");
        Assert(wide.CanvasSize % PixelEmojiCodec.SourceSize == 0f
               && wide.CanvasColumnWidth
                  + PixelEmojiWorkshopLayoutPolicy.ColumnGap
                  + wide.ToolsMinimumWidth
                  <= PixelEmojiWorkshopLayoutPolicy.WideMinimumWidth,
            "pixel emoji wide workshop uses integer-sized pixels and fits its declared breakpoint");

        var compact = PixelEmojiWorkshopLayoutPolicy.Resolve(780f);
        Assert(compact.Tier == PixelEmojiWorkshopLayoutTier.Compact
               && !compact.StackVertically
               && compact.CanvasSize == 360f
               && compact.CanvasSize % PixelEmojiCodec.SourceSize == 0f,
            "pixel emoji compact workshop preserves a usable integer-pixel canvas");
        Assert(compact.CanvasColumnWidth
               + PixelEmojiWorkshopLayoutPolicy.ColumnGap
               + compact.ToolsMinimumWidth
               <= PixelEmojiWorkshopLayoutPolicy.CompactMinimumWidth,
            "pixel emoji compact workshop fits its declared breakpoint");

        var stacked = PixelEmojiWorkshopLayoutPolicy.Resolve(700f);
        Assert(stacked.Tier == PixelEmojiWorkshopLayoutTier.Stacked
               && stacked.StackVertically
               && stacked.WorkspaceHeight
                  == stacked.ContentHeight * 2f + PixelEmojiWorkshopLayoutPolicy.ColumnGap,
            "pixel emoji workshop stacks only below the compact horizontal breakpoint");
    }

    private static uint ReadRgba(byte[] rgba, int x, int y)
    {
        var offset = (y * PixelEmojiCodec.NativeSize + x) * 4;
        return (uint)(rgba[offset] << 24
                      | rgba[offset + 1] << 16
                      | rgba[offset + 2] << 8
                      | rgba[offset + 3]);
    }

    private static PixelEmojiPresentation CreatePresentation(
        string eventId,
        long createdUtcTicks,
        IReadOnlyList<byte[]> frames,
        PixelEmojiPlaybackMode playbackMode)
    {
        return new PixelEmojiPresentation
        {
            EventId = eventId,
            CreatedUtcTicks = createdUtcTicks,
            PlaybackMode = playbackMode,
            FramesBase64 = PixelEmojiAnimationCodec.EncodeFrames(frames),
            ContentHash = PixelEmojiAnimationCodec.Sha256(frames, playbackMode)
        };
    }
}
