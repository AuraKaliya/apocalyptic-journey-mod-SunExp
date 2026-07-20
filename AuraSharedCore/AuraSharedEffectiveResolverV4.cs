using System;
using System.Collections.Generic;
using System.Linq;

namespace AuraShared.Core;

public static class AuraSharedEffectiveResolverV4
{
    public static AuraSharedEffectiveResolutionV4 Resolve(
        AuraSharedScopeKey scope,
        IEnumerable<AuraSharedRegistrationManifestV4> activeManifests,
        AuraSharedLocalOverrideV4? localOverride,
        Func<string, AuraSharedResourceDeclarationV4, AuraSharedResourceResolutionV4> resolveResource,
        long revision)
    {
        scope ??= new AuraSharedScopeKey();
        scope.Normalize();
        var manifests = (activeManifests ?? Array.Empty<AuraSharedRegistrationManifestV4>()).ToList();
        var profiles = manifests.SelectMany(manifest => manifest.Defaults
                .Where(profile => profile.Scope.Equals(scope))
                .Select(profile => new ProfileCandidate(manifest, profile)))
            .OrderByDescending(candidate => ConfigRank(candidate.Manifest.ParticipantKind))
            .ThenByDescending(candidate => candidate.Profile.Priority)
            .ThenBy(candidate => candidate.Manifest.OwnerModId, StringComparer.OrdinalIgnoreCase)
            .ThenBy(candidate => candidate.Profile.ProfileId, StringComparer.OrdinalIgnoreCase)
            .ToList();
        var selectedProfile = profiles.FirstOrDefault();
        var result = new AuraSharedEffectiveResolutionV4
        {
            ScopeKey = scope.Key,
            Revision = Math.Max(0, revision),
            Enabled = localOverride?.Enabled ?? selectedProfile?.Profile.Enabled ?? true,
            ConfigSource = localOverride != null ? "LocalUser" : SourceName(selectedProfile?.Manifest.ParticipantKind),
            ConfigOwnerModId = localOverride != null ? "LocalUser" : selectedProfile?.Manifest.OwnerModId ?? "ModuleDefault",
            ProfileId = localOverride != null ? "aura.user" : selectedProfile?.Profile.ProfileId ?? "module-default",
            SelectionMode = AuraSharedSelectionModes.Normalize(localOverride?.SelectionMode ?? AuraSharedSelectionModes.Priority),
            Values = MergeValues(selectedProfile?.Profile.Values, localOverride?.Values)
        };
        if (!result.Enabled)
        {
            result.Outcome = "Disabled";
            result.Fallback = "None";
            return result;
        }

        var resources = manifests.SelectMany(manifest => manifest.Resources
                .Where(resource => resource.Scope.Equals(scope) && !resource.Archived)
                .Select(resource => new ResourceCandidate(manifest, resource)))
            .Where(candidate => IsEnabled(candidate, localOverride))
            .OrderByDescending(candidate => candidate.Resource.Priority)
            .ThenBy(candidate => candidate.Manifest.OwnerModId, StringComparer.OrdinalIgnoreCase)
            .ThenBy(candidate => candidate.Resource.ResourceId, StringComparer.OrdinalIgnoreCase)
            .ToList();
        var available = new List<(ResourceCandidate Candidate, AuraSharedResourceResolutionV4 Resolution)>();
        foreach (var candidate in resources)
        {
            var resolution = resolveResource(candidate.Manifest.OwnerModId, candidate.Resource);
            if (resolution.Success) available.Add((candidate, resolution));
        }

        var selected = Select(available, result.SelectionMode, scope.Key, revision);
        result.Resources = selected.Select(item => new AuraSharedEffectiveResourceV4
        {
            OwnerModId = item.Candidate.Manifest.OwnerModId,
            ResourceId = item.Candidate.Resource.ResourceId,
            ResourcePath = item.Resolution.ResolvedPath,
            OriginKind = item.Candidate.Resource.OriginKind,
            Priority = item.Candidate.Resource.Priority
        }).ToList();
        var first = result.Resources.FirstOrDefault();
        if (first != null)
        {
            result.ResourceOwnerModId = first.OwnerModId;
            result.ResourceId = first.ResourceId;
            result.ResourcePath = first.ResourcePath;
            result.Outcome = "Resolved";
            result.Fallback = "None";
            return result;
        }

        var missingPolicy = resources.FirstOrDefault()?.Resource.MissingPolicy
                            ?? AuraSharedMissingPolicies.Skip;
        result.Outcome = "Unavailable";
        result.Fallback = missingPolicy;
        return result;
    }

    private static bool IsEnabled(ResourceCandidate candidate, AuraSharedLocalOverrideV4? localOverride)
    {
        var enabled = candidate.Resource.DefaultEnabled;
        if (localOverride?.ResourceOverrides == null) return enabled;
        var qualified = candidate.Resource.ModuleId + "/" + candidate.Resource.ScopeType + "/"
                        + candidate.Resource.ScopeId + "/" + candidate.Resource.FeatureId + "/"
                        + candidate.Manifest.OwnerModId + "/" + candidate.Resource.ResourceId;
        var shortId = candidate.Manifest.OwnerModId + ":" + candidate.Resource.ResourceId;
        if (localOverride.ResourceOverrides.TryGetValue(qualified, out var qualifiedValue)) return qualifiedValue;
        return localOverride.ResourceOverrides.TryGetValue(shortId, out var shortValue) ? shortValue : enabled;
    }

    private static List<(ResourceCandidate Candidate, AuraSharedResourceResolutionV4 Resolution)> Select(
        List<(ResourceCandidate Candidate, AuraSharedResourceResolutionV4 Resolution)> available,
        string selectionMode,
        string scopeKey,
        long revision)
    {
        if (available.Count == 0) return available;
        if (selectionMode == AuraSharedSelectionModes.All) return available;
        if (selectionMode == AuraSharedSelectionModes.Sequential)
        {
            return new List<(ResourceCandidate, AuraSharedResourceResolutionV4)>
            {
                available[(int)(Math.Abs(revision) % available.Count)]
            };
        }
        if (selectionMode == AuraSharedSelectionModes.Random)
        {
            var seed = StringComparer.OrdinalIgnoreCase.GetHashCode(scopeKey ?? "") ^ revision.GetHashCode();
            var index = new Random(seed).Next(available.Count);
            return new List<(ResourceCandidate, AuraSharedResourceResolutionV4)> { available[index] };
        }
        return new List<(ResourceCandidate, AuraSharedResourceResolutionV4)> { available[0] };
    }

    private static int ConfigRank(string participantKind)
    {
        if (string.Equals(participantKind, AuraSharedParticipantKinds.Tool, StringComparison.OrdinalIgnoreCase)) return 300;
        if (string.Equals(participantKind, AuraSharedParticipantKinds.Content, StringComparison.OrdinalIgnoreCase)) return 200;
        return 100;
    }

    private static string SourceName(string? participantKind)
    {
        if (string.Equals(participantKind, AuraSharedParticipantKinds.Tool, StringComparison.OrdinalIgnoreCase)) return "ToolDefault";
        if (string.Equals(participantKind, AuraSharedParticipantKinds.Content, StringComparison.OrdinalIgnoreCase)) return "ContentDefault";
        return "ModuleDefault";
    }

    private static Dictionary<string, string> MergeValues(
        IDictionary<string, string>? defaults,
        IDictionary<string, string>? local)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var pair in defaults ?? new Dictionary<string, string>()) result[pair.Key] = pair.Value;
        foreach (var pair in local ?? new Dictionary<string, string>()) result[pair.Key] = pair.Value;
        return result;
    }

    private sealed class ProfileCandidate
    {
        public ProfileCandidate(AuraSharedRegistrationManifestV4 manifest, AuraSharedDefaultProfileV4 profile)
        {
            Manifest = manifest;
            Profile = profile;
        }
        public AuraSharedRegistrationManifestV4 Manifest { get; }
        public AuraSharedDefaultProfileV4 Profile { get; }
    }

    private sealed class ResourceCandidate
    {
        public ResourceCandidate(AuraSharedRegistrationManifestV4 manifest, AuraSharedResourceDeclarationV4 resource)
        {
            Manifest = manifest;
            Resource = resource;
        }
        public AuraSharedRegistrationManifestV4 Manifest { get; }
        public AuraSharedResourceDeclarationV4 Resource { get; }
    }
}

