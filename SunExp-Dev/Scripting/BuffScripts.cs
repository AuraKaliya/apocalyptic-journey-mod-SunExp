using System;
using System.Collections.Generic;
using SunExp.Dll.GameApi;
using SunExp.Dll.Infrastructure;
using SunExp.Dll.Mechanics;

namespace SunExp.Dll.Scripting;

public static class BuffScripts
{
    private static readonly Dictionary<string, Action<ScriptExecutor>> ApplyHandlers = new(StringComparer.Ordinal)
    {
        ["solar_radiance"] = ApplySolarRadiance,
        ["gathered_flame"] = ApplyGatheredFlame,
        ["scorching_canopy"] = ApplyScorchingCanopy,
        ["body_burn"] = ApplyBodyBurn,
        ["ember"] = ApplyEmber,
        ["ember_cloak"] = ApplyEmberCloak,
        ["solar_crown"] = ApplySolarCrown,
        ["origin_core_radiance"] = ApplyOriginCoreRadiance,
        ["cycle_gathered_flame"] = ApplyCycleGatheredFlame,
        ["afterglow_omen"] = ApplyAfterglowOmen,
        ["star_stone_pouch"] = ApplyStarStonePouch,
        ["star_score"] = ApplyStarScore,
        ["star_stage"] = ApplyStarStage,
        ["abyss_blessing"] = EndlessAbyssBlessingService.Apply,
        [SunExpIds.PolymorphTraitBuffShortId] = ApplyPolymorphTrait,
        [SunExpIds.HeartChangeBuffShortId] = ApplyHeartChange
    };

    private static readonly Dictionary<string, Action<ScriptExecutor>> ClearHandlers = new(StringComparer.Ordinal)
    {
        ["solar_radiance"] = ClearSolarRadiance,
        ["gathered_flame"] = ClearGatheredFlame,
        ["scorching_canopy"] = ClearScorchingCanopy,
        ["body_burn"] = ClearBodyBurn,
        ["ember"] = ClearEmber,
        ["ember_cloak"] = ClearEmberCloak,
        ["solar_crown"] = ClearSolarCrown,
        ["origin_core_radiance"] = ClearOriginCoreRadiance,
        ["cycle_gathered_flame"] = ClearCycleGatheredFlame,
        ["afterglow_omen"] = ClearAfterglowOmen,
        ["star_stone_pouch"] = ClearStarStonePouch,
        ["star_score"] = ClearStarScore,
        ["star_stage"] = ClearStarStage,
        ["abyss_blessing"] = EndlessAbyssBlessingService.Clear,
        [SunExpIds.PolymorphTraitBuffShortId] = ClearPolymorphTrait,
        [SunExpIds.HeartChangeBuffShortId] = ClearHeartChange
    };

    private static readonly HashSet<string> BossTraitIds = new(StringComparer.Ordinal)
    {
        "boss_trait_mirror_array",
        "boss_trait_merciless_daylight",
        "boss_trait_white_radiance_saint"
    };

    public static void Apply(ScriptExecutor self, string id)
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(id) && ApplyHandlers.TryGetValue(id, out var handler))
            {
                handler(self);
                return;
            }

            if (!string.IsNullOrWhiteSpace(id) && BossTraitIds.Contains(id))
            {
                BossScripts.ApplyTrait(self, id);
            }
        }
        catch (Exception ex)
        {
            SunExpLog.Error("Buff Apply failed: " + id, ex);
        }
    }

    public static void Clear(ScriptExecutor self, string id)
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(id) && ClearHandlers.TryGetValue(id, out var handler))
            {
                handler(self);
                return;
            }

            if (!string.IsNullOrWhiteSpace(id) && BossTraitIds.Contains(id))
            {
                BossScripts.ClearTrait(self, id);
            }
        }
        catch (Exception ex)
        {
            SunExpLog.Error("Buff Clear failed: " + id, ex);
        }
    }

    private static void ClearSolarRadiance(ScriptExecutor self)
    {
        ExecutorApi.ClearHook(self, "SunExpSolarRadianceHook", "SunExpSolarRadianceToken");
    }

    private static void ClearGatheredFlame(ScriptExecutor self)
    {
        ExecutorApi.ClearHook(self, "SunExpGatheredFlameHook", "SunExpGatheredFlameToken");
    }

    private static void ClearBodyBurn(ScriptExecutor self)
    {
        ExecutorApi.ClearHook(self, "SunExpBodyBurnHook", "SunExpBodyBurnToken");
    }

    private static void ClearEmber(ScriptExecutor self)
    {
        BuffApi.ClearEmberDamageBonus(self, self?.Self);
        ExecutorApi.ClearHook(self, "SunExpEmberHook", "SunExpEmberToken");
    }

    private static void ClearEmberCloak(ScriptExecutor self)
    {
        ExecutorApi.ClearHook(self, "SunExpBurnWardHook", "SunExpBurnWardToken");
        ExecutorApi.SetVar(self, "SunExpBurnWardPending", "0");
    }

    private static void ClearOriginCoreRadiance(ScriptExecutor self)
    {
        ExecutorApi.ClearHook(self, "SunExpMiniCoronaHook", "SunExpMiniCoronaToken");
        ExecutorApi.SetVar(self, "SunExpMiniCoronaDone", "0");
    }

    private static void ClearCycleGatheredFlame(ScriptExecutor self)
    {
        ExecutorApi.ClearHook(self, "SunExpMeltingWheelHook", "SunExpMeltingWheelToken");
        ExecutorApi.SetVar(self, "SunExpMeltingWheelLastBurn", "0");
    }

    private static void ClearAfterglowOmen(ScriptExecutor self)
    {
        ExecutorApi.ClearHook(self, "SunExpAfterglowHook", "SunExpAfterglowToken");
    }

    private static void ClearStarStonePouch(ScriptExecutor self)
    {
        StarStonePouchService.Clear(self);
    }

    private static void ClearStarScore(ScriptExecutor self)
    {
        StarScoreService.ClearScoreBuff(self);
    }

    private static void ClearStarStage(ScriptExecutor self)
    {
        MorningStarOvertureService.ClearStarStage(self);
    }

    private static void ApplyPolymorphTrait(ScriptExecutor self)
    {
        PolymorphBuffService.Apply(self);
    }

    private static void ClearPolymorphTrait(ScriptExecutor self)
    {
        PolymorphBuffService.Clear(self);
    }

    private static void ApplyHeartChange(ScriptExecutor self)
    {
        HeartChangeControlService.Apply(self);
    }

    private static void ClearHeartChange(ScriptExecutor self)
    {
        HeartChangeControlService.Clear(self, "BuffScripts.Clear");
    }

    private static void ApplySolarRadiance(ScriptExecutor self)
    {
        var token = ExecutorApi.RegisterHook(self, "SunExpSolarRadianceHook", "SunExpSolarRadianceToken");
        if (token == null)
        {
            return;
        }

        ExecutorApi.TryAddTokenedEvent(self, "Action", "SunExpSolarRadianceToken", token, new Action(() =>
        {
            var level = ExecutorApi.SelfBuffLevel(self, SunExpIds.SolarRadiance);
            var gain = level * 5;
            if (gain <= 0)
            {
                return;
            }

            self.SetStatus("Self");
            self.AddBuff("buff_extraordinary", gain.ToString());
        }), "solar_radiance");
    }

    private static void ApplyStarStonePouch(ScriptExecutor self)
    {
        StarStonePouchService.Apply(self);
    }

    private static void ApplyStarScore(ScriptExecutor self)
    {
        StarScoreService.ApplyScoreBuff(self);
    }

    private static void ApplyStarStage(ScriptExecutor self)
    {
        MorningStarOvertureService.ApplyStarStage(self);
    }

    private static void ApplyGatheredFlame(ScriptExecutor self)
    {
        var token = ExecutorApi.RegisterHook(self, "SunExpGatheredFlameHook", "SunExpGatheredFlameToken");
        if (token == null)
        {
            return;
        }

        ExecutorApi.TryAddTokenedEvent(self, "StartRound", "SunExpGatheredFlameToken", token, new Action(() =>
        {
            var count = ExecutorApi.SelfBuffLevel(self, SunExpIds.GatheredFlame);
            if (count <= 0)
            {
                return;
            }

            ExecutorApi.ApplySelfBurn(self, count, true);
            self.SetStatus("Self");
            self.AddBuff("buff_extraordinary", (count * 10).ToString());
        }), "gathered_flame");
    }

    private static void ApplyScorchingCanopy(ScriptExecutor self)
    {
        if (self == null)
        {
            return;
        }

        var carrierStacks = Math.Max(1, ExecutorApi.SelfBuffLevel(self, SunExpIds.ScorchingCanopy));
        ExecutorApi.ActivateField(self, SunExpFieldId.ScorchingCanopy, carrierStacks, "carrier.scorching_canopy");

        self.SetStatus("Self");
        self.RemoveBuff(SunExpIds.ScorchingCanopy);
        SunExpLog.Debug("Scorching canopy carrier converted to field: carrierStacks="
            + carrierStacks
            + ", fieldStacks=" + ExecutorApi.FieldStacks(SunExpFieldId.ScorchingCanopy));
    }

    private static void ClearScorchingCanopy(ScriptExecutor self)
    {
        SunExpLog.Debug("Scorching canopy carrier clear ignored; field state is cleared only through FieldApi.TryClearActiveField.");
    }

    private static void ApplyBodyBurn(ScriptExecutor self)
    {
        var token = ExecutorApi.RegisterHook(self, "SunExpBodyBurnHook", "SunExpBodyBurnToken");
        if (token == null)
        {
            return;
        }

        ExecutorApi.TryAddTokenedEvent(self, "StartRound", "SunExpBodyBurnToken", token, new Action(() =>
        {
            TriggerBodyBurn(self);
        }), "body_burn");
    }

    private static bool TriggerBodyBurn(ScriptExecutor self)
    {
        var level = ExecutorApi.SelfBuffLevel(self, SunExpIds.BodyBurn);
        if (level <= 0)
        {
            return false;
        }

        var damage = BodyBurnDamagePerStack(self.Self) * level;
        self.SetStatus("Self");
        if (damage > 0)
        {
            self.Damage(damage.ToString(), "True");
        }

        self.RemoveBuff(SunExpIds.BodyBurn);
        return true;
    }

    private static int BodyBurnDamagePerStack(IStatusManager? target)
    {
        return StatusApi.MaxHp(target) / 100 + 1;
    }

    private static void ApplyEmber(ScriptExecutor self)
    {
        var executor = self;
        if (executor == null)
        {
            return;
        }

        BuffApi.SyncEmberDamageBonus(executor, executor.Self);
        var token = ExecutorApi.RegisterHook(executor, "SunExpEmberHook", "SunExpEmberToken");
        if (token == null)
        {
            return;
        }

        void Sync()
        {
            if (ExecutorApi.IsHookTokenActive(executor, "SunExpEmberToken", token))
            {
                BuffApi.SyncEmberDamageBonus(executor, executor.Self);
            }
        }

        ExecutorApi.TryAddTokenedEvent(executor, "SunExp_sunexp_emberOnLevelChange", "SunExpEmberToken", token, new Action(Sync), "ember");
        ExecutorApi.TryAddTokenedEvent(executor, "emberOnLevelChange", "SunExpEmberToken", token, new Action(Sync), "ember");
        ExecutorApi.TryAddTokenedEvent(executor, "StartRound", "SunExpEmberToken", token, new Action(() =>
        {
            BuffApi.ConsumeEmberBeforeBurn(executor, executor.Self);
        }), "ember");
    }

    private static void ApplyEmberCloak(ScriptExecutor self)
    {
        self.SetStatus("Self");
        self.RemoveBuff(SunExpIds.Burn);
        self.RemoveBuff(SunExpIds.BodyBurn);
        ExecutorApi.SetVar(self, "SunExpBurnWardPending", "1");

        var token = ExecutorApi.RegisterHook(self, "SunExpBurnWardHook", "SunExpBurnWardToken");
        if (token == null)
        {
            return;
        }

        ExecutorApi.TryAddTokenedEvent(self, "StartRound", "SunExpBurnWardToken", token, new Action(() =>
        {
            var activeWard = ExecutorApi.SelfBuffLevel(self, SunExpIds.EmberCloak) > 0;
            var pending = ExecutorApi.GetVar(self, "SunExpBurnWardPending", "0") == "1";
            if (!activeWard && !pending)
            {
                return;
            }

            self.SetStatus("Self");
            self.RemoveBuff(SunExpIds.Burn);
            self.RemoveBuff(SunExpIds.BodyBurn);
            self.RemoveBuff(SunExpIds.EmberCloak);
            ExecutorApi.SetVar(self, "SunExpBurnWardPending", "1");
            ExecutorApi.TryAddTempEvent(self, "EndRound", new Action(() => ExecutorApi.SetVar(self, "SunExpBurnWardPending", "0")), "ember_cloak");
        }), "ember_cloak");
    }

    private static void ApplySolarCrown(ScriptExecutor self)
    {
        if (ExecutorApi.SelfBuffLevel(self, SunExpIds.SolarCrown) <= 0)
        {
            return;
        }

        SetSolarCrownTier(self, CalculateSolarCrownTier(ExecutorApi.SelfBuffLevel(self, SunExpIds.SolarRadiance)));
    }

    private static void ClearSolarCrown(ScriptExecutor self)
    {
        var tier = ExecutorApi.SelfBuffLevel(self, SunExpIds.SolarCrownTier);
        if (tier > 0)
        {
            ConsumeRadiance(self, tier * 2);
        }

        self.SetStatus("Self");
        self.RemoveBuff(SunExpIds.SolarCrownTier);
    }

    private static int CalculateSolarCrownTier(int radiance)
    {
        if (radiance >= 15)
        {
            return 5;
        }

        if (radiance >= 12)
        {
            return 4;
        }

        if (radiance >= 8)
        {
            return 3;
        }

        if (radiance >= 4)
        {
            return 2;
        }

        return radiance >= 1 ? 1 : 0;
    }

    private static int SetSolarCrownTier(ScriptExecutor self, int tier)
    {
        var next = Math.Max(0, Math.Min(5, tier));
        self.SetStatus("Self");
        self.RemoveBuff(SunExpIds.SolarCrownTier);
        if (next > 0)
        {
            self.AddBuff(SunExpIds.SolarCrownTier, next.ToString());
        }

        return next;
    }

    private static int ConsumeRadiance(ScriptExecutor self, int amount)
    {
        if (amount <= 0 || self?.Self == null)
        {
            return 0;
        }

        var current = ExecutorApi.SelfBuffLevel(self, SunExpIds.SolarRadiance);
        var consumed = Math.Min(current, amount);
        if (consumed <= 0)
        {
            return 0;
        }

        var next = current - consumed;
        if (next <= 0)
        {
            ExecutorApi.RemoveStatusBuff(self, self.Self, SunExpIds.SolarRadiance, "Self");
        }
        else
        {
            self.Self.GetBuff(SunExpIds.SolarRadiance).buffConfig.Level = next;
        }

        return consumed;
    }

    private static void ApplyOriginCoreRadiance(ScriptExecutor self)
    {
        var token = ExecutorApi.RegisterHook(self, "SunExpMiniCoronaHook", "SunExpMiniCoronaToken");
        if (token == null)
        {
            return;
        }

        void Reset()
        {
            ExecutorApi.SetVar(self, "SunExpMiniCoronaDone", "0");
            ExecutorApi.SetVar(self, "SunExpMiniCoronaLast", ExecutorApi.SelfBuffLevel(self, SunExpIds.SolarRadiance));
        }

        ExecutorApi.TryAddTokenedEvent(self, "StartRound", "SunExpMiniCoronaToken", token, new Action(() =>
        {
            Reset();
        }), "origin_core_radiance");
        ExecutorApi.TryAddTokenedEvent(self, "Action", "SunExpMiniCoronaToken", token, new Action(() =>
        {
            if (ExecutorApi.SelfBuffLevel(self, SunExpIds.OriginCoreRadiance) <= 0)
            {
                return;
            }

            var current = ExecutorApi.SelfBuffLevel(self, SunExpIds.SolarRadiance);
            var last = DictionaryUtil.ParseInt(ExecutorApi.GetVar(self, "SunExpMiniCoronaLast", current.ToString()));
            if (ExecutorApi.GetVar(self, "SunExpMiniCoronaDone", "0") == "0" && current > last)
            {
                self.SetStatus("Self");
                self.AddBuff(SunExpIds.SolarRadiance, "1");
                ExecutorApi.SetVar(self, "SunExpMiniCoronaDone", "1");
                current = ExecutorApi.SelfBuffLevel(self, SunExpIds.SolarRadiance);
            }

            ExecutorApi.SetVar(self, "SunExpMiniCoronaLast", current);
        }), "origin_core_radiance");
        Reset();
    }

    private static void ApplyCycleGatheredFlame(ScriptExecutor self)
    {
        var token = ExecutorApi.RegisterHook(self, "SunExpMeltingWheelHook", "SunExpMeltingWheelToken");
        if (token == null)
        {
            return;
        }

        void SyncLast()
        {
            ExecutorApi.SetVar(self, "SunExpMeltingWheelLastBurn", ExecutorApi.SelfBuffLevel(self, SunExpIds.Burn));
        }

        ExecutorApi.TryAddTokenedEvent(self, "buff_burnOnLevelChange", "SunExpMeltingWheelToken", token, new Action(() =>
        {
            if (ExecutorApi.SelfBuffLevel(self, SunExpIds.CycleGatheredFlame) <= 0)
            {
                return;
            }

            var current = ExecutorApi.SelfBuffLevel(self, SunExpIds.Burn);
            var last = DictionaryUtil.ParseInt(ExecutorApi.GetVar(self, "SunExpMeltingWheelLastBurn", current.ToString()));
            if (current > last)
            {
                var gain = current - last;
                ExecutorApi.SetVar(self, "SunExpMeltingWheelLastBurn", current);
                self.SetStatus("Self");
                self.AddBuff(SunExpIds.GatheredFlame, gain.ToString());
                return;
            }

            ExecutorApi.SetVar(self, "SunExpMeltingWheelLastBurn", current);
        }), "cycle_gathered_flame");
        SyncLast();
    }

    private static void ApplyAfterglowOmen(ScriptExecutor self)
    {
        var token = ExecutorApi.RegisterHook(self, "SunExpAfterglowHook", "SunExpAfterglowToken");
        if (token == null)
        {
            return;
        }

        ExecutorApi.TryAddTokenedEvent(self, "StartRound", "SunExpAfterglowToken", token, new Action(() =>
        {
            if (ExecutorApi.SelfBuffLevel(self, SunExpIds.AfterglowOmen) <= 0)
            {
                return;
            }

            foreach (var target in ExecutorApi.EnemyTargets(self))
            {
                var vulnerability = ExecutorApi.StatusBuffLevel(target, SunExpIds.Burn) / 2;
                if (vulnerability > 0)
                {
                    ExecutorApi.AddStatusBuff(self, target, "buff_vulnerability", vulnerability);
                }
            }
        }), "afterglow_omen");
    }

}
