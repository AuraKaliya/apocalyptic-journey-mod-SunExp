using System;
using System.Collections.Generic;
using System.Linq;

namespace AuraDirector.Shared;

public static class AuraDirectorFoundationTrustProtocol
{
    public const int SchemaVersion = 1;
    public const string PreviousFoundationLineage = "Aura.Foundation.V1";
    public const string CurrentFoundationLineage = "Aura.Foundation.V2";
    public const string ReadyToStartCapabilityV1 = "ReadyToStartGate.V1";
}

public sealed class AuraDirectorFoundationTrustCatalog
{
    public int SchemaVersion { get; set; } = AuraDirectorFoundationTrustProtocol.SchemaVersion;

    public List<AuraDirectorFoundationTrustEntry> Entries { get; set; } = new();
}

public sealed class AuraDirectorFoundationTrustEntry
{
    public string FoundationLineage { get; set; } = "";

    public string ModelId { get; set; } = "";

    public string ArtifactSha256 { get; set; } = "";

    public string WeightsSha256 { get; set; } = "";

    public int FeatureSchemaVersion { get; set; }

    public string ContentSetHash { get; set; } = "";

    public string RulesetHash { get; set; } = "";

    public string NativeProgramPackageHash { get; set; } = "";

    public string RequiredStartGateCapability { get; set; } =
        AuraDirectorFoundationTrustProtocol.ReadyToStartCapabilityV1;
}

public sealed class AuraDirectorFoundationCandidate
{
    public string FoundationLineage { get; set; } = "";

    public string ModelId { get; set; } = "";

    public string ArtifactSha256 { get; set; } = "";

    public string WeightsSha256 { get; set; } = "";

    public int FeatureSchemaVersion { get; set; }

    public string ContentSetHash { get; set; } = "";

    public string RulesetHash { get; set; } = "";

    public string NativeProgramPackageHash { get; set; } = "";

    public string AvailableStartGateCapability { get; set; } = "";
}

public static class AuraDirectorFoundationTrustPolicy
{
    public static bool TryAuthorize(
        AuraDirectorFoundationTrustCatalog? catalog,
        AuraDirectorFoundationCandidate? candidate,
        out AuraDirectorFoundationTrustEntry? matched,
        out string diagnostic)
    {
        matched = null;
        if (catalog == null
            || catalog.SchemaVersion != AuraDirectorFoundationTrustProtocol.SchemaVersion
            || catalog.Entries == null)
        {
            diagnostic = "foundation trust catalog is missing or incompatible";
            return false;
        }
        if (candidate == null
            || string.IsNullOrWhiteSpace(candidate.FoundationLineage)
            || string.IsNullOrWhiteSpace(candidate.ModelId)
            || candidate.FeatureSchemaVersion <= 0
            || !CanonicalSha256(candidate.ArtifactSha256))
        {
            diagnostic = "foundation candidate identity is incomplete";
            return false;
        }

        foreach (var entry in catalog.Entries.Where(item => item != null))
        {
            if (!Same(entry.FoundationLineage, candidate.FoundationLineage)
                || !Same(entry.ModelId, candidate.ModelId)
                || entry.FeatureSchemaVersion != candidate.FeatureSchemaVersion
                || !Same(entry.ContentSetHash, candidate.ContentSetHash)
                || !Same(entry.RulesetHash, candidate.RulesetHash)
                || !Same(entry.NativeProgramPackageHash, candidate.NativeProgramPackageHash)
                || !Same(entry.RequiredStartGateCapability, candidate.AvailableStartGateCapability))
            {
                continue;
            }

            var artifactMatch = CanonicalSha256(entry.ArtifactSha256)
                && Same(entry.ArtifactSha256, candidate.ArtifactSha256);
            var weightsMatch = CanonicalSha256(entry.WeightsSha256)
                && CanonicalSha256(candidate.WeightsSha256)
                && Same(entry.WeightsSha256, candidate.WeightsSha256);
            if (!artifactMatch && !weightsMatch)
            {
                continue;
            }

            matched = entry;
            diagnostic = "foundation model is trusted by "
                + (weightsMatch ? "weights SHA-256" : "artifact SHA-256")
                + " for "
                + entry.FoundationLineage;
            return true;
        }

        diagnostic = "foundation model hash or compatibility tuple is not allowlisted";
        return false;
    }

    private static bool CanonicalSha256(string value)
    {
        return !string.IsNullOrWhiteSpace(value)
            && value.Length == 64
            && value.All(character => character >= '0' && character <= '9'
                || character >= 'a' && character <= 'f'
                || character >= 'A' && character <= 'F');
    }

    private static bool Same(string left, string right)
    {
        return string.Equals((left ?? "").Trim(), (right ?? "").Trim(), StringComparison.OrdinalIgnoreCase);
    }
}
