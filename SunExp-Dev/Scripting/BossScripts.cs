using System;
using SunExp.Dll.GameApi;
using SunExp.Dll.Infrastructure;

namespace SunExp.Dll.Scripting;

public static class BossScripts
{
    private const int MirrorArrayBurn = 2;
    private const int MirrorArrayBlockPerBurningTarget = 4;
    private const int MercilessDaylightBurnThreshold = 8;
    private const int MercilessDaylightBodyBurn = 2;
    private const int MercilessDaylightFlame = 2;
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
                    PlayerApi.SetGameVar(SunExpIds.SolarFinaleSecondSunDefeatedKey, "0");
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
                    ExecutorApi.DealDamage(self, 20);
                    self.AddBuff(SunExpIds.Burn, "10");
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
                    ExecutorApi.DealDamage(self, 18);
                    self.AddBuff("buff_weak", "2");
                    break;
                case "saint_purification":
                    ExecutorApi.DealDamage(self, 14);
                    self.AddBuff(SunExpIds.BodyBurn, "2");
                    break;
                case "saint_return_to_court":
                    MoveSavedNameToNameless();
                    ExecutorApi.DealDamage(self, 12);
                    break;
                default:
                    ExecutorApi.DealDamage(self, 10);
                    break;
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
            "last_day_noon_burn" => new BossCardSpec(1, 2, "Damage", 18),
            "saint_purification" => new BossCardSpec(0, 1, "Damage", 14),
            "saint_return_to_court" => new BossCardSpec(2, 2, "Damage", 12),
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

        self.SetStatus("Self");
        self.AddBuff(SunExpIds.GatheredFlame, MercilessDaylightFlame.ToString());
    }

    private static void TriggerWhiteRadianceSaint(ScriptExecutor self)
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
        var saved = Math.Max(0, DictionaryUtil.ParseInt(PlayerApi.GetGameVar(SunExpIds.SolarFinaleSavedNamesKey, "0")));
        if (saved <= 0)
        {
            return false;
        }

        var nameless = Math.Max(0, DictionaryUtil.ParseInt(PlayerApi.GetGameVar(SunExpIds.SolarFinaleNamelessNamesKey, "0")));
        PlayerApi.SetGameVar(SunExpIds.SolarFinaleSavedNamesKey, (saved - 1).ToString());
        PlayerApi.SetGameVar(SunExpIds.SolarFinaleNamelessNamesKey, (nameless + 1).ToString());
        return true;
    }

    private static bool MoveSavedNameToBurned()
    {
        var saved = Math.Max(0, DictionaryUtil.ParseInt(PlayerApi.GetGameVar(SunExpIds.SolarFinaleSavedNamesKey, "0")));
        if (saved <= 0)
        {
            return false;
        }

        var burned = Math.Max(0, DictionaryUtil.ParseInt(PlayerApi.GetGameVar(SunExpIds.SolarFinaleBurnedNamesKey, "0")));
        PlayerApi.SetGameVar(SunExpIds.SolarFinaleSavedNamesKey, (saved - 1).ToString());
        PlayerApi.SetGameVar(SunExpIds.SolarFinaleBurnedNamesKey, (burned + 1).ToString());
        return true;
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
