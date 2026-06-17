using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using SanGuoShaExp.Dll.Infrastructure;
using UnityEngine;
using UnityEngine.Audio;
using UnityEngine.Networking;
using Witch.Mod;

namespace SanGuoShaExp.Dll.GameApi;

public static class AudioApi
{
    private const string AudioDirectory = "ModResource/audio";
    private const string QixingKey = "qixing";
    private const string GaleKey = "gale";
    private const string MistKey = "mist";
    private const float DefaultVolumeGain = 2.5f;

    private static readonly Dictionary<string, AudioClip> Clips = new(StringComparer.Ordinal);
    private static AudioLoader? loader;
    private static AudioSource? effectSource;
    private static string? loadedModDirectory;
    private static bool loadStarted;
    private static bool effectMixerResolved;

    public static void Initialize(ModConfig modConfig)
    {
        if (modConfig == null)
        {
            SanGuoShaExpLog.Warn("Audio initialization skipped: mod config is null");
            return;
        }

        if (loadStarted && string.Equals(loadedModDirectory, modConfig.DirectoryName, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        loadedModDirectory = modConfig.DirectoryName;
        loadStarted = true;
        Clips.Clear();

        var root = Path.Combine(modConfig.DirectoryName, AudioDirectory);
        var gameObject = new GameObject("SanGuoShaExp.AudioLoader");
        UnityEngine.Object.DontDestroyOnLoad(gameObject);
        loader = gameObject.AddComponent<AudioLoader>();
        effectSource = gameObject.AddComponent<AudioSource>();
        effectSource.playOnAwake = false;
        effectMixerResolved = false;

        LoadClip(QixingKey, Path.Combine(root, "\u4e03\u661f.mp3"));
        LoadClip(GaleKey, Path.Combine(root, "\u72c2\u98ce.mp3"));
        LoadClip(MistKey, Path.Combine(root, "\u5927\u96fe.mp3"));
    }

    public static void PlayQixing()
    {
        Play(QixingKey, "Seven Stars");
    }

    public static void PlayRandomWindMist()
    {
        Play(UnityEngine.Random.Range(0, 2) == 0 ? GaleKey : MistKey, "Gale or Great Fog");
    }

    private static void LoadClip(string key, string path)
    {
        if (loader == null)
        {
            SanGuoShaExpLog.Warn("Audio loader is missing, skipped: " + key);
            return;
        }

        if (!File.Exists(path))
        {
            SanGuoShaExpLog.Warn("Audio file not found: " + path);
            return;
        }

        loader.Load(path, loadedClip =>
        {
            if (loadedClip == null)
            {
                SanGuoShaExpLog.Warn("Audio load failed: " + path);
                return;
            }

            Clips[key] = loadedClip;
            SanGuoShaExpLog.Info("Audio loaded: " + key + " -> " + loadedClip.name + ", gain=" + DefaultVolumeGain.ToString("0.##") + "x");
        });
    }

    private static void Play(string key, string label)
    {
        try
        {
            if (!Clips.TryGetValue(key, out var clip) || clip == null)
            {
                SanGuoShaExpLog.Debug("Audio not ready, skipped: " + label);
                return;
            }

            if (effectSource != null)
            {
                TryAttachEffectMixerGroup();
                effectSource.PlayOneShot(clip, DefaultVolumeGain);
            }
            else
            {
                AudioManager.Instance?.PlayEffect(clip);
            }
        }
        catch (Exception ex)
        {
            SanGuoShaExpLog.Warn("Audio play failed: " + label + ", error=" + ex.Message);
        }
    }

    private static void TryAttachEffectMixerGroup()
    {
        if (effectSource == null || effectMixerResolved)
        {
            return;
        }

        effectMixerResolved = true;
        try
        {
            var manager = AudioManager.Instance;
            var group = manager?.GetType()
                .GetField("effectGroup", BindingFlags.Instance | BindingFlags.NonPublic)
                ?.GetValue(manager) as AudioMixerGroup;
            if (group != null)
            {
                effectSource.outputAudioMixerGroup = group;
            }
        }
        catch (Exception ex)
        {
            SanGuoShaExpLog.Debug("Audio effect mixer attach skipped: " + ex.Message);
        }
    }

    private sealed class AudioLoader : MonoBehaviour
    {
        public void Load(string path, Action<AudioClip?> onLoaded)
        {
            StartCoroutine(LoadCoroutine(path, onLoaded));
        }

        private static IEnumerator LoadCoroutine(string path, Action<AudioClip?> onLoaded)
        {
            var uri = new Uri(path).AbsoluteUri;
            using var request = UnityWebRequestMultimedia.GetAudioClip(uri, AudioType.MPEG);
            yield return request.SendWebRequest();

            if (request.result != UnityWebRequest.Result.Success)
            {
                SanGuoShaExpLog.Warn("Audio request failed: " + Path.GetFileName(path) + ", error=" + request.error);
                onLoaded(null);
                yield break;
            }

            var clip = DownloadHandlerAudioClip.GetContent(request);
            if (clip != null)
            {
                clip.name = Path.GetFileNameWithoutExtension(path);
            }

            onLoaded(clip);
        }
    }
}
