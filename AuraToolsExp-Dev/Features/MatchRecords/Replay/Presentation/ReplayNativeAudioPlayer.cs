using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using AuraToolsExp.Dll.Features.MatchRecords.Replay.Core;
using AuraToolsExp.Dll.Features.MatchRecords.Storage;
using AuraToolsExp.Dll.Infrastructure;
using UnityEngine;
using Object = UnityEngine.Object;

namespace AuraToolsExp.Dll.Features.MatchRecords.Replay.Presentation;

/// <summary>
/// Plays the audio facts frozen into a native v11 replay. Embedded PCM is
/// authoritative; a stable native resource id is retained only as a matching
/// installation diagnostic and interactive fallback.
/// </summary>
internal sealed class ReplayNativeAudioPlayer : IDisposable
{
    private static readonly MethodInfo? AudioClipSetData = typeof(AudioClip)
        .GetMethod("SetData", new[] { typeof(float[]), typeof(int) });
    private readonly ReplayDocumentV11 document;
    private readonly GameObject root;
    private readonly AudioSource bgm;
    private readonly AudioSource effects;
    private readonly AudioSource vocals;
    private readonly Dictionary<string, AudioClip?> cache = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> missingLogged = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<AudioClip> ownedClips = new();
    private string currentBgmId = "";
    private long lastTimelineSample = -1;

    internal ReplayNativeAudioPlayer(ReplayDocumentV11 document, Transform parent)
    {
        this.document = document;
        root = new GameObject("ReplayNativeAudio");
        root.transform.SetParent(parent, false);
        bgm = AddSource("Bgm", loop: true);
        effects = AddSource("Effects", loop: false);
        vocals = AddSource("Vocals", loop: false);
    }

    internal void PlayEvent(ReplayTimelineEventV11? timelineEvent, float speed)
    {
        if (timelineEvent == null) return;
        foreach (var cue in timelineEvent.Audio ?? new List<ReplayAudioCueV11>())
        {
            if (IsBgm(cue)) continue;
            var clip = Resolve(cue);
            if (clip == null) throw new InvalidOperationException("Recorded replay PCM attachment is unavailable: " + cue.AssetSha256);
            var source = string.Equals(cue.Bus, "Vocal", StringComparison.OrdinalIgnoreCase)
                ? vocals
                : effects;
            source.pitch = Mathf.Clamp(speed, 0.5f, 2f);
            source.PlayOneShot(clip, Mathf.Clamp01(cue.GainQ16 / 65536f));
        }
    }

    internal void SyncTimeline(long milliseconds, float speed, bool paused)
    {
        var sample = Math.Max(0L, milliseconds) * ReplayOfflineAudioSampleRate / 1000L;
        if (sample < lastTimelineSample)
        {
            effects.Stop();
            vocals.Stop();
            lastTimelineSample = -1;
        }
        foreach (var cue in document.Events.SelectMany(value => value.Audio ?? new List<ReplayAudioCueV11>())
                     .Where(value => !IsBgm(value)
                                     && value.StartSample > lastTimelineSample
                                     && value.StartSample <= sample)
                     .OrderBy(value => value.StartSample))
        {
            var clip = Resolve(cue);
            if (clip == null) throw new InvalidOperationException("Recorded replay PCM attachment is unavailable: " + cue.AssetSha256);
            var source = string.Equals(cue.Bus, "Vocal", StringComparison.OrdinalIgnoreCase) ? vocals : effects;
            source.pitch = Mathf.Clamp(speed, 0.5f, 2f);
            source.PlayOneShot(clip, Mathf.Clamp01(cue.GainQ16 / 65536f));
        }
        lastTimelineSample = sample;
        SyncBgm(milliseconds * ReplayProtocolV11.TimebaseTicksPerSecond / 1000L, speed);
        SetPaused(paused);
    }

    internal void SyncBgm(long currentTicks, float speed)
    {
        var selected = document.Events
            .Where(value => value.TimeTicks <= currentTicks)
            .SelectMany(value => value.Audio ?? new List<ReplayAudioCueV11>())
            .Where(IsBgm)
            .LastOrDefault();
        var id = !string.IsNullOrWhiteSpace(selected?.AssetSha256)
            ? "attachment:" + selected!.AssetSha256
            : selected?.NativeResourceId?.Trim() ?? "";
        if (id.Length == 0)
        {
            if (bgm.isPlaying) bgm.Stop();
            currentBgmId = "";
            return;
        }

        bgm.pitch = Mathf.Clamp(speed, 0.5f, 2f);
        bgm.volume = Mathf.Clamp01((selected?.GainQ16 ?? 65_536) / 65536f);
        if (string.Equals(currentBgmId, id, StringComparison.OrdinalIgnoreCase) && bgm.clip != null)
            return;
        currentBgmId = id;
        bgm.Stop();
        var selectedCue = selected ?? throw new InvalidOperationException("Recorded replay BGM cue is unavailable.");
        bgm.clip = Resolve(selectedCue);
        if (bgm.clip == null) throw new InvalidOperationException("Recorded replay BGM attachment is unavailable: " + selectedCue.AssetSha256);
        bgm.loop = true;
        try
        {
            var offsetSeconds = Math.Max(0d, (selected?.SourceOffsetSample ?? 0L)
                                              / (double)ReplayOfflineAudioSampleRate);
            bgm.time = (float)Math.Min(Math.Max(0f, bgm.clip.length - 0.01f), offsetSeconds);
        }
        catch
        {
        }
        bgm.Play();
    }

    internal void SetPaused(bool paused)
    {
        foreach (var source in new[] { bgm, effects, vocals })
        {
            if (paused) source.Pause();
            else source.UnPause();
        }
    }

    public void Dispose()
    {
        if (root != null) Object.Destroy(root);
        foreach (var clip in ownedClips) if (clip != null) Object.Destroy(clip);
        ownedClips.Clear();
    }

    private AudioSource AddSource(string name, bool loop)
    {
        var child = new GameObject(name, typeof(AudioSource));
        child.transform.SetParent(root.transform, false);
        var source = child.GetComponent<AudioSource>();
        source.playOnAwake = false;
        source.loop = loop;
        source.spatialBlend = 0f;
        return source;
    }

    private AudioClip? Resolve(string resourceId)
    {
        var normalized = Normalize(resourceId);
        if (normalized.Length == 0) return null;
        if (cache.TryGetValue(normalized, out var cached)) return cached;
        AudioClip? result = null;
        foreach (var candidate in Candidates(normalized))
        {
            try
            {
                result = AuraToolsResourceCache.Load<AudioClip>(candidate);
                if (result != null) break;
            }
            catch
            {
            }
        }
        cache[normalized] = result;
        if (result == null && missingLogged.Add(normalized))
        {
            AuraToolsLog.Warn("[MatchRecords] native replay audio unavailable; continuing silently: " + normalized);
        }
        return result;
    }

    private AudioClip? Resolve(ReplayAudioCueV11 cue)
    {
        if (!string.IsNullOrWhiteSpace(cue.AssetSha256))
        {
            var key = "attachment:" + cue.AssetSha256;
            if (cache.TryGetValue(key, out var cached)) return cached;
            var clip = LoadEmbeddedWave(cue.AssetSha256);
            if (clip != null) ownedClips.Add(clip);
            cache[key] = clip;
            return clip;
        }
        return Resolve(cue.NativeResourceId);
    }

    private static AudioClip? LoadEmbeddedWave(string sha256)
    {
        var path = MatchRecordStorage.Database.ResolveReplayAsset(sha256);
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return null;
        try
        {
            var payload = File.ReadAllBytes(path);
            if (!ReplayPcm16WaveContractV11.TryRead(payload, out var wave, out _)) return null;
            var samples = ReplayPcm16WaveContractV11.DecodeSamples(
                payload,
                wave,
                maximumSampleValues: 64 * 1024 * 1024);
            var clip = AudioClip.Create(
                "ReplayAudio_" + sha256.Substring(0, Math.Min(12, sha256.Length)),
                checked((int)wave.SampleFrames),
                wave.Channels,
                wave.SampleRate,
                false);
            return AudioClipSetData?.Invoke(clip, new object[] { samples, 0 }) is true ? clip : null;
        }
        catch
        {
            return null;
        }
    }

    private static IEnumerable<string> Candidates(string id)
    {
        yield return id;
        if (!id.StartsWith("Sounds/", StringComparison.OrdinalIgnoreCase)) yield return "Sounds/" + id;
        if (!id.StartsWith("BGM/", StringComparison.OrdinalIgnoreCase)) yield return "BGM/" + id;
        if (!id.StartsWith("Sounds/BGM/", StringComparison.OrdinalIgnoreCase)) yield return "Sounds/BGM/" + id;
    }

    private static string Normalize(string value)
    {
        var id = (value ?? "").Trim().Replace('\\', '/').TrimStart('/');
        return id.Length == 0
               || id.Length > 240
               || id.Contains(":")
               || id.Split('/').Any(segment => segment == "..")
               || id.StartsWith("Mods/", StringComparison.OrdinalIgnoreCase)
               || id.StartsWith("SharedResources/", StringComparison.OrdinalIgnoreCase)
            ? ""
            : id;
    }

    private static bool IsBgm(ReplayAudioCueV11 cue)
    {
        return string.Equals(cue.Bus, "Bgm", StringComparison.OrdinalIgnoreCase)
               || string.Equals(cue.Kind, "BattleBgm", StringComparison.OrdinalIgnoreCase);
    }

    private const int ReplayOfflineAudioSampleRate = 48_000;
}
