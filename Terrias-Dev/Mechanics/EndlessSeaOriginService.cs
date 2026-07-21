using System;
using System.Reflection;
using Data.Save;
using AuraGameData.Shared.GameApi;
using Terrias.Dll.GameApi;
using Terrias.Dll.Infrastructure;
using Witch.Core;

namespace Terrias.Dll.Mechanics;

public static class EndlessSeaOriginService
{
    public const int OriginCap = 50;
    public const string Strength = "Strength";
    public const string Spirit = "Lucky";
    public const string Perceive = "Perceive";
    public const string Fortune = "Wisdom";

    private const string UnstableThoughtCard = "luckycard_7";
    private const int FortuneDiceBonus = 50;
    private const int FortuneExtraTriggerThreshold = 150;
    private const int FortuneExtraTriggers = 2;
    private const string OriginAppliedKey = "TerriasEndlessSeaOriginStartApplied";
    private static readonly Action<Dice.State> FortuneDiceBonusHandler = ApplyFortuneDiceBonus;

    public static void EnsureOriginCaps(string source)
    {
        try
        {
            var role = RoleTable.Instance;
            if (role == null)
            {
                return;
            }

            var changed = false;
            if (role.MainVarUpperBound < OriginCap)
            {
                role.MainVarUpperBound = OriginCap;
                changed = true;
            }

            if (role.SecondaryVarUpperBound < OriginCap)
            {
                role.SecondaryVarUpperBound = OriginCap;
                changed = true;
            }

            if (role.OtherVarUpperBound < OriginCap)
            {
                role.OtherVarUpperBound = OriginCap;
                changed = true;
            }

            if (!changed)
            {
                return;
            }

            GameSaveManager.UpdateRoles(role);
            TerriasLog.Info("[EndlessSeaOrigin] raised origin caps to 50 from " + source + ".");
        }
        catch (Exception ex)
        {
            TerriasLog.Warn("[EndlessSeaOrigin] origin cap update failed from " + source + ": " + ex.Message);
        }
    }

    public static void ApplyBattleStartEffects(string source)
    {
        EnsureOriginCaps(source);
        try
        {
            var fight = FightManager.Instance;
            if (fight == null || AlreadyApplied(fight))
            {
                return;
            }

            var executor = FightPlayer.Instance?.Status?.MirrorSc as ScriptExecutor;
            if (executor == null)
            {
                return;
            }

            if (OriginValue(Strength) >= OriginCap)
            {
                GrantUnstableThoughts(executor, 2);
            }

            if (OriginValue(Spirit) >= OriginCap)
            {
                executor.SetStatus("Self");
                executor.ChangeMaxPower("3");
            }

            if (OriginValue(Fortune) >= OriginCap)
            {
                AttachFortuneDiceBonus(executor);
            }

            MarkApplied(fight);
        }
        catch (Exception ex)
        {
            TerriasLog.Warn("[EndlessSeaOrigin] battle start effects failed from " + source + ": " + ex.Message);
        }
    }

    public static void ApplyBattleEndEffects(string source)
    {
        try
        {
            if (OriginValue(Perceive) < OriginCap)
            {
                return;
            }

            var status = FightPlayer.Instance?.Status;
            if (status != null && status.MaxHp > 0)
            {
                status.CurHp = status.MaxHp;
                status.UpdateStatus(true);
            }

            var role = RoleTable.Instance;
            if (role != null)
            {
                role.San = role.MaxSan;
                role.san = role.maxSan;
                GameSaveManager.UpdateRoles(role);
            }

            TerriasLog.Info("[EndlessSeaOrigin] restored player HP to max from " + source + ".");
        }
        catch (Exception ex)
        {
            TerriasLog.Warn("[EndlessSeaOrigin] battle end heal failed from " + source + ": " + ex.Message);
        }
    }

    public static bool FortuneDiceBonusActive()
    {
        return OriginValue(Fortune) >= OriginCap;
    }

    private static void AttachFortuneDiceBonus(ScriptExecutor executor)
    {
        AttachDiceWrapper(ReadMember(executor, "ValueDice"));
        AttachDiceWrapper(executor.CheckDice);
        TerriasLog.Info("[EndlessSeaOrigin] attached fortune dice +50.");
    }

    private static void AttachDiceWrapper(object? wrapper)
    {
        if (wrapper == null)
        {
            return;
        }

        const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        var field = wrapper.GetType().GetField("OnRoll", flags);
        if (field == null || !typeof(Delegate).IsAssignableFrom(field.FieldType))
        {
            return;
        }

        var current = field.GetValue(wrapper) as Delegate;
        current = current == null
            ? FortuneDiceBonusHandler
            : Delegate.Combine(Delegate.Remove(current, FortuneDiceBonusHandler), FortuneDiceBonusHandler);
        field.SetValue(wrapper, current);
    }

    private static object? ReadMember(object target, string name)
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        var type = target.GetType();
        return type.GetProperty(name, flags)?.GetValue(target)
            ?? type.GetField(name, flags)?.GetValue(target);
    }

    private static void ApplyFortuneDiceBonus(Dice.State result)
    {
        if (result == null || !FortuneDiceBonusActive())
        {
            return;
        }

        var value = result.Value + FortuneDiceBonus;
        var bonus = result.Bonus + FortuneDiceBonus;
        if (value >= FortuneExtraTriggerThreshold)
        {
            bonus += FortuneExtraTriggers;
        }

        new Dice.State(value, bonus).CopyTo(result);
    }

    private static void GrantUnstableThoughts(ScriptExecutor executor, int count)
    {
        for (var i = 0; i < count; i++)
        {
            CardApi.GrantCardToHand(
                executor,
                CardGrantRequest.ToHand(UnstableThoughtCard)
                    .WithRuntimeTags("Burnout", "Fragmented")
                    .WithSource("EndlessSeaOrigin.Strength50")
                    .WithWritableRuntimeConfig()
                    .Configure("extinction-enchant", AttachExtinctionEnchTag));
        }
    }

    public static void AttachExtinctionEnchTag(DataConfig card)
    {
        CardMutationService.AddNativeTags(card, "Fragmented");
        try
        {
            var role = RoleTable.Instance;
            if (role?.enchasedDict == null || string.IsNullOrWhiteSpace(card.InstanceID))
            {
                return;
            }

            var enchant = AuraGameDataHostApi.Materialize(DataType.EnchTag, "enchtag_2").Instance as DataConfig;
            if (enchant == null)
            {
                return;
            }
            role.enchasedDict[card.InstanceID] = enchant;
        }
        catch (Exception ex)
        {
            TerriasLog.Debug("[EndlessSeaOrigin] attach extinction enchant skipped: " + ex.Message);
        }
    }

    private static bool AlreadyApplied(FightManager manager)
    {
        return manager.TempVarsMap != null
            && manager.TempVarsMap.TryGetValue(OriginAppliedKey, out var floor)
            && floor == CurrentFloor();
    }

    private static void MarkApplied(FightManager manager)
    {
        if (manager.TempVarsMap != null)
        {
            manager.TempVarsMap[OriginAppliedKey] = CurrentFloor();
        }
    }

    private static int CurrentFloor()
    {
        return Math.Max(1, GameSaveManager.GetValue<int>(TerriasIds.EndlessSeaFloorKey));
    }

    private static int OriginValue(string key)
    {
        try
        {
            var role = RoleTable.Instance;
            return role?.VarsMap != null && role.VarsMap.TryGetValue(key, out var value) ? value : 0;
        }
        catch
        {
            return 0;
        }
    }
}
