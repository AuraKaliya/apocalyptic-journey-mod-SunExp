using System;
using System.Collections.Generic;
using System.Linq;
using SunExp.Dll.GameApi;
using SunExp.Dll.Infrastructure;
using Witch;
using Witch.Core;

namespace SunExp.Dll.Mechanics;

public static class EndlessAbyssRewardService
{
    private static readonly string[] OriginKeys =
    {
        EndlessSeaOriginService.Strength,
        EndlessSeaOriginService.Spirit,
        EndlessSeaOriginService.Fortune,
        EndlessSeaOriginService.Perceive
    };

    private static readonly string[] EvolutionTraitPool =
    {
        SunExpIds.BossTraitMirrorArray,
        SunExpIds.BossTraitMercilessDaylight,
        SunExpIds.BossTraitWhiteRadianceSaint,
        "SpecialBuff_CAR_Momentum"
    };

    public static int GrantRandomCards(int count, string source)
    {
        var pool = NonHiddenCards();
        var granted = 0;
        for (var i = 0; i < count && pool.Count > 0; i++)
        {
            var id = pool[PickIndex(pool.Count, source + ":card:" + i)];
            if (PlayerApi.TryAddCardToDeck(id, out _, out var error))
            {
                granted++;
            }
            else
            {
                SunExpLog.Warn("[EndlessAbyssReward] card grant failed: " + error);
            }
        }

        if (granted > 0)
        {
            EndlessSeaCardAffixService.NormalizeOwnedCards("EndlessAbyssReward.Card");
            EndlessSeaCardAffixService.TryPersistCurrentRole("EndlessAbyssReward.Card");
        }

        return granted;
    }

    public static bool GrantRandomBlessing(string source)
    {
        var pool = NonHiddenBlessings();
        if (pool.Count == 0)
        {
            return false;
        }

        var id = pool[PickIndex(pool.Count, source + ":bless")];
        PlayerApi.AddBless(id);
        SunExpLog.Info("[EndlessAbyssReward] granted blessing " + id + " from " + source + ".");
        return true;
    }

    public static void IncreasePlayerMaxHp(int amount, string source)
    {
        if (amount <= 0)
        {
            return;
        }

        AdventureRoleRewardApi.AddMaxHp(amount, source);
    }

    public static bool AddRandomOrigin(int amount, string source)
    {
        if (amount <= 0)
        {
            return false;
        }

        try
        {
            var key = OriginKeys[PickIndex(OriginKeys.Length, source + ":origin")];
            return AdventureRoleRewardApi.AddOrigin(key, amount, source);
        }
        catch (Exception ex)
        {
            SunExpLog.Warn("[EndlessAbyssReward] origin reward failed: " + ex.Message);
            return false;
        }
    }

    public static int EvolutionLevel()
    {
        return Math.Max(0, DictionaryUtil.ParseInt(PlayerApi.GetGameVar(SunExpIds.EndlessAbyssEvolutionLevelKey, "0")));
    }

    public static int IncreaseEvolution(int amount, string source)
    {
        var next = EvolutionLevel() + Math.Max(0, amount);
        PlayerApi.SetGameVar(SunExpIds.EndlessAbyssEvolutionLevelKey, next.ToString());
        SunExpLog.Info("[EndlessAbyssReward] evolution level=" + next + " from " + source + ".");
        return next;
    }

    public static void ApplyEvolutionTraits(Enemy enemy, string source)
    {
        var stacks = EvolutionLevel();
        if (stacks <= 0 || enemy?.Status == null)
        {
            return;
        }

        for (var i = 0; i < stacks; i++)
        {
            var id = EvolutionTraitPool[PickIndex(EvolutionTraitPool.Length, source + ":" + enemy.InstanceId + ":" + i)];
            enemy.Status.AddBuff(id, 1);
        }
    }

    private static List<string> NonHiddenCards()
    {
        try
        {
            var enabledPacks = EnabledCardPacks();
            var rows = SunExpConfigIndex.Rows(DataType.Card);
            var result = rows
                .Where(row => IsOpenRewardCard(row, enabledPacks))
                .Select(row => CardApi.ResolveCardId(DictionaryUtil.Get(row, "Id")))
                .Where(id => !string.IsNullOrWhiteSpace(id)
                    && SunExpConfigIndex.Row(DataType.Card, id) != null)
                .Distinct(StringComparer.Ordinal)
                .ToList();
            SunExpLog.Info("[EndlessAbyssReward] open card reward pool size=" + result.Count + ".");
            return result;
        }
        catch (Exception ex)
        {
            SunExpLog.Warn("[EndlessAbyssReward] card pool failed: " + ex.Message);
            return new List<string>();
        }
    }

    private static List<string> NonHiddenBlessings()
    {
        try
        {
            var rows = Singleton<GameConfigManager>.Instance.CardPackCheck(SunExpConfigIndex.Rows(DataType.Bless));
            return rows
                .Select(row => DictionaryUtil.Get(row, "Id"))
                .Where(id => !string.IsNullOrWhiteSpace(id)
                    && !id.StartsWith("*", StringComparison.Ordinal))
                .Distinct(StringComparer.Ordinal)
                .ToList();
        }
        catch (Exception ex)
        {
            SunExpLog.Warn("[EndlessAbyssReward] blessing pool failed: " + ex.Message);
            return new List<string>();
        }
    }

    private static int PickIndex(int count, string seed)
    {
        if (count <= 1)
        {
            return 0;
        }

        unchecked
        {
            var hash = 23;
            foreach (var ch in seed ?? "")
            {
                hash = hash * 31 + ch;
            }

            hash = hash * 31 + Environment.TickCount;
            return Math.Abs(hash == int.MinValue ? int.MaxValue : hash) % count;
        }
    }

    private static bool IsOpenRewardCard(Dictionary<string, string> row, HashSet<string> enabledPacks)
    {
        var id = DictionaryUtil.Get(row, "Id");
        var pack = DictionaryUtil.Get(row, "PackBelong");
        var tag = DictionaryUtil.Get(row, "Tag");
        if (string.IsNullOrWhiteSpace(id)
            || id.StartsWith("*", StringComparison.Ordinal)
            || string.IsNullOrWhiteSpace(pack)
            || DictionaryUtil.ContainsToken(tag, "Curse")
            || DictionaryUtil.ContainsToken(tag, "Unusable"))
        {
            return false;
        }

        return enabledPacks.Count == 0 || CardPackEnabled(pack, enabledPacks);
    }

    private static bool CardPackEnabled(string pack, HashSet<string> enabledPacks)
    {
        if (enabledPacks.Contains(pack))
        {
            return true;
        }

        const string prefix = "SunExp_sunexp_";
        if (pack.StartsWith(prefix, StringComparison.Ordinal)
            && enabledPacks.Contains(pack.Substring(prefix.Length)))
        {
            return true;
        }

        return enabledPacks.Contains(prefix + pack);
    }

    private static HashSet<string> EnabledCardPacks()
    {
        try
        {
            return new HashSet<string>(
                Singleton<GameRuntimeData>.Instance.UseCardPack
                    .Where(pack => !string.IsNullOrWhiteSpace(pack)),
                StringComparer.OrdinalIgnoreCase);
        }
        catch (Exception ex)
        {
            SunExpLog.Warn("[EndlessAbyssReward] enabled card packs unavailable: " + ex.Message);
            return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }
    }
}
