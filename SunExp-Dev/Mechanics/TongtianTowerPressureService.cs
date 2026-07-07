using System;
using System.Collections.Generic;
using System.Linq;
using Data.Save;
using SunExp.Dll.Infrastructure;
using Witch;
using Witch.Core;

namespace SunExp.Dll.Mechanics;

public static class TongtianTowerPressureService
{
    private const string AnnihilationTag = "Annihilation";

    public static bool DestroyRandomEquippedRelic(string source)
    {
        return DestroyRandomEquippedRelic(source, "");
    }

    public static bool DestroyRandomEquippedRelic(string source, string seed)
    {
        try
        {
            var role = RoleTable.Instance;
            if (role?.relicList == null || role.relicList.Count == 0)
            {
                return false;
            }

            var index = PickIndex(role.relicList.Count, seed);
            var relic = role.relicList[index];
            role.relicList.RemoveAt(index);
            GameSaveManager.UpdateRoles(role);
            SunExpLog.Info("[TongtianPressure] destroyed equipped relic from "
                + source
                + ": "
                + DictionaryUtil.Get(relic?.data, "Id"));
            return true;
        }
        catch (Exception ex)
        {
            SunExpLog.Warn("[TongtianPressure] destroy relic failed from " + source + ": " + ex.Message);
            return false;
        }
    }

    public static int AddAnnihilationToRandomDeckCards(int count, string source)
    {
        return AddAnnihilationToRandomDeckCards(count, source, "");
    }

    public static int AddAnnihilationToRandomDeckCards(int count, string source, string seed)
    {
        try
        {
            var role = RoleTable.Instance;
            if (role?.cardList == null || count <= 0)
            {
                return 0;
            }

            var candidates = role.cardList
                .Where(card => card != null && !HasNativeTag(card, AnnihilationTag))
                .ToList();
            var changed = 0;
            while (changed < count && candidates.Count > 0)
            {
                var index = PickIndex(candidates.Count, seed + ":" + changed);
                var card = candidates[index];
                candidates.RemoveAt(index);
                if (CardMutationService.AddNativeTags(card, AnnihilationTag))
                {
                    changed++;
                }
            }

            if (changed > 0)
            {
                GameSaveManager.UpdateRoles(role);
                SunExpLog.Info("[TongtianPressure] added Annihilation to "
                    + changed
                    + " deck cards from "
                    + source
                    + ".");
            }

            return changed;
        }
        catch (Exception ex)
        {
            SunExpLog.Warn("[TongtianPressure] add Annihilation failed from " + source + ": " + ex.Message);
            return 0;
        }
    }

    public static void ApplyPostBattlePressure(int floor, bool boss, string source)
    {
        EndlessAbyssShockService.TryEnqueueEndlessBattleShock(
            floor,
            boss ? TongtianTowerNodeKind.Boss : TongtianTowerNodeKind.Monster,
            source);
    }

    private static bool HasNativeTag(IDataConfig config, string tag)
    {
        return DictionaryUtil.ContainsToken(DictionaryUtil.Get(config.Vars, "Tag"), tag)
            || DictionaryUtil.ContainsToken(DictionaryUtil.Get(config.data, "Tag"), tag);
    }

    private static int PickIndex(int count, string seed = "")
    {
        if (count <= 1)
        {
            return 0;
        }

        if (!string.IsNullOrWhiteSpace(seed))
        {
            return StableHash(seed) % count;
        }

        try
        {
            var value = Math.Abs((MapManager.Instance?.NowDice ?? Dice.Default).Roll().Value);
            return value % count;
        }
        catch
        {
            return Math.Abs(Environment.TickCount) % count;
        }
    }

    private static int StableHash(string value)
    {
        unchecked
        {
            var hash = 23;
            foreach (var ch in value ?? "")
            {
                hash = hash * 31 + ch;
            }

            return Math.Abs(hash == int.MinValue ? int.MaxValue : hash);
        }
    }
}
