using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using AuraToolsExp.Dll.Features.MatchRecords.ReplayV17.Core;
using AuraToolsExp.Dll.GameApi;
using AuraToolsExp.Dll.Infrastructure;
using UnityEngine;
using Object = UnityEngine.Object;

namespace AuraToolsExp.Dll.Features.MatchRecords.ReplayV17.Playback;

internal sealed class ReplayAssetCacheV17 : IDisposable
{
    private readonly Dictionary<string, ReplayAssetV17> assets;
    private readonly Dictionary<string, Texture2D> textures = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Sprite> sprites = new(StringComparer.Ordinal);
    private Texture2D? whiteTexture;
    private Sprite? whiteSprite;

    internal ReplayAssetCacheV17(IEnumerable<ReplayAssetV17> values)
    {
        assets = (values ?? Array.Empty<ReplayAssetV17>())
            .Where(item => item != null && !string.IsNullOrWhiteSpace(item.Sha256))
            .GroupBy(item => item.Sha256, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.Last(), StringComparer.OrdinalIgnoreCase);
    }

    internal Sprite? FullSprite(string sha256, float pixelsPerUnit)
    {
        var texture = Texture(sha256);
        if (texture == null) return null;
        var key = "full|" + sha256 + "|" + pixelsPerUnit;
        if (sprites.TryGetValue(key, out var cached)) return cached;
        var sprite = UnityEngine.Sprite.Create(texture, new Rect(0, 0, texture.width, texture.height), new Vector2(0.5f, 0.5f), Math.Max(1f, pixelsPerUnit));
        sprites[key] = sprite;
        return sprite;
    }

    internal Sprite? Sprite(ReplaySpriteFrameV17 frame)
    {
        var texture = Texture(frame.AssetSha256);
        if (texture == null) return null;
        var key = "frame|" + frame.AssetSha256 + "|" + frame.RectX + "|" + frame.RectY
                  + "|" + frame.RectWidth + "|" + frame.RectHeight + "|"
                  + frame.PivotXQ16 + "|" + frame.PivotYQ16 + "|" + frame.PixelsPerUnitQ16 + "|"
                  + frame.Border.X + "|" + frame.Border.Y + "|" + frame.Border.Z + "|" + frame.Border.W;
        if (sprites.TryGetValue(key, out var cached)) return cached;
        var x = Mathf.Clamp(frame.RectX, 0, Math.Max(0, texture.width - 1));
        var y = Mathf.Clamp(frame.RectY, 0, Math.Max(0, texture.height - 1));
        var width = Mathf.Clamp(frame.RectWidth <= 0 ? texture.width : frame.RectWidth, 1, texture.width - x);
        var height = Mathf.Clamp(frame.RectHeight <= 0 ? texture.height : frame.RectHeight, 1, texture.height - y);
        var sprite = UnityEngine.Sprite.Create(
            texture,
            new Rect(x, y, width, height),
            new Vector2(ReplayPresentationPrimitivesV17.FromQ16(frame.PivotXQ16), ReplayPresentationPrimitivesV17.FromQ16(frame.PivotYQ16)),
            Math.Max(1f, ReplayPresentationPrimitivesV17.FromQ16(frame.PixelsPerUnitQ16)),
            0,
            SpriteMeshType.FullRect,
            new Vector4(
                ReplayPresentationPrimitivesV17.FromQ16(frame.Border.X),
                ReplayPresentationPrimitivesV17.FromQ16(frame.Border.Y),
                ReplayPresentationPrimitivesV17.FromQ16(frame.Border.Z),
                ReplayPresentationPrimitivesV17.FromQ16(frame.Border.W)));
        sprites[key] = sprite;
        return sprite;
    }

    internal Sprite WhiteSprite()
    {
        if (whiteSprite != null) return whiteSprite;
        whiteTexture = new Texture2D(1, 1, TextureFormat.RGBA32, false);
        whiteTexture.SetPixel(0, 0, Color.white);
        whiteTexture.Apply(false, true);
        whiteSprite = UnityEngine.Sprite.Create(whiteTexture, new Rect(0, 0, 1, 1), new Vector2(0.5f, 0.5f), 1f);
        sprites["white"] = whiteSprite;
        return whiteSprite;
    }

    internal byte[] Bytes(string sha256)
    {
        return assets.TryGetValue(sha256 ?? "", out var value) ? value.Payload ?? Array.Empty<byte>() : Array.Empty<byte>();
    }

    public void Dispose()
    {
        foreach (var sprite in sprites.Values.Where(item => item != null)) Object.Destroy(sprite);
        sprites.Clear();
        foreach (var texture in textures.Values.Where(item => item != null)) Object.Destroy(texture);
        textures.Clear();
        if (whiteTexture != null) Object.Destroy(whiteTexture);
        whiteTexture = null;
        whiteSprite = null;
    }

    internal Texture2D? Texture(string sha256)
    {
        if (string.IsNullOrWhiteSpace(sha256)) return null;
        if (textures.TryGetValue(sha256, out var cached)) return cached;
        var bytes = Bytes(sha256);
        if (bytes.Length == 0) return null;
        var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
        var method = typeof(ImageConversion).GetMethod(
            "LoadImage",
            new[] { typeof(Texture2D), typeof(byte[]), typeof(bool) });
        if (method?.Invoke(null, new object[] { texture, bytes, false }) is not true)
        {
            Object.Destroy(texture);
            return null;
        }
        texture.wrapMode = TextureWrapMode.Clamp;
        texture.filterMode = FilterMode.Bilinear;
        textures[sha256] = texture;
        return texture;
    }
}


internal sealed class ReplayEffectRuntimeV17 : IDisposable
{
    private readonly Transform parent;
    private readonly ReplayAssetCacheV17 assets;
    private readonly IReadOnlyDictionary<string, ReplayEffectDescriptorV17> descriptors;
    private readonly List<ActiveEffect> active = new();

    internal ReplayEffectRuntimeV17(
        Transform parent,
        ReplayAssetCacheV17 assets,
        IReadOnlyDictionary<string, ReplayEffectDescriptorV17> descriptors)
    {
        this.parent = parent;
        this.assets = assets;
        this.descriptors = descriptors;
        foreach (var descriptor in descriptors.Values)
        {
            var hasPrefab = ReplayNativeEffectCompatibilityApi.Resolve(descriptor.ResourcePath).Count > 0;
            var hasFrames = descriptor.Frames.Count > 0
                            || !string.IsNullOrWhiteSpace(descriptor.ResourcePath)
                            && ReplayResourceResolverV17.Sprites(descriptor.ResourcePath).Length > 0;
            if (!hasPrefab && !hasFrames)
                throw new InvalidOperationException(
                    "Replay effect has no native prefab or sprite sequence: " + descriptor.DescriptorId
                    + " -> " + descriptor.ResourcePath);
        }
    }

    internal void Play(
        ReplayPresentationMessageV17 message,
        Vector3 position,
        long logicalTicks)
    {
        if (!descriptors.TryGetValue(message.EffectDescriptorId ?? "", out var descriptor)) return;
        var nativeSpecs = ReplayNativeEffectCompatibilityApi.Resolve(descriptor.ResourcePath);
        var frames = descriptor.Frames.Select(assets.Sprite).Where(item => item != null).Select(item => item!).ToArray();
        if (frames.Length == 0 && !string.IsNullOrWhiteSpace(descriptor.ResourcePath))
            frames = ReplayResourceResolverV17.Sprites(descriptor.ResourcePath);
        GameObject root;
        SpriteRenderer? renderer = null;
        long nativeDuration = 0L;
        if (nativeSpecs.Count > 0)
        {
            root = new GameObject("ReplayNativeEffect:" + descriptor.DescriptorId);
            root.transform.SetParent(parent, false);
            root.layer = 30;
            foreach (var spec in nativeSpecs)
            {
                _ = ReplayNativePrefabInstanceV17.Clone(
                    spec.Prefab,
                    root.transform,
                    "ReplayNativeEffectPart:" + spec.EffectId);
                nativeDuration = Math.Max(nativeDuration, spec.DurationMicroseconds);
            }
        }
        else
        {
            if (frames.Length == 0)
                throw new InvalidOperationException("Replay native effect resource disappeared: " + descriptor.DescriptorId);
            root = new GameObject("ReplayEffect:" + descriptor.DescriptorId, typeof(SpriteRenderer));
            root.transform.SetParent(parent, false);
            root.layer = 30;
            renderer = root.GetComponent<SpriteRenderer>();
            renderer.sprite = frames[0];
            renderer.color = ReplayPresentationPrimitivesV17.Color(descriptor.Color);
            renderer.sortingOrder = 400;
        }
        root.transform.localPosition = new Vector3(position.x, position.y, -0.5f);
        active.Add(new ActiveEffect(
            root,
            renderer,
            frames,
            Math.Max(0.1f, ReplayPresentationPrimitivesV17.FromQ16(descriptor.FramesPerSecondQ16)),
            logicalTicks,
            logicalTicks + Math.Max(120_000, Math.Max(nativeDuration, Math.Max(descriptor.DurationTicks, message.DurationTicks)))));
    }

    internal void Tick(long logicalTicks)
    {
        foreach (var value in active.ToList())
        {
            if (logicalTicks >= value.End)
            {
                Object.Destroy(value.Root);
                active.Remove(value);
                continue;
            }
            var progress = (logicalTicks - value.Start) / (float)Math.Max(1, value.End - value.Start);
            if (value.Renderer != null && value.Frames.Length > 0)
            {
                var frame = (int)Math.Floor(
                    Math.Max(0L, logicalTicks - value.Start)
                    / (double)ReplayProtocolV17.TimebaseTicksPerSecond * value.FramesPerSecond);
                value.Renderer.sprite = value.Frames[Math.Min(value.Frames.Length - 1, Math.Max(0, frame))];
            }
            if (value.Renderer != null)
            {
                var color = value.Renderer.color;
                color.a = 1f - Mathf.Clamp01(progress);
                value.Renderer.color = color;
            }
        }
    }

    internal void Clear()
    {
        foreach (var value in active) if (value.Root != null) Object.Destroy(value.Root);
        active.Clear();
    }

    public void Dispose() => Clear();

    private sealed class ActiveEffect
    {
        internal ActiveEffect(
            GameObject root,
            SpriteRenderer? renderer,
            Sprite[] frames,
            float framesPerSecond,
            long start,
            long end)
        {
            Root = root;
            Renderer = renderer;
            Frames = frames;
            FramesPerSecond = framesPerSecond;
            Start = start;
            End = end;
        }
        internal GameObject Root { get; }
        internal SpriteRenderer? Renderer { get; }
        internal Sprite[] Frames { get; }
        internal float FramesPerSecond { get; }
        internal long Start { get; }
        internal long End { get; }
    }
}

internal sealed class ReplayAudioRuntimeV17 : IDisposable
{
    private static readonly MethodInfo? AudioClipSetData = typeof(AudioClip)
        .GetMethod("SetData", new[] { typeof(float[]), typeof(int) });
    private readonly GameObject root;
    private readonly ReplayAssetCacheV17 assets;
    private readonly List<AudioSource> sources = new();
    private readonly Dictionary<AudioSource, float> sourceRates = new();
    private readonly Dictionary<AudioSource, ActiveCue> activeCues = new();
    private readonly List<AudioClip> clips = new();
    private float transportSpeed = 1f;

    internal ReplayAudioRuntimeV17(Transform parent, ReplayAssetCacheV17 assets)
    {
        this.assets = assets;
        root = new GameObject("ReplayAudio");
        root.transform.SetParent(parent, false);
        root.layer = 30;
    }

    internal void Play(ReplayAudioCueV17 cue, long logicalStartTicks, long resumeAtTicks = -1L)
    {
        AudioClip? clip = null;
        var ownsClip = false;
        var bytes = assets.Bytes(cue.AssetSha256);
        if (TryDecodePcm16(bytes, out var samples, out var channels, out var sampleRate))
        {
            clip = AudioClip.Create("ReplayAudio:" + cue.AssetSha256, samples.Length / channels, channels, sampleRate, false);
            if (AudioClipSetData?.Invoke(clip, new object[] { samples, 0 }) is not true)
            {
                Object.Destroy(clip);
                return;
            }
            ownsClip = true;
        }
        else if (!string.IsNullOrWhiteSpace(cue.ResourcePath))
        {
            clip = AuraToolsResourceCache.Load<AudioClip>(cue.ResourcePath, true)
                   ?? AuraToolsResourceCache.Load<AudioClip>(cue.ResourcePath, false);
        }
        if (clip == null) return;
        sampleRate = Math.Max(1, clip.frequency);
        if (ownsClip) clips.Add(clip);
        var source = root.AddComponent<AudioSource>();
        source.clip = clip;
        source.volume = Mathf.Max(0f, cue.GainQ16 / 65_536f);
        source.panStereo = Mathf.Clamp(cue.PanQ16 / 65_536f, -1f, 1f);
        var sourceRate = Mathf.Clamp(cue.PlaybackRateQ16 / 65_536f, 0.1f, 3f);
        source.pitch = Mathf.Clamp(sourceRate * transportSpeed, 0.1f, 3f);
        source.loop = cue.LoopEndSample > cue.LoopStartSample;
        var resumedTicks = resumeAtTicks < logicalStartTicks ? logicalStartTicks : resumeAtTicks;
        var elapsedTimelineSamples = Math.Max(0L, resumedTicks - logicalStartTicks)
                                     * 48_000L / ReplayProtocolV17.TimebaseTicksPerSecond;
        var elapsedSourceFrames = (long)Math.Floor(
            elapsedTimelineSamples * sourceRate * sampleRate / 48_000d);
        var sourceFrame = Math.Max(0L, cue.SourceOffsetSample + elapsedSourceFrames);
        if (source.loop && cue.LoopEndSample > cue.LoopStartSample && sourceFrame >= cue.LoopEndSample)
            sourceFrame = cue.LoopStartSample + (sourceFrame - cue.LoopStartSample)
                % Math.Max(1L, cue.LoopEndSample - cue.LoopStartSample);
        source.timeSamples = (int)Math.Min(clip.samples - 1L, sourceFrame);
        source.Play();
        sources.Add(source);
        sourceRates[source] = sourceRate;
        var durationSamples = cue.DurationSamples > 0
            ? cue.DurationSamples
            : clip.samples * 48_000L / Math.Max(1, clip.frequency);
        activeCues[source] = new ActiveCue(
            logicalStartTicks,
            logicalStartTicks + durationSamples * ReplayProtocolV17.TimebaseTicksPerSecond / 48_000L,
            source.volume,
            cue.FadeInSamples,
            cue.FadeOutSamples);
    }

    internal void Tick(long logicalTicks)
    {
        foreach (var source in sources.ToList())
        {
            if (source == null || !activeCues.TryGetValue(source, out var cue)) continue;
            if (logicalTicks >= cue.EndTicks)
            {
                source.Stop();
                Object.Destroy(source);
                sources.Remove(source);
                sourceRates.Remove(source);
                activeCues.Remove(source);
                continue;
            }
            var elapsedSamples = Math.Max(0L, logicalTicks - cue.StartTicks) * 48_000L / ReplayProtocolV17.TimebaseTicksPerSecond;
            var remainingSamples = Math.Max(0L, cue.EndTicks - logicalTicks) * 48_000L / ReplayProtocolV17.TimebaseTicksPerSecond;
            var envelope = 1f;
            if (cue.FadeInSamples > 0) envelope = Math.Min(envelope, elapsedSamples / (float)cue.FadeInSamples);
            if (cue.FadeOutSamples > 0) envelope = Math.Min(envelope, remainingSamples / (float)cue.FadeOutSamples);
            source.volume = cue.Gain * Mathf.Clamp01(envelope);
        }
    }

    internal void SetTransportSpeed(float speed)
    {
        transportSpeed = Mathf.Clamp(speed, 0.1f, 4f);
        foreach (var source in sources.Where(item => item != null))
            source.pitch = Mathf.Clamp(sourceRates.TryGetValue(source, out var rate) ? rate * transportSpeed : transportSpeed, 0.1f, 3f);
    }

    internal void SetPaused(bool paused)
    {
        foreach (var source in sources.Where(item => item != null))
        {
            if (paused) source.Pause();
            else source.UnPause();
        }
    }

    internal void StopAll()
    {
        foreach (var source in sources.Where(item => item != null))
        {
            source.Stop();
            Object.Destroy(source);
        }
        sources.Clear();
        sourceRates.Clear();
        activeCues.Clear();
        foreach (var clip in clips.Where(item => item != null)) Object.Destroy(clip);
        clips.Clear();
    }

    public void Dispose()
    {
        StopAll();
        if (root != null) Object.Destroy(root);
    }

    private sealed class ActiveCue
    {
        internal ActiveCue(long startTicks, long endTicks, float gain, long fadeInSamples, long fadeOutSamples)
        {
            StartTicks = startTicks;
            EndTicks = Math.Max(startTicks, endTicks);
            Gain = gain;
            FadeInSamples = Math.Max(0L, fadeInSamples);
            FadeOutSamples = Math.Max(0L, fadeOutSamples);
        }
        internal long StartTicks { get; }
        internal long EndTicks { get; }
        internal float Gain { get; }
        internal long FadeInSamples { get; }
        internal long FadeOutSamples { get; }
    }

    private static bool TryDecodePcm16(byte[] bytes, out float[] samples, out int channels, out int sampleRate)
    {
        samples = Array.Empty<float>();
        channels = 0;
        sampleRate = 0;
        if (bytes == null || bytes.Length < 44
            || Encoding(bytes, 0, 4) != "RIFF"
            || Encoding(bytes, 8, 4) != "WAVE"
            || BitConverter.ToInt16(bytes, 20) != 1
            || BitConverter.ToInt16(bytes, 34) != 16)
            return false;
        channels = BitConverter.ToInt16(bytes, 22);
        sampleRate = BitConverter.ToInt32(bytes, 24);
        var offset = 12;
        var dataOffset = -1;
        var dataLength = 0;
        while (offset + 8 <= bytes.Length)
        {
            var id = Encoding(bytes, offset, 4);
            var length = BitConverter.ToInt32(bytes, offset + 4);
            if (length < 0 || offset + 8L + length > bytes.Length) return false;
            if (id == "data")
            {
                dataOffset = offset + 8;
                dataLength = length;
                break;
            }
            offset += 8 + length + (length & 1);
        }
        if (channels is < 1 or > 2 || sampleRate <= 0 || dataOffset < 0 || dataLength % 2 != 0) return false;
        samples = new float[dataLength / 2];
        for (var index = 0; index < samples.Length; index++)
            samples[index] = BitConverter.ToInt16(bytes, dataOffset + index * 2) / 32768f;
        return true;
    }

    private static string Encoding(byte[] bytes, int offset, int count) =>
        System.Text.Encoding.ASCII.GetString(bytes, offset, count);
}
