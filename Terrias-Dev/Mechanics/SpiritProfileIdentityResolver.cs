using System;
using System.Collections.Generic;

namespace Terrias.Dll.Mechanics;

public sealed class SpiritProfileResolution<TProfile> where TProfile : class
{
    public SpiritProfileResolution(
        TProfile profile,
        string rawEnemyId,
        string rawVariantId,
        string matchedEnemyId,
        string matchedVariantId,
        string matchKind,
        bool usedAlias,
        bool usedVariantWildcard,
        bool usedGlobalFallback)
    {
        Profile = profile;
        RawEnemyId = rawEnemyId;
        RawVariantId = rawVariantId;
        MatchedEnemyId = matchedEnemyId;
        MatchedVariantId = matchedVariantId;
        MatchKind = matchKind;
        UsedAlias = usedAlias;
        UsedVariantWildcard = usedVariantWildcard;
        UsedGlobalFallback = usedGlobalFallback;
    }

    public TProfile Profile { get; }

    public string RawEnemyId { get; }

    public string RawVariantId { get; }

    public string MatchedEnemyId { get; }

    public string MatchedVariantId { get; }

    public string MatchKind { get; }

    public bool UsedAlias { get; }

    public bool UsedVariantWildcard { get; }

    public bool UsedGlobalFallback { get; }

    public string MatchedProfileKey => SpiritProfileIdentityResolver.CreateProfileKey(MatchedEnemyId, MatchedVariantId);
}

/// <summary>
/// Resolves the runtime enemy identity used by captured cards to the stable ids used by
/// the capture and intent registries. Raw ids remain untouched for save/network/data lookup.
/// </summary>
public static class SpiritProfileIdentityResolver
{
    private const string BaseGameRuntimePrefix = "enemy_";
    private const string TerriasRuntimePrefix = "Terrias_terrias_";

    public static SpiritProfileResolution<TProfile> Resolve<TProfile>(
        IReadOnlyList<TProfile> profiles,
        Func<TProfile, string> enemyIdSelector,
        Func<TProfile, string> variantIdSelector,
        string rawEnemyId,
        string rawVariantId)
        where TProfile : class
    {
        if (profiles == null)
        {
            throw new ArgumentNullException(nameof(profiles));
        }
        if (enemyIdSelector == null)
        {
            throw new ArgumentNullException(nameof(enemyIdSelector));
        }
        if (variantIdSelector == null)
        {
            throw new ArgumentNullException(nameof(variantIdSelector));
        }

        var enemy = Normalize(rawEnemyId);
        var variant = Normalize(rawVariantId);
        if (variant.Length == 0)
        {
            variant = enemy;
        }

        var enemyCandidates = Candidates(enemy);
        var variantCandidates = Same(enemy, variant) ? enemyCandidates : Candidates(variant);

        var exact = Find(profiles, enemyIdSelector, variantIdSelector, enemyCandidates[0], variantCandidates[0]);
        if (exact != null)
        {
            return Match(exact, enemyIdSelector, variantIdSelector, enemy, variant, "exact", false, false, false);
        }

        foreach (var enemyCandidate in enemyCandidates)
        {
            foreach (var variantCandidate in variantCandidates)
            {
                if (Same(enemyCandidate, enemy) && Same(variantCandidate, variant))
                {
                    continue;
                }

                var aliasExact = Find(profiles, enemyIdSelector, variantIdSelector, enemyCandidate, variantCandidate);
                if (aliasExact != null)
                {
                    return Match(aliasExact, enemyIdSelector, variantIdSelector, enemy, variant, "alias-exact", true, false, false);
                }
            }
        }

        var rawWildcard = Find(profiles, enemyIdSelector, variantIdSelector, enemy, "*");
        if (rawWildcard != null)
        {
            return Match(rawWildcard, enemyIdSelector, variantIdSelector, enemy, variant, "enemy-wildcard", false, true, false);
        }

        for (var index = 1; index < enemyCandidates.Count; index++)
        {
            var aliasWildcard = Find(profiles, enemyIdSelector, variantIdSelector, enemyCandidates[index], "*");
            if (aliasWildcard != null)
            {
                return Match(aliasWildcard, enemyIdSelector, variantIdSelector, enemy, variant, "alias-enemy-wildcard", true, true, false);
            }
        }

        var global = Find(profiles, enemyIdSelector, variantIdSelector, "*", "*");
        if (global == null)
        {
            throw new InvalidOperationException("Spirit profile registry has no global fallback profile (*#*).");
        }

        return Match(global, enemyIdSelector, variantIdSelector, enemy, variant, "global-fallback", false, true, true);
    }

    public static void ParseProfileKey(string profileKey, out string enemyId, out string variantId)
    {
        var value = Normalize(profileKey);
        if (value.StartsWith("spirit:", StringComparison.Ordinal))
        {
            value = value.Substring("spirit:".Length);
        }

        var separator = value.IndexOf('#');
        enemyId = separator < 0 ? value : value.Substring(0, separator);
        variantId = separator < 0 ? enemyId : value.Substring(separator + 1);
    }

    public static string CreateProfileKey(string enemyId, string variantId)
    {
        return "spirit:" + Normalize(enemyId) + "#" + Normalize(variantId);
    }

    private static SpiritProfileResolution<TProfile> Match<TProfile>(
        TProfile profile,
        Func<TProfile, string> enemyIdSelector,
        Func<TProfile, string> variantIdSelector,
        string rawEnemyId,
        string rawVariantId,
        string matchKind,
        bool usedAlias,
        bool usedVariantWildcard,
        bool usedGlobalFallback)
        where TProfile : class
    {
        return new SpiritProfileResolution<TProfile>(
            profile,
            rawEnemyId,
            rawVariantId,
            Normalize(enemyIdSelector(profile)),
            Normalize(variantIdSelector(profile)),
            matchKind,
            usedAlias,
            usedVariantWildcard,
            usedGlobalFallback);
    }

    private static TProfile? Find<TProfile>(
        IReadOnlyList<TProfile> profiles,
        Func<TProfile, string> enemyIdSelector,
        Func<TProfile, string> variantIdSelector,
        string enemyId,
        string variantId)
        where TProfile : class
    {
        for (var index = 0; index < profiles.Count; index++)
        {
            var profile = profiles[index];
            if (Same(enemyIdSelector(profile), enemyId) && Same(variantIdSelector(profile), variantId))
            {
                return profile;
            }
        }

        return null;
    }

    private static List<string> Candidates(string rawId)
    {
        var candidates = new List<string> { Normalize(rawId) };
        AddKnownAlias(candidates, rawId, BaseGameRuntimePrefix);
        AddKnownAlias(candidates, rawId, TerriasRuntimePrefix);
        return candidates;
    }

    private static void AddKnownAlias(List<string> candidates, string rawId, string prefix)
    {
        var normalized = Normalize(rawId);
        if (!normalized.StartsWith(prefix, StringComparison.Ordinal) || normalized.Length <= prefix.Length)
        {
            return;
        }

        var alias = normalized.Substring(prefix.Length);
        if (!candidates.Contains(alias))
        {
            candidates.Add(alias);
        }
    }

    private static string Normalize(string value) => (value ?? "").Trim();

    private static bool Same(string left, string right) => string.Equals(Normalize(left), Normalize(right), StringComparison.Ordinal);
}
