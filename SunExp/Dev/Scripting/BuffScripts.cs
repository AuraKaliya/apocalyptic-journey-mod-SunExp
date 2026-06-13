using System;
using SunExp.Dll.GameApi;
using SunExp.Dll.Infrastructure;

namespace SunExp.Dll.Scripting;

public static class BuffScripts
{
    public static void Apply(ScriptExecutor self, string id)
    {
        try
        {
            switch (id)
            {
                case "solar_radiance":
                    ApplySolarRadiance(self);
                    break;
                case "gathered_flame":
                    ApplyGatheredFlame(self);
                    break;
                case "scorching_canopy":
                    ApplyScorchingCanopy(self);
                    break;
                case "body_burn":
                    ApplyBodyBurn(self);
                    break;
                case "ember":
                    ApplyEmber(self);
                    break;
                case "ember_cloak":
                    ApplyEmberCloak(self);
                    break;
                case "solar_crown":
                    ApplySolarCrown(self);
                    break;
                case "origin_core_radiance":
                    ApplyOriginCoreRadiance(self);
                    break;
                case "cycle_gathered_flame":
                    ApplyCycleGatheredFlame(self);
                    break;
                case "afterglow_omen":
                    ApplyAfterglowOmen(self);
                    break;
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
            switch (id)
            {
                case "solar_radiance":
                    ExecutorApi.ClearHook(self, "SunExpSolarRadianceHook", "SunExpSolarRadianceToken");
                    break;
                case "gathered_flame":
                    ExecutorApi.ClearHook(self, "SunExpGatheredFlameHook", "SunExpGatheredFlameToken");
                    break;
                case "scorching_canopy":
                    ClearScorchingCanopy(self);
                    break;
                case "body_burn":
                    ExecutorApi.ClearHook(self, "SunExpBodyBurnHook", "SunExpBodyBurnToken");
                    break;
                case "ember":
                    BuffApi.ClearEmberDamageBonus(self, self?.Self);
                    ExecutorApi.ClearHook(self, "SunExpEmberHook", "SunExpEmberToken");
                    break;
                case "ember_cloak":
                    ExecutorApi.ClearHook(self, "SunExpBurnWardHook", "SunExpBurnWardToken");
                    ExecutorApi.SetVar(self, "SunExpBurnWardPending", "0");
                    break;
                case "solar_crown":
                    ClearSolarCrown(self);
                    break;
                case "origin_core_radiance":
                    ExecutorApi.ClearHook(self, "SunExpMiniCoronaHook", "SunExpMiniCoronaToken");
                    ExecutorApi.SetVar(self, "SunExpMiniCoronaDone", "0");
                    break;
                case "cycle_gathered_flame":
                    ExecutorApi.ClearHook(self, "SunExpMeltingWheelHook", "SunExpMeltingWheelToken");
                    ExecutorApi.SetVar(self, "SunExpMeltingWheelLastBurn", "0");
                    break;
                case "afterglow_omen":
                    ExecutorApi.ClearHook(self, "SunExpAfterglowHook", "SunExpAfterglowToken");
                    break;
            }
        }
        catch (Exception ex)
        {
            SunExpLog.Error("Buff Clear failed: " + id, ex);
        }
    }

    private static void ApplySolarRadiance(ScriptExecutor self)
    {
        var token = ExecutorApi.RegisterHook(self, "SunExpSolarRadianceHook", "SunExpSolarRadianceToken");
        if (token == null)
        {
            return;
        }

        self.AddEvent("Action", new Action(() =>
        {
            if (!ExecutorApi.IsHookTokenActive(self, "SunExpSolarRadianceToken", token))
            {
                return;
            }

            var level = ExecutorApi.SelfBuffLevel(self, SunExpIds.SolarRadiance);
            var gain = level * 5;
            if (gain <= 0)
            {
                return;
            }

            self.SetStatus("Self");
            self.AddBuff("buff_extraordinary", gain.ToString());
        }));
    }

    private static void ApplyGatheredFlame(ScriptExecutor self)
    {
        var token = ExecutorApi.RegisterHook(self, "SunExpGatheredFlameHook", "SunExpGatheredFlameToken");
        if (token == null)
        {
            return;
        }

        self.AddEvent("StartRound", new Action(() =>
        {
            if (!ExecutorApi.IsHookTokenActive(self, "SunExpGatheredFlameToken", token))
            {
                return;
            }

            var count = ExecutorApi.SelfBuffLevel(self, SunExpIds.GatheredFlame);
            if (count <= 0)
            {
                return;
            }

            ExecutorApi.ApplySelfBurn(self, count, true);
            self.SetStatus("Self");
            self.AddBuff("buff_extraordinary", (count * 10).ToString());
        }));
    }

    private static void ApplyScorchingCanopy(ScriptExecutor self)
    {
        if (self == null)
        {
            return;
        }

        if (ExecutorApi.GetVar(self, "SunExpActiveFieldId") == "")
        {
            ExecutorApi.SetActiveField(self, "scorching_canopy");
        }

        var token = ExecutorApi.RegisterHook(self, "SunExpField_scorching_canopyHook", "SunExpField_scorching_canopyToken");
        ExecutorApi.SyncFieldStacks(self, "scorching_canopy");
        if (token == null)
        {
            return;
        }

        var epoch = DictionaryUtil.ParseInt(ExecutorApi.GetVar(self, "SunExpActiveFieldEpoch", "0"));
        self.AddEvent("StartRound", new Action(() =>
        {
            if (!ExecutorApi.IsActiveField(self, "scorching_canopy", epoch, token))
            {
                return;
            }

            FieldStartRound(self, "scorching_canopy");
        }));
    }

    private static void ClearScorchingCanopy(ScriptExecutor self)
    {
        var externalClear = ExecutorApi.GetVar(self, "SunExpFieldInternalClear", "0") != "1";
        var wasActive = ExecutorApi.GetVar(self, "SunExpActiveFieldId") == "scorching_canopy";
        var stacks = DictionaryUtil.ParseInt(ExecutorApi.GetVar(self, "SunExpActiveFieldStacks", "1"));

        ExecutorApi.ClearHook(self, "SunExpField_scorching_canopyHook", "SunExpField_scorching_canopyToken");
        if (!externalClear || stacks <= 0)
        {
            if (wasActive)
            {
                ExecutorApi.SetSharedFieldState("scorching_canopy", 0);
            }

            return;
        }

        if (!wasActive)
        {
            return;
        }

        self.SetStatus("Self");
        self.AddBuff(SunExpIds.ScorchingCanopy, stacks.ToString());
    }

    private static bool FieldStartRound(ScriptExecutor self, string fieldId)
    {
        if (fieldId != "scorching_canopy")
        {
            return false;
        }

        var count = ExecutorApi.SyncFieldStacks(self, fieldId);
        if (count <= 0)
        {
            count = ExecutorApi.CombatIntGet(ExecutorApi.FieldCombatKey(fieldId, "Stacks"));
        }

        if (count <= 0 || !ExecutorApi.BeginSharedFieldStartRound(self, fieldId))
        {
            return false;
        }

        self.SetStatus("All");
        self.AddBuff(SunExpIds.Burn, count.ToString());
        ExecutorApi.ClearSelfBurnIfProtected(self, true);
        return true;
    }

    private static void ApplyBodyBurn(ScriptExecutor self)
    {
        var token = ExecutorApi.RegisterHook(self, "SunExpBodyBurnHook", "SunExpBodyBurnToken");
        if (token == null)
        {
            return;
        }

        self.AddEvent("StartRound", new Action(() =>
        {
            if (!ExecutorApi.IsHookTokenActive(self, "SunExpBodyBurnToken", token))
            {
                return;
            }

            TriggerBodyBurn(self);
        }));
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
        if (target == null)
        {
            return 1;
        }

        try
        {
            var value = target.GetType().GetProperty("MaxHp")?.GetValue(target)
                ?? target.GetType().GetField("MaxHp")?.GetValue(target);
            var maxHp = value is int intValue ? intValue : DictionaryUtil.ParseInt(Convert.ToString(value));
            return maxHp / 200 + 1;
        }
        catch
        {
            return 1;
        }
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

        executor.AddEvent("SunExp_sunexp_emberOnLevelChange", new Action(Sync));
        executor.AddEvent("emberOnLevelChange", new Action(Sync));
        executor.AddEvent("StartRound", new Action(() =>
        {
            if (!ExecutorApi.IsHookTokenActive(executor, "SunExpEmberToken", token))
            {
                return;
            }

            BuffApi.ConsumeEmberBeforeBurn(executor, executor.Self);
        }));
    }

    private static void ApplyEmberCloak(ScriptExecutor self)
    {
        self.SetStatus("Self");
        self.RemoveBuff(SunExpIds.Burn);
        ExecutorApi.SetVar(self, "SunExpBurnWardPending", "1");

        var token = ExecutorApi.RegisterHook(self, "SunExpBurnWardHook", "SunExpBurnWardToken");
        if (token == null)
        {
            return;
        }

        self.AddEvent("StartRound", new Action(() =>
        {
            if (!ExecutorApi.IsHookTokenActive(self, "SunExpBurnWardToken", token))
            {
                return;
            }

            var activeWard = ExecutorApi.SelfBuffLevel(self, SunExpIds.EmberCloak) > 0;
            var pending = ExecutorApi.GetVar(self, "SunExpBurnWardPending", "0") == "1";
            if (!activeWard && !pending)
            {
                return;
            }

            self.SetStatus("Self");
            self.RemoveBuff(SunExpIds.Burn);
            self.RemoveBuff(SunExpIds.EmberCloak);
            ExecutorApi.SetVar(self, "SunExpBurnWardPending", "1");
            self.AddTempEvent("EndRound", new Action(() => ExecutorApi.SetVar(self, "SunExpBurnWardPending", "0")));
        }));
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

        self.AddEvent("StartRound", new Action(() =>
        {
            if (ExecutorApi.IsHookTokenActive(self, "SunExpMiniCoronaToken", token))
            {
                Reset();
            }
        }));
        self.AddEvent("Action", new Action(() =>
        {
            if (!ExecutorApi.IsHookTokenActive(self, "SunExpMiniCoronaToken", token)
                || ExecutorApi.SelfBuffLevel(self, SunExpIds.OriginCoreRadiance) <= 0)
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
        }));
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

        self.AddEvent("buff_burnOnLevelChange", new Action(() =>
        {
            if (!ExecutorApi.IsHookTokenActive(self, "SunExpMeltingWheelToken", token)
                || ExecutorApi.SelfBuffLevel(self, SunExpIds.CycleGatheredFlame) <= 0)
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
        }));
        SyncLast();
    }

    private static void ApplyAfterglowOmen(ScriptExecutor self)
    {
        var token = ExecutorApi.RegisterHook(self, "SunExpAfterglowHook", "SunExpAfterglowToken");
        if (token == null)
        {
            return;
        }

        self.AddEvent("StartRound", new Action(() =>
        {
            if (!ExecutorApi.IsHookTokenActive(self, "SunExpAfterglowToken", token)
                || ExecutorApi.SelfBuffLevel(self, SunExpIds.AfterglowOmen) <= 0)
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
        }));
    }
}
