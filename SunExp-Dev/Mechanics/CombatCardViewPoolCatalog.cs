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
            || DictionaryUtil.ContainsToken(markers, SunExpIds.ProjectionRoleCardMarker);
    }
}
