using System;
using System.Collections.Generic;
using System.Reflection;
using AuraGameData.Shared.GameApi;
using Data.Save;
using Terrias.Dll.GameApi;
using Terrias.Dll.Infrastructure;
using Witch.Core;

namespace Terrias.Dll.Mechanics;

public sealed class OriginMilestoneDefinition
{
    public OriginMilestoneDefinition(string originKey, int threshold, string blessingId)
    {
        OriginKey = originKey ?? "";
        Threshold = Math.Max(1, threshold);
        BlessingId = blessingId ?? "";
    }

    public string OriginKey { get; }
    public int Threshold { get; }
    public string BlessingId { get; }
}

public static class OriginMilestoneService
{
    public const string Strength = "Strength";
    public const string Spirit = "Wisdom";
    public const string Fortune = "Lucky";
    public const string Perceive = "Perceive";

    private const string UnstableThoughtCard = "luckycard_7";
    private const int FortuneDiceBonus = 50;
    private const int FortuneExtraTriggerThreshold = 150;
    private const int FortuneExtraTriggers = 2;
    private static readonly Action<Dice.State> FortuneDiceBonusHandler = ApplyFortuneDiceBonus;

    private static readonly IReadOnlyList<OriginMilestoneDefinition> Definitions =
        new[]
        {
            new OriginMilestoneDefinition(Strength, 50, TerriasIds.OriginStrength50Blessing),
            new OriginMilestoneDefinition(Spirit, 50, TerriasIds.OriginSpirit50Blessing),
            new OriginMilestoneDefinition(Fortune, 50, TerriasIds.OriginFortune50Blessing),
            new OriginMilestoneDefinition(Perceive, 50, TerriasIds.OriginPerceive50Blessing)
        };

    public static IReadOnlyList<OriginMilestoneDefinition> All => Definitions;

    public static int Reconcile(RoleTable? role, string source)
    {
        if (role?.VarsMap == null || role.ExtraordinaryBlessings == null)
        {
            return 0;
        }

        var granted = 0;
        foreach (var definition in Definitions)
        {
            try
            {
                if (!role.VarsMap.TryGetValue(definition.OriginKey, out var value)
                    || value < definition.Threshold
                    || role.ExtraordinaryBlessings.Contains(definition.BlessingId))
                {
                    continue;
                }

                role.TryAddBless(definition.BlessingId);
                granted++;
                TerriasLog.Info("[OriginMilestone] granted "
                    + definition.BlessingId
                    + "; origin="
                    + definition.OriginKey
                    + "; value="
                    + value
                    + "; threshold="
                    + definition.Threshold
                    + "; source="
                    + source
                    + ".");
            }
            catch (Exception ex)
            {
                TerriasLog.Warn("[OriginMilestone] reconcile failed for "
                    + definition.BlessingId
                    + " from "
                    + source
                    + ": "
                    + ex.Message);
            }
        }

        if (granted > 0)
        {
            TerriasFrameDispatcher.RunOnceAfterFrames(
                "OriginMilestone.Save." + (role.Id ?? "local"),
                2,
                () => GameSaveManager.UpdateRoles(role));
        }

        return granted;
    }

    public static void ApplyFightScript(ScriptExecutor? self, string id)
    {
        if (self == null)
        {
            return;
        }

        var context = (id ?? "").Trim();
        switch (context)
        {
            case "origin_strength_50":
                RegisterFightStart(self, Strength, 50, () => GrantUnstableThoughts(self, 2), context);
                break;
            case "origin_spirit_50":
                RegisterFightStart(self, Spirit, 50, () =>
                {
                    self.SetStatus("Self");
                    self.ChangeMaxPower("3");
                }, context);
                break;
            case "origin_fortune_50":
                if (HasReached(Fortune, 50))
                {
                    AttachFortuneDiceBonus(self);
                }
                break;
            case "origin_perceive_50":
                if (HasReached(Perceive, 50))
                {
                    ExecutorApi.TryAddEvent(self, "Win", new Action(() => RestoreHpToMax("Origin.Perceive50")), context);
                }
                break;
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
            if (enchant != null)
            {
                role.enchasedDict[card.InstanceID] = enchant;
            }
        }
        catch (Exception ex)
        {
            TerriasLog.Debug("[OriginMilestone] attach extinction enchant skipped: " + ex.Message);
        }
    }

    private static void RegisterFightStart(
        ScriptExecutor self,
        string originKey,
        int threshold,
        Action action,
        string context)
    {
        if (!HasReached(originKey, threshold))
        {
            return;
        }

        ExecutorApi.TryAddEvent(self, "FightStart", new Action(() =>
        {
            if (HasReached(originKey, threshold))
            {
                action();
            }
        }), context);
    }

    private static bool HasReached(string key, int threshold)
    {
        try
        {
            return RoleTable.Instance?.VarsMap != null
                && RoleTable.Instance.VarsMap.TryGetValue(key, out var value)
                && value >= threshold;
        }
        catch
        {
            return false;
        }
    }

    private static void GrantUnstableThoughts(ScriptExecutor executor, int count)
    {
        for (var i = 0; i < count; i++)
        {
            CardApi.GrantCardToHand(
                executor,
                CardGrantRequest.ToHand(UnstableThoughtCard)
                    .WithRuntimeTags("Burnout", "Fragmented")
                    .WithSource("OriginMilestone.Strength50")
                    .WithWritableRuntimeConfig()
                    .Configure("extinction-enchant", AttachExtinctionEnchTag));
        }
    }

    private static void AttachFortuneDiceBonus(ScriptExecutor executor)
    {
        AttachDiceWrapper(ReadMember(executor, "ValueDice"));
        AttachDiceWrapper(executor.CheckDice);
        TerriasLog.Info("[OriginMilestone] attached fortune 50 dice bonus.");
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
        if (result == null || !HasReached(Fortune, 50))
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

    private static void RestoreHpToMax(string source)
    {
        try
        {
            if (!HasReached(Perceive, 50))
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

            TerriasLog.Info("[OriginMilestone] restored player HP to max from " + source + ".");
        }
        catch (Exception ex)
        {
            TerriasLog.Warn("[OriginMilestone] battle end heal failed from " + source + ": " + ex.Message);
        }
    }
}
