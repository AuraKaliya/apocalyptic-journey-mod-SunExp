using System;
using System.Collections.Generic;
using SunExp.Dll.GameApi;
using SunExp.Dll.Infrastructure;
using Witch.Core;

namespace SunExp.Dll.Mechanics;

public static class CombatCardViewPoolCatalog
{
    public const string CommonBucket = "CommonCardItem";
    public const string AttackBucket = "AttackCardItem";

    private static readonly Dictionary<string, string> ExplicitBuckets = new(StringComparer.Ordinal)
    {
        ["SunExp_sunexp_polymorph"] = CommonBucket,
        [SunExpIds.PolymorphRoleTemplateCardId] = CommonBucket,
        ["SunExp_sunexp_witch_projection"] = CommonBucket,
        [SunExpIds.ProjectionRoleTemplateCardId] = CommonBucket,
        [SunExpIds.SpiritCardTemplateId] = CommonBucket,
        [SunExpIds.SpiritBallCardId] = AttackBucket,
        ["SunExp_sunexp_heart_change"] = AttackBucket,
        [SunExpIds.WunaCoronationTokenCardId] = CommonBucket,
        [SunExpIds.WitchStarScoreCardId] = CommonBucket,
        [SunExpIds.StellarOvertureStartCardId] = CommonBucket,
        [SunExpIds.StellarOvertureSustainCardId] = CommonBucket,
        [SunExpIds.StellarOvertureTurnCardId] = AttackBucket,
        [SunExpIds.StellarOvertureCloseCardId] = AttackBucket
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

        var markers = DictionaryUtil.Get(config.Vars, SunExpIds.RuntimeMarkersKey);
        return DictionaryUtil.ContainsToken(markers, SunExpIds.PolymorphRoleCardMarker)
            || DictionaryUtil.ContainsToken(markers, SunExpIds.ProjectionRoleCardMarker)
            || DictionaryUtil.ContainsToken(markers, SunExpIds.SpiritCardMarker);
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
