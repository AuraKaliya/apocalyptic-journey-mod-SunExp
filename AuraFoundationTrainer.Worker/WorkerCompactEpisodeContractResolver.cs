using System;
using System.Reflection;
using AuraCombatAi.Shared;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;

namespace AuraFoundationTrainer.Worker;

internal sealed class WorkerCompactEpisodeContractResolver :
    DefaultContractResolver
{
    public static WorkerCompactEpisodeContractResolver Instance { get; } =
        new();

    protected override JsonProperty CreateProperty(
        MemberInfo member,
        MemberSerialization memberSerialization)
    {
        var property = base.CreateProperty(member, memberSerialization);
        if (member.DeclaringType == typeof(CombatEpisodeFrame))
        {
            if (string.Equals(
                    property.PropertyName,
                    nameof(CombatEpisodeFrame.Observation),
                    StringComparison.Ordinal))
            {
                // The projected compact features are the durable training
                // payload. Keeping the source observation duplicates the
                // largest object graph across process-boundary checkpoints.
                property.Ignored = true;
            }
            else if (string.Equals(
                    property.PropertyName,
                    nameof(CombatEpisodeFrame.StateFeatures),
                    StringComparison.Ordinal))
            {
                property.ShouldSerialize = value =>
                    value is CombatEpisodeFrame frame
                    && frame.CompactStateFeatures == null;
            }
            else if (string.Equals(
                         property.PropertyName,
                         nameof(CombatEpisodeFrame.CompactStateFeatureTokenIds),
                         StringComparison.Ordinal)
                     || string.Equals(
                         property.PropertyName,
                         nameof(CombatEpisodeFrame.CompactStateFeatureValues),
                         StringComparison.Ordinal))
            {
                property.ShouldSerialize = value =>
                    value is CombatEpisodeFrame frame
                    && frame.CompactStateFeatures != null;
            }
        }
        else if (member.DeclaringType == typeof(CombatEpisodeCandidate))
        {
            if (string.Equals(
                    property.PropertyName,
                    nameof(CombatEpisodeCandidate.Features),
                    StringComparison.Ordinal))
            {
                property.ShouldSerialize = value =>
                    value is CombatEpisodeCandidate candidate
                    && candidate.CompactFeatures == null;
            }
            else if (string.Equals(
                         property.PropertyName,
                         nameof(CombatEpisodeCandidate.CompactFeatureTokenIds),
                         StringComparison.Ordinal)
                     || string.Equals(
                         property.PropertyName,
                         nameof(CombatEpisodeCandidate.CompactFeatureValues),
                         StringComparison.Ordinal))
            {
                property.ShouldSerialize = value =>
                    value is CombatEpisodeCandidate candidate
                    && candidate.CompactFeatures != null;
            }
        }
        return property;
    }
}
