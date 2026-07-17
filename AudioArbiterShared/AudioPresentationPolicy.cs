using System;
using System.Collections.Generic;
using System.Linq;

namespace AudioArbiter.Shared;

internal enum AudioNativeEffectAction
{
    None,
    SuppressOriginal,
    ReplaceOriginalClip,
    PlayReplacementAfterDelay
}

internal sealed class AudioPresentationPlan
{
    public bool QueueNativeEffectReplacement { get; set; }

    public bool StartRemoteFallback { get; set; }

    public float PairingSeconds { get; set; }

    public string PendingOutcome { get; set; } = "";
}

internal static class AudioPresentationPolicy
{
    public const float VolumeIdentityTolerance = 0.001f;

    public static AudioPresentationPlan CreatePlan(
        string bus,
        string policy,
        string kind,
        bool isRemote,
        float remotePairingSeconds,
        float localPairingSeconds)
    {
        var queueReplacement = string.Equals(bus, SoundBuses.Effect, StringComparison.OrdinalIgnoreCase)
            && IsReplacementPolicy(policy)
            && string.Equals(kind, SoundEventKinds.CardUse, StringComparison.OrdinalIgnoreCase);
        return new AudioPresentationPlan
        {
            QueueNativeEffectReplacement = queueReplacement,
            StartRemoteFallback = queueReplacement && isRemote,
            PairingSeconds = isRemote ? remotePairingSeconds : localPairingSeconds,
            PendingOutcome = isRemote ? "remote-pair-pending" : "local-pair-pending"
        };
    }

    public static bool IsReplacementPolicy(string policy)
    {
        return string.Equals(policy, SoundPolicies.Replace, StringComparison.OrdinalIgnoreCase)
            || string.Equals(policy, SoundPolicies.ReplaceOriginal, StringComparison.OrdinalIgnoreCase)
            || string.Equals(policy, SoundPolicies.SuppressOriginal, StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsVocalBus(string bus)
    {
        return string.Equals(bus, SoundBuses.Vocal, StringComparison.OrdinalIgnoreCase);
    }

    public static string ResolveVocalRoleId(
        string statusInstanceId,
        string roleId,
        string careerId,
        string ownerModId,
        string providerId)
    {
        var resolved = !string.IsNullOrWhiteSpace(statusInstanceId)
            ? statusInstanceId
            : string.IsNullOrWhiteSpace(roleId)
                ? careerId
                : roleId;
        return string.IsNullOrWhiteSpace(resolved)
            ? ownerModId + "." + providerId
            : resolved;
    }

    public static AudioNativeEffectAction ResolveNativeEffectAction(
        string policy,
        bool hasReplacement,
        float volumeMultiplier)
    {
        if (!hasReplacement)
        {
            return AudioNativeEffectAction.None;
        }

        if (string.Equals(policy, SoundPolicies.SuppressOriginal, StringComparison.OrdinalIgnoreCase))
        {
            return AudioNativeEffectAction.SuppressOriginal;
        }

        return UsesCustomVolume(volumeMultiplier)
            ? AudioNativeEffectAction.PlayReplacementAfterDelay
            : AudioNativeEffectAction.ReplaceOriginalClip;
    }

    public static bool UsesCustomVolume(float volumeMultiplier)
    {
        return Math.Abs(volumeMultiplier - 1f) >= VolumeIdentityTolerance;
    }
}

internal static class AudioSuppressionPolicy
{
    public static void ArmNarrationSuppressions(
        IDictionary<int, float> suppressUntil,
        IEnumerable<int> narrationIds,
        float now,
        float durationSeconds)
    {
        var until = now + durationSeconds;
        foreach (var id in narrationIds)
        {
            suppressUntil[id] = until;
        }
    }

    public static bool ShouldSuppressNarration(
        IDictionary<int, float> suppressUntil,
        IEnumerable<int>? narrationIds,
        float now)
    {
        var ids = narrationIds?.ToArray() ?? Array.Empty<int>();
        if (ids.Length == 0 || suppressUntil.Count == 0)
        {
            return false;
        }

        var shouldSuppress = ids.Any(id => suppressUntil.TryGetValue(id, out var until) && now <= until);
        foreach (var expired in suppressUntil
                     .Where(item => now > item.Value)
                     .Select(item => item.Key)
                     .ToArray())
        {
            suppressUntil.Remove(expired);
        }

        return shouldSuppress;
    }
}
