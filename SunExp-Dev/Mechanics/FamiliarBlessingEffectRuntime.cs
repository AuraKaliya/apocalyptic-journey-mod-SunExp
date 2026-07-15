using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using AuraShared.Core;
using SunExp.Dll.GameApi;
using SunExp.Dll.Infrastructure;

namespace SunExp.Dll.Mechanics;

public static class FamiliarBlessingEffectRuntime
{
    private const string FeatureId = "FamiliarGrowth";
    private const string RunStarScoreClaimKey = "SunExpFamiliarFirstStarScoreExtraBlessing";
    private static readonly Dictionary<string, Func<IStatusManager, FamiliarBlessingEffect, bool>> CombatStartHandlers =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["CombatStartBuff"] = AddBuff,
            ["CombatStartResource"] = AddBuff,
            ["CombatStartHeal"] = Heal,
            ["CombatStartShield"] = AddShield,
            ["CombatStartDraw"] = DrawCards,
            ["CombatStartEnemyBuffRandom"] = AddRandomEnemyBuff
        };

    private static long activeEpoch;
    private static int round;
    private static bool firstActionPending;
    private static readonly Action<Dice.State> DiceBonusHandler = ApplyDiceBonus;

    public static long ActiveEpoch => activeEpoch;

    public static void BeginRun()
    {
        PlayerApi.SetGameVar(RunStarScoreClaimKey, "0");
    }

    public static int BeginEpoch(IStatusManager status)
    {
        activeEpoch = AuraBattleLifecycleRouter.EnsureBattleSession();
        round = 0;
        firstActionPending = false;
        AttachDiceBonus(status.MirrorSc as ScriptExecutor);
        if (status.MirrorSc is ScriptExecutor executor && HasAnyEffect("BurnTriggeredEmber", "BurnStackToEmber", "EmberOffsetBurnTransfer"))
        {
            DuskAfterheatRecoveryService.ActivateFamiliar(executor, "FamiliarGrowth.BeginEpoch");
        }

        return ApplyCombatStartEffects(status);
    }

    public static void EndEpoch()
    {
        activeEpoch = 0;
        round = 0;
        firstActionPending = false;
    }

    public static void BeginPlayerRound()
    {
        if (activeEpoch <= 0)
        {
            return;
        }

        round++;
        firstActionPending = true;
        DuskAfterheatRecoveryService.BeginPlayerRound();
    }

    public static void AfterPlayerAction()
    {
        if (!firstActionPending || activeEpoch != AuraBattleLifecycleRouter.CurrentBattleSessionId)
        {
            return;
        }

        firstActionPending = false;
        var status = FightPlayer.Instance?.Status;
        if (status == null)
        {
            return;
        }

        foreach (var entry in SelectedEffects("FirstActionResource"))
        {
            if (!TryClaim(status, entry, "FirstActionResource", "round:" + Math.Max(1, round)))
            {
                continue;
            }

            AddBuff(status, entry.Effect);
        }
    }

    public static void AfterDamage(IStatusManager? target, int amount, string sourceStatusId)
    {
        var owner = FightPlayer.Instance?.Status;
        if (target == null || owner == null || amount <= 0 || activeEpoch != AuraBattleLifecycleRouter.CurrentBattleSessionId)
        {
            return;
        }

        if (!string.Equals(owner.InstanceId, sourceStatusId ?? "", StringComparison.Ordinal))
        {
            return;
        }

        foreach (var entry in SelectedEffects("FirstDamageTargetBuff"))
        {
            if (!TryClaim(owner, entry, "FirstDamageTargetBuff", "battle"))
            {
                continue;
            }

            var buffId = NormalizeRuntimeBuffId(entry.Effect.Value);
            if (buffId.Length > 0 && entry.Effect.Amount > 0)
            {
                target.AddBuff(buffId, entry.Effect.Amount);
            }
        }
    }

    public static void BeforePotentialLethal(IStatusManager? target, int amount)
    {
        var owner = FightPlayer.Instance?.Status;
        if (target == null || owner == null || !string.Equals(target.InstanceId, owner.InstanceId, StringComparison.Ordinal)
            || amount < Math.Max(1, target.CurHp))
        {
            return;
        }

        foreach (var entry in SelectedEffects("BeforeLethalStarClayBody"))
        {
            if (BuffApi.Has(owner, SunExpIds.StarClayBody)
                || !TryClaim(owner, entry, "BeforeLethalStarClayBody", "battle"))
            {
                continue;
            }

            owner.AddBuff(SunExpIds.StarClayBody, Math.Max(1, entry.Effect.Amount));
        }
    }

    public static void OnStarScoreCadenceCompleted(IStatusManager? owner)
    {
        if (owner == null || PlayerApi.GetGameVar(RunStarScoreClaimKey, "0") == "1")
        {
            return;
        }

        var amount = SelectedEffects("FirstStarScoreExtraBlessing")
            .Sum(entry => Math.Max(0, entry.Effect.Amount));
        if (amount <= 0)
        {
            return;
        }

        owner.AddBuff(SunExpIds.StarBlessing, amount);
        PlayerApi.SetGameVar(RunStarScoreClaimKey, "1");
    }

    public static IReadOnlyList<string> UnsupportedSelectedEffectKinds()
    {
        var handled = new HashSet<string>(CombatStartHandlers.Keys, StringComparer.OrdinalIgnoreCase)
        {
            "BattleWinGold",
            "FirstDamageTargetBuff",
            "FirstActionResource",
            "FirstStarScoreExtraBlessing",
            "BeforeLethalStarClayBody",
            "RunDiceBonus",
            "BattleRewardExtraChoice",
            "BurnTriggeredEmber",
            "BurnStackToEmber",
            "EmberOffsetBurnTransfer",
            "CombatStartField"
        };
        return SelectedEffects()
            .Select(entry => entry.Effect.Kind ?? "")
            .Where(kind => kind.Length > 0 && !handled.Contains(kind))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(kind => kind, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public static int EffectAmount(string kind)
    {
        return SelectedEffects(kind).Sum(entry => Math.Max(0, entry.Effect.Amount));
    }

    public static bool HasAnyEffect(params string[] kinds)
    {
        var set = new HashSet<string>(kinds ?? Array.Empty<string>(), StringComparer.OrdinalIgnoreCase);
        return SelectedEffects().Any(entry => set.Contains(entry.Effect.Kind ?? ""));
    }

    public static IReadOnlyList<FieldStartGrant> OpeningFieldGrants()
    {
        var grants = new List<FieldStartGrant>();
        foreach (var entry in SelectedEffects("CombatStartField"))
        {
            var field = FieldEffectRegistry.FieldIdFromBuffId(entry.Effect.Value);
            var amount = Math.Max(0, entry.Effect.Amount);
            if (field == SunExpFieldId.None || amount <= 0)
            {
                continue;
            }

            grants.Add(new FieldStartGrant(
                "blessing." + entry.Blessing.Id + "." + entry.Index,
                field,
                amount,
                entry.Index));
        }

        return grants;
    }

    public static void ApplyBattleRewardExtraChoices(Witch.UI.Window.BattleRewardsUI? rewardUi)
    {
        var amount = EffectAmount("BattleRewardExtraChoice");
        if (amount > 0)
        {
            BattleRewardApi.AppendRandomCardRewards(rewardUi, amount, "FamiliarGrowth.BattleRewardExtraChoice");
        }
    }

    private static void AttachDiceBonus(ScriptExecutor? executor)
    {
        if (executor == null || EffectAmount("RunDiceBonus") <= 0)
        {
            return;
        }

        AttachDiceWrapper(ReadMember(executor, "ValueDice"));
        AttachDiceWrapper(executor.CheckDice);
    }

    private static void AttachDiceWrapper(object? wrapper)
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        var field = wrapper?.GetType().GetField("OnRoll", flags);
        if (field == null || !typeof(Delegate).IsAssignableFrom(field.FieldType))
        {
            return;
        }

        var current = field.GetValue(wrapper) as Delegate;
        field.SetValue(wrapper, current == null
            ? DiceBonusHandler
            : Delegate.Combine(Delegate.Remove(current, DiceBonusHandler), DiceBonusHandler));
    }

    private static object? ReadMember(object target, string name)
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        return target.GetType().GetProperty(name, flags)?.GetValue(target)
               ?? target.GetType().GetField(name, flags)?.GetValue(target);
    }

    private static void ApplyDiceBonus(Dice.State result)
    {
        var amount = EffectAmount("RunDiceBonus");
        if (result == null || amount <= 0)
        {
            return;
        }

        new Dice.State(result.Value + amount, result.Bonus + amount).CopyTo(result);
    }

    private static int ApplyCombatStartEffects(IStatusManager status)
    {
        var applied = 0;
        foreach (var entry in SelectedEffects())
        {
            var kind = (entry.Effect.Kind ?? "").Trim();
            if (!CombatStartHandlers.TryGetValue(kind, out var handler))
            {
                continue;
            }

            if (!TryClaim(status, entry, "CombatStartEffect", "epoch:" + activeEpoch))
            {
                LogCombatStartEffect(status, entry, "skipped", "already-claimed");
                continue;
            }

            try
            {
                if (handler(status, entry.Effect))
                {
                    applied++;
                    LogCombatStartEffect(status, entry, "applied", "ok");
                }
                else
                {
                    LogCombatStartEffect(status, entry, "skipped", "no-op-or-unavailable");
                }
            }
            catch (Exception ex)
            {
                LogCombatStartEffect(status, entry, "failed", ex.Message);
            }
        }

        return applied;
    }

    private static void LogCombatStartEffect(
        IStatusManager status,
        SelectedEffect entry,
        string result,
        string reason)
    {
        var message = "[FamiliarGrowth] combat-start effect "
            + result
            + ": epoch="
            + activeEpoch
            + ", status="
            + (status.InstanceId ?? "local")
            + ", familiar="
            + (entry.Familiar.InstanceId ?? entry.Familiar.SpeciesId ?? "unknown")
            + ", blessing="
            + (entry.Blessing.Id ?? "unknown")
            + ", effectIndex="
            + entry.Index
            + ", kind="
            + (entry.Effect.Kind ?? "unknown")
            + ", reason="
            + (reason ?? "unknown")
            + ".";

        if (string.Equals(result, "failed", StringComparison.Ordinal))
        {
            SunExpLog.Warn(message);
        }
        else
        {
            SunExpLog.Debug(message);
        }
    }

    private static IEnumerable<SelectedEffect> SelectedEffects(string kind = "")
    {
        var selected = FamiliarGrowthService.Active();
        if (selected == null)
        {
            yield break;
        }

        foreach (var blessing in FamiliarGrowthService.BlessingsFor(selected))
        {
            for (var index = 0; index < blessing.Effects.Count; index++)
            {
                var effect = blessing.Effects[index];
                if (kind.Length == 0 || string.Equals(effect.Kind, kind, StringComparison.OrdinalIgnoreCase))
                {
                    yield return new SelectedEffect(selected, blessing, effect, index);
                }
            }
        }
    }

    private static bool TryClaim(IStatusManager status, SelectedEffect entry, string operation, string phase)
    {
        var statusId = string.IsNullOrWhiteSpace(status.InstanceId) ? "local" : status.InstanceId;
        var familiarId = string.IsNullOrWhiteSpace(entry.Familiar.InstanceId) ? entry.Familiar.SpeciesId : entry.Familiar.InstanceId;
        var effectId = entry.Blessing.Id + ":" + entry.Index + ":" + (entry.Effect.Kind ?? "") + ":" + phase;
        return AuraLifecycleOperationLedger.TryClaimBattleOperation(
            SunExpIds.ModId,
            FeatureId,
            operation,
            statusId + ":" + familiarId,
            entry.Effect.Kind ?? "effect",
            effectId);
    }

    private static bool AddBuff(IStatusManager status, FamiliarBlessingEffect effect)
    {
        var buffId = NormalizeRuntimeBuffId(effect.Value);
        var amount = Math.Max(0, effect.Amount);
        if (buffId.Length == 0 || amount <= 0)
        {
            return false;
        }

        status.AddBuff(buffId, amount);
        return true;
    }

    private static bool DrawCards(IStatusManager status, FamiliarBlessingEffect effect)
    {
        if (effect.Amount <= 0)
        {
            return false;
        }

        return CombatCardApi.TryDrawPlayerCards(
            effect.Amount,
            "FamiliarGrowth.CombatStartDraw:epoch:" + activeEpoch + ":status:" + (status.InstanceId ?? "local"));
    }

    private static bool AddRandomEnemyBuff(IStatusManager status, FamiliarBlessingEffect effect)
    {
        if (status.MirrorSc is not ScriptExecutor executor || effect.Amount <= 0)
        {
            return false;
        }

        var target = TargetApi.RandomEnemyTarget(executor, requireBurn: false);
        var buffId = NormalizeRuntimeBuffId(effect.Value);
        if (target == null || buffId.Length == 0)
        {
            return false;
        }

        target.AddBuff(buffId, effect.Amount);
        return true;
    }

    private static bool Heal(IStatusManager status, FamiliarBlessingEffect effect)
    {
        var amount = Math.Max(0, effect.Amount);
        var next = Math.Min(Math.Max(1, status.MaxHp), Math.Max(0, status.CurHp) + amount);
        if (amount <= 0 || next == status.CurHp)
        {
            return false;
        }

        status.CurHp = next;
        if (string.Equals(status.fatherObject?.GetType().Name, "FightPlayer", StringComparison.Ordinal)
            && RoleTable.Instance != null)
        {
            RoleTable.Instance.san = Math.Max(1, next);
        }

        status.UpdateStatus(true);
        return true;
    }

    private static bool AddShield(IStatusManager status, FamiliarBlessingEffect effect)
    {
        var amount = Math.Max(0, effect.Amount);
        if (amount <= 0)
        {
            return false;
        }

        status.Defend = Math.Max(0, status.Defend) + amount;
        status.UpdateStatus(true);
        return true;
    }

    private static string NormalizeRuntimeBuffId(string value)
    {
        var id = (value ?? "").Trim();
        return id.Equals("starlight", StringComparison.OrdinalIgnoreCase) ? SunExpIds.Starlight : id;
    }

    private readonly struct SelectedEffect
    {
        public SelectedEffect(FamiliarInstance familiar, FamiliarBlessingDefinition blessing, FamiliarBlessingEffect effect, int index)
        {
            Familiar = familiar;
            Blessing = blessing;
            Effect = effect;
            Index = index;
        }

        public FamiliarInstance Familiar { get; }
        public FamiliarBlessingDefinition Blessing { get; }
        public FamiliarBlessingEffect Effect { get; }
        public int Index { get; }
    }
}
