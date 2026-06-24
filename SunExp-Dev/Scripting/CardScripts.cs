using System;
using System.Linq;
using SunExp.Dll.GameApi;
using SunExp.Dll.Infrastructure;
using SunExp.Dll.Mechanics;

namespace SunExp.Dll.Scripting;

public static class CardScripts
{
    public static void Init(ScriptExecutor self, string id)
    {
        try
        {
            id = NormalizeId(id);
            switch (id)
            {
                case "spark":
                    ExecutorApi.SetBaseScript(self, "AttackCardItem", canSelf: false);
                    ExecutorApi.AddDamageDescription(self, "1", 5);
                    break;
                case "stellar_overture_start":
                case "stellar_overture_sustain":
                case "stellar_overture_turn":
                case "stellar_overture_close":
                case "witch_star_score":
                    StarScoreService.Init(self, id);
                    break;
                case "radiant_flame_slash":
                    ExecutorApi.SetBaseScript(self, "AttackCardItem", canSelf: false);
                    ExecutorApi.AddDamageDescription(self, "1", ExecutorApi.SolarKeywordDamage(self, 10, ExecutorApi.PrimaryTarget(self)));
                    ExecutorApi.AddValueDescription(self, "2", 10);
                    ExecutorApi.AddValueDescription(self, "3", 1);
                    break;
                case "burning_star_hex":
                    ExecutorApi.SetBaseScript(self, "AttackCardItem", canSelf: false);
                    var baseDamage = CalcSolarSparkBaseDamage(self);
                    ExecutorApi.AddDamageDescription(self, "1", ExecutorApi.SolarKeywordDamage(self, baseDamage, ExecutorApi.PrimaryTarget(self)));
                    ExecutorApi.AddValueDescription(self, "2", baseDamage);
                    ExecutorApi.AddValueDescription(self, "3", 1);
                    break;
                case "blazing_crown_collapse":
                    ExecutorApi.SetBaseScript(self, "CommonCardItem");
                    ExecutorApi.AddDamageDescription(self, "1", ExecutorApi.SolarKeywordDamage(self, 40, ExecutorApi.PrimaryTarget(self), ExecutorApi.SolarCrownTier(self)));
                    break;
                case "morning_light_bulwark":
                    ExecutorApi.SetBaseScript(self, "CommonCardItem");
                    self.AddDescription("1", "Defence", ExecutorApi.SolarKeywordBlock(self, 6).ToString());
                    break;
                case "gathered_flame_shield":
                    ExecutorApi.SetBaseScript(self, "CommonCardItem");
                    self.AddDescription("1", "Defence", (6 + ExecutorApi.SelfBuffLevel(self, SunExpIds.GatheredFlame)).ToString());
                    break;
                case "smoke_erosion":
                    ExecutorApi.SetBaseScript(self, "AttackCardItem", canSelf: false);
                    ExecutorApi.AddDamageDescription(self, "1", CalcSmokeErosionDamage(self));
                    ExecutorApi.AddValueDescription(self, "2", 7);
                    ExecutorApi.AddValueDescription(self, "3", 1);
                    break;
                case "solar_scorching_light":
                    ExecutorApi.SetBaseScript(self, "AttackCardItem", canSelf: false);
                    ExecutorApi.AddDamageDescription(self, "1", CalcFlamePierceDamage(self));
                    break;
                case "draw_flame":
                    ExecutorApi.SetBaseScript(self, "AttackCardItem");
                    break;
                case "scorching_flow_reclaim":
                case "eclipse_hex":
                case "burning_calamity":
                    ExecutorApi.SetBaseScript(self, "AttackCardItem", canSelf: false);
                    break;
                default:
                    ExecutorApi.SetBaseScript(self, "CommonCardItem");
                    if (id == "flamewheel_recurrence")
                    {
                        InitFlamewheel(self);
                    }
                    break;
            }
        }
        catch (Exception ex)
        {
            SunExpLog.Error("Card Init failed: " + id, ex);
        }
    }

    public static void Use(ScriptExecutor self, string id)
    {
        try
        {
            id = NormalizeId(id);
            switch (id)
            {
                case "spark":
                    UseSpark(self);
                    break;
                case "stellar_overture_start":
                case "stellar_overture_sustain":
                case "stellar_overture_turn":
                case "stellar_overture_close":
                case "witch_star_score":
                    StarScoreService.Use(self, id);
                    break;
                case "scorching_canopy_card":
                    ExecutorApi.ApplyFieldBuff(self, "scorching_canopy", 1);
                    self.SetStatus("All");
                    self.AddBuff(SunExpIds.Burn, "2");
                    ExecutorApi.ClearSelfBurnIfProtected(self, includePending: false);
                    break;
                case "radiant_flame_slash":
                    ExecutorApi.DealSolarKeywordDamage(self, 10, ExecutorApi.PrimaryTarget(self));
                    break;
                case "ember_cloak_card":
                    UseEmberCloakCard(self);
                    break;
                case "draw_flame":
                    UseDrawFlame(self);
                    break;
                case "solar_prayer":
                    self.SetStatus("Self");
                    self.AddBuff(SunExpIds.SolarRadiance, "2");
                    ExecutorApi.TransferSelfBurnToRandomFriendly(self);
                    break;
                case "burning_star_hex":
                    UseBurningStarHex(self);
                    break;
                case "crown_radiance":
                    UseCrownRadiance(self);
                    break;
                case "canopy_return":
                    UseCanopyReturn(self);
                    break;
                case "solar_phase_tuning":
                    UseSolarPhaseTuning(self);
                    break;
                case "solar_coronation":
                    self.SetStatus("Self");
                    self.AddBuff(SunExpIds.SolarRadiance, "3");
                    self.AddBuff(SunExpIds.SolarCrown, "2");
                    break;
                case "blazing_crown_collapse":
                    UseBlazingCrownCollapse(self);
                    break;
                case "radiant_oath":
                    UseRadiantOath(self);
                    break;
                case "solar_ignition":
                    foreach (var target in ExecutorApi.EnemyTargets(self))
                    {
                        ExecutorApi.AddStatusBuff(self, target, SunExpIds.Burn, 2);
                    }
                    ExecutorApi.TriggerBurnAllEnemies(self);
                    break;
                case "scorching_flow_reclaim":
                    UseScorchingFlowReclaim(self);
                    break;
                case "impurity_purge":
                    UseImpurityPurge(self);
                    break;
                case "flamewheel_recurrence":
                    UseFlamewheel(self);
                    break;
                case "eclipse_hex":
                    UseEclipseHex(self);
                    break;
                case "solar_scorching_light":
                    ExecutorApi.DealDamage(self, CalcFlamePierceDamage(self));
                    break;
                case "burning_calamity":
                    UseBurningCalamity(self);
                    break;
                case "burning_crown_oath":
                    UseBurningCrownOath(self);
                    break;
                case "morning_light_bulwark":
                    ExecutorApi.ApplySolarKeywordSkill(self, 6);
                    break;
                case "solar_return":
                    self.SetStatus("Self");
                    self.AddBuff(SunExpIds.SolarRadiance, "1");
                    self.DrawCount("1");
                    break;
                case "solar_origin_core":
                    self.SetStatus("Self");
                    self.AddBuff(SunExpIds.OriginCoreRadiance, "1");
                    break;
                case "ember_tower":
                    UseEmberTower(self);
                    break;
                case "gathered_flame_shield":
                    UseGatheredFlameShield(self);
                    break;
                case "gathered_flame_cycle":
                    self.SetStatus("Self");
                    self.AddBuff(SunExpIds.CycleGatheredFlame, "1");
                    break;
                case "solar_eclipse":
                    UseSolarEclipse(self);
                    break;
                case "smoke_erosion":
                    UseSmokeErosion(self);
                    break;
                case "afterglow_omen_card":
                    self.SetStatus("Self");
                    self.AddBuff(SunExpIds.AfterglowOmen, "1");
                    break;
            }
        }
        catch (Exception ex)
        {
            SunExpLog.Error("Card Use failed: " + id, ex);
        }
    }

    private static void UseSpark(ScriptExecutor self)
    {
        var target = ExecutorApi.PrimaryTarget(self);
        ExecutorApi.SetStatusForTarget(self, target, "Target");
        ExecutorApi.DealDamage(self, 5);
        ExecutorApi.AddStatusBuff(self, target, SunExpIds.Burn, 1, "Target");
        self.SetStatus("Self");
        self.AddBuff(SunExpIds.SolarRadiance, "1");
    }

    private static void UseEmberCloakCard(ScriptExecutor self)
    {
        var shield = (ExecutorApi.SelfBuffLevel(self, SunExpIds.Burn) + ExecutorApi.SelfBuffLevel(self, SunExpIds.BodyBurn)) / 2;
        self.SetStatus("Self");
        self.RemoveBuff(SunExpIds.Burn);
        self.RemoveBuff(SunExpIds.BodyBurn);
        if (shield > 0)
        {
            self.ChangeDefence(shield.ToString());
        }
        self.AddBuff(SunExpIds.EmberCloak, "1");
    }

    private static void UseDrawFlame(ScriptExecutor self)
    {
        var target = ExecutorApi.PrimaryTargetIncludingSelf(self);
        var gain = ExecutorApi.StatusBuffLevel(target, SunExpIds.Burn);
        if (gain > 0)
        {
            ExecutorApi.RemoveStatusBuff(self, target, SunExpIds.Burn, "Self");
            self.SetStatus("Self");
            self.AddBuff(SunExpIds.GatheredFlame, gain.ToString());
        }
    }

    private static void UseBurningStarHex(ScriptExecutor self)
    {
        var target = ExecutorApi.PrimaryTarget(self);
        var flame = self.Self?.GetBuff(SunExpIds.GatheredFlame);
        var useFlame = Math.Min(5, flame?.buffConfig?.Level ?? 0);
        var baseDamage = 8 + useFlame * 2;
        ExecutorApi.DealSolarKeywordDamage(self, baseDamage, target);
        ExecutorApi.AddStatusBuff(self, target, SunExpIds.Burn, 2, "Target");
        if (flame?.buffConfig != null && useFlame > 0)
        {
            flame.buffConfig.Level -= useFlame;
            if (flame.buffConfig.Level <= 0)
            {
                self.SetStatus("Self");
                self.RemoveBuff(SunExpIds.GatheredFlame);
            }
        }
    }

    private static void UseCrownRadiance(ScriptExecutor self)
    {
        foreach (var target in ExecutorApi.EnemyTargets(self))
        {
            ExecutorApi.AddStatusBuff(self, target, SunExpIds.Burn, 6);
        }
        if (ExecutorApi.IsActiveField(self, "scorching_canopy"))
        {
            var tier = ExecutorApi.SolarCrownTier(self);
            if (tier > 0)
            {
                ExecutorApi.TriggerBurnAll(self, tier);
            }
        }
    }

    private static void UseCanopyReturn(ScriptExecutor self)
    {
        ExecutorApi.ApplyFieldBuff(self, "scorching_canopy", 2);
        ExecutorApi.ApplySelfBurn(self, 3, includePending: false);
        foreach (var target in ExecutorApi.EnemyTargets(self))
        {
            ExecutorApi.AddStatusBuff(self, target, SunExpIds.Burn, 3);
        }
        ExecutorApi.TriggerBurnAllEnemies(self);
    }

    private static void UseSolarPhaseTuning(ScriptExecutor self)
    {
        self.SetStatus("Self");
        self.AddBuff(SunExpIds.SolarRadiance, "3");
        var burn = self.Self?.GetBuff(SunExpIds.Burn);
        var gain = Math.Min(6, burn?.buffConfig?.Level ?? 0);
        if (burn?.buffConfig != null && gain > 0)
        {
            burn.buffConfig.Level -= gain;
            if (burn.buffConfig.Level <= 0)
            {
                self.RemoveBuff(SunExpIds.Burn);
            }
            self.AddBuff(SunExpIds.GatheredFlame, gain.ToString());
        }
        if (gain >= 6)
        {
            self.DrawCount((1 + ExecutorApi.SelfBuffLevel(self, SunExpIds.SolarRadiance) / 3).ToString());
        }
    }

    private static void UseBlazingCrownCollapse(ScriptExecutor self)
    {
        var crown = self.Self?.GetBuff(SunExpIds.SolarCrown);
        var dealt = ExecutorApi.DealSolarKeywordDamageAllEnemies(self, 40, ExecutorApi.SolarCrownTier(self));
        SolarRadianceService.HandleSolarCardUsed(self, 3, "CardScripts.blazing_crown_collapse");
        self.SetStatus("Self");
        if (crown == null)
        {
            ExecutorApi.DealDamage(self, dealt);
        }
        self.SetStatus("Self");
        self.RemoveBuff(SunExpIds.SolarCrown);
        var consumedFlame = ExecutorApi.SelfBuffLevel(self, SunExpIds.GatheredFlame);
        self.SetStatus("Self");
        self.RemoveBuff(SunExpIds.GatheredFlame);
        ExecutorApi.ApplySelfBurn(self, consumedFlame / 2, includePending: false);
    }

    private static void UseRadiantOath(ScriptExecutor self)
    {
        self.SetStatus("Self");
        self.AddBuff(SunExpIds.SolarRadiance, "3");
        if (!ExecutorApi.IsActiveField(self, "scorching_canopy"))
        {
            ExecutorApi.ApplyFieldBuff(self, "scorching_canopy", 1);
        }
        else
        {
            self.DrawCount("1");
        }
    }

    private static void UseScorchingFlowReclaim(ScriptExecutor self)
    {
        var target = ExecutorApi.PrimaryTarget(self);
        if (ExecutorApi.StatusBuffLevel(target, SunExpIds.Burn) > 0)
        {
            ExecutorApi.TriggerBurn(self, target);
        }
        var gain = ExecutorApi.StatusBuffLevel(target, SunExpIds.Burn);
        if (gain > 0)
        {
            ExecutorApi.RemoveBuffStacks(self, target, SunExpIds.Burn, gain);
            self.SetStatus("Self");
            self.AddBuff(SunExpIds.GatheredFlame, gain.ToString());
        }
    }

    private static void UseImpurityPurge(ScriptExecutor self)
    {
        var total = ExecutorApi.NegativeBuffTotal(self.Self);
        if (total > 0)
        {
            ExecutorApi.RemoveAllNegativeBuffs(self, self.Self);
            self.SetStatus("Self");
            self.AddBuff(SunExpIds.Burn, total.ToString());
        }
    }

    private static void UseEclipseHex(ScriptExecutor self)
    {
        var target = ExecutorApi.PrimaryTarget(self);
        var level = ExecutorApi.StatusBuffLevel(target, SunExpIds.Burn);
        if (level <= 0)
        {
            ExecutorApi.AddStatusBuff(self, target, SunExpIds.Burn, 6, "Target");
        }
        else
        {
            ExecutorApi.AddStatusBuff(self, target, SunExpIds.Burn, level, "Target");
        }
        ExecutorApi.TriggerBurn(self, target);
    }

    private static string NormalizeId(string id)
    {
        return (id ?? "").Replace("*", "").Trim();
    }

    private static void UseBurningCalamity(ScriptExecutor self)
    {
        var target = ExecutorApi.PrimaryTarget(self);
        var level = ExecutorApi.StatusBuffLevel(target, SunExpIds.Burn);
        var spread = level / 2;
        if (spread > 0)
        {
            self.SetStatus("AllTarget");
            self.AddBuff(SunExpIds.Burn, spread.ToString());
            var selectedBurn = target?.GetBuff(SunExpIds.Burn);
            if (selectedBurn?.buffConfig != null)
            {
                var next = selectedBurn.buffConfig.Level - spread;
                if (next <= 0)
                {
                    ExecutorApi.RemoveStatusBuff(self, target, SunExpIds.Burn, "Target");
                }
                else
                {
                    selectedBurn.buffConfig.Level = next;
                }
            }
        }
        if (level > 0)
        {
            ExecutorApi.TriggerBurn(self, target, "Target");
        }
    }

    private static void UseBurningCrownOath(ScriptExecutor self)
    {
        var flame = self.Self?.GetBuff(SunExpIds.GatheredFlame);
        var used = flame?.buffConfig?.Level ?? 0;
        if (used > 0)
        {
            self.SetStatus("Self");
            self.RemoveBuff(SunExpIds.GatheredFlame);
        }
        var add = used / 2;
        if (add > 0)
        {
            self.SetStatus("AllTarget");
            self.AddBuff(SunExpIds.Burn, add.ToString());
            ExecutorApi.TriggerBurnAllEnemies(self);
        }
    }

    private static void UseEmberTower(ScriptExecutor self)
    {
        var converted = ExecutorApi.SelfBuffLevel(self, SunExpIds.Burn);
        if (converted > 0)
        {
            self.SetStatus("Self");
            self.RemoveBuff(SunExpIds.Burn);
            self.AddBuff(SunExpIds.GatheredFlame, converted.ToString());
        }
        if (converted >= 5)
        {
            self.DrawCount("1");
        }
    }

    private static void UseGatheredFlameShield(ScriptExecutor self)
    {
        var flame = self.Self?.GetBuff(SunExpIds.GatheredFlame);
        var used = flame?.buffConfig?.Level ?? 0;
        self.SetStatus("Self");
        if (used > 0)
        {
            self.RemoveBuff(SunExpIds.GatheredFlame);
        }
        self.ChangeDefence((6 + used).ToString());
    }

    private static void UseSolarEclipse(ScriptExecutor self)
    {
        var hasField = ExecutorApi.IsActiveField(self, "scorching_canopy");
        self.SetStatus("AllTarget");
        self.AddBuff(SunExpIds.Burn, "2");
        self.AddBuff("buff_weak", "1");
        if (hasField)
        {
            self.AddBuff("buff_weak", "1");
            self.AddBuff("buff_rotten", "1");
        }
    }

    private static void UseSmokeErosion(ScriptExecutor self)
    {
        var target = ExecutorApi.PrimaryTarget(self);
        var hasNegative = (target?.GetBuffs() ?? Array.Empty<IBuffItem>()).Any(IsNegativeBuff);
        ExecutorApi.SetStatusForTarget(self, target, "Target");
        ExecutorApi.DealDamage(self, CalcSmokeErosionDamage(self));
        if (hasNegative)
        {
            ExecutorApi.AddStatusBuff(self, target, SunExpIds.Burn, 2, "Target");
        }
    }

    private static bool IsNegativeBuff(IBuffItem buff)
    {
        var type = buff?.buffConfig?.Type ?? "";
        return type == "Negative" || type.Contains("负面");
    }

    private static int CalcSolarSparkBaseDamage(ScriptExecutor self)
    {
        return 8 + Math.Min(5, ExecutorApi.SelfBuffLevel(self, SunExpIds.GatheredFlame)) * 2;
    }

    private static int CalcFlamePierceDamage(ScriptExecutor self)
    {
        var target = ExecutorApi.PrimaryTarget(self);
        var burnLevel = ExecutorApi.StatusBuffLevel(target, SunExpIds.Burn);
        var flameLevel = ExecutorApi.SelfBuffLevel(self, SunExpIds.GatheredFlame);
        var multiplier = Math.Max(1, flameLevel / 4);
        return 8 + burnLevel * multiplier;
    }

    private static int CalcSmokeErosionDamage(ScriptExecutor self)
    {
        return 7 + ExecutorApi.StatusBuffLevel(ExecutorApi.PrimaryTarget(self), SunExpIds.Burn);
    }

    private static void InitFlamewheel(ScriptExecutor self)
    {
        SetFlamewheelCost(self, FlamewheelUsed());
        if (ExecutorApi.GetVar(self, "SunExpFlamewheelCostHook", "0") == "1")
        {
            return;
        }

        var token = (DictionaryUtil.ParseInt(ExecutorApi.GetVar(self, "SunExpFlamewheelCostToken", "0")) + 1).ToString();
        var fightStartRegistered = ExecutorApi.TryAddEvent(self, "FightStart", new Action(() =>
        {
            if (!ExecutorApi.IsHookTokenActive(self, "SunExpFlamewheelCostToken", token))
            {
                return;
            }

            SetFlamewheelUsed(0);
            SetFlamewheelCost(self, 0);
            RefreshFlamewheelHand(self, 0);
        }), "flamewheel_recurrence");
        var actionRegistered = ExecutorApi.TryAddEvent(self, "Action", new Action(() =>
        {
            if (!ExecutorApi.IsHookTokenActive(self, "SunExpFlamewheelCostToken", token))
            {
                return;
            }

            RefreshFlamewheelHand(self, FlamewheelUsed());
        }), "flamewheel_recurrence");

        if (fightStartRegistered && actionRegistered)
        {
            ExecutorApi.SetVar(self, "SunExpFlamewheelCostHook", "1");
            ExecutorApi.SetVar(self, "SunExpFlamewheelCostToken", token);
        }
    }

    private static void UseFlamewheel(ScriptExecutor self)
    {
        var times = FlamewheelUsed() + 1;
        SetFlamewheelUsed(times);
        SetFlamewheelCost(self, times);
        RefreshFlamewheelHand(self, times);
        ExecutorApi.TriggerBurnAllEnemies(self, times * 2);
        DictionaryUtil.Set(self.Vars, SunExpIds.SolarTriggerCost, times.ToString());
    }

    private static string FlamewheelKey => "SunExp_flamewheel_recurrence_count";

    private static int FlamewheelUsed()
    {
        return ExecutorApi.CombatIntGet(FlamewheelKey);
    }

    private static void SetFlamewheelUsed(int value)
    {
        ExecutorApi.CombatIntSet(FlamewheelKey, Math.Max(0, value));
    }

    private static void SetFlamewheelCost(ScriptExecutor self, int used)
    {
        DictionaryUtil.Set(self.Vars, "ExCost", used.ToString());
    }

    private static void RefreshFlamewheelHand(ScriptExecutor self, int used)
    {
        foreach (var card in self.HandCard ?? Enumerable.Empty<CardItem>())
        {
            if (card == null)
            {
                continue;
            }

            var id = DictionaryUtil.Get(card.data, "Id");
            if (string.IsNullOrWhiteSpace(id))
            {
                id = DictionaryUtil.Get(card.dataConfig?.data, "Id");
            }

            if (!id.Contains("flamewheel_recurrence"))
            {
                continue;
            }

            DictionaryUtil.Set(card.Vars, "ExCost", used.ToString());
            DictionaryUtil.Set(card.dataConfig?.Vars, "ExCost", used.ToString());
            card.DataUpdate();
        }
    }
}
