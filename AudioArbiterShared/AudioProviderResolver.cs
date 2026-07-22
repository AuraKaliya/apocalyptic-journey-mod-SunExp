using System;
using System.Collections.Generic;

namespace AudioArbiter.Shared;

internal interface IAudioProviderCandidate<TResource>
    where TResource : class
{
    string ProviderId { get; }

    string OwnerModId { get; }

    string QualifiedProviderId { get; }

    int Priority { get; }

    bool HardClaim { get; }

    bool Evaluate(object request);

    string GetLoadState();

    TResource? GetResource(object request);
}

internal enum AudioProviderResolutionStatus
{
    None,
    Selected,
    HardClaimBlocked,
    IdentityMismatch
}

internal sealed class AudioProviderResolution<TProvider, TResource>
    where TProvider : class, IAudioProviderCandidate<TResource>
    where TResource : class
{
    public AudioProviderResolutionStatus Status { get; set; }

    public TProvider? Provider { get; set; }

    public TResource? Resource { get; set; }

    public bool StrictIdentityMatched { get; set; }

    public bool UsedLegacyFallback { get; set; }

    public bool ShouldWarnRemoteMismatch { get; set; }

    public bool HasTransientCandidate { get; set; }

    public bool HasSelection => Status == AudioProviderResolutionStatus.Selected
        && Provider != null
        && Resource != null;
}

internal static class AudioProviderResolver
{
    public static AudioProviderResolution<TProvider, TResource> Resolve<TProvider, TResource>(
        IReadOnlyList<TProvider> providers,
        object request,
        string requestedProviderId,
        string requestedOwnerModId,
        bool isRemote,
        Action<TProvider, string>? providerNotReady = null,
        Action<TProvider, TResource>? providerSelected = null)
        where TProvider : class, IAudioProviderCandidate<TResource>
        where TResource : class
    {
        var requestedId = (requestedProviderId ?? "").Trim();
        if (requestedId.Length == 0)
        {
            var unscoped = ResolveCandidates(
                providers,
                request,
                _ => true,
                providerNotReady,
                providerSelected);
            unscoped.StrictIdentityMatched = false;
            return unscoped;
        }

        var requestedOwner = (requestedOwnerModId ?? "").Trim();
        var hasOwnerScope = requestedOwner.Length > 0;
        var isQualifiedProviderId = requestedId.Contains(":");
        if (hasOwnerScope || isQualifiedProviderId)
        {
            var strict = ResolveCandidates(
                providers,
                request,
                provider => MatchesProviderRequest(
                    provider.ProviderId,
                    provider.OwnerModId,
                    provider.QualifiedProviderId,
                    requestedId,
                    requestedOwner,
                    ownerStrict: true),
                providerNotReady,
                providerSelected);
            if (strict.HasSelection || strict.StrictIdentityMatched || isRemote || isQualifiedProviderId)
            {
                if (!strict.HasSelection && !strict.StrictIdentityMatched && (isRemote || isQualifiedProviderId))
                {
                    strict.Status = AudioProviderResolutionStatus.IdentityMismatch;
                }

                strict.ShouldWarnRemoteMismatch = isRemote && !strict.HasSelection && !strict.StrictIdentityMatched;
                return strict;
            }

            var legacy = ResolveCandidates(
                providers,
                request,
                provider => MatchesProviderRequest(
                    provider.ProviderId,
                    provider.OwnerModId,
                    provider.QualifiedProviderId,
                    requestedId,
                    "",
                    ownerStrict: false),
                providerNotReady,
                providerSelected);
            legacy.UsedLegacyFallback = true;
            legacy.StrictIdentityMatched = false;
            return legacy;
        }

        var bare = ResolveCandidates(
            providers,
            request,
            provider => MatchesProviderRequest(
                provider.ProviderId,
                provider.OwnerModId,
                provider.QualifiedProviderId,
                requestedId,
                "",
                ownerStrict: false),
            providerNotReady,
            providerSelected);
        bare.StrictIdentityMatched = false;
        return bare;
    }

    public static string QualifyProviderId(string ownerModId, string providerId)
    {
        var owner = (ownerModId ?? "").Trim();
        var id = (providerId ?? "").Trim();
        if (string.IsNullOrWhiteSpace(id))
        {
            id = "unknown";
        }

        if (id.Contains(":") || string.IsNullOrWhiteSpace(owner))
        {
            return id;
        }

        return owner + ":" + id;
    }

    public static bool MatchesProviderRequest(
        string providerId,
        string ownerModId,
        string qualifiedProviderId,
        string requestedProviderId,
        string requestedOwnerModId,
        bool ownerStrict)
    {
        var request = (requestedProviderId ?? "").Trim();
        var owner = (requestedOwnerModId ?? "").Trim();
        if (request.Length == 0)
        {
            return true;
        }

        if (string.Equals(request, qualifiedProviderId, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (!string.Equals(request, providerId, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return !ownerStrict
            || owner.Length == 0
            || string.Equals(owner, ownerModId, StringComparison.OrdinalIgnoreCase);
    }

    public static int CompareProviderOrder(
        int leftPriority,
        string leftQualifiedProviderId,
        int rightPriority,
        string rightQualifiedProviderId)
    {
        var priority = rightPriority.CompareTo(leftPriority);
        return priority != 0
            ? priority
            : string.Compare(leftQualifiedProviderId, rightQualifiedProviderId, StringComparison.OrdinalIgnoreCase);
    }

    private static AudioProviderResolution<TProvider, TResource> ResolveCandidates<TProvider, TResource>(
        IReadOnlyList<TProvider> providers,
        object request,
        Func<TProvider, bool> matchesProvider,
        Action<TProvider, string>? providerNotReady,
        Action<TProvider, TResource>? providerSelected)
        where TProvider : class, IAudioProviderCandidate<TResource>
        where TResource : class
    {
        var result = new AudioProviderResolution<TProvider, TResource>();
        for (var i = 0; i < providers.Count; i++)
        {
            var provider = providers[i];
            if (!matchesProvider(provider))
            {
                continue;
            }

            result.StrictIdentityMatched = true;
            if (!provider.Evaluate(request))
            {
                continue;
            }

            var loadState = provider.GetLoadState();
            if (!string.Equals(loadState, "Ready", StringComparison.OrdinalIgnoreCase))
            {
                providerNotReady?.Invoke(provider, loadState);
                result.HasTransientCandidate |= AudioProviderLoadStatePolicy.IsTransient(loadState);
                if (provider.HardClaim)
                {
                    result.Status = AudioProviderResolutionStatus.HardClaimBlocked;
                    return result;
                }

                continue;
            }

            var resource = provider.GetResource(request);
            if (resource != null)
            {
                result.Status = AudioProviderResolutionStatus.Selected;
                result.Provider = provider;
                result.Resource = resource;
                providerSelected?.Invoke(provider, resource);
                return result;
            }

            if (provider.HardClaim)
            {
                result.Status = AudioProviderResolutionStatus.HardClaimBlocked;
                return result;
            }
        }

        return result;
    }

}

internal static class AudioProviderLoadStatePolicy
{
    public static bool IsTransient(string loadState)
    {
        var state = (loadState ?? "").Trim();
        return !string.Equals(state, "Ready", StringComparison.OrdinalIgnoreCase)
               && !string.Equals(state, "Failed", StringComparison.OrdinalIgnoreCase)
               && !string.Equals(state, "Missing", StringComparison.OrdinalIgnoreCase)
               && !string.Equals(state, "Disposed", StringComparison.OrdinalIgnoreCase);
    }
}

internal static class AudioProviderCooldownPolicy
{
    public static bool TryAcquire(
        IDictionary<string, float> cooldownUntil,
        string qualifiedProviderId,
        string kind,
        string roleId,
        string statusInstanceId,
        float cooldownSeconds,
        float now)
    {
        var key = BuildKey(qualifiedProviderId, kind, roleId, statusInstanceId);
        if (cooldownUntil.TryGetValue(key, out var until) && now < until)
        {
            return false;
        }

        if (cooldownSeconds > 0f)
        {
            cooldownUntil[key] = now + cooldownSeconds;
        }

        return true;
    }

    public static string BuildKey(
        string qualifiedProviderId,
        string kind,
        string roleId,
        string statusInstanceId)
    {
        return qualifiedProviderId + "|" + kind + "|" + roleId + "|" + statusInstanceId;
    }
}
