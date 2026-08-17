using System;
using System.Collections.Generic;
using System.Linq;
using AuraGameData.Shared.GameApi;
using Terrias.Dll.GameApi;
using Terrias.Dll.Infrastructure;
using Witch.Core;

namespace Terrias.Dll.Mechanics;

public static class MorningStarCurseCatalog
{
    private static IReadOnlyList<string>? randomCursePool;
    private static long randomCursePoolEpoch = -1;

    public static readonly string[] AllBeingsBlessingIds =
    {
        TerriasIds.DreamTalkerBlessing,
        TerriasIds.DeliriousTalkerBlessing,
        TerriasIds.ForgottenOneBlessing,
        TerriasIds.WisherBlessing,
        TerriasIds.UnspeakableOneBlessing,
        TerriasIds.WitheredOneBlessing,
        TerriasIds.BlindOneBlessing
    };

    public static bool IsCurse(IDataConfig? config)
    {
        if (config == null)
        {
            return false;
        }

        return DictionaryUtil.ContainsToken(DictionaryUtil.Get(config.data, "Tag"), "Curse")
               || DictionaryUtil.ContainsToken(DictionaryUtil.Get(config.Vars, "Tag"), "Curse")
               || DictionaryUtil.ContainsToken(DictionaryUtil.Get(config.Vars, "SpecialTag"), "Curse");
    }

    public static int Rarity(IDataConfig? config)
    {
        var runtime = DictionaryUtil.GetInt(config?.Vars, "Rarity", int.MinValue);
        var baseValue = runtime == int.MinValue
            ? DictionaryUtil.GetInt(config?.data, "Rarity", 1)
            : runtime;
        return MorningStarCurseFormula.NormalizeTier(baseValue);
    }

    public static string CardId(IDataConfig? config)
    {
        var id = DictionaryUtil.Get(config?.Vars, "Id");
        return string.IsNullOrWhiteSpace(id) ? DictionaryUtil.Get(config?.data, "Id") : id;
    }

    public static IReadOnlyList<string> MissingAllBeingsBlessings(IEnumerable<string>? ownedIds)
    {
        var owned = new HashSet<string>(
            (ownedIds ?? Array.Empty<string>())
            .Select(NormalizeBlessingId)
            .Where(id => id.Length > 0),
            StringComparer.Ordinal);
        return AllBeingsBlessingIds.Where(id => !owned.Contains(id)).ToList();
    }

    public static int CountAllBeingsBlessings(IEnumerable<string>? ownedIds)
    {
        var normalized = (ownedIds ?? Array.Empty<string>())
            .Select(NormalizeBlessingId)
            .Where(id => id.Length > 0);
        return MorningStarCurseFormula.DistinctBlessingCount(normalized, AllBeingsBlessingIds);
    }

    public static string RandomCurseCardId()
    {
        var pool = RandomCursePool();
        return pool.Count == 0 ? "" : pool[UnityEngine.Random.Range(0, pool.Count)];
    }

    public static IReadOnlyList<string> RandomCursePool()
    {
        var snapshot = AuraGameDataHostApi.AcquireSnapshot();
        if (snapshot.Version.NativeReady
            && randomCursePool != null
            && randomCursePoolEpoch == snapshot.Version.Epoch)
        {
            return randomCursePool;
        }

        var seen = new HashSet<string>(StringComparer.Ordinal);
        try
        {
            randomCursePool = TerriasConfigIndex.Rows(DataType.Card)
                .Select(row => new
                {
                    Id = DictionaryUtil.Get(row, "Id"),
                    Tag = DictionaryUtil.Get(row, "Tag")
                })
                .Where(row => !string.IsNullOrWhiteSpace(row.Id)
                              && !row.Id.StartsWith("*", StringComparison.Ordinal)
                              && DictionaryUtil.ContainsToken(row.Tag, "Curse"))
                .Select(row => CardApi.ResolveCardId(row.Id))
                .Where(id => !string.IsNullOrWhiteSpace(id)
                             && AuraGameDataHostApi.ResolveHandle(DataType.Card, id) != null
                             && seen.Add(id))
                .ToList();
            if (randomCursePool.Count == 0)
            {
                randomCursePool = FallbackCurseIds()
                    .Select(CardApi.ResolveCardId)
                    .Where(id => !string.IsNullOrWhiteSpace(id) && seen.Add(id))
                    .ToList();
            }
        }
        catch (Exception ex)
        {
            TerriasLog.Warn("[MorningStarCurse] random curse pool refresh failed: " + ex.Message);
            randomCursePool = FallbackCurseIds()
                .Select(CardApi.ResolveCardId)
                .Where(id => !string.IsNullOrWhiteSpace(id) && seen.Add(id))
                .ToList();
        }

        randomCursePoolEpoch = snapshot.Version.NativeReady ? snapshot.Version.Epoch : -1;
        return randomCursePool;
    }

    public static string NormalizeBlessingId(string? id)
    {
        return TerriasContentIdCompatibility.LocalId((id ?? "").Trim()).TrimStart('*');
    }

    private static IEnumerable<string> FallbackCurseIds()
    {
        for (var index = 1; index <= 15; index++)
        {
            yield return "cursecard_" + index;
        }

        yield return TerriasIds.AbyssLifeTheftCardId;
        yield return TerriasIds.AbyssDeficitCardId;
    }
}
