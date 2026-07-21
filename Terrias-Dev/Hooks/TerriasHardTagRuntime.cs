using System;
using System.Collections.Generic;
using System.Linq;
using AuraShared.Core;
using AuraGameData.Shared.GameApi;
using Data.Save;
using Terrias.Dll.GameApi;
using Terrias.Dll.Infrastructure;
using Terrias.Dll.Mechanics;
using Witch;
using Witch.Core;
using Witch.Mod;
using Witch.UI.Window;

namespace Terrias.Dll.Hooks;

public static class TerriasHardTagRuntime
{
    private const string AbyssalShockAppliedBoundariesKey = "Terrias_Hard_AbyssalShock_AppliedBoundaries";
    private const string AbyssalShockHpStacksKey = "TerriasHard_AbyssalShockHpStacks";
    private const string AbyssalShockHpStacksAppliedKey = "TerriasHardAbyssalShockHpStacksApplied";
    private const string FragmentedTag = "Fragmented";
    private static readonly object EventOwner = new();
    private static readonly Dictionary<string, int> SkillCooldownBeforeUse = new(StringComparer.Ordinal);
    private static string? registeredPlayerStatusId;
    private static string? registeredAbyssGazeEndRoundStatusId;
    private static int stagnantWaterRefreshSequence;
    private static bool cardLifecycleRegistered;

    public static void Initialize(ModConfig modConfig)
    {
        TerriasCombatActionRouter.RegisterActionEventHandler(
            "EndlessAbyssGaze",
            context => EndlessAbyssGazePressureService.OnCardAction(context.Config, "Action"),
            () => EndlessAbyssGazePressureService.OnCardActionAfter("ActionAfter"));
        TerriasBattleLifecycleRouter.Register("HardTag", new TerriasBattleLifecycleSubscription
        {
            FightInitializing = OnFightInitializing,
            FightInitialized = OnFightInitialized,
            FightEnding = OnFightEnding
        });
        TerriasStatusLifecycleRouter.Register("HardTag", new TerriasStatusLifecycleSubscription
        {
            AfterEnemyInit = OnEnemyInit
        });
        TerriasCombatActionRouter.Register("HardTag", new TerriasCombatActionSubscription
        {
            BeforeOtherObjAction = OnEnemyDoOneAction
        });
        RegisterAfter(modConfig, TerriasHookTargets.FightPlayerTurnInit, OnPlayerTurn);
        RegisterBefore(modConfig, TerriasHookTargets.SkillItemTrueUse, OnSkillUseBefore);
        RegisterAfter(modConfig, TerriasHookTargets.SkillItemTrueUse, OnSkillUseAfter);
        TerriasLog.Info("Terrias hard tag runtime initialized");
    }

    private static void EnsureCardLifecycleRegistered()
    {
        if (cardLifecycleRegistered)
        {
            return;
        }

        cardLifecycleRegistered = true;
        TerriasCardLifecycleRouter.Register("HardTag", new TerriasCardLifecycleSubscription
        {
            AfterCardItemInit = OnCardItemChanged,
            AfterAttackCardItemInit = OnCardItemChanged,
            AfterCardItemDataUpdate = OnCardItemChanged,
            AfterAttackCardItemDataUpdate = OnCardItemChanged,
            AfterFightUiCreateCardItem = OnFightUiCreateCard,
            AfterFightUiCreateCardItemInternal = OnFightUiCreateCardInternal,
            AfterScriptExecutorGetCardFromDeck = OnScriptExecutorGetCardFromDeck,
            AfterScriptExecutorRandomAddCard = OnScriptExecutorRandomAddCard,
            BeforeCommonCardUse = OnCardUseBefore,
            BeforeAttackCardUse = OnCardUseBefore,
            AfterCommonCardUse = OnCardUseAfter,
            AfterAttackCardUse = OnCardUseAfter
        });
    }

    public static List<DataConfig> SelectedRuntimeHardTags()
    {
        var result = new List<DataConfig>();
        try
        {
            var entries = Singleton<GameRuntimeData>.Instance?.HardTags;
            if (entries == null)
            {
                return result;
            }

            foreach (var entry in entries)
            {
                if (entry == null)
                {
                    continue;
                }

                var id = DictionaryUtil.Get(entry.Data, "Id");
                if (string.IsNullOrWhiteSpace(id) || entry.DynamicValue <= 0)
                {
                    continue;
                }

                for (var i = 0; i < entry.DynamicValue; i++)
                {
                    if (AuraGameDataHostApi.Materialize(DataType.Hard, id).Instance is DataConfig hard)
                    {
                        result.Add(hard);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            TerriasLog.Warn("Failed to capture selected hard tags: " + ex.Message);
        }

        return result;
    }

    private static void RegisterBefore(ModConfig config, string target, Action<ModHookContext> action)
    {
        TerriasHookRegistry.Before(config, target, action, "HardTag");
    }

    private static void RegisterAfter(ModConfig config, string target, Action<ModHookContext> action)
    {
        TerriasHookRegistry.After(config, target, action, "HardTag");
    }

    private static void OnFightInitializing(ModHookContext context)
    {
        registeredPlayerStatusId = null;
        registeredAbyssGazeEndRoundStatusId = null;
        SkillCooldownBeforeUse.Clear();
        EventCenter.Instance.Clear(EventOwner);
    }

    private static void OnFightInitialized(ModHookContext context)
    {
        try
        {
            if (!HasAnyTerriasHardTag())
            {
                return;
            }

            EnsureCardLifecycleRegistered();
            TerriasLifecycleStepRunner.RunBattleOnce(
                "HardTag",
                "FightInitialized",
                new[]
                {
                    new TerriasFrameStep("SunsetExpedition", ApplySunsetExpedition),
                    new TerriasFrameStep("MorningStarDimmed", () => MorningStarDimmedService.OnFightStarted(CurrentPlayerExecutor(), "FightInit.Init")),
                    new TerriasFrameStep("AbyssGazeReset", () => EndlessAbyssGazePressureService.ResetPlayerTurn(CurrentPlayerExecutor(), "FightInit.Init")),
                    new TerriasFrameStep("AbyssGazeActionRouter", () => TerriasActionEventRouter.ResetForFight("AbyssGaze.FightInit.Init")),
                    new TerriasFrameStep("AbyssGazeEndRoundListener", () => RegisterAbyssGazeEndRoundListener("FightInit.Init")),
                    new TerriasFrameStep("BlackSunListener", () => RegisterPlayerRoundListener("FightInit.Init"))
                },
                AuraSharedFramePhase.GameplayMutation,
                priority: 10,
                estimatedCost: 8);
        }
        catch (Exception ex)
        {
            TerriasLog.Error("Terrias hard tag fight initialized failed", ex);
        }
    }

    private static void OnPlayerTurn(ModHookContext context)
    {
        try
        {
            if (!HasAnyTerriasHardTag())
            {
                return;
            }

            MorningStarDimmedService.ApplyToCombatCards(CurrentPlayerExecutor(), "Fight_PlayerTurn.Init");
            EndlessAbyssGazePressureService.ResetPlayerTurn(CurrentPlayerExecutor(), "Fight_PlayerTurn.Init");
            TerriasActionEventRouter.EnsureRegistered("AbyssGaze.Fight_PlayerTurn.Init");
            RegisterAbyssGazeEndRoundListener("Fight_PlayerTurn.Init");
            RegisterPlayerRoundListener("Fight_PlayerTurn.Init");
        }
        catch (Exception ex)
        {
            TerriasLog.Error("Terrias hard tag player turn failed", ex);
        }
    }

    private static void OnFightEnding(ModHookContext context)
    {
        try
        {
            EndlessAbyssCrackService.RestoreTemporaryCracks("FightEnding");
            EndlessAbyssCurseService.CleanupTemporaryCombatCurses("FightEnding");
        }
        catch (Exception ex)
        {
            TerriasLog.Error("Terrias crack restore failed", ex);
        }
    }

    private static void OnEnemyInit(ModHookContext context)
    {
        try
        {
            if (!HasAnyTerriasHardTag() || !IsServerAuthority() || context.Target is not Enemy enemy)
            {
                return;
            }

            TryTriggerAbyssalShock("Enemy.Init");
            ApplyAbyssalShockHpToEnemy(enemy, "Enemy.Init");

            var status = enemy.Status;
            if (status == null)
            {
                return;
            }

            if (TerriasHardTagState.Active(TerriasHardTagIds.Rebirth))
            {
                status.AddBuff(TerriasHardTagIds.RebirthBuff, 50);
            }
        }
        catch (Exception ex)
        {
            TerriasLog.Error("Terrias hard tag enemy init failed", ex);
        }
    }

    private static void OnEnemyDoOneAction(ModHookContext context)
    {
        try
        {
            if (!TerriasHardTagState.Active(TerriasHardTagIds.WhiteRadianceCourt)
                || !IsServerAuthority()
                || context.Target is not Enemy enemy
                || enemy.Status == null
                || enemy.Status.state != IStatusManager.State.Default
                || enemy.Status.CurHp <= 0)
            {
                return;
            }

            enemy.Status.AddBuff(TerriasIds.SolarRadiance, 1);
        }
        catch (Exception ex)
        {
            TerriasLog.Error("Terrias white court enemy action failed", ex);
        }
    }

    private static void OnCardUseBefore(ModHookContext context)
    {
        try
        {
            if (TerriasHardTagState.Active(TerriasHardTagIds.MorningStarDimmed))
            {
                MorningStarDimmedService.ApplyToCard(context.Target as CardItem, "CardUseBefore");
            }

            if (TerriasHardTagState.Active(TerriasHardTagIds.AbyssGaze))
            {
                TerriasActionEventRouter.EnsureRegistered("AbyssGaze.CardUseBefore");
            }
        }
        catch (Exception ex)
        {
            TerriasLog.Error("Terrias hard tag card-use before failed", ex);
        }
    }

    private static void OnCardUseAfter(ModHookContext context)
    {
        try
        {
            if (TerriasHardTagState.Active(TerriasHardTagIds.MorningStarDimmed))
            {
                MorningStarDimmedService.ApplyToCombatCards(CurrentPlayerExecutor(), "CardUseAfter");
            }

            EndlessAbyssCrackService.OnCardPlayed(context.Target as CardItem, "CardUseAfter");
            EndlessAbyssGazePressureService.OnCardUseAfter(context.Target as CardItem, "CardUseAfter");
        }
        catch (Exception ex)
        {
            TerriasLog.Error("Terrias hard tag card-use after failed", ex);
        }
    }

    private static void OnCardItemChanged(ModHookContext context)
    {
        try
        {
            if (TerriasHardTagState.Active(TerriasHardTagIds.MorningStarDimmed))
            {
                MorningStarDimmedService.ApplyToCard(context.Target as CardItem, "CardItem");
            }
        }
        catch (Exception ex)
        {
            TerriasLog.Error("Terrias hard tag card item hook failed", ex);
        }
    }

    private static void OnFightUiCreateCard(ModHookContext context)
    {
        try
        {
            if (TerriasHardTagState.Active(TerriasHardTagIds.MorningStarDimmed))
            {
                MorningStarDimmedService.ApplyToCombatCards(CurrentPlayerExecutor(), "FightUI.CreateCardItem");
            }
        }
        catch (Exception ex)
        {
            TerriasLog.Error("Terrias hard tag combat card scan failed", ex);
        }
    }

    private static void OnFightUiCreateCardInternal(ModHookContext context)
    {
        try
        {
            if (TerriasHardTagState.Active(TerriasHardTagIds.MorningStarDimmed))
            {
                MorningStarDimmedService.ApplyToCombatCards(CurrentPlayerExecutor(), "FightUI.CreateCardItemInternal");
            }

            var args = context.Arguments;
            if (args != null && args.Length > 0 && args[0] is IDataConfig config)
            {
                EndlessAbyssGazePressureService.OnCardGained(CurrentPlayerExecutor(), config, "FightUI.CreateCardItemInternal");
            }
        }
        catch (Exception ex)
        {
            TerriasLog.Error("Terrias hard tag combat card materialize hook failed", ex);
        }
    }

    private static void OnScriptExecutorGetCardFromDeck(ModHookContext context)
    {
        try
        {
            var args = context.Arguments;
            if (args != null && args.Length > 0 && args[0] is IDataConfig config)
            {
                if (TerriasHardTagState.Active(TerriasHardTagIds.MorningStarDimmed))
                {
                    MorningStarDimmedService.ApplyToConfig(config, "ScriptExecutor.GetCardFromDeck:arg");
                }

                EndlessAbyssGazePressureService.OnCardGained(context.Target as ScriptExecutor, config, "ScriptExecutor.GetCardFromDeck");
            }

            if (TerriasHardTagState.Active(TerriasHardTagIds.MorningStarDimmed))
            {
                MorningStarDimmedService.ApplyToCombatCards(context.Target as ScriptExecutor, "ScriptExecutor.GetCardFromDeck");
            }
        }
        catch (Exception ex)
        {
            TerriasLog.Error("Terrias hard tag deck draw hook failed", ex);
        }
    }

    private static void OnScriptExecutorRandomAddCard(ModHookContext context)
    {
        try
        {
            var args = context.Arguments;
            var cardId = args != null && args.Length > 0 ? Convert.ToString(args[0]) ?? "" : "";
            EndlessAbyssGazePressureService.OnCardGainedById(context.Target as ScriptExecutor, cardId, "ScriptExecutor.RandomAddCard");
        }
        catch (Exception ex)
        {
            TerriasLog.Error("Terrias hard tag random add card hook failed", ex);
        }
    }

    private static void OnSkillUseBefore(ModHookContext context)
    {
        try
        {
            if (!TerriasHardTagState.Active(TerriasHardTagIds.OtherDimensionStagnantWater)
                || context.Target is not SkillItem skillItem)
            {
                return;
            }

            CaptureStagnantWaterSkillCooldown(skillItem);
        }
        catch (Exception ex)
        {
            TerriasLog.Error("Terrias hard tag skill-use before failed", ex);
        }
    }

    private static void OnSkillUseAfter(ModHookContext context)
    {
        try
        {
            if (!TerriasHardTagState.Active(TerriasHardTagIds.OtherDimensionStagnantWater)
                || context.Target is not SkillItem skillItem)
            {
                return;
            }

            ScheduleStagnantWaterCooldownDouble(skillItem);
        }
        catch (Exception ex)
        {
            TerriasLog.Error("Terrias hard tag skill-use after failed", ex);
        }
    }

    private static void RegisterPlayerRoundListener(string source)
    {
        if (!TerriasHardTagState.Active(TerriasHardTagIds.BlackSunCalamity))
        {
            return;
        }

        var player = FightPlayer.Instance;
        var status = player?.Status;
        var executor = status?.MirrorSc as ScriptExecutor;
        var statusId = status?.InstanceId;
        if (string.IsNullOrWhiteSpace(statusId) || executor == null || registeredPlayerStatusId == statusId)
        {
            return;
        }

        EventCenter.Instance.AddEventListener("StartRound" + statusId, new Action(() => OnLocalPlayerStartRound(executor)), EventOwner, EventDispose.OnFightEnd);
        registeredPlayerStatusId = statusId;
        TerriasLog.Info("Registered black sun player StartRound listener from " + source + ": statusId=" + statusId);
    }

    private static void RegisterAbyssGazeEndRoundListener(string source)
    {
        if (!TerriasHardTagState.Active(TerriasHardTagIds.AbyssGaze))
        {
            return;
        }

        var player = FightPlayer.Instance;
        var status = player?.Status;
        var executor = status?.MirrorSc as ScriptExecutor;
        var statusId = status?.InstanceId;
        if (string.IsNullOrWhiteSpace(statusId) || executor == null || registeredAbyssGazeEndRoundStatusId == statusId)
        {
            return;
        }

        EventCenter.Instance.AddEventListener(
            "EndRound" + statusId,
            new Action(() => EndlessAbyssGazePressureService.ResetPlayerTurn(executor, "EndRound")),
            EventOwner,
            EventDispose.OnFightEnd);
        registeredAbyssGazeEndRoundStatusId = statusId;
        TerriasLog.Info("Registered abyss gaze EndRound listener from " + source + ": statusId=" + statusId);
    }

    private static void OnLocalPlayerStartRound(ScriptExecutor executor)
    {
        if (TerriasHardTagState.Active(TerriasHardTagIds.MorningStarDimmed))
        {
            MorningStarDimmedService.ApplyToCombatCards(executor, "StartRound");
        }

        if (!TerriasHardTagState.Active(TerriasHardTagIds.BlackSunCalamity))
        {
            return;
        }

        var statusId = PlayerApi.LocalPlayerStatusId();
        var key = string.IsNullOrWhiteSpace(statusId)
            ? "TerriasHard_BlackSun_LocalTurnCount"
            : "TerriasHard_BlackSun_TurnCount_" + statusId;
        var count = ExecutorApi.CombatIntAdd(key, 1);
        if (count % 5 == 0)
        {
            AnnihilateRandomLocalCard(executor);
        }
    }

    private static void ApplySunsetExpedition()
    {
        if (!TerriasHardTagState.Active(TerriasHardTagIds.SunsetExpedition))
        {
            return;
        }

        var count = Math.Max(0, DictionaryUtil.ParseInt(PlayerApi.GetGameVar(TerriasIds.HardSunsetFightCountKey, "0")));
        var status = FightPlayer.Instance?.Status;
        var statusId = string.IsNullOrWhiteSpace(status?.InstanceId) ? "local" : status?.InstanceId ?? "local";
        if (!AuraLifecycleOperationLedger.TryClaimBattleOperation(
                TerriasIds.ModId,
                "HardTag",
                "SunsetExpedition",
                statusId,
                "start-effect",
                "hp-and-count"))
        {
            return;
        }

        var percent = Math.Min(50, count);
        if (percent > 0 && status != null && status.CurHp > 1)
        {
            var oldHp = status.CurHp;
            var damage = Math.Max(1, oldHp * percent / 100);
            var nextHp = Math.Max(1, oldHp - damage);
            if (nextHp < oldHp)
            {
                status.CurHp = nextHp;
                TerriasLog.Info("[SunsetExpedition] count="
                    + count
                    + "; percent="
                    + percent
                    + "; hp="
                    + oldHp
                    + "->"
                    + nextHp
                    + "; statusId="
                    + status.InstanceId);
            }
        }

        if (IsServerAuthority())
        {
            PlayerApi.SetGameVar(TerriasIds.HardSunsetFightCountKey, (count + 1).ToString());
        }
    }

    private static void CaptureStagnantWaterSkillCooldown(SkillItem skillItem)
    {
        var config = skillItem.dataConfig;
        if (!RoleSkillApi.IsCurrentCareerSkill(config))
        {
            return;
        }

        var skillId = RoleSkillApi.NormalizeSkillId(CardConfigApi.Id(config));
        if (string.IsNullOrWhiteSpace(skillId))
        {
            return;
        }

        SkillCooldownBeforeUse[skillId] = PlayerApi.GetSkillTime(skillId);
    }

    private static void ScheduleStagnantWaterCooldownDouble(SkillItem skillItem)
    {
        var config = skillItem.dataConfig;
        if (!RoleSkillApi.IsCurrentCareerSkill(config))
        {
            return;
        }

        var skillId = RoleSkillApi.NormalizeSkillId(CardConfigApi.Id(config));
        if (string.IsNullOrWhiteSpace(skillId) || !SkillCooldownBeforeUse.TryGetValue(skillId, out var before))
        {
            return;
        }

        SkillCooldownBeforeUse.Remove(skillId);
        var executor = skillItem.scriptExecutor as ScriptExecutor;
        var token = ++stagnantWaterRefreshSequence;
        TerriasFrameDispatcher.RunOnceNextFrame(
            "TerriasHard.StagnantWaterCooldown." + token,
            () => DoubleStagnantWaterCooldown(skillId, before, executor, "SkillItem.TrueUse"));
    }

    private static void DoubleStagnantWaterCooldown(string skillId, int before, ScriptExecutor? executor, string source)
    {
        try
        {
            var current = PlayerApi.GetSkillTime(skillId);
            if (current <= 0 || current <= before)
            {
                return;
            }

            var doubled = current > int.MaxValue / 2 ? int.MaxValue : current * 2;
            PlayerApi.SetSkillTime(skillId, doubled);
            try
            {
                executor?.UpdateSkillTime();
            }
            catch (Exception ex)
            {
                TerriasLog.Debug("[StagnantWater] skill UI refresh skipped: " + ex.Message);
            }

            PlayerApi.ShowCaption("迟滞之水：技能冷却翻倍。");
            TerriasLog.Info("[StagnantWater] doubled skill cooldown from "
                + source
                + ": "
                + skillId
                + "="
                + current
                + "->"
                + doubled
                + ".");
        }
        catch (Exception ex)
        {
            TerriasLog.Warn("[StagnantWater] cooldown double failed from " + source + ": " + ex.Message);
        }
    }

    private static void TryTriggerAbyssalShock(string source)
    {
        if (!TerriasHardTagState.Active(TerriasHardTagIds.AbyssalShock))
        {
            return;
        }

        var boundary = CurrentAbyssalShockBoundary();
        if (boundary <= 0 || HasTriggeredAbyssalShockBoundary(boundary))
        {
            return;
        }

        MarkTriggeredAbyssalShockBoundary(boundary);
        var option = PickIndex(3);
        switch (option)
        {
            case 0:
                var changed = AddFragmentedToRandomDeckCards(2, source + ":boundary:" + boundary);
                PlayerApi.ShowCaption(changed > 0
                    ? "深渊震荡：" + changed + "张卡牌获得【碎裂】。"
                    : "深渊震荡：没有可添加【碎裂】的卡牌。");
                break;
            case 1:
                var stacks = CombatVarApi.AddInt(AbyssalShockHpStacksKey, 1);
                PlayerApi.ShowCaption("深渊震荡：敌方全体生命值提高30%。");
                TerriasLog.Info("[AbyssalShock] HP stack increased to "
                    + stacks
                    + " at boundary "
                    + boundary
                    + " from "
                    + source
                    + ".");
                break;
            default:
                var destroyed = DestroyRandomEquippedRelic(source + ":boundary:" + boundary);
                PlayerApi.ShowCaption(destroyed
                    ? "深渊震荡：随机已装备遗物被销毁。"
                    : "深渊震荡：没有可销毁的已装备遗物。");
                break;
        }
    }

    private static int CurrentAbyssalShockBoundary()
    {
        var level = Math.Max(0, MapManager.Instance?.Level ?? 0);
        return level > 0 && level % 6 == 0 ? level / 6 : 0;
    }

    private static bool HasTriggeredAbyssalShockBoundary(int boundary)
    {
        var token = boundary.ToString();
        var applied = PlayerApi.GetGameVar(AbyssalShockAppliedBoundariesKey, "");
        return DictionaryUtil.ContainsToken(applied, token);
    }

    private static void MarkTriggeredAbyssalShockBoundary(int boundary)
    {
        var token = boundary.ToString();
        var applied = PlayerApi.GetGameVar(AbyssalShockAppliedBoundariesKey, "");
        if (DictionaryUtil.ContainsToken(applied, token))
        {
            return;
        }

        PlayerApi.SetGameVar(AbyssalShockAppliedBoundariesKey, string.IsNullOrWhiteSpace(applied) ? token : applied + "," + token);
    }

    private static int AddFragmentedToRandomDeckCards(int count, string source)
    {
        try
        {
            var role = RoleTable.Instance;
            if (role?.cardList == null || count <= 0)
            {
                return 0;
            }

            var candidates = role.cardList
                .Where(card => card != null && !HasNativeTag(card, FragmentedTag))
                .ToList();
            var changed = 0;
            while (changed < count && candidates.Count > 0)
            {
                var index = PickIndex(candidates.Count);
                var card = candidates[index];
                candidates.RemoveAt(index);
                if (CardMutationService.AddNativeTags(card, FragmentedTag))
                {
                    changed++;
                }
            }

            if (changed > 0)
            {
                GameSaveManager.UpdateRoles(role);
                TerriasLog.Info("[AbyssalShock] added Fragmented to "
                    + changed
                    + " deck cards from "
                    + source
                    + ".");
            }

            return changed;
        }
        catch (Exception ex)
        {
            TerriasLog.Warn("[AbyssalShock] add Fragmented failed from " + source + ": " + ex.Message);
            return 0;
        }
    }

    private static bool DestroyRandomEquippedRelic(string source)
    {
        try
        {
            var role = RoleTable.Instance;
            if (role?.relicList == null || role.relicList.Count == 0)
            {
                return false;
            }

            var index = PickIndex(role.relicList.Count);
            var relic = role.relicList[index];
            role.relicList.RemoveAt(index);
            GameSaveManager.UpdateRoles(role);
            TerriasLog.Info("[AbyssalShock] destroyed equipped relic from "
                + source
                + ": "
                + DictionaryUtil.Get(relic?.data, "Id"));
            return true;
        }
        catch (Exception ex)
        {
            TerriasLog.Warn("[AbyssalShock] destroy relic failed from " + source + ": " + ex.Message);
            return false;
        }
    }

    private static void ApplyAbyssalShockHpToEnemy(Enemy enemy, string source)
    {
        var stacks = CombatVarApi.GetInt(AbyssalShockHpStacksKey);
        if (stacks <= 0
            || enemy.Status is not StatusManager status
            || status.state != IStatusManager.State.Default
            || status.CurHp <= 0)
        {
            return;
        }

        var applied = AppliedAbyssalShockHpStacks(status);
        if (applied >= stacks)
        {
            return;
        }

        var oldMaxHp = Math.Max(1, enemy.MaxHp);
        var oldCurHp = Math.Max(1, enemy.CurHp);
        var nextMaxHp = oldMaxHp;
        var nextCurHp = oldCurHp;
        while (applied < stacks)
        {
            nextMaxHp = ScaleAbyssalShockHp(nextMaxHp);
            nextCurHp = Math.Min(nextMaxHp, ScaleAbyssalShockHp(nextCurHp));
            applied++;
        }

        enemy.MaxHp = nextMaxHp;
        enemy.CurHp = nextCurHp;
        status.MaxHp = nextMaxHp;
        status.CurHp = nextCurHp;
        MarkAbyssalShockHpStacks(status, applied);
        RefreshStatusTransfer(enemy, status);

        TerriasLog.Info("[AbyssalShock] scaled enemy HP from "
            + source
            + ": stacks="
            + stacks
            + "; id="
            + DictionaryUtil.Get(enemy.data, "Id")
            + "; instance="
            + enemy.InstanceId
            + "; max="
            + oldMaxHp
            + "->"
            + nextMaxHp
            + "; cur="
            + oldCurHp
            + "->"
            + nextCurHp
            + ".");
    }

    private static int ScaleAbyssalShockHp(int value)
    {
        var scaled = Math.Ceiling(Math.Max(1, value) * 1.3);
        return (int)Math.Max(1, Math.Min(int.MaxValue, scaled));
    }

    private static int AppliedAbyssalShockHpStacks(StatusManager status)
    {
        return status.dynamicVariables != null
            && status.dynamicVariables.TryGetValue(AbyssalShockHpStacksAppliedKey, out var value)
            ? Math.Max(0, (int)value)
            : 0;
    }

    private static void MarkAbyssalShockHpStacks(StatusManager status, int stacks)
    {
        status.dynamicVariables ??= new Dictionary<string, float>();
        status.dynamicVariables[AbyssalShockHpStacksAppliedKey] = stacks;
    }

    private static bool HasNativeTag(IDataConfig config, string tag)
    {
        return DictionaryUtil.ContainsToken(DictionaryUtil.Get(config.Vars, "Tag"), tag)
            || DictionaryUtil.ContainsToken(DictionaryUtil.Get(config.data, "Tag"), tag);
    }

    private static int PickIndex(int count)
    {
        if (count <= 1)
        {
            return 0;
        }

        try
        {
            var value = Math.Abs((MapManager.Instance?.NowDice ?? Dice.Default).Roll().Value);
            return value % count;
        }
        catch
        {
            return Math.Abs(Environment.TickCount) % count;
        }
    }

    private static void RefreshStatusTransfer(Enemy enemy, StatusManager status)
    {
        try
        {
            var manager = FightManager.Instance;
            if (manager == null
                || string.IsNullOrWhiteSpace(enemy.InstanceId)
                || !manager.statusData.ContainsKey(enemy.InstanceId))
            {
                return;
            }

            manager.statusData[enemy.InstanceId] = new StatusDataTransfer(status);
        }
        catch (Exception ex)
        {
            TerriasLog.Warn("[AbyssalShock] enemy HP status transfer refresh failed: " + ex.Message);
        }
    }

    private static ScriptExecutor? CurrentPlayerExecutor()
    {
        return FightPlayer.Instance?.Status?.MirrorSc as ScriptExecutor;
    }

    private static void AnnihilateRandomLocalCard(ScriptExecutor executor)
    {
        var pool = BuildAnnihilationPool(executor);
        if (pool.Count <= 0)
        {
            return;
        }

        var index = UnityEngine.Random.Range(0, pool.Count);
        executor.BurnCardByData(pool[index]);
    }

    private static List<IDataConfig> BuildAnnihilationPool(ScriptExecutor executor)
    {
        var cards = new List<IDataConfig>();
        foreach (var card in executor.HandCard ?? Enumerable.Empty<CardItem>())
        {
            if (card?.dataConfig != null)
            {
                cards.Add(card.dataConfig);
            }
        }

        cards.AddRange((executor.DeckCard ?? new List<DataConfig>()).Where(card => card != null));
        cards.AddRange((executor.UsedCard ?? new List<DataConfig>()).Where(card => card != null));
        return cards;
    }

    private static bool IsServerAuthority()
    {
        return PlayerManager.Instance == null || PlayerManager.Instance.isServer;
    }

    private static bool HasAnyTerriasHardTag()
    {
        return TerriasHardTagState.Active(TerriasHardTagIds.ScorchedWorld)
            || TerriasHardTagState.Active(TerriasHardTagIds.SamsaraGarden)
            || TerriasHardTagState.Active(TerriasHardTagIds.BlackSunCalamity)
            || TerriasHardTagState.Active(TerriasHardTagIds.WhiteRadianceCourt)
            || TerriasHardTagState.Active(TerriasHardTagIds.SunsetExpedition)
            || TerriasHardTagState.Active(TerriasHardTagIds.Rebirth)
            || TerriasHardTagState.Active(TerriasHardTagIds.AbyssalShock)
            || TerriasHardTagState.Active(TerriasHardTagIds.AbyssGaze)
            || TerriasHardTagState.Active(TerriasHardTagIds.MorningStarDimmed)
            || TerriasHardTagState.Active(TerriasHardTagIds.OtherDimensionStagnantWater);
    }
}
