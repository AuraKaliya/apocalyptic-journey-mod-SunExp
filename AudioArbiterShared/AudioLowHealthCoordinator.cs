using System;
using System.Collections.Generic;

namespace AudioArbiter.Shared;

internal enum AudioLowHealthObservationOutcome
{
    Seeded,
    Increased,
    Unchanged,
    AlreadyAnnounced,
    MissingRoleIdentity,
    Candidate
}

internal readonly struct AudioLowHealthObservationDecision
{
    public AudioLowHealthObservationDecision(
        AudioLowHealthObservationOutcome outcome,
        float previousHpRatio,
        bool announcementReset = false)
    {
        Outcome = outcome;
        PreviousHpRatio = previousHpRatio;
        AnnouncementReset = announcementReset;
    }

    public AudioLowHealthObservationOutcome Outcome { get; }

    public float PreviousHpRatio { get; }

    public bool AnnouncementReset { get; }

    public bool ShouldRequest => Outcome == AudioLowHealthObservationOutcome.Candidate;
}

internal readonly struct AudioLowHealthProviderDescriptor
{
    public AudioLowHealthProviderDescriptor(string kind, float crossDownThreshold)
    {
        Kind = kind ?? "";
        CrossDownThreshold = crossDownThreshold;
    }

    public string Kind { get; }

    public float CrossDownThreshold { get; }
}

internal sealed class AudioLowHealthCoordinator
{
    internal const float DefaultNoProviderCooldownSeconds = 0.75f;
    internal const float DefaultRecoveryMargin = 0.05f;
    internal const float DefaultLegacyFallbackThreshold = 0.35f;

    private readonly float noProviderCooldownSeconds;
    private readonly float recoveryMargin;
    private readonly float legacyFallbackThreshold;
    private readonly HashSet<string> announcedStatusIds = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, float> lastHpRatioByStatus = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, float> noProviderUntil = new(StringComparer.OrdinalIgnoreCase);
    private ProviderIndex providerIndex = ProviderIndex.Empty;

    public AudioLowHealthCoordinator(
        float noProviderCooldownSeconds = DefaultNoProviderCooldownSeconds,
        float recoveryMargin = DefaultRecoveryMargin,
        float legacyFallbackThreshold = DefaultLegacyFallbackThreshold)
    {
        this.noProviderCooldownSeconds = Math.Max(0f, noProviderCooldownSeconds);
        this.recoveryMargin = Math.Max(0f, recoveryMargin);
        this.legacyFallbackThreshold = legacyFallbackThreshold;
    }

    public void ResetFight()
    {
        announcedStatusIds.Clear();
        lastHpRatioByStatus.Clear();
        noProviderUntil.Clear();
    }

    public void ConfigureProviders(IEnumerable<AudioLowHealthProviderDescriptor>? providers)
    {
        var hasUnknownProvider = false;
        var explicitCandidates = 0;
        var thresholdCandidates = 0;
        var lowestThreshold = -1f;
        var thresholds = new List<float>();
        if (providers != null)
        {
            foreach (var provider in providers)
            {
                if (string.IsNullOrWhiteSpace(provider.Kind))
                {
                    hasUnknownProvider = true;
                    continue;
                }

                if (!string.Equals(provider.Kind, SoundEventKinds.LowHealth, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                explicitCandidates++;
                if (provider.CrossDownThreshold < 0f)
                {
                    continue;
                }

                thresholdCandidates++;
                thresholds.Add(provider.CrossDownThreshold);
                lowestThreshold = lowestThreshold < 0f
                    ? provider.CrossDownThreshold
                    : Math.Min(lowestThreshold, provider.CrossDownThreshold);
            }
        }

        providerIndex = new ProviderIndex(
            hasUnknownProvider,
            explicitCandidates,
            thresholdCandidates,
            lowestThreshold,
            thresholds.ToArray());
        noProviderUntil.Clear();
    }

    public void Seed(AudioStatusSnapshot snapshot)
    {
        if (snapshot == null) throw new ArgumentNullException(nameof(snapshot));
        if (!string.IsNullOrWhiteSpace(snapshot.StatusInstanceId) && snapshot.HpRatio > 0f)
        {
            lastHpRatioByStatus[snapshot.StatusInstanceId] = snapshot.HpRatio;
        }
    }

    public AudioLowHealthObservationDecision Observe(AudioStatusSnapshot snapshot)
    {
        if (snapshot == null) throw new ArgumentNullException(nameof(snapshot));
        var statusId = snapshot.StatusInstanceId;
        var ratio = snapshot.HpRatio;
        if (!lastHpRatioByStatus.TryGetValue(statusId, out var previousRatio))
        {
            lastHpRatioByStatus[statusId] = ratio;
            return new AudioLowHealthObservationDecision(AudioLowHealthObservationOutcome.Seeded, ratio);
        }

        lastHpRatioByStatus[statusId] = ratio;
        if (ratio > previousRatio)
        {
            var resetAt = providerIndex.LowestThreshold >= 0f
                ? providerIndex.LowestThreshold + recoveryMargin
                : 0.5f;
            var reset = ratio >= resetAt && announcedStatusIds.Remove(statusId);
            return new AudioLowHealthObservationDecision(
                AudioLowHealthObservationOutcome.Increased,
                previousRatio,
                reset);
        }

        if (ratio >= previousRatio)
        {
            return new AudioLowHealthObservationDecision(
                AudioLowHealthObservationOutcome.Unchanged,
                previousRatio);
        }

        if (announcedStatusIds.Contains(statusId))
        {
            return new AudioLowHealthObservationDecision(
                AudioLowHealthObservationOutcome.AlreadyAnnounced,
                previousRatio);
        }

        if (string.IsNullOrWhiteSpace(snapshot.CareerId) && string.IsNullOrWhiteSpace(snapshot.RoleId))
        {
            return new AudioLowHealthObservationDecision(
                AudioLowHealthObservationOutcome.MissingRoleIdentity,
                previousRatio);
        }

        return new AudioLowHealthObservationDecision(
            AudioLowHealthObservationOutcome.Candidate,
            previousRatio);
    }

    public bool ShouldAttempt(SoundPlaybackRequest request)
    {
        if (!IsLowHealthRequest(request))
        {
            return false;
        }

        if (providerIndex.ExplicitCandidates > 0)
        {
            return providerIndex.ThresholdCandidates < providerIndex.ExplicitCandidates
                   || providerIndex.CrossedThreshold(request.PreviousHpRatio, request.HpRatio);
        }

        if (!providerIndex.HasUnknownProvider)
        {
            return false;
        }

        return request.PreviousHpRatio > legacyFallbackThreshold
               && request.HpRatio <= legacyFallbackThreshold;
    }

    public bool IsNoProviderSuppressed(SoundPlaybackRequest request, float currentTime)
    {
        if (!IsLowHealthRequest(request))
        {
            return false;
        }

        var key = BuildNoProviderKey(request);
        if (!noProviderUntil.TryGetValue(key, out var until))
        {
            return false;
        }

        if (currentTime < until)
        {
            return true;
        }

        noProviderUntil.Remove(key);
        return false;
    }

    public void RememberNoProvider(SoundPlaybackRequest request, float currentTime)
    {
        if (!IsLowHealthRequest(request))
        {
            return;
        }

        noProviderUntil[BuildNoProviderKey(request)] = currentTime + noProviderCooldownSeconds;
    }

    public void MarkAnnounced(string statusInstanceId)
    {
        if (!string.IsNullOrWhiteSpace(statusInstanceId))
        {
            announcedStatusIds.Add(statusInstanceId);
        }
    }

    private static bool IsLowHealthRequest(SoundPlaybackRequest? request)
    {
        return request != null
               && string.Equals(request.Kind, SoundEventKinds.LowHealth, StringComparison.OrdinalIgnoreCase);
    }

    private static string BuildNoProviderKey(SoundPlaybackRequest request)
    {
        var ratioBucket = (int)Math.Floor(request.HpRatio * 10f);
        ratioBucket = Math.Max(0, Math.Min(10, ratioBucket));
        return request.StatusInstanceId
               + "|"
               + request.RoleId
               + "|"
               + request.CareerId
               + "|"
               + ratioBucket;
    }

    private readonly struct ProviderIndex
    {
        public static readonly ProviderIndex Empty = new(false, 0, 0, -1f, Array.Empty<float>());

        public ProviderIndex(
            bool hasUnknownProvider,
            int explicitCandidates,
            int thresholdCandidates,
            float lowestThreshold,
            float[] thresholds)
        {
            HasUnknownProvider = hasUnknownProvider;
            ExplicitCandidates = explicitCandidates;
            ThresholdCandidates = thresholdCandidates;
            LowestThreshold = lowestThreshold;
            Thresholds = thresholds ?? Array.Empty<float>();
        }

        public bool HasUnknownProvider { get; }

        public int ExplicitCandidates { get; }

        public int ThresholdCandidates { get; }

        public float LowestThreshold { get; }

        private float[] Thresholds { get; }

        public bool CrossedThreshold(float previousRatio, float ratio)
        {
            for (var i = 0; i < Thresholds.Length; i++)
            {
                var threshold = Thresholds[i];
                if (previousRatio > threshold && ratio <= threshold)
                {
                    return true;
                }
            }

            return false;
        }
    }
}
