using System;
using System.Collections.Generic;
using System.Linq;
using SunExp.Dll.GameApi;
using SunExp.Dll.Infrastructure;
using SunExp.Dll.Mechanics;

namespace SunExp.Dll.Scripting;

public static class CardScripts
{
    private static readonly object DirectInitGate = new();
    private static readonly Dictionary<string, Action<ScriptExecutor>> DirectInitDelegates = new(StringComparer.Ordinal);
    private static readonly Dictionary<string, Action<ScriptExecutor>> InitHandlers = new(StringComparer.Ordinal)
    {
        ["spark"] = InitSpark,
        ["radiant_flame_slash"] = InitRadiantFlameSlash,
        ["burning_star_hex"] = InitBurningStarHex,
        ["blazing_crown_collapse"] = InitBlazingCrownCollapse,
        ["morning_light_bulwark"] = InitMorningLightBulwark,
        ["gathered_flame_shield"] = InitGatheredFlameShield,
        ["smoke_erosion"] = InitSmokeErosion,
        ["solar_scorching_light"] = InitCommonCard,
        ["draw_flame"] = InitDrawFlame,
        ["afterglow_omen_card"] = InitTargetedAttackCard,
        ["scorching_flow_reclaim"] = InitTargetedAttackCard,
        ["eclipse_hex"] = InitTargetedAttackCard,
        ["burning_calamity"] = InitTargetedAttackCard,
        ["flamewheel_recurrence"] = InitFlamewheelCard,
        [SunExpIds.PolymorphCardShortId] = InitCommonCard,
        [SunExpIds.PolymorphRoleTemplateShortId] = InitCommonCard,
        [SunExpIds.ProjectionCardShortId] = InitCommonCard,
        [SunExpIds.ProjectionRoleTemplateShortId] = InitCommonCard,
        [SunExpIds.SpiritBallCardShortId] = InitSpiritBall,
        [SunExpIds.SpiritCardTemplateShortId] = InitSpiritCard,
        [SunExpIds.HeartChangeCardShortId] = InitHeartChange
    };

    private static readonly Dictionary<string, Action<ScriptExecutor>> UseHandlers = new(StringComparer.Ordinal)
    {
        ["spark"] = UseSpark,
        ["scorching_canopy_card"] = UseScorchingCanopyCard,
        ["radiant_flame_slash"] = UseRadiantFlameSlash,
        ["ember_cloak_card"] = UseEmberCloakCard,
        ["draw_flame"] = UseDrawFlame,
        ["solar_prayer"] = UseSolarPrayer,
        ["burning_star_hex"] = UseBurningStarHex,
        ["crown_radiance"] = UseCrownRadiance,
        ["canopy_return"] = UseCanopyReturn,
        ["solar_phase_tuning"] = UseSolarPhaseTuning,
        ["solar_coronation"] = UseSolarCoronation,
        ["blazing_crown_collapse"] = UseBlazingCrownCollapse,
        ["radiant_oath"] = UseRadiantOath,
        ["solar_ignition"] = UseSolarIgnition,
        ["scorching_flow_reclaim"] = UseScorchingFlowReclaim,
        ["impurity_purge"] = UseImpurityPurge,
        ["flamewheel_recurrence"] = UseFlamewheel,
        ["eclipse_hex"] = UseEclipseHex,
        ["solar_scorching_light"] = UseSolarScorchingLight,
        ["burning_calamity"] = UseBurningCalamity,
        ["burning_crown_oath"] = UseBurningCrownOath,
        ["morning_light_bulwark"] = UseMorningLightBulwark,
        ["solar_return"] = UseSolarReturn,
        ["solar_origin_core"] = UseSolarOriginCore,
        ["ember_tower"] = UseEmberTower,
        ["gathered_flame_shield"] = UseGatheredFlameShield,
        ["gathered_flame_cycle"] = UseGatheredFlameCycle,
        ["solar_eclipse"] = UseSolarEclipse,
        ["smoke_erosion"] = UseSmokeErosion,
        ["afterglow_omen_card"] = UseAfterglowOmenCard,
        [SunExpIds.PolymorphCardShortId] = UsePolymorph,
        [SunExpIds.PolymorphRoleTemplateShortId] = UsePolymorphRoleCard,
        [SunExpIds.ProjectionCardShortId] = UseProjection,
        [SunExpIds.ProjectionRoleTemplateShortId] = UseProjectionRoleCard,
        [SunExpIds.SpiritBallCardShortId] = UseSpiritBall,
        [SunExpIds.SpiritCardTemplateShortId] = UseSpiritCard,
        [SunExpIds.HeartChangeCardShortId] = UseHeartChange
    };

    public static void Init(ScriptExecutor self, string id)
    {
        var start = SunExpPerformanceCounters.Timestamp();
        try
        {
            id = NormalizeId(id);
            if (EndlessAbyssCurseService.IsCurseCard(id))
            {
                EndlessAbyssCurseService.Init(self, id);
                return;
            }

            if (IsStarScoreEntry(id))
            {
                StarScoreService.Init(self, id);
                return;
            }

            if (MorningStarCardScripts.IsMorningStarCard(id))
            {
                MorningStarCardScripts.Init(self, id);
                return;
            }

            if (InitHandlers.TryGetValue(id, out var handler))
            {
                handler(self);
                return;
            }

            ExecutorApi.SetBaseScript(self, "CommonCardItem");
        }
        catch (Exception ex)
        {
            SunExpLog.Error("Card Init failed: " + id, ex);
        }
        finally
        {
            BindDirectInit(self, id);
            SunExpCombatCardUiDiagnostics.RecordCurrentSegment("Manual.CardScripts.Init", start);
        }
    }

    private static void BindDirectInit(ScriptExecutor self, string id)
    {
        if (self?.ScriptDict == null)
        {
            return;
        }

        var normalized = NormalizeId(id);
        Action<ScriptExecutor> direct;
        lock (DirectInitGate)
        {
            if (!DirectInitDelegates.TryGetValue(normalized, out direct))
            {
                direct = executor => Init(executor, normalized);
                DirectInitDelegates[normalized] = direct;
            }
        }

        self.ScriptDict["InitScript"] = direct;
    }

    public static void Use(ScriptExecutor self, string id)
    {
        try
        {
            id = NormalizeId(id);
            if (IsStarScoreEntry(id))
            {
                StarScoreService.Use(self, id);
                return;
            }

            if (MorningStarCardScripts.IsMorningStarCard(id))
            {
                MorningStarCardScripts.Use(self, id);
                return;
            }

            if (UseHandlers.TryGetValue(id, out var handler))
            {
                handler(self);
            }
        }
        catch (Exception ex)
        {
            SunExpLog.Error("Card Use failed: " + id, ex);
        }
    }

    private static bool IsStarScoreEntry(string id)
    {
        return StarScoreService.IsStellarOvertureCard(id) || StarScoreService.IsWitchStarScoreCard(id);
    }

    private static void RestorePrimaryTargetForAnimation(ScriptExecutor self, IStatusManager? target)
    {
        ExecutorApi.SetStatusForTarget(self, target, "Target");
    }

    private static void InitSpark(ScriptExecutor self)
    {
        ExecutorApi.SetBaseScript(self, "AttackCardItem", canSelf: false);
        ExecutorApi.AddDamageDescription(self, "1", 5);
    }

    private static void InitRadiantFlameSlash(ScriptExecutor self)
    {
        ExecutorApi.SetBaseScript(self, "AttackCardItem", canSelf: false);
        ExecutorApi.AddDamageDescription(self, "1", ExecutorApi.SolarKeywordDamage(self, 10, ExecutorApi.PrimaryTarget(self)));
        ExecutorApi.AddValueDescription(self, "2", 10);
        ExecutorApi.AddValueDescription(self, "3", 1);
    }

    private static void InitBurningStarHex(ScriptExecutor self)
    {
        ExecutorApi.SetBaseScript(self, "AttackCardItem", canSelf: false);
        const int baseDamage = 8;
        ExecutorApi.AddDamageDescription(self, "1", CalcBurningStarHexDamageAfterGain(self, ExecutorApi.PrimaryTarget(self)));
        ExecutorApi.AddValueDescription(self, "2", baseDamage);
        ExecutorApi.AddValueDescription(self, "3", 1);
    }

    private static void InitBlazingCrownCollapse(ScriptExecutor self)
    {
        ExecutorApi.SetBaseScript(self, "CommonCardItem");
        ExecutorApi.AddDamageDescription(self, "1", ExecutorApi.SolarKeywordDamage(self, 40, ExecutorApi.PrimaryTarget(self), ExecutorApi.SolarCrownTier(self)));
    }

    private static void InitMorningLightBulwark(ScriptExecutor self)
    {
        ExecutorApi.SetBaseScript(self, "CommonCardItem");
        self.AddDescription("1", "Defence", ExecutorApi.SolarKeywordBlock(self, 6).ToString());
    }

    private static void InitGatheredFlameShield(ScriptExecutor self)
    {
        ExecutorApi.SetBaseScript(self, "CommonCardItem");
        self.AddDescription("1", "Defence", (6 + ExecutorApi.SelfBuffLevel(self, SunExpIds.GatheredFlame)).ToString());
    }

    private static void InitSmokeErosion(ScriptExecutor self)
    {
        ExecutorApi.SetBaseScript(self, "AttackCardItem", canSelf: false);
        ExecutorApi.AddDamageDescription(self, "1", CalcSmokeErosionDamage(self));
        ExecutorApi.AddValueDescription(self, "2", 7);
        ExecutorApi.AddValueDescription(self, "3", 1);
    }

    private static void InitSolarScorchingLight(ScriptExecutor self)
    {
        ExecutorApi.SetBaseScript(self, "AttackCardItem", canSelf: false);
        ExecutorApi.AddDamageDescription(self, "1", CalcFlamePierceDamage(self));
    }

    private static void InitCommonCard(ScriptExecutor self)
    {
        ExecutorApi.SetBaseScript(self, "CommonCardItem");
    }

    private static void InitSpiritBall(ScriptExecutor self)
    {
        ExecutorApi.SetBaseScript(self, "AttackCardItem", canSelf: false);
    }

    private static void InitSpiritCard(ScriptExecutor self)
    {
        ExecutorApi.SetBaseScript(self, "CommonCardItem");
    }

    private static void InitDrawFlame(ScriptExecutor self)
    {
        ExecutorApi.SetBaseScript(self, "AttackCardItem");
    }

    private static void InitTargetedAttackCard(ScriptExecutor self)
    {
        ExecutorApi.SetBaseScript(self, "AttackCardItem", canSelf: false);
    }

    private static void InitFlamewheelCard(ScriptExecutor self)
    {
        ExecutorApi.SetBaseScript(self, "CommonCardItem");
        InitFlamewheel(self);
    }

    private static void UseSpark(ScriptExecutor self)
    {
        var target = ExecutorApi.PrimaryTarget(self);
        ExecutorApi.SetStatusForTarget(self, target, "Target");
        ExecutorApi.DealDamage(self, 5);
        ExecutorApi.AddStatusBuff(self, target, SunExpIds.Burn, 2, "Target");
        self.SetStatus("Self");
        self.AddBuff(SunExpIds.SolarRadiance, "1");
        RestorePrimaryTargetForAnimation(self, target);
    }

    private static void UseScorchingCanopyCard(ScriptExecutor self)
    {
        ExecutorApi.ApplyFieldBuff(self, "scorching_canopy", 1, "card.scorching_canopy");
        self.SetStatus("All");
        self.AddBuff(SunExpIds.Burn, "2");
        ExecutorApi.ClearSelfBurnIfProtected(self, includePending: false);
    }

    private static void UseRadiantFlameSlash(ScriptExecutor self)
    {
        ExecutorApi.DealSolarKeywordDamage(self, 10, ExecutorApi.PrimaryTarget(self));
    }

    private static void UseEmberCloakCard(ScriptExecutor self)
    {
        var shield = (ExecutorApi.SelfBuffLevel(self, SunExpIds.Burn) + ExecutorApi.SelfBuffLevel(self, SunExpIds.BodyBurn)) / 2;
        self.SetStatus("Self");
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
        RestorePrimaryTargetForAnimation(self, target);
    }

    private static void UseSolarPrayer(ScriptExecutor self)
    {
        self.SetStatus("Self");
        self.AddBuff(SunExpIds.SolarRadiance, "2");
        ExecutorApi.TransferSelfBurnToRandomFriendly(self);
    }

    private static void UseBurningStarHex(ScriptExecutor self)
    {
        var target = ExecutorApi.PrimaryTarget(self);
        self.SetStatus("Self");
        self.AddBuff(SunExpIds.GatheredFlame, "5");
        ExecutorApi.DealSolarKeywordDamage(self, 8, target);
        ExecutorApi.AddStatusBuff(self, target, SunExpIds.Burn, 2, "Target");
        RestorePrimaryTargetForAnimation(self, target);
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
        ExecutorApi.ApplyFieldBuff(self, "scorching_canopy", 2, "card.canopy_return");
        ExecutorApi.ApplySelfBurn(self, 3, includePending: false);
        foreach (var target in ExecutorApi.EnemyTargets(self))
        {
            ExecutorApi.AddStatusBuff(self, target, SunExpIds.Burn, 3);
        }
        ExecutorApi.TriggerBurnAllEnemies(self);
    }

    private static void UseSolarPhaseTuning(ScriptExecutor self)
    {
        var discarded = CardApi.ThrowAllHandCards(self);
        self.SetStatus("Self");
        if (discarded > 0)
        {
            self.AddBuff(SunExpIds.SolarRadiance, discarded.ToString());
        }
        self.DrawCount("3");
    }

    private static void UseSolarCoronation(ScriptExecutor self)
    {
        self.SetStatus("Self");
        self.AddBuff(SunExpIds.SolarRadiance, "3");
        self.AddBuff(SunExpIds.SolarCrown, "2");
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
        self.SetStatus("AllTarget");
    }

    private static void UseRadiantOath(ScriptExecutor self)
    {
        self.SetStatus("Self");
        self.AddBuff(SunExpIds.SolarRadiance, "3");
        if (!ExecutorApi.IsActiveField(self, "scorching_canopy"))
        {
            ExecutorApi.ApplyFieldBuff(self, "scorching_canopy", 1, "card.radiant_oath");
        }
        else
        {
            self.DrawCount("1");
        }
    }

    private static void UseSolarIgnition(ScriptExecutor self)
    {
        foreach (var target in ExecutorApi.EnemyTargets(self))
        {
            ExecutorApi.AddStatusBuff(self, target, SunExpIds.Burn, 2);
        }

        ExecutorApi.TriggerBurnAllEnemies(self);
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
        RestorePrimaryTargetForAnimation(self, target);
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
        ExecutorApi.AddStatusBuff(self, target, SunExpIds.Burn, Math.Max(8, level), "Target");
        ExecutorApi.TriggerBurn(self, target);
    }

    private static void UseSolarScorchingLight(ScriptExecutor self)
    {
        var burn = ExecutorApi.SelfBuffLevel(self, SunExpIds.Burn);
        ExecutorApi.TriggerBurn(self, self.Self, "Self");
        if (burn > 0)
        {
            self.SetStatus("AllTarget");
            self.AddBuff(SunExpIds.Burn, (burn * 2).ToString());
        }
    }

    private static void UseMorningLightBulwark(ScriptExecutor self)
    {
        ExecutorApi.ApplySolarKeywordSkill(self, 6);
    }

    private static void UseSolarReturn(ScriptExecutor self)
    {
        self.SetStatus("Self");
        self.AddBuff(SunExpIds.SolarRadiance, "1");
        self.DrawCount("1");
    }

    private static void UseSolarOriginCore(ScriptExecutor self)
    {
        var burned = CardApi.BurnAllHandCards(self);
        if (burned > 0)
        {
            self.ChangePower(burned.ToString());
        }
    }

    private static void UseGatheredFlameCycle(ScriptExecutor self)
    {
        self.SetStatus("Self");
        self.AddBuff(SunExpIds.CycleGatheredFlame, "1");
    }

    private static void UseAfterglowOmenCard(ScriptExecutor self)
    {
        var target = ExecutorApi.PrimaryTarget(self);
        var removed = ExecutorApi.RemoveBuffsExceptAndCount(self, target, SunExpIds.Burn, SunExpIds.BodyBurn);
        if (removed > 0)
        {
            ExecutorApi.AddStatusBuff(self, target, SunExpIds.Burn, removed, "Target");
        }
        RestorePrimaryTargetForAnimation(self, target);
    }

    private static void UsePolymorph(ScriptExecutor self)
    {
        PolymorphActivationService.OpenRoleSelection(self);
    }

    private static void UsePolymorphRoleCard(ScriptExecutor self)
    {
        PolymorphActivationService.ApplyRoleFromCard(self);
    }

    private static void UseProjection(ScriptExecutor self)
    {
        ProjectionActivationService.GrantCurrentRoleCard(self);
    }

    private static void UseProjectionRoleCard(ScriptExecutor self)
    {
        ProjectionActivationService.SummonFromCard(self);
    }

    private static void UseSpiritBall(ScriptExecutor self)
    {
        SpiritCaptureService.TryCapture(self);
    }

    private static void UseSpiritCard(ScriptExecutor self)
    {
        SpiritSummonService.TrySummon(self);
    }

    private static void InitHeartChange(ScriptExecutor self)
    {
        ExecutorApi.SetBaseScript(self, "AttackCardItem", canSelf: false);
    }

    private static void UseHeartChange(ScriptExecutor self)
    {
        HeartChangeControlService.TryControlFromCard(self);
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
        RestorePrimaryTargetForAnimation(self, target);
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
        var converted = ExecutorApi.SelfBuffLevel(self, SunExpIds.Ember)
            + ExecutorApi.SelfBuffLevel(self, SunExpIds.Burn);
        if (converted > 0)
        {
            self.SetStatus("Self");
            self.RemoveBuff(SunExpIds.Ember);
            self.RemoveBuff(SunExpIds.Burn);
            self.AddBuff(SunExpIds.GatheredFlame, converted.ToString());
        }

        var draw = converted / 5;
        if (draw > 0)
        {
            self.DrawCount(draw.ToString());
        }
    }

    public static void Draw(ScriptExecutor self, string id)
    {
        try
        {
            id = NormalizeId(id);
            if (EndlessAbyssCurseService.IsCurseCard(id))
            {
                EndlessAbyssCurseService.Draw(self, id);
            }
        }
        catch (Exception ex)
        {
            SunExpLog.Error("Card Draw failed: " + id, ex);
        }
    }

    public static void Drop(ScriptExecutor self, string id)
    {
        try
        {
            id = NormalizeId(id);
            if (EndlessAbyssCurseService.IsCurseCard(id))
            {
                EndlessAbyssCurseService.Drop(self, id);
            }
        }
        catch (Exception ex)
        {
            SunExpLog.Error("Card Drop failed: " + id, ex);
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
        self.AddBuff(SunExpIds.Burn, "3");
        if (hasField)
        {
            self.AddBuff("buff_rotten", "1");
            foreach (var target in ExecutorApi.EnemyTargets(self))
            {
                ExecutorApi.RemoveRandomPositiveBuff(self, target);
            }
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

    private static int CalcBurningStarHexDamageAfterGain(ScriptExecutor self, IStatusManager? target)
    {
        var radiance = ExecutorApi.SelfBuffLevel(self, SunExpIds.SolarRadiance);
        var flame = ExecutorApi.SelfBuffLevel(self, SunExpIds.GatheredFlame) + 5;
        var burn = ExecutorApi.StatusBuffLevel(target, SunExpIds.Burn);
        var coefficient = ExecutorApi.SolarMultiplier(self) * (radiance * 2 + flame / 3 + burn / 2);
        return 8 + coefficient;
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
        var fightStartRegistered = ExecutorApi.TryAddTokenedEvent(self, "FightStart", "SunExpFlamewheelCostToken", token, new Action(() =>
        {
            SetFlamewheelUsed(0);
            SetFlamewheelCost(self, 0);
            RefreshFlamewheelHand(self, 0);
        }), "flamewheel_recurrence");
        var actionRegistered = ExecutorApi.TryAddTokenedEvent(self, "Action", "SunExpFlamewheelCostToken", token, new Action(() =>
        {
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
            SunExpCardRefreshQueue.RequestCostUpdate(card, "FlamewheelHand");
        }
    }
}
