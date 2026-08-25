using System;
using System.Collections.Generic;
using System.Linq;
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

        var canonicalId = TerriasContentIdCompatibility.Canonicalize(CardConfigApi.Id(config));
        if (ExplicitBuckets.TryGetValue(canonicalId, out var explicitBucket))
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

    public static bool TryResolveInitializedBucket(IDataConfig? config, out string bucket)
    {
        bucket = "";
        var baseScript = DictionaryUtil.Get(config?.Vars, "BaseScript");
        if (baseScript.EndsWith(AttackBucket, StringComparison.Ordinal))
        {
            bucket = AttackBucket;
            return true;
        }

        if (baseScript.EndsWith(CommonBucket, StringComparison.Ordinal))
        {
            bucket = CommonBucket;
            return true;
        }

        return false;
    }

    public static bool IsEligible(IDataConfig? config)
    {
        if (config == null)
        {
            return false;
        }

        if (ExplicitBuckets.ContainsKey(TerriasContentIdCompatibility.Canonicalize(CardConfigApi.Id(config))))
        {
            return true;
        }

        var markers = DictionaryUtil.Get(config.Vars, TerriasIds.RuntimeMarkersKey);
        return DictionaryUtil.ContainsToken(markers, TerriasIds.PolymorphRoleCardMarker)
            || DictionaryUtil.ContainsToken(markers, TerriasIds.ProjectionRoleCardMarker)
            || DictionaryUtil.ContainsToken(markers, TerriasIds.SpiritCardMarker)
            || DictionaryUtil.ContainsToken(markers, TerriasIds.LoneerDerivedMarker)
            || DictionaryUtil.ContainsToken(markers, TerriasIds.LoneerGuidanceMarker);
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
            Mix(ref hash, CardConfigApi.Id(config));
            MixDictionary(ref hash, config.data, _ => true);
            MixDictionary(
                ref hash,
                config.Vars,
                key => !IsCostOnlyField(key));
            try
            {
                Mix(ref hash, config.Description());
            }
            catch
            {
                Mix(ref hash, DictionaryUtil.Get(config.data, "Description"));
            }
            return hash.ToString("X16");
        }
    }

    private static void MixDictionary(
        ref ulong hash,
        IEnumerable<KeyValuePair<string, string>>? values,
        Func<string, bool> include)
    {
        foreach (var pair in (values ?? Array.Empty<KeyValuePair<string, string>>())
                     .Where(pair => include(pair.Key ?? ""))
                     .OrderBy(pair => pair.Key, StringComparer.Ordinal))
        {
            Mix(ref hash, pair.Key);
            Mix(ref hash, pair.Value);
        }
    }

    private static bool IsCostOnlyField(string key)
    {
        return string.Equals(key, "ExCost", StringComparison.Ordinal)
               || string.Equals(key, "OnceExCost", StringComparison.Ordinal)
               || string.Equals(key, "TotalExCost", StringComparison.Ordinal);
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
