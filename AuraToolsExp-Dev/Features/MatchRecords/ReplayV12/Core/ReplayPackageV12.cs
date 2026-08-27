using System.Collections.Generic;
using System.Linq;

namespace AuraToolsExp.Dll.Features.MatchRecords.ReplayV12.Core;

internal sealed class ReplayPackageManifestV12
{
    public string Format { get; set; } = "AuraTools.MatchReplay";
    public int PackageVersion { get; set; } = ReplayProtocolV12.PackageVersion;
    public int DocumentVersion { get; set; } = ReplayProtocolV12.DocumentVersion;
    public string ExportedUtc { get; set; } = "";
    public string RecordId { get; set; } = "";
    public string DocumentRoot { get; set; } = "";
    public string TruthRoot { get; set; } = "";
    public string PresentationRoot { get; set; } = "";
    public List<ReplayPackageEntryV12> Entries { get; set; } = new();
}

internal sealed class ReplayPackageEntryV12
{
    public string Path { get; set; } = "";
    public string Kind { get; set; } = "";
    public long ByteLength { get; set; }
    public string Sha256 { get; set; } = "";
}

internal static class ReplayPovContractV12
{
    internal static void Finalize(ReplayPovSidecarV12 sidecar)
    {
        var previous = "";
        var sequence = 0L;
        foreach (var value in sidecar.Events)
        {
            value.Sequence = ++sequence;
            value.PreviousEventHash = previous;
            value.EventHash = "";
            value.EventHash = ReplayCanonicalJsonV12.Sha256(value);
            previous = value.EventHash;
        }
        sidecar.EventChainSha256 = previous;
        sidecar.SidecarSha256 = "";
        sidecar.SidecarSha256 = ReplayCanonicalJsonV12.Sha256(HashPayload(sidecar));
    }

    internal static string Validate(ReplayPovSidecarV12 sidecar, bool requirePayloads)
    {
        if (sidecar == null
            || sidecar.SidecarVersion != 1
            || string.IsNullOrWhiteSpace(sidecar.ParentDocumentRoot)
            || string.IsNullOrWhiteSpace(sidecar.PlayerId)) return "identity-invalid";
        var previous = "";
        var expected = 1L;
        var previousCanonicalSequence = 0L;
        foreach (var value in sidecar.Events)
        {
            if (value.Sequence != expected++
                || value.PreviousEventHash != previous
                || value.CanonicalSequence <= 0
                || value.CanonicalSequence < previousCanonicalSequence
                || string.IsNullOrWhiteSpace(value.TransactionId)
                || value.StepOrdinal < 0
                || !ReplayPovEventKindsV12.Supported.Contains(value.Kind ?? "")) return "event-chain-invalid";
            if (value.Kind == ReplayPovEventKindsV12.UpsertPrivateCard
                && (value.Card == null || string.IsNullOrWhiteSpace(value.Card.CardInstanceId))) return "event-card-invalid";
            if (value.Kind == ReplayPovEventKindsV12.RemovePrivateCard
                && string.IsNullOrWhiteSpace(value.CardInstanceId)) return "event-card-invalid";
            var clone = ReplayCanonicalJsonV12.Clone(value);
            clone.EventHash = "";
            if (value.EventHash != ReplayCanonicalJsonV12.Sha256(clone)) return "event-hash-invalid";
            previous = value.EventHash;
            previousCanonicalSequence = value.CanonicalSequence;
        }
        if (sidecar.EventChainSha256 != previous) return "event-root-invalid";
        var descriptorIds = sidecar.PrivateCards.Select(item => item.DescriptorId).ToHashSet(System.StringComparer.Ordinal);
        if (descriptorIds.Count != sidecar.PrivateCards.Count || descriptorIds.Any(string.IsNullOrWhiteSpace))
            return "descriptor-invalid";
        if (sidecar.Events.Where(item => item.Card != null)
            .Any(item => !descriptorIds.Contains(item.Card!.DescriptorId))) return "event-descriptor-missing";
        var reachableAssets = sidecar.PrivateCards
            .SelectMany(item => new[] { item.ArtworkAssetSha256, item.FrameAssetSha256 })
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .ToHashSet(System.StringComparer.OrdinalIgnoreCase);
        var assetIds = sidecar.Assets.Select(item => item.Sha256).ToHashSet(System.StringComparer.OrdinalIgnoreCase);
        if (assetIds.Count != sidecar.Assets.Count || !reachableAssets.SetEquals(assetIds)) return "asset-reachability-invalid";
        foreach (var asset in sidecar.Assets)
        {
            var assetError = ReplayAssetContractV12.Validate(asset, requirePayloads);
            if (assetError.Length > 0) return "asset-invalid:" + assetError;
        }
        var copy = ReplayCanonicalJsonV12.Clone(sidecar);
        copy.SidecarSha256 = "";
        foreach (var asset in copy.Assets) asset.Payload = System.Array.Empty<byte>();
        return sidecar.SidecarSha256 == ReplayCanonicalJsonV12.Sha256(copy) ? "" : "sidecar-hash-invalid";
    }

    internal static string ValidateAlignment(ReplayPovSidecarV12 sidecar, ReplayDocumentEnvelopeV12 envelope)
    {
        if (sidecar == null || envelope?.Document == null
            || !string.Equals(sidecar.ParentDocumentRoot, envelope.DeclaredDocumentRoot, System.StringComparison.OrdinalIgnoreCase))
            return "parent-document-invalid";
        var canonical = envelope.Document.TruthEvents.Concat(envelope.Document.PresentationEvents)
            .GroupBy(item => item.Sequence)
            .ToDictionary(group => group.Key, group => group.Single());
        foreach (var value in sidecar.Events)
            if (!canonical.TryGetValue(value.CanonicalSequence, out var anchor)
                || !string.Equals(value.TransactionId, anchor.TransactionId, System.StringComparison.Ordinal)
                || value.StepOrdinal != anchor.StepOrdinal)
                return "canonical-anchor-invalid";
        return "";
    }

    private static ReplayPovSidecarV12 HashPayload(ReplayPovSidecarV12 sidecar)
    {
        var copy = ReplayCanonicalJsonV12.Clone(sidecar);
        copy.SidecarSha256 = "";
        foreach (var asset in copy.Assets) asset.Payload = System.Array.Empty<byte>();
        return copy;
    }
}
