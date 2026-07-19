using System;
using System.Collections.Generic;
using System.Linq;

namespace AuraShared.Core;

public static class AuraSharedEffectiveResolverV3
{
    public static AuraSharedEffectiveResolutionV3 Resolve(
        AuraSharedScopeKey scope,
        IEnumerable<AuraSharedRegistrationManifestV3> activeManifests,
        AuraSharedLocalOverrideV3? localOverride,
        Func<string, AuraSharedResourceDeclarationV3, AuraSharedResourceResolutionV3> resolveResource,
        long revision)
    {
        scope ??= new AuraSharedScopeKey();
        scope.Normalize();
        var manifests = (activeManifests ?? Array.Empty<AuraSharedRegistrationManifestV3>()).ToList();
        var profiles = manifests.SelectMany(manifest => manifest.Defaults
                .Where(profile => profile.Scope.Equals(scope))
                .Select(profile => new ProfileCandidate(manifest, profile)))
            .OrderByDescending(candidate => ConfigRank(candidate.Manifest.ParticipantKind))
            .ThenByDescending(candidate => candidate.Profile.Priority)
            .ThenBy(candidate => candidate.Manifest.OwnerModId, StringComparer.OrdinalIgnoreCase)
            .ThenBy(candidate => candidate.Profile.ProfileId, StringComparer.OrdinalIgnoreCase)
            .ToList();
        var selectedProfile = profiles.FirstOrDefault();
        var result = new AuraSharedEffectiveResolutionV3
        {
            ScopeKey = scope.Key,
            Revision = Math.Max(0, revision),
            Enabled = localOverride?.Enabled ?? selectedProfile?.Profile.Enabled ?? true,
            ConfigSource = localOverride != null ? "LocalUser" : SourceName(selectedProfile?.Manifest.ParticipantKind),
            ConfigOwnerModId = localOverride != null ? "LocalUser" : selectedProfile?.Manifest.OwnerModId ?? "ModuleDefault",
            ProfileId = localOverride != null ? "aura.user" : selectedProfile?.Profile.ProfileId ?? "module-default",
            Values = MergeValues(selectedProfile?.Profile.Values, localOverride?.Values)
        };
        if (!result.Enabled)
        {
            result.Outcome = "Disabled";
            result.Fallback = "None";
            return result;
        }

        var requestedOwner = (localOverride?.ResourceOwnerModId ?? "").Trim();
        var requestedId = (localOverride?.ResourceId ?? "").Trim();
        if (requestedOwner.Length == 0) requestedOwner = selectedProfile?.Profile.ResourceOwnerModId ?? "";
        if (requestedId.Length == 0) requestedId = selectedProfile?.Profile.ResourceId ?? "";
        var resources = manifests.SelectMany(manifest => manifest.Resources
                .Where(resource => resource.Scope.Equals(scope))
                .Select(resource => new ResourceCandidate(manifest, resource)))
            .OrderByDescending(candidate => candidate.Resource.Priority)
            .ThenBy(candidate => candidate.Manifest.OwnerModId, StringComparer.OrdinalIgnoreCase)
            .ThenBy(candidate => candidate.Resource.ResourceId, StringComparer.OrdinalIgnoreCase)
            .ToList();
        var requested = resources.FirstOrDefault(candidate =>
            string.Equals(candidate.Manifest.OwnerModId, requestedOwner, StringComparison.OrdinalIgnoreCase)
            && string.Equals(candidate.Resource.ResourceId, requestedId, StringComparison.OrdinalIgnoreCase));
        var ordered = requested == null
            ? resources
            : new[] { requested }.Concat(resources.Where(candidate => !ReferenceEquals(candidate, requested))).ToList();
        foreach (var candidate in ordered)
        {
            var resolution = resolveResource(candidate.Manifest.OwnerModId, candidate.Resource);
            if (!resolution.Success)
            {
                continue;
            }

            result.ResourceOwnerModId = candidate.Manifest.OwnerModId;
            result.ResourceId = candidate.Resource.ResourceId;
            result.ResourcePath = resolution.ResolvedPath;
            result.Outcome = resolution.UsedLegacyPath ? "LegacyFallback" : "Resolved";
            result.Fallback = "None";
            return result;
        }

        var missingPolicy = requested?.Resource.MissingPolicy
                            ?? resources.FirstOrDefault()?.Resource.MissingPolicy
                            ?? AuraSharedMissingPolicies.Skip;
        result.Outcome = "Unavailable";
        result.Fallback = missingPolicy;
        return result;
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
        public ProfileCandidate(AuraSharedRegistrationManifestV3 manifest, AuraSharedDefaultProfileV3 profile)
        {
            Manifest = manifest;
            Profile = profile;
        }
        public AuraSharedRegistrationManifestV3 Manifest { get; }
        public AuraSharedDefaultProfileV3 Profile { get; }
    }

    private sealed class ResourceCandidate
    {
        public ResourceCandidate(AuraSharedRegistrationManifestV3 manifest, AuraSharedResourceDeclarationV3 resource)
        {
            Manifest = manifest;
            Resource = resource;
        }
        public AuraSharedRegistrationManifestV3 Manifest { get; }
        public AuraSharedResourceDeclarationV3 Resource { get; }
    }
}
