using System;
using System.Reflection;
using UnityEngine;
using Witch.Core;

namespace AudioArbiter.Shared;

internal static class AudioUnityPlaybackService
{
    public static void PlayVocal(string roleId, AudioClip clip, float volumeMultiplier)
    {
        var manager = AudioManager.Instance;
        if (manager == null || clip == null)
        {
            return;
        }

        if (!AudioPresentationPolicy.UsesCustomVolume(volumeMultiplier))
        {
            manager.PlayVocal(roleId, clip);
            return;
        }

        var source = GetOrCreateVocalSource(manager, roleId);
        if (source == null)
        {
            manager.PlayVocal(roleId, clip);
            return;
        }

        source.Stop();
        source.clip = clip;
        source.volume = ResolveManagerVolume(manager, "NarrationVolume");
        source.PlayOneShot(clip, volumeMultiplier);
    }

    public static void PlayEffect(AudioClip clip, float volumeMultiplier)
    {
        var manager = AudioManager.Instance;
        if (manager == null || clip == null)
        {
            return;
        }

        if (!AudioPresentationPolicy.UsesCustomVolume(volumeMultiplier))
        {
            manager.PlayEffect(clip);
            return;
        }

        var source = ReadMember(manager, "effectSource") as AudioSource;
        if (source == null)
        {
            manager.PlayEffect(clip);
            return;
        }

        source.PlayOneShot(clip, ResolveManagerVolume(manager, "EffectVolume") * volumeMultiplier);
    }

    public static void StopVocalSource(string roleId)
    {
        try
        {
            var manager = AudioManager.Instance;
            if (manager == null || string.IsNullOrWhiteSpace(roleId))
            {
                return;
            }

            var sources = ReadMember(manager, "_vocalSources") as System.Collections.IDictionary;
            if (sources != null && sources.Contains(roleId) && sources[roleId] is AudioSource source)
            {
                source.Stop();
            }
        }
        catch
        {
        }
    }

    private static AudioSource? GetOrCreateVocalSource(AudioManager manager, string roleId)
    {
        var sources = ReadMember(manager, "_vocalSources") as System.Collections.IDictionary;
        if (sources == null)
        {
            return null;
        }

        if (sources.Contains(roleId) && sources[roleId] is AudioSource existing)
        {
            return existing;
        }

        var source = manager.gameObject.AddComponent<AudioSource>();
        var vocalGroup = ReadMember(manager, "vocalGroup") as UnityEngine.Audio.AudioMixerGroup;
        if (vocalGroup != null)
        {
            source.outputAudioMixerGroup = vocalGroup;
        }

        sources[roleId] = source;
        return source;
    }

    private static float ResolveManagerVolume(AudioManager manager, string volumeField)
    {
        var volume = ReadFloatMember(manager, volumeField, 1f);
        if (ReadMember(manager, "audioMixer") != null)
        {
            return volume;
        }

        return volume * ReadFloatMember(manager, "masterVolume", 1f);
    }

    private static object? ReadMember(object target, string name)
    {
        try
        {
            var type = target.GetType();
            var field = type.GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (field != null)
            {
                return field.GetValue(target);
            }

            var property = type.GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            return property?.GetValue(target);
        }
        catch
        {
            return null;
        }
    }

    private static float ReadFloatMember(object target, string name, float fallback)
    {
        try
        {
            var value = ReadMember(target, name);
            if (value is float typed)
            {
                return typed;
            }

            return float.TryParse(value?.ToString(), out var parsed) ? parsed : fallback;
        }
        catch
        {
            return fallback;
        }
    }
}
