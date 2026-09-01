using System;

namespace AuraToolsExp.Dll.Features.MatchRecords.ReplayV17.Core;

internal static class ReplayActionSourceDescriptorKindsV17
{
    internal const string Card = "Card";
    internal const string Intent = "Intent";
}

internal sealed class ReplayActionSourceClassificationV17
{
    internal bool Supported { get; set; }
    internal string NativeDataType { get; set; } = "";
    internal string DescriptorKind { get; set; } = "";
    internal string TransactionKind { get; set; } = "";
    internal string SourceZone { get; set; } = "";
    internal string FailureReason { get; set; } = "";
}

internal sealed class ReplayActionSourceDescriptorIdentityV17
{
    internal string DescriptorId { get; set; } = "";
    internal string Name { get; set; } = "";
}

internal static class ReplayActionSourceClassifierV17
{
    internal static ReplayActionSourceClassificationV17 Classify(string nativeDataType)
    {
        var normalized = (nativeDataType ?? "").Trim();
        return normalized switch
        {
            "Card" => Supported(
                normalized,
                ReplayActionSourceDescriptorKindsV17.Card,
                ReplayTransactionKindsV17.ImplicitObserved,
                ""),
            "EnemyCard" or "PartnerCard" => Supported(
                normalized,
                ReplayActionSourceDescriptorKindsV17.Intent,
                ReplayTransactionKindsV17.Intent,
                "Intent"),
            _ => new ReplayActionSourceClassificationV17
            {
                NativeDataType = normalized,
                FailureReason = "unsupported-native-action-data-type:" + (normalized.Length == 0 ? "<empty>" : normalized)
            }
        };
    }

    internal static ReplayActionSourceDescriptorIdentityV17 RouteDescriptor(
        ReplayActionSourceClassificationV17 classification,
        Func<ReplayActionSourceDescriptorIdentityV17> registerCard,
        Func<ReplayActionSourceDescriptorIdentityV17> registerIntent)
    {
        if (classification == null || !classification.Supported)
            throw new InvalidOperationException(
                "Replay action source cannot be routed: "
                + (classification?.FailureReason ?? "classification-missing") + ".");

        var descriptor = classification.DescriptorKind switch
        {
            ReplayActionSourceDescriptorKindsV17.Card => registerCard(),
            ReplayActionSourceDescriptorKindsV17.Intent => registerIntent(),
            _ => throw new InvalidOperationException(
                "Replay action source descriptor kind is unsupported: "
                + classification.DescriptorKind + ".")
        };
        if (descriptor == null || string.IsNullOrWhiteSpace(descriptor.DescriptorId))
            throw new InvalidOperationException(
                "Replay action source descriptor registration returned no identity for "
                + classification.NativeDataType + ".");
        return descriptor;
    }

    private static ReplayActionSourceClassificationV17 Supported(
        string nativeDataType,
        string descriptorKind,
        string transactionKind,
        string sourceZone) => new()
    {
        Supported = true,
        NativeDataType = nativeDataType,
        DescriptorKind = descriptorKind,
        TransactionKind = transactionKind,
        SourceZone = sourceZone
    };
}
