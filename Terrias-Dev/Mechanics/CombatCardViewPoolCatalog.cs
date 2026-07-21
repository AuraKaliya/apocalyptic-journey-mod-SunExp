using System;
using System.Collections.Generic;
using Terrias.Dll.GameApi;
using Terrias.Dll.Infrastructure;
using Witch.Core;

namespace Terrias.Dll.Mechanics;

public static class CombatCardViewPoolCatalog
{
    public const string CommonBucket = "CommonCardItem";
    public const string AttackBucket = "AttackCardItem";

    private static readonly Dictionary<string, string> ExplicitBuckets = new(StringComparer.Ordinal)
    {
        ["Terrias_terrias_polymorph"] = CommonBucket,
        [TerriasIds.PolymorphRoleTemplateCardId] = CommonBucket,
        ["Terrias_terrias_witch_projection"] = CommonBucket,
        [TerriasIds.ProjectionRoleTemplateCardId] = CommonBucket,
        [TerriasIds.SpiritCardTemplateId] = CommonBucket,
        [TerriasIds.SpiritBallCardId] = AttackBucket,
        ["Terrias_terrias_heart_change"] = AttackBucket,
        [TerriasIds.WunaCoronationTokenCardId] = CommonBucket,
        [TerriasIds.WitchStarScoreCardId] = CommonBucket,
        [TerriasIds.StellarOvertureStartCardId] = CommonBucket,
        [TerriasIds.StellarOvertureSustainCardId] = CommonBucket,
        [TerriasIds.StellarOvertureTurnCardId] = AttackBucket,
        [TerriasIds.StellarOvertureCloseCardId] = AttackBucket
    };

    public static bool TryResolveBucket(IDataConfig? config, out string bucket)
    {
        bucket = "";
        if (!IsEligible(config))
        {
            return false;
        }

        if (ExplicitBuckets.TryGetValue(CardConfigApi.Id(config), out var explicitBucket))
        {
            bucket = explicitBucket;
            return true;
        }

        bucket = CommonBucket;
        return true;
    }

    public static bool MatchesInitializedBucket(IDataConfig? config, string expectedBucket, out string actualBaseScript)
    {
        actualBaseScript = DictionaryUtil.Get(config?.Vars, "BaseScript");
        return actualBaseScript.EndsWith(expectedBucket ?? "", StringComparison.Ordinal);
    }

    public static bool IsEligible(IDataConfig? config)
    {
        if (config == null)
        {
            return false;
        }

        if (ExplicitBuckets.ContainsKey(CardConfigApi.Id(config)))
        {
            return true;
        }

        var markers = DictionaryUtil.Get(config.Vars, TerriasIds.RuntimeMarkersKey);
        return DictionaryUtil.ContainsToken(markers, TerriasIds.PolymorphRoleCardMarker)
            || DictionaryUtil.ContainsToken(markers, TerriasIds.ProjectionRoleCardMarker)
            || DictionaryUtil.ContainsToken(markers, TerriasIds.SpiritCardMarker);
    }

    public static string PresentationSignature(IDataConfig? config, string bucket)
    {
        if (config == null)
        {
            return "";
        }

        unchecked
        {
            var hash = 14695981039346656037UL;
            Mix(ref hash, bucket);
            Mix(ref hash, config.GetType().FullName ?? config.GetType().Name);
            MixMap(ref hash, config.data);
            MixMap(ref hash, config.Vars);
            return hash.ToString("X16");
        }
    }

    private static void MixMap(ref ulong hash, IDictionary<string, string>? values)
    {
        if (values == null)
        {
            Mix(ref hash, "<null>");
            return;
        }

        var keys = new List<string>(values.Keys);
        keys.Sort(StringComparer.Ordinal);
        foreach (var key in keys)
        {
            if (string.Equals(key, "InstanceID", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            Mix(ref hash, key);
            Mix(ref hash, values.TryGetValue(key, out var value) ? value : "");
        }
    }

    private static void Mix(ref ulong hash, string? value)
    {
        var text = value ?? "";
        for (var index = 0; index < text.Length; index++)
        {
            hash ^= text[index];
            hash *= 1099511628211UL;
        }

        hash ^= 0xFF;
        hash *= 1099511628211UL;
    }
}
