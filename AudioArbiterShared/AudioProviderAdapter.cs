using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using AuraShared.Core;
using UnityEngine;

namespace AudioArbiter.Shared;

internal sealed class SoundProviderHandle : IAudioProviderCandidate<AudioClip>
{
    private readonly object provider;
    private readonly Type providerType;

    public SoundProviderHandle(object provider)
    {
        this.provider = provider ?? throw new ArgumentNullException(nameof(provider));
        providerType = provider.GetType();
        ProviderId = ReadString("ProviderId", providerType.FullName ?? "");
        OwnerModId = ReadString("OwnerModId", "");
        if (string.IsNullOrWhiteSpace(OwnerModId))
        {
            OwnerModId = providerType.Assembly.GetName().Name ?? "";
        }

        QualifiedProviderId = AudioProviderResolver.QualifyProviderId(OwnerModId, ProviderId);
        Kind = ReadString("Kind", "");
        LowHealthCrossDownThreshold = ReadFloat("LowHealthCrossDownThreshold", -1f);
        Priority = ReadInt("Priority", 0);
        HardClaim = ReadBool("HardClaim", false);
        Sync = ReadBool("Sync", true);
        CooldownSeconds = ReadFloat("CooldownSeconds", 0f);
        GainDb = ReadFloat("GainDb", 0f);
        VolumeMultiplier = Math.Max(0f, ReadFloat("VolumeMultiplier", 1f)) * Mathf.Pow(10f, GainDb / 20f);
        Bus = ReadString("Bus", SoundBuses.Effect);
        Policy = ReadString("Policy", SoundPolicies.Additive);
        SuppressVocalStates = SplitString(ReadString("SuppressVocalStates", ""));
        SuppressNarrationIds = SplitInts(ReadString("SuppressNarrationIds", ""));
    }

    public string ProviderId { get; }

    public string OwnerModId { get; }

    public string QualifiedProviderId { get; }

    public string Kind { get; }

    public float LowHealthCrossDownThreshold { get; }

    public int Priority { get; }

    public bool HardClaim { get; }

    public bool Sync { get; }

    public float CooldownSeconds { get; }

    public float GainDb { get; }

    public float VolumeMultiplier { get; }

    public string Bus { get; }

    public string Policy { get; }

    public HashSet<string> SuppressVocalStates { get; }

    public HashSet<int> SuppressNarrationIds { get; }

    public bool Evaluate(object request)
    {
        return InvokeBool("Evaluate", request, true);
    }

    public bool MatchesProviderId(string requestedProviderId)
    {
        return MatchesProviderRequest(requestedProviderId, "", ownerStrict: false);
    }

    public bool MatchesProviderRequest(string requestedProviderId, string requestedOwnerModId, bool ownerStrict)
    {
        return AudioProviderResolver.MatchesProviderRequest(
            ProviderId,
            OwnerModId,
            QualifiedProviderId,
            requestedProviderId,
            requestedOwnerModId,
            ownerStrict);
    }

    public string GetLoadState()
    {
        return InvokeString("GetLoadState", "Disabled");
    }

    public bool Preload()
    {
        try
        {
            var method = providerType.GetMethod("Preload", BindingFlags.Instance | BindingFlags.Public);
            if (method == null)
            {
                return false;
            }

            method.Invoke(provider, Array.Empty<object>());
            return true;
        }
        catch (Exception ex)
        {
            Debug.LogWarning("[AudioArbiter] Provider preload failed: " + ProviderId + " -> " + ex.Message);
            return false;
        }
    }

    public AudioClip? GetClip(object request)
    {
        try
        {
            var method = providerType.GetMethod("GetClip", BindingFlags.Instance | BindingFlags.Public);
            if (method == null)
            {
                return null;
            }

            var parameters = method.GetParameters();
            return parameters.Length == 0
                ? method.Invoke(provider, Array.Empty<object>()) as AudioClip
                : method.Invoke(provider, new[] { request }) as AudioClip;
        }
        catch (Exception ex)
        {
            Debug.LogWarning("[AudioArbiter] Provider GetClip failed: " + ProviderId + " -> " + ex.Message);
            return null;
        }
    }

    AudioClip? IAudioProviderCandidate<AudioClip>.GetResource(object request)
    {
        return GetClip(request);
    }

    public string Describe()
    {
        return "providerId=" + ProviderId
               + ", qualifiedProviderId=" + QualifiedProviderId
               + ", owner=" + OwnerModId
               + ", priority=" + Priority
               + ", bus=" + Bus
               + ", policy=" + Policy
               + ", hardClaim=" + HardClaim
               + ", sync=" + Sync
               + ", gainDb=" + GainDb.ToString("0.##")
               + ", volumeMultiplier=" + VolumeMultiplier.ToString("0.###")
               + ", suppressNarrationIds=" + string.Join("|", SuppressNarrationIds);
    }

    public void Dispose(string reason)
    {
        try
        {
            if (provider is IDisposable disposable)
            {
                disposable.Dispose();
                AuraSharedLog.DebugLog("AudioArbiter", "Sound provider disposed: " + ProviderId + ", reason=" + reason, false);
                return;
            }

            providerType.GetMethod("Dispose", BindingFlags.Instance | BindingFlags.Public, null, Type.EmptyTypes, null)
                ?.Invoke(provider, Array.Empty<object>());
        }
        catch (Exception ex)
        {
            Debug.LogWarning("[AudioArbiter] Sound provider dispose failed: " + ProviderId + " -> " + ex.Message);
        }
    }

    private string ReadString(string propertyName, string fallback)
    {
        return AudioPropertyReader.ReadString(provider, propertyName, fallback);
    }

    private int ReadInt(string propertyName, int fallback)
    {
        return AudioPropertyReader.ReadInt(provider, propertyName, fallback);
    }

    private bool ReadBool(string propertyName, bool fallback)
    {
        return AudioPropertyReader.ReadBool(provider, propertyName, fallback);
    }

    private float ReadFloat(string propertyName, float fallback)
    {
        return AudioPropertyReader.ReadFloat(provider, propertyName, fallback);
    }

    private static HashSet<string> SplitString(string value)
    {
        return new HashSet<string>(
            (value ?? "")
            .Split(new[] { ',', ';', '|' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(item => item.Trim())
            .Where(item => item.Length > 0),
            StringComparer.OrdinalIgnoreCase);
    }

    private static HashSet<int> SplitInts(string value)
    {
        var result = new HashSet<int>();
        foreach (var item in (value ?? "").Split(new[] { ',', ';', '|' }, StringSplitOptions.RemoveEmptyEntries))
        {
            if (int.TryParse(item.Trim(), out var id))
            {
                result.Add(id);
            }
        }

        return result;
    }

    private bool InvokeBool(string methodName, object arg, bool fallback)
    {
        try
        {
            var method = providerType.GetMethod(methodName, BindingFlags.Instance | BindingFlags.Public);
            if (method == null)
            {
                return fallback;
            }

            return method.Invoke(provider, new[] { arg }) is bool value ? value : fallback;
        }
        catch (Exception ex)
        {
            Debug.LogWarning("[AudioArbiter] Provider " + methodName + " failed: " + ProviderId + " -> " + ex.Message);
            return false;
        }
    }

    private string InvokeString(string methodName, string fallback)
    {
        try
        {
            var method = providerType.GetMethod(methodName, BindingFlags.Instance | BindingFlags.Public);
            return method?.Invoke(provider, Array.Empty<object>()) as string ?? fallback;
        }
        catch
        {
            return fallback;
        }
    }
}

internal readonly struct ResolvedSound
{
    public ResolvedSound(SoundProviderHandle provider, AudioClip clip)
    {
        Provider = provider;
        Clip = clip;
    }

    public SoundProviderHandle Provider { get; }

    public AudioClip Clip { get; }
}
