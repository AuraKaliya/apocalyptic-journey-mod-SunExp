using System;
using System.Collections.Generic;

namespace AudioArbiter.Shared;

internal sealed class AudioPendingReplacement<TResource>
    where TResource : class
{
    public TResource? Resource { get; set; }

    public string Policy { get; set; } = "";

    public float VolumeMultiplier { get; set; }

    public float UntilTime { get; set; }

    public int Remaining { get; set; }

    public string EventId { get; set; } = "";

    public string CardId { get; set; } = "";

    public string RoleId { get; set; } = "";

    public string ProviderId { get; set; } = "";

    public bool IsRemote { get; set; }

    public bool FallbackAlreadyPlayed { get; set; }

    public AudioPendingReplacement<TResource>? ConsumeOne()
    {
        var next = Remaining - 1;
        return next <= 0
            ? null
            : new AudioPendingReplacement<TResource>
            {
                Resource = Resource,
                Policy = Policy,
                VolumeMultiplier = VolumeMultiplier,
                UntilTime = UntilTime,
                Remaining = next,
                EventId = EventId,
                CardId = CardId,
                RoleId = RoleId,
                ProviderId = ProviderId,
                IsRemote = IsRemote,
                FallbackAlreadyPlayed = FallbackAlreadyPlayed
            };
    }
}

internal sealed class AudioNativeEffectDecision<TResource>
    where TResource : class
{
    public bool Handled { get; set; }

    public AudioNativeEffectAction Action { get; set; }

    public AudioPendingReplacement<TResource>? Pending { get; set; }

    public string RemoteOutcome { get; set; } = "";
}

internal sealed class AudioReplacementCoordinator<TResource>
    where TResource : class
{
    private readonly HashSet<string> pairedRemoteReplacementIds = new(StringComparer.Ordinal);
    private AudioPendingReplacement<TResource>? pending;

    public int PairedRemoteCount => pairedRemoteReplacementIds.Count;

    public AudioPendingReplacement<TResource>? Pending => pending;

    public void Arm(
        TResource? resource,
        string policy,
        float volumeMultiplier,
        float untilTime,
        string eventId,
        string cardId,
        string roleId,
        string providerId,
        bool isRemote,
        bool fallbackAlreadyPlayed,
        int remaining = 1)
    {
        pending = new AudioPendingReplacement<TResource>
        {
            Resource = resource,
            Policy = policy ?? "",
            VolumeMultiplier = volumeMultiplier,
            UntilTime = untilTime,
            Remaining = remaining,
            EventId = eventId ?? "",
            CardId = cardId ?? "",
            RoleId = roleId ?? "",
            ProviderId = providerId ?? "",
            IsRemote = isRemote,
            FallbackAlreadyPlayed = fallbackAlreadyPlayed
        };
    }

    public bool HasActivePending(float now)
    {
        if (pending == null || now > pending.UntilTime || pending.Remaining <= 0)
        {
            pending = null;
            return false;
        }

        return true;
    }

    public AudioNativeEffectDecision<TResource> ConsumeNativeEffect(float now)
    {
        if (!HasActivePending(now) || pending == null)
        {
            return new AudioNativeEffectDecision<TResource>();
        }

        var current = pending;
        var action = AudioPresentationPolicy.ResolveNativeEffectAction(
            current.Policy,
            current.Resource != null,
            current.VolumeMultiplier);
        var outcome = "";
        if (current.IsRemote)
        {
            if (!current.FallbackAlreadyPlayed && !string.IsNullOrWhiteSpace(current.EventId))
            {
                pairedRemoteReplacementIds.Add(current.EventId);
            }

            outcome = current.FallbackAlreadyPlayed ? "fallback-original-suppressed" : "paired-native";
        }

        pending = current.ConsumeOne();
        return new AudioNativeEffectDecision<TResource>
        {
            Handled = true,
            Action = action,
            Pending = current,
            RemoteOutcome = outcome
        };
    }

    public bool TryClaimPairedFallback(string eventId)
    {
        return pairedRemoteReplacementIds.Remove(eventId ?? "");
    }

    public void ClearPendingForEvent(string eventId)
    {
        if (pending != null && string.Equals(pending.EventId, eventId, StringComparison.Ordinal))
        {
            pending = null;
        }
    }

    public void ClearPairingClaims()
    {
        pairedRemoteReplacementIds.Clear();
    }

    public void Clear()
    {
        pending = null;
        pairedRemoteReplacementIds.Clear();
    }
}
