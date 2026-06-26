using System;
using System.Collections.Generic;
using System.Linq;
using SunExp.Dll.GameApi;
using SunExp.Dll.Infrastructure;
using SunExp.Dll.Mechanics;

namespace SunExp.Dll.Scripting;

public static class BossScripts
{
    private const int MirrorArrayBurn = 2;
    private const int MirrorArrayBlockPerBurningTarget = 4;
    private const int MercilessDaylightBurnThreshold = 8;
    private const int MercilessDaylightBodyBurn = 5;
    private const int LastDayNoonDamage = 28;
    private const int LastDayNoonWeak = 2;
    private const int LastDayNoonCripple = 2;
    private const int SaintPurificationDamage = 20;
    private const int SaintPurificationBodyBurn = 3;
    private const int SaintPurificationRadiance = 1;
    private const int SaintReturnDamage = 18;
    private const int SaintReturnRadianceNoName = 6;
    private const int SaintCoronationRadianceThreshold = 12;
    private const int SaintCrownMaxTier = 5;
    private const int SaintCrownExtraordinaryPerTier = 8;
    private const int SaintCrownAnnihilateCount = 3;
    private const int WhiteRadianceSaintBlock = 12;

    public static void InitEnemy(ScriptExecutor self, string bossId)
    {
        try
        {
            switch (bossId)
            {
                case "orbit_mirror_array":
                    ApplyBossTraitBuff(self, SunExpIds.BossTraitMirrorArray);
                    break;
                case "second_sun_last_day":
                    ApplyBossTraitBuff(self, SunExpIds.BossTraitMercilessDaylight);
                    break;
                case "saint_wuna":
                    ApplyBossTraitBuff(self, SunExpIds.BossTraitWhiteRadianceSaint);
                    break;
            }
        }
        catch (Exception ex)
        {
            SunExpLog.Error("Boss enemy init failed: " + bossId, ex);
        }
    }

    public static void ApplyTrait(ScriptExecutor self, string traitId)
    {
        try
        {
            switch (traitId)
            {
                case "boss_trait_mirror_array":
                    RegisterTraitStartRound(self, traitId, SunExpIds.BossTraitMirrorArray, TriggerMirrorArray);
                    break;
                case "boss_trait_merciless_daylight":
                    RegisterTraitStartRound(self, traitId, SunExpIds.BossTraitMercilessDaylight, TriggerMercilessDaylight);
                    break;
                case "boss_trait_white_radiance_saint":
                    RegisterTraitStartRound(self, traitId, SunExpIds.BossTraitWhiteRadianceSaint, TriggerWhiteRadianceSaint);
                    break;
            }
        }
        catch (Exception ex)
        {
            SunExpLog.Error("Boss trait apply failed: " + traitId, ex);
        }
    }

    public static void ClearTrait(ScriptExecutor self, string traitId)
    {
        try
        {
            ExecutorApi.ClearHook(self, TraitHookKey(traitId), TraitTokenKey(traitId));
        }
        catch (Exception ex)
        {
            SunExpLog.Error("Boss trait clear failed: " + traitId, ex);
        }
    }

    public static void InitCard(ScriptExecutor self, string cardId)
    {
        try
        {
            var spec = Spec(cardId);
            self.Vars["CD"] = spec.Cooldown.ToString();
            self.Vars["priority"] = spec.Priority.ToString();
            self.AddDescription("1", spec.DescriptionType, spec.DescriptionValue.ToString());
        }
        catch (Exception ex)
        {
            SunExpLog.Error("Boss card init failed: " + cardId, ex);
        }
    }

    public static void Target(ScriptExecutor self, string target)
    {
        try
        {
            self.SetStatus(string.IsNullOrWhiteSpace(target) ? "Target" : target);
        }
        catch (Exception ex)
        {
            SunExpLog.Error("Boss card target failed: " + target, ex);
        }
    }

    public static void UseCard(ScriptExecutor self, string cardId)
    {
        try
        {
            var isSaintAction = IsSaintAction(cardId);
            switch (cardId)
            {
                case "mirror_calibration":
                    self.SetStatus("All");
                    self.AddBuff(SunExpIds.Burn, "5");
                    self.SetStatus("Self");
                    self.ChangeDefence("10");
                    break;
                case "orbit_refraction":
                    var target = ExecutorApi.PrimaryTarget(self);
                    var hadBurn = ExecutorApi.StatusBuffLevel(target, SunExpIds.Burn) > 0;
                    ExecutorApi.DealDamageToTarget(self, target, 20);
                    ExecutorApi.AddStatusBuff(self, target, SunExpIds.Burn, 10);
                    if (hadBurn && ExecutorApi.SelfBuffLevel(self, SunExpIds.BossTraitMirrorArray) > 0)
                    {
                        ExecutorApi.TriggerBurn(self, target);
                    }

                    break;
                case "last_day_morning_prayer":
                    self.SetStatus("All");
                    self.AddBuff(SunExpIds.Burn, "5");
                    self.SetStatus("Self");
                    self.AddBuff(SunExpIds.GatheredFlame, "10");
                    break;
                case "last_day_noon_burn":
                    var noonTarget = ExecutorApi.PrimaryTarget(self);
                    ExecutorApi.DealDamageToTarget(self, noonTarget, LastDayNoonDamage);
                    if (ExecutorApi.StatusBuffLevel(noonTarget, SunExpIds.Burn) >= MercilessDaylightBurnThreshold)
                    {
                        ExecutorApi.TriggerBurn(self, noonTarget);
                        ExecutorApi.AddStatusBuff(self, noonTarget, "buff_weak", LastDayNoonWeak);
                        ExecutorApi.AddStatusBuff(self, noonTarget, SunExpIds.Cripple, LastDayNoonCripple);
                    }

                    break;
                case "saint_purification":
                    UseSaintPurification(self);
                    break;
                case "saint_return_to_court":
                    UseSaintReturnToCourt(self);
                    break;
                case "saint_white_edict":
                    UseSaintPurification(self);
                    break;
                default:
                    ExecutorApi.DealDamageToTarget(self, ExecutorApi.PrimaryTarget(self), 10);
                    break;
            }

            if (isSaintAction)
            {
                ResolveWhiteRadianceAfterAction(self);
            }
        }
        catch (Exception ex)
        {
            SunExpLog.Error("Boss card use failed: " + cardId, ex);
        }
    }

    private static BossCardSpec Spec(string cardId)
    {
        return cardId switch
        {
            "mirror_calibration" => new BossCardSpec(0, 1, "Buff", 5),
            "orbit_refraction" => new BossCardSpec(2, 2, "Damage", 20),
            "last_day_morning_prayer" => new BossCardSpec(0, 1, "Buff", 5),
            "last_day_noon_burn" => new BossCardSpec(1, 2, "Damage", LastDayNoonDamage),
            "saint_purification" => new BossCardSpec(0, 1, "Damage", SaintPurificationDamage),
            "saint_return_to_court" => new BossCardSpec(2, 2, "Damage", SaintReturnDamage),
            "saint_white_edict" => new BossCardSpec(0, 3, "Damage", SaintPurificationDamage),
            _ => new BossCardSpec(0, 1, "Damage", 10)
        };
    }

    private static void ApplyBossTraitBuff(ScriptExecutor self, string buffId)
    {
        self.SetStatus("Self");
        self.AddBuff(buffId, "1");
    }

    private static void RegisterTraitStartRound(
        ScriptExecutor self,
        string traitId,
        string fullBuffId,
        Action<ScriptExecutor> trigger)
    {
        var hookKey = TraitHookKey(traitId);
        var tokenKey = TraitTokenKey(traitId);
        var token = ExecutorApi.RegisterHook(self, hookKey, tokenKey);
        if (token == null)
        {
            return;
        }

        ExecutorApi.TryAddEvent(self, "StartRound", new Action(() =>
        {
            if (!ExecutorApi.IsHookTokenActive(self, tokenKey, token)
                || ExecutorApi.SelfBuffLevel(self, fullBuffId) <= 0)
            {
                return;
            }

            trigger(self);
        }), "Boss trait " + traitId);
    }

    private static string TraitHookKey(string traitId)
    {
        return "SunExpBossTrait_" + traitId + "Hook";
    }

    private static string TraitTokenKey(string traitId)
    {
        return "SunExpBossTrait_" + traitId + "Token";
    }

    private static void TriggerMirrorArray(ScriptExecutor self)
    {
        var targets = ExecutorApi.EnemyTargets(self);
        if (targets.Count <= 0)
        {
            return;
        }

        var burningTargets = 0;
        foreach (var target in targets)
        {
            if (ExecutorApi.StatusBuffLevel(target, SunExpIds.Burn) > 0)
            {
                burningTargets++;
            }
        }

        if (burningTargets <= 0)
        {
            foreach (var target in targets)
            {
                ExecutorApi.AddStatusBuff(self, target, SunExpIds.Burn, MirrorArrayBurn);
            }

            return;
        }

        ChangeSelfDefence(self, burningTargets * MirrorArrayBlockPerBurningTarget);
    }

    private static void TriggerMercilessDaylight(ScriptExecutor self)
    {
        var targets = ExecutorApi.EnemyTargets(self);
        var burnTotal = 0;
        foreach (var target in targets)
        {
            burnTotal += ExecutorApi.StatusBuffLevel(target, SunExpIds.Burn);
        }

        if (burnTotal < MercilessDaylightBurnThreshold)
        {
            return;
        }

        if (MoveSavedNameToBurned())
        {
            PlayerApi.ShowCaption("第二日轮焚毁了一个保存名字。");
        }
        else
        {
            foreach (var target in targets)
            {
                ExecutorApi.AddStatusBuff(self, target, SunExpIds.BodyBurn, MercilessDaylightBodyBurn);
            }

            PlayerApi.ShowCaption("无名可焚，白昼转为焚身压力。");
        }
    }

    private static void TriggerWhiteRadianceSaint(ScriptExecutor self)
    {
        if (ExecutorApi.SelfBuffLevel(self, SunExpIds.BodyBurn) <= 0 && MoveSavedNameToNameless())
        {
            var shield = Math.Max(1, ExecutorApi.StatusMaxHp(self.Self) / 10);
            ChangeSelfDefence(self, shield);
            PlayerApi.ShowCaption("白曜圣女将一个保存名字铭刻为无名之人。");
        }

        var crownedBefore = IsWhiteRadianceCrowned(self);
        if (!EnsureWhiteRadianceCoronation(self))
        {
            return;
        }

        if (crownedBefore)
        {
            SetWhiteRadianceTier(self, WhiteRadianceTier(self) + 1);
        }

        ResolveWhiteRadianceStartRound(self);
    }

    private static void TriggerWhiteRadianceSaintLegacy(ScriptExecutor self)
    {
        if (ExecutorApi.SelfBuffLevel(self, SunExpIds.Burn) > 0
            || ExecutorApi.SelfBuffLevel(self, SunExpIds.BodyBurn) > 0)
        {
            return;
        }

        if (!MoveSavedNameToNameless())
        {
            return;
        }

        ChangeSelfDefence(self, WhiteRadianceSaintBlock);
        PlayerApi.ShowCaption("白曜圣女将一个保存名字铭刻为无名之人。");
    }

    private static bool IsSaintAction(string cardId)
    {
        return cardId == "saint_purification"
            || cardId == "saint_return_to_court"
            || cardId == "saint_white_edict";
    }

    private static void UseSaintPurification(ScriptExecutor self)
    {
        var target = ExecutorApi.PrimaryTarget(self);
        ExecutorApi.DealDamageToTarget(self, target, SaintPurificationDamage);
        ExecutorApi.AddStatusBuff(self, target, SunExpIds.BodyBurn, SaintPurificationBodyBurn);
        ExecutorApi.RemoveAllPositiveBuffs(self, target);
        AddSelfBuff(self, SunExpIds.SolarRadiance, SaintPurificationRadiance);
        EnsureWhiteRadianceCoronation(self);
    }

    private static void UseSaintReturnToCourt(ScriptExecutor self)
    {
        ExecutorApi.DealDamageToTarget(self, ExecutorApi.PrimaryTarget(self), SaintReturnDamage);
        if (!MoveSavedNameToNameless())
        {
            AddSelfBuff(self, SunExpIds.SolarRadiance, SaintReturnRadianceNoName);
        }

        EnsureWhiteRadianceCoronation(self);
    }

    private static void AddSelfBuff(ScriptExecutor self, string buffId, int amount)
    {
        if (amount <= 0)
        {
            return;
        }

        self.SetStatus("Self");
        self.AddBuff(buffId, amount.ToString());
    }

    private static bool IsWhiteRadianceCrowned(ScriptExecutor self)
    {
        return ExecutorApi.SelfBuffLevel(self, SunExpIds.BossWhiteRadianceCrown) > 0
            || ExecutorApi.GetVar(self, "SunExpBossWhiteRadianceCrowned", "0") == "1";
    }

    private static bool EnsureWhiteRadianceCoronation(ScriptExecutor self)
    {
        if (IsWhiteRadianceCrowned(self))
        {
            return true;
        }

        if (ExecutorApi.SelfBuffLevel(self, SunExpIds.SolarRadiance) < SaintCoronationRadianceThreshold)
        {
            return false;
        }

        ExecutorApi.SetVar(self, "SunExpBossWhiteRadianceCrowned", "1");
        SetWhiteRadianceTier(self, 1);
        PlayerApi.ShowCaption("白曜圣女进入【圣冕显化·白曜】。");
        return true;
    }

    private static int WhiteRadianceTier(ScriptExecutor self)
    {
        return Math.Max(0, ExecutorApi.SelfBuffLevel(self, SunExpIds.BossWhiteRadianceCrown));
    }

    private static int SetWhiteRadianceTier(ScriptExecutor self, int tier)
    {
        var next = Math.Max(1, Math.Min(SaintCrownMaxTier, tier));
        self.SetStatus("Self");
        self.RemoveBuff(SunExpIds.BossWhiteRadianceCrown);
        self.AddBuff(SunExpIds.BossWhiteRadianceCrown, next.ToString());
        return next;
    }

    private static void ResolveWhiteRadianceStartRound(ScriptExecutor self)
    {
        var tier = WhiteRadianceTier(self);
        if (tier <= 0)
        {
            return;
        }

        AddSelfBuff(self, SunExpIds.Extraordinary, tier * SaintCrownExtraordinaryPerTier);
        foreach (var target in ExecutorApi.EnemyTargets(self))
        {
            ExecutorApi.AddStatusBuff(self, target, SunExpIds.Burn, tier, "AllTarget");
        }

        if (tier >= 1)
        {
            var negativeTotal = ExecutorApi.NegativeBuffTotal(self.Self);
            if (negativeTotal > 0 && ExecutorApi.RemoveAllNegativeBuffs(self, self.Self))
            {
                AddSelfBuff(self, SunExpIds.Ember, negativeTotal);
            }
        }

        if (tier >= 2)
        {
            AddWhiteRadianceExtraAction(self);
        }

        if (tier >= 3)
        {
            ExecutorApi.TriggerBurnAllEnemies(self);
        }

        if (tier >= 4)
        {
            AnnihilateRandomPlayerCards(self, SaintCrownAnnihilateCount);
        }
    }

    private static void AddWhiteRadianceExtraAction(ScriptExecutor self)
    {
        if (ExecutorApi.GetVar(self, "SunExpBossWhiteRadianceExtraActionQueued", "0") == "1")
        {
            return;
        }

        if (ExecutorApi.AddEnemyAction(self, SunExpIds.EnemyCardSaintWhiteEdict))
        {
            ExecutorApi.SetVar(self, "SunExpBossWhiteRadianceExtraActionQueued", "1");
            ExecutorApi.TryAddTempEvent(self, "EndRound", new Action(() =>
            {
                ExecutorApi.SetVar(self, "SunExpBossWhiteRadianceExtraActionQueued", "0");
            }), "boss_white_radiance_extra_action");
        }
    }

    private static void AnnihilateRandomPlayerCards(ScriptExecutor self, int count)
    {
        var localLockKey = LocalAnnihilationLockKey();
        if (count <= 0 || ExecutorApi.GetVar(self, localLockKey, "0") == "1")
        {
            return;
        }

        var pool = BuildAnnihilationPool(self);
        var remaining = Math.Min(count, pool.Count);
        for (var i = 0; i < remaining; i++)
        {
            var index = UnityEngine.Random.Range(0, pool.Count);
            var card = pool[index];
            pool.RemoveAt(index);
            if (card != null)
            {
                self.BurnCardByData(card);
            }
        }

        if (remaining > 0)
        {
            ExecutorApi.SetVar(self, localLockKey, "1");
            ExecutorApi.TryAddTempEvent(self, "EndRound", new Action(() =>
            {
                ExecutorApi.SetVar(self, localLockKey, "0");
            }), "boss_white_radiance_annihilation");
        }
    }

    private static string LocalAnnihilationLockKey()
    {
        var statusId = PlayerApi.LocalPlayerStatusId();
        return string.IsNullOrWhiteSpace(statusId)
            ? "SunExpBossWhiteRadianceAnnihilatedThisRound_Local"
            : "SunExpBossWhiteRadianceAnnihilatedThisRound_" + statusId;
    }

    private static List<IDataConfig> BuildAnnihilationPool(ScriptExecutor self)
    {
        var cards = new List<IDataConfig>();
        foreach (var card in self.HandCard ?? Enumerable.Empty<CardItem>())
        {
            if (card?.dataConfig != null)
            {
                cards.Add(card.dataConfig);
            }
        }

        cards.AddRange((self.DeckCard ?? new List<DataConfig>()).Where(card => card != null));
        cards.AddRange((self.UsedCard ?? new List<DataConfig>()).Where(card => card != null));
        return cards;
    }

    private static void ResolveWhiteRadianceAfterAction(ScriptExecutor self)
    {
        if (WhiteRadianceTier(self) < SaintCrownMaxTier)
        {
            return;
        }

        ExecutorApi.DealTrueDamageAllEnemiesByMaxHp(self);
    }

    private static void ChangeSelfDefence(ScriptExecutor self, int amount)
    {
        if (amount <= 0)
        {
            return;
        }

        self.SetStatus("Self");
        self.ChangeDefence(amount.ToString());
    }

    private static bool MoveSavedNameToNameless()
    {
        return SolarFinaleStateService.MakeNameless(1) > 0;
    }

    private static bool MoveSavedNameToBurned()
    {
        return SolarFinaleStateService.BurnNames(1) > 0;
    }

    private readonly struct BossCardSpec
    {
        public BossCardSpec(int cooldown, int priority, string descriptionType, int descriptionValue)
        {
            Cooldown = cooldown;
            Priority = priority;
            DescriptionType = descriptionType;
            DescriptionValue = descriptionValue;
        }

        public int Cooldown { get; }

        public int Priority { get; }

        public string DescriptionType { get; }

        public int DescriptionValue { get; }
    }
}
