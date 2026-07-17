using System;
using System.Collections;
using System.IO;
using AuraShared.Core;
using UnityEngine;
using UnityEngine.Networking;

namespace AudioArbiter.Shared;

public sealed class FileSoundProvider : IDisposable
{
    private readonly Func<object?, bool>? condition;
    private readonly string audioPath;
    private readonly ProviderRunner runner;
    private AudioClip? clip;
    private string loadState = "NotStarted";
    private int generation;
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
        this.audioPath = audioPath;
        this.condition = condition;

        var gameObject = new GameObject("AudioProvider." + ownerModId + "." + providerId);
        UnityEngine.Object.DontDestroyOnLoad(gameObject);
        runner = gameObject.AddComponent<ProviderRunner>();
        StartLoad();
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
        return condition == null || condition(request);
    }

    public string GetLoadState()
    {
        return loadState;
    }

    public AudioClip? GetClip(object? request)
    {
        return clip;
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

        clip = null;
        loadState = "Disposed";
    }

    private void StartLoad()
    {
        generation++;
        var currentGeneration = generation;
        if (!File.Exists(audioPath))
        {
            loadState = "Missing";
            Debug.LogWarning("[AudioArbiter] Sound file missing: provider=" + ProviderId + ", path=" + audioPath);
            return;
        }

        if (AudioFileLoadPolicy.Classify(audioPath) == AudioFileEncoding.UnsupportedVideoContainer)
        {
            loadState = "Unsupported";
            Debug.LogWarning("[AudioArbiter] Sound file uses a video container and will not be loaded as AudioClip. "
                + "Export the audio track as .mp3, .wav, or .ogg. provider=" + ProviderId + ", path=" + audioPath);
            return;
        }

        loadState = "Loading";
        runner.LoadAudio(audioPath, currentGeneration, (completedGeneration, loadedClip, error) =>
        {
            if (disposed || completedGeneration != generation)
            {
                return;
            }

            if (loadedClip == null)
            {
                loadState = "Failed";
                Debug.LogWarning("[AudioArbiter] Sound load failed: provider=" + ProviderId + ", error=" + (error ?? "<none>"));
                return;
            }

            loadedClip.name = Path.GetFileNameWithoutExtension(audioPath);
            clip = loadedClip;
            loadState = "Ready";
            AuraSharedLog.DebugLog("AudioArbiter", "Sound loaded: provider=" + ProviderId + ", clip=" + loadedClip.name, false);
        });
    }

    private sealed class ProviderRunner : MonoBehaviour
    {
        public void LoadAudio(string path, int generation, Action<int, AudioClip?, string?> onCompleted)
        {
            StartCoroutine(LoadAudioCoroutine(path, generation, onCompleted));
        }

        private static IEnumerator LoadAudioCoroutine(string path, int generation, Action<int, AudioClip?, string?> onCompleted)
        {
            var uri = new Uri(path).AbsoluteUri;
            string? lastError = null;
            foreach (var audioType in ResolveAudioTypes(path))
            {
                using var request = UnityWebRequestMultimedia.GetAudioClip(uri, audioType);
                yield return request.SendWebRequest();

                if (request.result != UnityWebRequest.Result.Success)
                {
                    lastError = "type=" + audioType + ", result=" + request.result + ", error=" + request.error;
                    continue;
                }

                AudioClip? loadedClip = null;
                string? error = null;
                try
                {
                    loadedClip = DownloadHandlerAudioClip.GetContent(request);
                }
                catch (Exception ex)
                {
                    error = ex.ToString();
                }

                if (loadedClip != null)
                {
                    onCompleted(generation, loadedClip, null);
                    yield break;
                }

                lastError = "type=" + audioType + ", contentError=" + (error ?? "AudioClip is null");
            }

            onCompleted(generation, null, lastError);
        }

        private static AudioType[] ResolveAudioTypes(string path)
        {
            switch (AudioFileLoadPolicy.Classify(path))
            {
                case AudioFileEncoding.Wav:
                    return new[] { AudioType.WAV };
                case AudioFileEncoding.OggVorbis:
                    return new[] { AudioType.OGGVORBIS };
                case AudioFileEncoding.Mpeg:
                case AudioFileEncoding.UnsupportedVideoContainer:
                default:
                    return new[] { AudioType.MPEG };
            }
        }
    }
}
