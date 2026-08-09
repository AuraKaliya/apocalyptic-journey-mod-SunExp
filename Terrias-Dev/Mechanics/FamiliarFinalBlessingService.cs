using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Terrias.Dll.GameApi;
using Terrias.Dll.Hooks;
using Terrias.Dll.Infrastructure;
using UnityEngine;
using Witch.Core;

namespace Terrias.Dll.Mechanics;

public static class FamiliarFinalBlessingService
{
    private const string PocketMarker = "TerriasFamiliarPocketCard";
    private const string SoulBuff = "buff_Soul";
    private const string SoulVar = "Soul";
    private const string NetherChaseBuff = "SpecialBuff_meow";
    private const string NativeJackpotCard = "nocard_5";
    private static readonly Dictionary<IStatusManager, Stack<HitSnapshot>> HitSnapshots = new();
    private static readonly Dictionary<IStatusManager, Stack<int>> DeathBurnSnapshots = new();
    private static readonly HashSet<string> RoundClaims = new(StringComparer.Ordinal);
    private static IDataConfig? pendingAction;
    private static ScriptExecutor? owner;
    private static int crowSettlements;
    private static int generatedDamageDepth;

    public static void BeginEpoch(IStatusManager status)
    {
        owner = status.MirrorSc as ScriptExecutor;
        HitSnapshots.Clear();
        DeathBurnSnapshots.Clear();
        RoundClaims.Clear();
        pendingAction = null;
        crowSettlements = 0;
        generatedDamageDepth = 0;
        if (owner == null)
        {
            return;
        }

        ApplyCombatStartCards(owner);
        ApplyCombatStartRandomModification(status);
        ApplyNetherChaseBonus(status);
        RestoreSoulDisplay(status);

        if (HasEffect("CrowExtraSettlement") || HasEffect("CrowEveryNthSettlementHpDamage"))
        {
            ScriptEventApi.TryAddEvent(owner, "AttackDone", OnCrowSettlement, "FamiliarFinal.Crow");
        }

        if (HasEffect("AfterResurrectionRecovery"))
        {
            ScriptEventApi.TryAddEvent(owner, "ResurrectionEnd", OnResurrectionEnd, "FamiliarFinal.Resurrection");
        }

        if (HasEffect("CompositeDoomProcPlayerBuff") || HasEffect("CompositeDoomProcRandomEnemyBundle"))
        {
            ScriptEventApi.TryAddEvent<AddBuffData>(owner, "AddBuff", OnBuffAdded, "FamiliarFinal.Nightmare");
        }
    }

    public static void EndEpoch()
    {
        owner = null;
        pendingAction = null;
        HitSnapshots.Clear();
        DeathBurnSnapshots.Clear();
        RoundClaims.Clear();
    }

    public static void BeginPlayerRound()
    {
        RoundClaims.Clear();
        var currentOwner = owner;
        if (currentOwner == null)
        {
            return;
        }

        var multiplier = EffectAmount("RoundStartExtraordinaryPerEnemyDebuffKind");
        if (multiplier > 0)
        {
            var kinds = TargetApi.EnemyTargets(currentOwner).Sum(BuffApi.NegativeKindCount);
            if (kinds > 0)
            {
                currentOwner.Self?.AddBuff(TerriasIds.Extraordinary, kinds * multiplier);
            }
        }
    }

    public static void OnAction(TerriasActionEventContext context)
    {
        pendingAction = context.Config;
    }

    public static void OnActionAfter()
    {
        var config = pendingAction;
        pendingAction = null;
        if (config == null)
        {
            return;
        }

        var id = CardConfigApi.Id(config);
        foreach (var effect in Effects("CardUseAdventureOrigin"))
        {
            if (string.Equals(id, effect.Value, StringComparison.Ordinal))
            {
                AdventureRoleRewardApi.AddOrigin(Parameter(effect, "origin", "Lucky"), Math.Max(1, effect.Amount), "FamiliarGrowth.Jackpot");
            }
        }

        if (HasEffect("PocketCardReplacePerRound") && IsPocketCard(config) && RoundClaims.Add("PocketCardReplacePerRound"))
        {
            GrantRandomPocketCard();
        }
    }

    public static void BeforeHit(ModHookContext context)
    {
        var target = context.Target as IStatusManager;
        if (target == null)
        {
            return;
        }

        FamiliarBlessingEffectRuntime.BeforePotentialLethal(target, HitAmount(context.Arguments));
        if (!HitSnapshots.TryGetValue(target, out var stack))
        {
            stack = new Stack<HitSnapshot>();
            HitSnapshots[target] = stack;
        }

        stack.Push(new HitSnapshot(target.CurHp, target.Defend));
    }

    public static void AfterHit(ModHookContext context)
    {
        var target = context.Target as IStatusManager;
        if (target == null || !HitSnapshots.TryGetValue(target, out var stack) || stack.Count == 0)
        {
            return;
        }

        var snapshot = stack.Pop();
        var amount = HitAmount(context.Arguments);
        var sourceStatusId = context.Arguments != null && context.Arguments.Length > 3
            ? Convert.ToString(context.Arguments[3]) ?? ""
            : "";
        FamiliarBlessingEffectRuntime.AfterDamage(target, amount, sourceStatusId);
        if (generatedDamageDepth > 0 || owner?.Self == null
            || !string.Equals(owner.Self.InstanceId, sourceStatusId, StringComparison.Ordinal)
            || target.fatherObject is not Enemy
            || (target.CurHp >= snapshot.Hp && target.Defend >= snapshot.Defence))
        {
            return;
        }

        ApplyDamageTriggeredEffects(target, amount);
    }

    public static void BeforeEnemyDead(ModHookContext context)
    {
        var target = context.Target as IStatusManager;
        if (target == null)
        {
            return;
        }

        if (!DeathBurnSnapshots.TryGetValue(target, out var stack))
        {
            stack = new Stack<int>();
            DeathBurnSnapshots[target] = stack;
        }

        stack.Push(BuffApi.Level(target, TerriasIds.Burn));
    }

    public static void AfterEnemyDead(ModHookContext context)
    {
        var target = context.Target as IStatusManager;
        var burn = 0;
        if (target != null && DeathBurnSnapshots.TryGetValue(target, out var stack) && stack.Count > 0)
        {
            burn = stack.Pop();
        }

        ApplySoulOnKill();
        var multiplier = EffectAmount("EnemyDeathBurnTransfer");
        if (owner != null && burn > 0 && multiplier > 0)
        {
            var candidates = TargetApi.EnemyTargets(owner)
                .Where(candidate => !ReferenceEquals(candidate, target) && StatusApi.IsAlive(candidate))
                .ToList();
            if (candidates.Count > 0)
            {
                candidates[UnityEngine.Random.Range(0, candidates.Count)].AddBuff(TerriasIds.Burn, burn * multiplier);
            }
        }
    }

    public static void OnStarScoreCadenceCompleted(ScriptExecutor self)
    {
        var count = EffectAmount("StarScoreCadenceRandomOverture");
        for (var index = 0; index < count; index++)
        {
            CardApi.AddCardToHand(self, StarScoreService.RandomBlessingOvertureCardId());
        }
    }

    public static void OnStarlightCycle(ScriptExecutor self)
    {
        foreach (var effect in Effects("StarlightCycleBuffs"))
        {
            self.Self?.AddBuff(TerriasIds.SolarRadiance, ParameterInt(effect, "solarRadiance", 2));
            self.Self?.AddBuff(TerriasIds.Moonlight, ParameterInt(effect, "moonlight", 1));
        }

        foreach (var effect in Effects("StarlightCycleStarClayShape"))
        {
            var threshold = ParameterInt(effect, "maxHpThreshold", 50);
            if (StatusApi.MaxHp(self.Self) > threshold)
            {
                if (BuffApi.Level(self.Self, TerriasIds.StarClayBody) <= 0)
                {
                    self.Self?.AddBuff(TerriasIds.StarClayBody, Math.Max(1, effect.Amount));
                }
            }
            else
            {
                StatusApi.TryIncreaseMaxHp(self.Self, ParameterInt(effect, "maxHpGain", 100));
            }
        }
    }

    public static void ApplyBattleWinEffects()
    {
        if (!HasEffect("UnusedNetherChaseWinBlessing") || PlayerApi.GetSpecialVar("meowCount", "0") == "1")
        {
            return;
        }

        var owned = new HashSet<string>(RoleTable.Instance?.blessingConfigs?
            .Select(CardConfigApi.Id) ?? Enumerable.Empty<string>(), StringComparer.Ordinal);
        var candidates = Singleton<GameConfigManager>.Instance.CardPackCheck(TerriasConfigIndex.Rows(DataType.Bless))
            .Where(row => DictionaryUtil.GetInt(row, "Rarity") == 1)
            .Select(row => DictionaryUtil.Get(row, "Id"))
            .Where(id => !string.IsNullOrWhiteSpace(id)
                && !id.StartsWith("*", StringComparison.Ordinal)
                && !TerriasIds.IsTechnicalBlessingId(id))
            .Distinct(StringComparer.Ordinal)
            .ToList();
        var unowned = candidates.Where(id => !owned.Contains(id)).ToList();
        var pool = unowned.Count > 0 ? unowned : candidates;
        if (pool.Count > 0)
        {
            PlayerApi.AddBless(pool[UnityEngine.Random.Range(0, pool.Count)]);
        }
    }

    public static int EffectAmountFor(string kind) => EffectAmount(kind);

    public static int EffectParameterInt(string kind, string key, int fallback)
    {
        var effect = Effects(kind).FirstOrDefault();
        return effect == null ? fallback : ParameterInt(effect, key, fallback);
    }

    private static void ApplyDamageTriggeredEffects(IStatusManager target, int hitInput)
    {
        foreach (var effect in Effects("FirstDamageTargetBuffPerRound"))
        {
            var claim = "FirstDamageTargetBuffPerRound:" + effect.Value;
            if (RoundClaims.Add(claim))
            {
                target.AddBuff(effect.Value, Math.Max(1, effect.Amount));
            }
        }

        foreach (var effect in Effects("FirstDamageTrueEchoPerRound"))
        {
            if (RoundClaims.Add("FirstDamageTrueEchoPerRound"))
            {
                DealGeneratedDamage(target, Math.Max(1, hitInput * Math.Max(1, effect.Amount) / 100), "True");
            }
        }

        foreach (var effect in Effects("DamageTrueEchoByBuff"))
        {
            DealGeneratedDamage(target, BuffApi.Level(owner?.Self, effect.Value) * Math.Max(1, effect.Amount), "True");
        }

        foreach (var effect in Effects("DamageNormalEchoByBuff"))
        {
            DealGeneratedDamage(target, BuffApi.Level(owner?.Self, effect.Value) * Math.Max(1, effect.Amount), "");
        }
    }

    private static void DealGeneratedDamage(IStatusManager target, int amount, string damageType)
    {
        if (owner == null || amount <= 0)
        {
            return;
        }

        generatedDamageDepth++;
        try
        {
            DamageApi.DealDamageToTarget(owner, target, amount, "Target", damageType);
        }
        finally
        {
            generatedDamageDepth--;
        }
    }

    private static void OnCrowSettlement()
    {
        if (owner == null)
        {
            return;
        }

        var extraSettlements = EffectAmount("CrowExtraSettlement");
        var previousSettlements = crowSettlements;
        crowSettlements += 1 + extraSettlements;
        for (var count = 0; count < extraSettlements; count++)
        {
            foreach (var target in TargetApi.EnemyTargets(owner))
            {
                DealGeneratedDamage(target, 5, "True");
            }
        }

        foreach (var effect in Effects("CrowEveryNthSettlementHpDamage"))
        {
            var threshold = ParameterInt(effect, "threshold", 5);
            if (threshold <= 0)
            {
                continue;
            }

            var damage = Math.Max(ParameterInt(effect, "minimum", 1), (owner.Self?.CurHp ?? 0) * Math.Max(1, effect.Amount) / 100);
            var triggerCount = crowSettlements / threshold - previousSettlements / threshold;
            for (var trigger = 0; trigger < triggerCount; trigger++)
            {
                foreach (var target in TargetApi.EnemyTargets(owner))
                {
                    DealGeneratedDamage(target, damage, "True");
                }
            }
        }
    }

    private static void OnResurrectionEnd()
    {
        if (owner == null)
        {
            return;
        }

        BuffApi.RemoveNegativeBuffs(owner, owner.Self);
        PlayerPowerApi.TryRestoreToMax();
        CombatCardApi.TryDrawPlayerCards(Math.Max(1, EffectAmount("AfterResurrectionRecovery")), "FamiliarGrowth.Resurrection");
    }

    private static void OnBuffAdded(AddBuffData data)
    {
        if (!string.Equals(ReadString(data, "dataFromid"), "blessing_40", StringComparison.Ordinal))
        {
            return;
        }

        foreach (var effect in Effects("CompositeDoomProcPlayerBuff"))
        {
            owner?.Self?.AddBuff(effect.Value, Math.Max(1, effect.Amount));
        }

        foreach (var effect in Effects("CompositeDoomProcRandomEnemyBundle"))
        {
            var target = TargetApi.RandomEnemyTarget(owner, requireBurn: false);
            if (target == null)
            {
                continue;
            }

            foreach (var buff in Parameter(effect, "buffs", "").Split(',').Where(value => !string.IsNullOrWhiteSpace(value)))
            {
                target.AddBuff(buff.Trim(), Math.Max(1, effect.Amount));
            }
        }
    }

    private static void ApplyCombatStartCards(ScriptExecutor self)
    {
        foreach (var effect in Effects("CombatStartCard"))
        {
            for (var count = 0; count < Math.Max(1, effect.Amount); count++)
            {
                CardApi.AddCardToHand(self, effect.Value);
            }
        }

        foreach (var effect in Effects("CombatStartModifiedCard"))
        {
            for (var count = 0; count < Math.Max(1, effect.Amount); count++)
            {
                CardApi.GrantCardToHand(self, CardGrantRequest.ToHand(effect.Value)
                    .WithRuntimeTags(Parameter(effect, "runtimeTags", "Retain,Burnout").Split(','))
                    .WithSource("FamiliarGrowth.ModifiedCard"));
            }
        }
    }

    private static void ApplyCombatStartRandomModification(IStatusManager status)
    {
        if (!HasEffect("CombatStartRandomModification"))
        {
            return;
        }

        switch (UnityEngine.Random.Range(0, 3))
        {
            case 0:
                status.AddBuff(TerriasIds.Extraordinary, 100);
                break;
            case 1:
                StatusApi.TryHeal(status, Math.Max(1, StatusApi.MaxHp(status) / 10));
                break;
            default:
                if (status.MirrorSc is ScriptExecutor executor)
                {
                    executor.SetStatus("Self");
                    executor.ChangePower("2");
                }
                else
                {
                    PlayerPowerApi.TryGainPower(2);
                }
                break;
        }
    }

    private static void ApplyNetherChaseBonus(IStatusManager status)
    {
        var bonus = EffectAmount("NetherChaseRebirthBonus");
        if (bonus > 0 && PlayerApi.GetSpecialVar("meowCount", "0") != "1")
        {
            status.AddBuff("buff_rebirth", bonus);
        }
    }

    private static void RestoreSoulDisplay(IStatusManager status)
    {
        if (!HasEffect("EnemyDeathPersistentSoul"))
        {
            return;
        }

        var stored = Math.Max(0, DictionaryUtil.ParseInt(PlayerApi.GetSpecialVar(SoulVar, "0")));
        if (stored > 0)
        {
            BuffApi.SetExactLevel(status, SoulBuff, stored);
        }
    }

    private static void ApplySoulOnKill()
    {
        var gain = EffectAmount("EnemyDeathPersistentSoul");
        if (owner?.Self == null || gain <= 0)
        {
            return;
        }

        var next = Math.Max(0, DictionaryUtil.ParseInt(PlayerApi.GetSpecialVar(SoulVar, "0"))) + gain;
        PlayerApi.SetSpecialVar(SoulVar, next.ToString());
        BuffApi.SetExactLevel(owner.Self, SoulBuff, next);
    }

    private static bool IsPocketCard(IDataConfig config)
    {
        return CardMutationService.HasRuntimeMarker(config, PocketMarker)
            || DictionaryUtil.GetInt(config.Vars, "TotalExCost") <= -999
               && (DictionaryUtil.ContainsToken(DictionaryUtil.Get(config.Vars, "Tag"), "Burnout")
                   || DictionaryUtil.ContainsToken(DictionaryUtil.Get(config.data, "Tag"), "Burnout"));
    }

    private static void GrantRandomPocketCard()
    {
        if (owner == null)
        {
            return;
        }

        var rows = Singleton<GameConfigManager>.Instance.CardPackCheck(TerriasConfigIndex.Rows(DataType.Card))
            .Where(row => DictionaryUtil.GetInt(row, "Rarity") is >= 1 and <= 3)
            .Where(row => !DictionaryUtil.ContainsToken(DictionaryUtil.Get(row, "Tag"), "Curse"))
            .Select(row => DictionaryUtil.Get(row, "Id"))
            .Where(id => !string.IsNullOrWhiteSpace(id) && !id.StartsWith("*", StringComparison.Ordinal))
            .ToList();
        if (rows.Count == 0)
        {
            return;
        }

        CardApi.GrantCardToHand(owner, CardGrantRequest.ToHand(rows[UnityEngine.Random.Range(0, rows.Count)])
            .WithRuntimeTags("Burnout")
            .Configure(CardMutationService.SetRuntimeMarkersMutation(PocketMarker))
            .Configure(CardMutationService.SetTemporaryCostMutation(0))
            .WithSource("FamiliarGrowth.PocketCard"));
    }

    private static IEnumerable<FamiliarBlessingEffect> Effects(string kind)
    {
        var active = FamiliarGrowthService.Active();
        return active == null
            ? Enumerable.Empty<FamiliarBlessingEffect>()
            : FamiliarGrowthService.BlessingsFor(active)
                .SelectMany(blessing => blessing.Effects)
                .Where(effect => string.Equals(effect.Kind, kind, StringComparison.OrdinalIgnoreCase));
    }

    private static bool HasEffect(string kind) => Effects(kind).Any();

    private static int EffectAmount(string kind) => Effects(kind).Sum(effect => Math.Max(0, effect.Amount));

    private static string Parameter(FamiliarBlessingEffect effect, string key, string fallback)
    {
        return effect.Parameters.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value) ? value : fallback;
    }

    private static int ParameterInt(FamiliarBlessingEffect effect, string key, int fallback)
    {
        return DictionaryUtil.ParseInt(Parameter(effect, key, fallback.ToString()), fallback);
    }

    private static int HitAmount(object[]? args)
    {
        try
        {
            return args == null || args.Length == 0 ? 0 : Math.Max(0, Convert.ToInt32(args[0]));
        }
        catch
        {
            return 0;
        }
    }

    private static string ReadString(object source, string name)
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        return Convert.ToString(source.GetType().GetProperty(name, flags)?.GetValue(source)
            ?? source.GetType().GetField(name, flags)?.GetValue(source)) ?? "";
    }

    private readonly struct HitSnapshot
    {
        public HitSnapshot(int hp, int defence)
        {
            Hp = hp;
            Defence = defence;
        }

        public int Hp { get; }
        public int Defence { get; }
    }
}
