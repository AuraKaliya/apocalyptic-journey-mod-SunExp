using System;
using System.Collections.Generic;
using Terrias.Dll.GameApi;
using Terrias.Dll.Infrastructure;
using Terrias.Dll.Mechanics;

namespace Terrias.Dll.Scripting;

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
        ["relic_star_stone_pouch"] = ApplyRelicStarStonePouch,
        ["star_score"] = ApplyStarScore,
        ["star_stage"] = ApplyStarStage,
        ["moonlight"] = ApplyMoonlight,
        ["frozen"] = ApplyFrozen,
        ["dendro_core"] = ApplyDendroCore,
        ["abyss_blessing"] = EndlessAbyssBlessingService.Apply,
        [TerriasIds.PolymorphTraitBuffShortId] = ApplyPolymorphTrait,
        [TerriasIds.HeartChangeBuffShortId] = ApplyHeartChange
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
        ["relic_star_stone_pouch"] = ClearRelicStarStonePouch,
        ["star_score"] = ClearStarScore,
        ["star_stage"] = ClearStarStage,
        ["moonlight"] = ClearMoonlight,
        ["frozen"] = ClearFrozen,
        ["dendro_core"] = ClearDendroCore,
        ["abyss_blessing"] = EndlessAbyssBlessingService.Clear,
        [TerriasIds.PolymorphTraitBuffShortId] = ClearPolymorphTrait,
        [TerriasIds.HeartChangeBuffShortId] = ClearHeartChange
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
            TerriasLog.Error("Buff Apply failed: " + id, ex);
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
            TerriasLog.Error("Buff Clear failed: " + id, ex);
        }
    }

    private static void ClearSolarRadiance(ScriptExecutor self)
    {
        TerriasActionPassiveRegistry.Unregister(self, "Buff.SolarRadiance");
    }

    private static void ClearGatheredFlame(ScriptExecutor self)
    {
        ScriptEventApi.InvalidateFightScope(self, "Buff.GatheredFlame");
    }

    private static void ClearBodyBurn(ScriptExecutor self)
    {
        ScriptEventApi.InvalidateFightScope(self, "Buff.BodyBurn");
    }

    private static void ClearEmber(ScriptExecutor self)
    {
        BuffApi.ClearEmberDamageBonus(self, self?.Self);
        ScriptEventApi.InvalidateFightScope(self, "Buff.Ember");
    }

    private static void ClearEmberCloak(ScriptExecutor self)
    {
        ScriptEventApi.InvalidateFightScope(self, "Buff.EmberCloak");
        ExecutorApi.SetVar(self, "TerriasBurnWardPending", "0");
    }

    private static void ClearOriginCoreRadiance(ScriptExecutor self)
    {
        ScriptEventApi.InvalidateFightScope(self, "Buff.OriginCoreRadiance");
        TerriasActionPassiveRegistry.Unregister(self, "Buff.OriginCoreRadiance");
        ExecutorApi.SetVar(self, "TerriasMiniCoronaDone", "0");
    }

    private static void ClearCycleGatheredFlame(ScriptExecutor self)
    {
        ScriptEventApi.InvalidateFightScope(self, "Buff.CycleGatheredFlame");
        ExecutorApi.SetVar(self, "TerriasMeltingWheelLastBurn", "0");
    }

    private static void ClearAfterglowOmen(ScriptExecutor self)
    {
        ScriptEventApi.InvalidateFightScope(self, "Buff.AfterglowOmen");
    }

    private static void ClearStarStonePouch(ScriptExecutor self)
    {
        StarStonePouchService.Clear(self);
    }

    private static void ClearRelicStarStonePouch(ScriptExecutor self)
    {
        StarStonePouchService.ClearRelic(self);
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
        TerriasActionPassiveRegistry.Register(
            self,
            "Buff.SolarRadiance",
            AuraShared.Core.AuraCardActionPhase.NativeStarted,
            _ =>
        {
            var level = ExecutorApi.SelfBuffLevel(self, TerriasIds.SolarRadiance);
            var gain = level * 5;
            if (gain <= 0)
            {
                return;
            }

            self.SetStatus("Self");
            self.AddBuff("buff_extraordinary", gain.ToString());
        });
    }

    private static void ApplyMoonlight(ScriptExecutor self)
    {
        using var scope = ScriptEventApi.BeginFightScope(self, "Buff.Moonlight");
        if (scope == null)
        {
            return;
        }

        scope.AddRequired("EndRound", new Action(() =>
        {
            var level = ExecutorApi.SelfBuffLevel(self, TerriasIds.Moonlight);
            if (level <= 0)
            {
                return;
            }

            self.SetStatus("Self");
            self.AddBuff("buff_keenedge", level.ToString());
            self.AddBuff("buff_resilient", level.ToString());
        }), "moonlight");
        scope.Commit();
    }

    private static void ClearMoonlight(ScriptExecutor self)
    {
        ScriptEventApi.InvalidateFightScope(self, "Buff.Moonlight");
    }

    private static void ApplyFrozen(ScriptExecutor self)
    {
        using var scope = ScriptEventApi.BeginFightScope(self, "Buff.Frozen");
        if (scope == null)
        {
            return;
        }

        scope.AddRequired("StartRound", new Action(() =>
        {
            self.SetStatus("Self");
            self.ChangeRound();
        }), "elemental.frozen");
        scope.Commit();
    }

    private static void ClearFrozen(ScriptExecutor self)
    {
        ScriptEventApi.InvalidateFightScope(self, "Buff.Frozen");
    }

    private static void ApplyDendroCore(ScriptExecutor self)
    {
        using var scope = ScriptEventApi.BeginFightScope(self, "Buff.DendroCore");
        if (scope == null)
        {
            return;
        }

        scope.AddRequired("StartRound", new Action(() =>
        {
            var stacks = ExecutorApi.SelfBuffLevel(self, TerriasIds.DendroCore);
            if (stacks <= 0)
            {
                return;
            }

            self.SetStatus("Self");
            self.Damage((10 * stacks).ToString(), "True");
            self.SetStatus("Self");
            self.RemoveBuff(TerriasIds.DendroCore);
        }), "elemental.dendro-core");
        scope.Commit();
    }

    private static void ClearDendroCore(ScriptExecutor self)
    {
        ScriptEventApi.InvalidateFightScope(self, "Buff.DendroCore");
    }

    private static void ApplyStarStonePouch(ScriptExecutor self)
    {
        StarStonePouchService.Apply(self);
    }

    private static void ApplyRelicStarStonePouch(ScriptExecutor self)
    {
        StarStonePouchService.ApplyRelic(self);
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
        using var scope = ScriptEventApi.BeginFightScope(self, "Buff.GatheredFlame");
        if (scope == null)
        {
            return;
        }

        scope.AddRequired("StartRound", new Action(() =>
        {
            var count = ExecutorApi.SelfBuffLevel(self, TerriasIds.GatheredFlame);
            if (count <= 0)
            {
                return;
            }

            ExecutorApi.ApplySelfBurn(self, count, true);
            self.SetStatus("Self");
            self.AddBuff("buff_extraordinary", (count * 10).ToString());
        }), "gathered_flame");
        scope.Commit();
    }

    private static void ApplyScorchingCanopy(ScriptExecutor self)
    {
        if (self == null)
        {
            return;
        }

        var carrierStacks = Math.Max(1, ExecutorApi.SelfBuffLevel(self, TerriasIds.ScorchingCanopy));
        ExecutorApi.ActivateField(self, TerriasFieldId.ScorchingCanopy, carrierStacks, "carrier.scorching_canopy");

        self.SetStatus("Self");
        self.RemoveBuff(TerriasIds.ScorchingCanopy);
        TerriasLog.Debug("Scorching canopy carrier converted to field: carrierStacks="
            + carrierStacks
            + ", fieldStacks=" + ExecutorApi.FieldStacks(TerriasFieldId.ScorchingCanopy));
    }

    private static void ClearScorchingCanopy(ScriptExecutor self)
    {
        TerriasLog.Debug("Scorching canopy carrier clear ignored; field state is cleared only through FieldApi.TryClearActiveField.");
    }

    private static void ApplyBodyBurn(ScriptExecutor self)
    {
        using var scope = ScriptEventApi.BeginFightScope(self, "Buff.BodyBurn");
        if (scope == null)
        {
            return;
        }

        scope.AddRequired("StartRound", new Action(() =>
        {
            TriggerBodyBurn(self);
        }), "body_burn");
        scope.Commit();
    }

    private static bool TriggerBodyBurn(ScriptExecutor self)
    {
        var level = ExecutorApi.SelfBuffLevel(self, TerriasIds.BodyBurn);
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

        self.RemoveBuff(TerriasIds.BodyBurn);
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
        using var scope = ScriptEventApi.BeginFightScope(executor, "Buff.Ember");
        if (scope == null)
        {
            return;
        }

        void Sync()
        {
            if (scope.IsActive)
            {
                BuffApi.SyncEmberDamageBonus(executor, executor.Self);
            }
        }

        scope.AddRequired("Terrias_terrias_emberOnLevelChange", new Action(Sync), "ember");
        scope.AddRequired(TerriasContentIdCompatibility.LegacyMainPrefix + "emberOnLevelChange", new Action(Sync), "ember");
        scope.AddRequired("emberOnLevelChange", new Action(Sync), "ember");
        scope.AddRequired("StartRound", new Action(() =>
        {
            var consumed = BuffApi.ConsumeEmberBeforeBurn(executor, executor.Self);
            WunaPassiveService.ResolveEmberConsumed(
                executor,
                executor.Self,
                consumed,
                "BuffScripts.Ember.StartRound");
        }), "ember");
        scope.Commit();
    }

    private static void ApplyEmberCloak(ScriptExecutor self)
    {
        self.SetStatus("Self");
        self.RemoveBuff(TerriasIds.Burn);
        self.RemoveBuff(TerriasIds.BodyBurn);
        ExecutorApi.SetVar(self, "TerriasBurnWardPending", "1");

        using var scope = ScriptEventApi.BeginFightScope(self, "Buff.EmberCloak");
        if (scope == null)
        {
            return;
        }

        scope.AddRequired("StartRound", new Action(() =>
        {
            var activeWard = ExecutorApi.SelfBuffLevel(self, TerriasIds.EmberCloak) > 0;
            var pending = ExecutorApi.GetVar(self, "TerriasBurnWardPending", "0") == "1";
            if (!activeWard && !pending)
            {
                return;
            }

            self.SetStatus("Self");
            self.RemoveBuff(TerriasIds.Burn);
            self.RemoveBuff(TerriasIds.BodyBurn);
            self.RemoveBuff(TerriasIds.EmberCloak);
            ExecutorApi.SetVar(self, "TerriasBurnWardPending", "1");
            ExecutorApi.TryAddTempEvent(self, "EndRound", new Action(() => ExecutorApi.SetVar(self, "TerriasBurnWardPending", "0")), "ember_cloak");
        }), "ember_cloak");
        scope.Commit();
    }

    private static void ApplySolarCrown(ScriptExecutor self)
    {
        if (ExecutorApi.SelfBuffLevel(self, TerriasIds.SolarCrown) <= 0)
        {
            return;
        }

        SetSolarCrownTier(self, CalculateSolarCrownTier(ExecutorApi.SelfBuffLevel(self, TerriasIds.SolarRadiance)));
    }

    private static void ClearSolarCrown(ScriptExecutor self)
    {
        var tier = ExecutorApi.SelfBuffLevel(self, TerriasIds.SolarCrownTier);
        if (tier > 0)
        {
            ConsumeRadiance(self, tier * 2);
        }

        self.SetStatus("Self");
        self.RemoveBuff(TerriasIds.SolarCrownTier);
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
        self.RemoveBuff(TerriasIds.SolarCrownTier);
        if (next > 0)
        {
            self.AddBuff(TerriasIds.SolarCrownTier, next.ToString());
        }

        return next;
    }

    private static int ConsumeRadiance(ScriptExecutor self, int amount)
    {
        if (amount <= 0 || self?.Self == null)
        {
            return 0;
        }

        var current = ExecutorApi.SelfBuffLevel(self, TerriasIds.SolarRadiance);
        var consumed = Math.Min(current, amount);
        if (consumed <= 0)
        {
            return 0;
        }

        var next = current - consumed;
        if (next <= 0)
        {
            ExecutorApi.RemoveStatusBuff(self, self.Self, TerriasIds.SolarRadiance, "Self");
        }
        else
        {
            self.Self.GetBuff(TerriasIds.SolarRadiance).buffConfig.Level = next;
        }

        return consumed;
    }

    private static void ApplyOriginCoreRadiance(ScriptExecutor self)
    {
        using var scope = ScriptEventApi.BeginFightScope(self, "Buff.OriginCoreRadiance");
        if (scope == null)
        {
            return;
        }

        void Reset()
        {
            ExecutorApi.SetVar(self, "TerriasMiniCoronaDone", "0");
            ExecutorApi.SetVar(self, "TerriasMiniCoronaLast", ExecutorApi.SelfBuffLevel(self, TerriasIds.SolarRadiance));
        }

        scope.AddRequired("StartRound", new Action(() =>
        {
            Reset();
        }), "origin_core_radiance");
        TerriasActionPassiveRegistry.Register(
            self,
            "Buff.OriginCoreRadiance",
            AuraShared.Core.AuraCardActionPhase.NativeStarted,
            _ =>
        {
            if (ExecutorApi.SelfBuffLevel(self, TerriasIds.OriginCoreRadiance) <= 0)
            {
                return;
            }

            var current = ExecutorApi.SelfBuffLevel(self, TerriasIds.SolarRadiance);
            var last = DictionaryUtil.ParseInt(ExecutorApi.GetVar(self, "TerriasMiniCoronaLast", current.ToString()));
            if (ExecutorApi.GetVar(self, "TerriasMiniCoronaDone", "0") == "0" && current > last)
            {
                self.SetStatus("Self");
                self.AddBuff(TerriasIds.SolarRadiance, "1");
                ExecutorApi.SetVar(self, "TerriasMiniCoronaDone", "1");
                current = ExecutorApi.SelfBuffLevel(self, TerriasIds.SolarRadiance);
            }

            ExecutorApi.SetVar(self, "TerriasMiniCoronaLast", current);
        });
        Reset();
        if (!scope.Commit())
        {
            TerriasActionPassiveRegistry.Unregister(self, "Buff.OriginCoreRadiance");
        }
    }

    private static void ApplyCycleGatheredFlame(ScriptExecutor self)
    {
        using var scope = ScriptEventApi.BeginFightScope(self, "Buff.CycleGatheredFlame");
        if (scope == null)
        {
            return;
        }

        void SyncLast()
        {
            ExecutorApi.SetVar(self, "TerriasMeltingWheelLastBurn", ExecutorApi.SelfBuffLevel(self, TerriasIds.Burn));
        }

        scope.AddRequired("buff_burnOnLevelChange", new Action(() =>
        {
            if (ExecutorApi.SelfBuffLevel(self, TerriasIds.CycleGatheredFlame) <= 0)
            {
                return;
            }

            var current = ExecutorApi.SelfBuffLevel(self, TerriasIds.Burn);
            var last = DictionaryUtil.ParseInt(ExecutorApi.GetVar(self, "TerriasMeltingWheelLastBurn", current.ToString()));
            if (current > last)
            {
                var gain = current - last;
                ExecutorApi.SetVar(self, "TerriasMeltingWheelLastBurn", current);
                self.SetStatus("Self");
                self.AddBuff(TerriasIds.GatheredFlame, gain.ToString());
                return;
            }

            ExecutorApi.SetVar(self, "TerriasMeltingWheelLastBurn", current);
        }), "cycle_gathered_flame");
        SyncLast();
        scope.Commit();
    }

    private static void ApplyAfterglowOmen(ScriptExecutor self)
    {
        using var scope = ScriptEventApi.BeginFightScope(self, "Buff.AfterglowOmen");
        if (scope == null)
        {
            return;
        }

        scope.AddRequired("StartRound", new Action(() =>
        {
            if (ExecutorApi.SelfBuffLevel(self, TerriasIds.AfterglowOmen) <= 0)
            {
                return;
            }

            foreach (var target in ExecutorApi.EnemyTargets(self))
            {
                var vulnerability = ExecutorApi.StatusBuffLevel(target, TerriasIds.Burn) / 2;
                if (vulnerability > 0)
                {
                    ExecutorApi.AddStatusBuff(self, target, "buff_vulnerability", vulnerability);
                }
            }
        }), "afterglow_omen");
        scope.Commit();
    }

}
