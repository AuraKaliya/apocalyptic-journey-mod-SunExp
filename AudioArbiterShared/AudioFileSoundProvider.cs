using System;
using System.Collections;
using System.IO;
using AuraAudio.Shared;
using AuraShared.Core;
using UnityEngine;
using UnityEngine.Networking;

namespace AudioArbiter.Shared;

public sealed class FileSoundProvider : IDisposable
{
    private readonly Func<object?, bool>? condition;
    private readonly string[] audioPaths;
    private readonly ProviderRunner runner;
    private AudioClip?[] clips;
    private string loadState = "NotStarted";
    private int generation;
    private int pendingLoads;
    private bool disposed;

    public FileSoundProvider(
        string providerId,
        string ownerModId,
        string audioPath,
        int priority,
        string bus,
        string policy,
        bool hardClaim,
        Func<object?, bool>? condition,
        float cooldownSeconds = 0f,
        bool sync = true,
        float gainDb = 0f,
        float volumeMultiplier = 1f,
        string kind = "",
        float? lowHealthCrossDownThreshold = null,
        string[]? suppressVocalStates = null,
        int[]? suppressNarrationIds = null)
        : this(
            providerId,
            ownerModId,
            audioPath,
            Array.Empty<string>(),
            priority,
            bus,
            policy,
            hardClaim,
            condition,
            cooldownSeconds,
            sync,
            gainDb,
            volumeMultiplier,
            kind,
            lowHealthCrossDownThreshold,
            suppressVocalStates,
            suppressNarrationIds)
    {
    }

    public FileSoundProvider(
        string providerId,
        string ownerModId,
        string audioPath,
        string[]? variantAudioPaths,
        int priority,
        string bus,
        string policy,
        bool hardClaim,
        Func<object?, bool>? condition,
        float cooldownSeconds = 0f,
        bool sync = true,
        float gainDb = 0f,
        float volumeMultiplier = 1f,
        string kind = "",
        float? lowHealthCrossDownThreshold = null,
        string[]? suppressVocalStates = null,
        int[]? suppressNarrationIds = null)
    {
        ProviderId = providerId;
        OwnerModId = ownerModId;
        Kind = (kind ?? "").Trim();
        LowHealthCrossDownThreshold = lowHealthCrossDownThreshold ?? -1f;
        Priority = priority;
        Bus = bus;
        Policy = policy;
        HardClaim = hardClaim;
        CooldownSeconds = cooldownSeconds;
        Sync = sync;
        GainDb = gainDb;
        VolumeMultiplier = volumeMultiplier;
        SuppressVocalStates = string.Join("|", suppressVocalStates ?? Array.Empty<string>());
        SuppressNarrationIds = string.Join("|", suppressNarrationIds ?? Array.Empty<int>());
        audioPaths = BuildAudioPaths(audioPath, variantAudioPaths);
        clips = new AudioClip?[audioPaths.Length];
        this.condition = condition;

        var gameObject = new GameObject("AudioProvider." + ownerModId + "." + providerId);
        UnityEngine.Object.DontDestroyOnLoad(gameObject);
        runner = gameObject.AddComponent<ProviderRunner>();
    }

    public string ProviderId { get; }

    public string OwnerModId { get; }

    public string Kind { get; }

    public float LowHealthCrossDownThreshold { get; }

    public int Priority { get; }

    public string Bus { get; }

    public string Policy { get; }

    public bool HardClaim { get; }

    public bool Sync { get; }

    public float CooldownSeconds { get; }

    public float GainDb { get; }

    public float VolumeMultiplier { get; }

    public string SuppressVocalStates { get; }

    public string SuppressNarrationIds { get; }

    public bool Evaluate(object? request)
    {
        var eligible = condition == null || condition(request);
        if (eligible)
        {
            EnsureStarted();
        }

        return eligible;
    }

    public string GetLoadState()
    {
        return loadState;
    }

    public AudioClip? GetClip(object? request)
    {
        EnsureStarted();
        if (clips.Length == 0)
        {
            return null;
        }

        var eventId = AudioPropertyReader.ReadString(request, "EventId");
        var providerIdentity = AudioProviderResolver.QualifyProviderId(OwnerModId, ProviderId);
        var startIndex = AudioVariantSelectionPolicy.SelectStartIndex(eventId, providerIdentity, clips.Length);
        for (var offset = 0; offset < clips.Length; offset++)
        {
            var candidate = clips[(startIndex + offset) % clips.Length];
            if (candidate != null)
            {
                return candidate;
            }
        }

        return null;
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        generation++;
        runner.StopAllCoroutines();
        if (runner.gameObject != null)
        {
            UnityEngine.Object.Destroy(runner.gameObject);
        }

        clips = Array.Empty<AudioClip?>();
        loadState = "Disposed";
    }

    private void StartLoad()
    {
        generation++;
        var currentGeneration = generation;
        if (audioPaths.Length == 0)
        {
            loadState = "Missing";
            return;
        }

        clips = new AudioClip?[audioPaths.Length];
        pendingLoads = audioPaths.Length;
        loadState = "Loading";
        for (var i = 0; i < audioPaths.Length; i++)
        {
            StartPathLoad(i, audioPaths[i], currentGeneration);
        }
    }

    private void EnsureStarted()
    {
        if (!disposed && string.Equals(loadState, "NotStarted", StringComparison.Ordinal))
        {
            StartLoad();
        }
    }

    private void StartPathLoad(int index, string audioPath, int currentGeneration)
    {
        if (!File.Exists(audioPath))
        {
            CompletePathLoad(currentGeneration, index, audioPath, null, null, "file missing");
            return;
        }

        var descriptor = AudioFileFormatProbe.Probe(audioPath);
        AuraSharedLog.Info("AudioArbiter", "Sound probe: provider=" + ProviderId
            + ", owner=" + OwnerModId
            + ", path=" + audioPath
            + ", sourceExtension=" + Display(Path.GetExtension(audioPath))
            + ", " + descriptor.Describe());
        if (!descriptor.Success || !AudioUnityFileLoadPolicy.TryResolve(descriptor, out var audioType))
        {
            CompletePathLoad(
                currentGeneration,
                index,
                audioPath,
                descriptor,
                null,
                "probe-failed: code=" + Display(descriptor.FailureCode) + ", message=" + descriptor.Message);
            return;
        }

        runner.LoadAudio(audioPath, audioType, currentGeneration, (completedGeneration, loadedClip, error) =>
            CompletePathLoad(completedGeneration, index, audioPath, descriptor, loadedClip, error));
    }

    private void CompletePathLoad(
        int completedGeneration,
        int index,
        string audioPath,
        AudioFileFormatDescriptor? descriptor,
        AudioClip? loadedClip,
        string? error)
    {
        if (disposed || completedGeneration != generation)
        {
            return;
        }

        if (loadedClip == null)
        {
            Debug.LogWarning("[AudioArbiter] Sound load failed: provider=" + ProviderId
                + ", owner=" + OwnerModId
                + ", path=" + audioPath
                + ", format=" + (descriptor?.Format.ToString() ?? "Unknown")
                + ", failureCode=" + Display(descriptor?.FailureCode)
                + ", error=" + (error ?? "<none>"));
        }
        else
        {
            loadedClip.name = Path.GetFileNameWithoutExtension(audioPath);
            clips[index] = loadedClip;
            AuraSharedLog.Info("AudioArbiter", "Sound load ready: provider=" + ProviderId
                + ", owner=" + OwnerModId
                + ", path=" + audioPath
                + ", format=" + (descriptor?.Format.ToString() ?? "Unknown")
                + ", codec=" + (descriptor?.Codec ?? "Unknown")
                + ", clip=" + loadedClip.name
                + ", duration=" + loadedClip.length.ToString("0.000")
                + ", channels=" + loadedClip.channels
                + ", frequency=" + loadedClip.frequency);
        }

        pendingLoads = Math.Max(0, pendingLoads - 1);
        if (pendingLoads == 0)
        {
            loadState = HasLoadedClip() ? "Ready" : "Failed";
        }
    }

    private static string Display(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? "<none>" : value ?? "<none>";
    }

    private bool HasLoadedClip()
    {
        for (var i = 0; i < clips.Length; i++)
        {
            if (clips[i] != null)
            {
                return true;
            }
        }

        return false;
    }

    private static string[] BuildAudioPaths(string audioPath, string[]? variantAudioPaths)
    {
        var paths = new System.Collections.Generic.List<string>();
        var seen = new System.Collections.Generic.HashSet<string>(StringComparer.OrdinalIgnoreCase);
        AddPath(audioPath);
        foreach (var variantPath in variantAudioPaths ?? Array.Empty<string>())
        {
            AddPath(variantPath);
        }

        return paths.ToArray();

        void AddPath(string path)
        {
            var normalized = (path ?? "").Trim();
            if (normalized.Length > 0 && seen.Add(normalized))
            {
                paths.Add(normalized);
            }
        }
    }

    private sealed class ProviderRunner : MonoBehaviour
    {
        public void LoadAudio(
            string path,
            AudioType audioType,
            int generation,
            Action<int, AudioClip?, string?> onCompleted)
        {
            StartCoroutine(LoadAudioCoroutine(path, audioType, generation, onCompleted));
        }

        private static IEnumerator LoadAudioCoroutine(
            string path,
            AudioType audioType,
            int generation,
            Action<int, AudioClip?, string?> onCompleted)
        {
            var uri = new Uri(path).AbsoluteUri;
            using var request = UnityWebRequestMultimedia.GetAudioClip(uri, audioType);
            yield return request.SendWebRequest();

            if (request.result != UnityWebRequest.Result.Success)
            {
                onCompleted(generation, null, "type=" + audioType + ", result=" + request.result + ", error=" + request.error);
                yield break;
            }

            AudioClip? loadedClip = null;
            string? error = null;
            try
            {
                loadedClip = DownloadHandlerAudioClip.GetContent(request);
            }
            catch (Exception ex)
            {
                error = ex.Message;
            }

            if (loadedClip != null)
            {
                onCompleted(generation, loadedClip, null);
                yield break;
            }

            onCompleted(generation, null, "type=" + audioType + ", contentError=" + (error ?? "AudioClip is null"));
        }
    }
}
