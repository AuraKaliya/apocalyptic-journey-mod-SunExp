using System;
using System.Collections.Generic;
using AuraMode.Shared;
using AuraShared.Core;
using AuraToolsExp.Dll.Features.DamageMeter;
using AuraToolsExp.Dll.Infrastructure;
using Witch.Core;
using Witch.Mod;

namespace AuraToolsExp.Dll.Features.MatchRecords.Recording;

internal static class MatchReplayHookAdapter
{
    private static readonly Dictionary<string, IDisposable> Hooks = new(StringComparer.Ordinal);
    private static ModConfig? modConfig;

    internal static void Initialize(ModConfig config)
    {
        modConfig = config;
        EnsureHooksMatchConfig();
    }

    internal static void EnsureHooksMatchConfig()
    {
        if (AuraToolsMatchRecordsRuntime.ReplayEnabled)
        {
            EnsureRegistered();
        }
        else
        {
            Release();
        }
    }

    private static void EnsureRegistered()
    {
        if (modConfig == null || Hooks.Count > 0)
        {
            return;
        }

        Register("before:FightManager.Init", AuraToolsHookRegistry.BeforeRouted(
            modConfig,
            "FightManager.Init",
            context => Observe("fight-init", () => MatchReplayRecorder.Start(context.Arguments)),
            "MatchRecords.Replay"));
        Register("before:FightUI.CallActionAnimation", AuraToolsHookRegistry.BeforeRouted(
            modConfig!,
            "FightUI.CallActionAnimation",
            context => Observe("action-presentation-before", () => MatchReplayRecorder.CaptureActionPresentation(context.Target, context.Arguments)),
            "MatchRecords.Replay.ActionPresentation"));
        Register("after:FightUI.CallActionAnimation", AuraToolsHookRegistry.AfterRouted(
            modConfig!,
            "FightUI.CallActionAnimation",
            context => Observe("action-presentation-after", () => MatchReplayRecorder.CompleteActionPresentation(context.Target, context.Arguments)),
            "MatchRecords.Replay.ActionPresentation"));
        Register("before:FightUI.DoCardUseAnimation", AuraToolsHookRegistry.BeforeRouted(
            modConfig!,
            "FightUI.DoCardUseAnimation",
            context => Observe("native-card-motion-before", () => MatchReplayRecorder.BeginNativeCardMotion(context.Target, context.Arguments)),
            "MatchRecords.Replay.NativeCardMotion"));
        Register("after:CardItem.OnBeginDrag", AuraToolsHookRegistry.AfterRouted(
            modConfig!, "CardItem.OnBeginDrag",
            context => Observe("card-drag-start", () => MatchReplayRecorder.BeginCardDrag(context.Target)),
            "MatchRecords.Replay.CardDrag"));
        foreach (var target in new[] { "CardItem.DrawEffect", "CommonCardItem.DrawEffect", "AttackCardItem.DrawEffect" })
            Register("before:" + target, AuraToolsHookRegistry.BeforeRouted(
                modConfig!, target, context => Observe("card-arrival", () => MatchReplayRecorder.ObserveCardDraw(context.Target)),
                "MatchRecords.Replay.CardArrival"));
        Register("after:FightUI.CreateCardItemInternal", AuraToolsHookRegistry.AfterRouted(
            modConfig!, "FightUI.CreateCardItemInternal",
            context => Observe("hand-created", () => MatchReplayRecorder.ObserveHandCreated(context.Target, context.Arguments)),
            "MatchRecords.Replay.HandCreated"));
        Register("after:FightUI.CreateCardItem", AuraToolsHookRegistry.AfterRouted(
            modConfig!, "FightUI.CreateCardItem",
            context => Observe("hand-request", () => MatchReplayRecorder.ObserveHandRequest(context.Target)),
            "MatchRecords.Replay.HandRequest"));
        Register("before:FightUI.UpdateCardItemPos", AuraToolsHookRegistry.BeforeRouted(
            modConfig!, "FightUI.UpdateCardItemPos",
            context => Observe("hand-layout-before", () => MatchReplayRecorder.BeginHandLayout(context.Target)),
            "MatchRecords.Replay.HandLayoutBefore"));
        Register("after:FightUI.UpdateCardItemPos", AuraToolsHookRegistry.AfterRouted(
            modConfig!, "FightUI.UpdateCardItemPos",
            context => Observe("hand-layout-after", () => MatchReplayRecorder.EndHandLayout(context.Target)),
            "MatchRecords.Replay.HandLayoutAfter"));
        foreach (var target in new[] { "CommonCardItem.OnEndDrag", "CardItem.OnEndDrag", "CardItem.CancelUseDrag" })
            Register("after:" + target, AuraToolsHookRegistry.AfterRouted(
                modConfig!, target,
                context => Observe("card-drag-end", () => MatchReplayRecorder.EndCardDrag(context.Target)),
                "MatchRecords.Replay.CardDragEnd"));
        Register("after:FightUI.DoCardUseAnimation", AuraToolsHookRegistry.AfterRouted(
            modConfig!,
            "FightUI.DoCardUseAnimation",
            context => Observe("native-card-motion-after", () => MatchReplayRecorder.EndNativeCardMotion(context.Target, context.Arguments)),
            "MatchRecords.Replay.NativeCardMotion"));
        Register("after:CardItem.EffectOfThrowCard", AuraToolsHookRegistry.AfterRouted(
            modConfig!,
            "CardItem.EffectOfThrowCard",
            context => Observe("native-card-throw", () => MatchReplayRecorder.ObserveNativeCardExitMotion(
                context.Target,
                "Discard")),
            "MatchRecords.Replay.NativeCardExit"));
        Register("after:CardItem.EffectOfBurnCard", AuraToolsHookRegistry.AfterRouted(
            modConfig!,
            "CardItem.EffectOfBurnCard",
            context => Observe("native-card-burn", () => MatchReplayRecorder.ObserveNativeCardExitMotion(
                context.Target,
                "Burn")),
            "MatchRecords.Replay.NativeCardExit"));
        Register("before:UIManager.ShowPopUpDamage", AuraToolsHookRegistry.BeforeRouted(
            modConfig!,
            "UIManager.ShowPopUpDamage",
            context => Observe("native-damage-text", () => MatchReplayRecorder.CaptureNativeDamagePopup(context.Arguments)),
            "MatchRecords.Replay.NativeDamageText"));
        Register("remote-combat-actions", AuraRemoteCombatActionRouter.Register(
            modConfig!,
            AuraToolsIds.ModId + ".MatchRecords.Replay",
            new AuraRemoteCombatActionSubscription
            {
                CommandObserved = context => Observe("remote-command", () => MatchReplayRecorder.CaptureRemoteCommand(context)),
                AuthoritativeStatusApplied = context => Observe(
                    "authoritative-status", () => MatchReplayRecorder.ObserveAuthoritativeStatus(context))
            },
            AuraToolsLog.Debug,
            AuraToolsLog.Warn));
        Register("before:OtherObj.DoOneAction", AuraToolsHookRegistry.BeforeRouted(
            modConfig!,
            "OtherObj.DoOneAction",
            context => Observe("enemy-intent-before", () => MatchReplayRecorder.BeginEnemyIntentAction(context.Target, context.Arguments)),
            "MatchRecords.Replay.EnemyIntent"));
        Register("after:OtherObj.DoOneAction", AuraToolsHookRegistry.AfterRouted(
            modConfig!,
            "OtherObj.DoOneAction",
            context => Observe("enemy-intent-after", () => MatchReplayRecorder.EndEnemyIntentAction(context.Target)),
            "MatchRecords.Replay.EnemyIntent"));
        Register("before:AudioManager.PlayEffect", AuraToolsHookRegistry.BeforeRouted(
            modConfig!,
            "AudioManager.PlayEffect",
            context => Observe("effect-audio-before", () => MatchReplayRecorder.BeginNativeAudioCapture(context.Arguments, "Effect")),
            "MatchRecords.Replay.Audio.Effect"));
        Register("after:AudioManager.PlayEffect", AuraToolsHookRegistry.AfterRouted(
            modConfig!,
            "AudioManager.PlayEffect",
            context => Observe("effect-audio-after", () => MatchReplayRecorder.EndNativeAudioCapture(context.Arguments, "Effect")),
            "MatchRecords.Replay.Audio.Effect"));
        Register("before:AudioManager.PlayVocal", AuraToolsHookRegistry.BeforeRouted(
            modConfig!,
            "AudioManager.PlayVocal",
            context => Observe("vocal-audio-before", () => MatchReplayRecorder.BeginNativeAudioCapture(context.Arguments, "Vocal")),
            "MatchRecords.Replay.Audio.Vocal"));
        Register("after:AudioManager.PlayVocal", AuraToolsHookRegistry.AfterRouted(
            modConfig!,
            "AudioManager.PlayVocal",
            context => Observe("vocal-audio-after", () => MatchReplayRecorder.EndNativeAudioCapture(context.Arguments, "Vocal")),
            "MatchRecords.Replay.Audio.Vocal"));
        Register("after:AudioManager.PlayBGMList", AuraToolsHookRegistry.AfterRouted(
            modConfig!,
            "AudioManager.PlayBGMList",
            context => Observe("bgm-after", () => MatchReplayRecorder.CaptureNativeBgm(context.Target, context.Arguments)),
            "MatchRecords.Replay.Audio.Bgm"));
        Register("card-presentation-reset", AuraCardPresentationRuntime.Register(
            modConfig!,
            AuraToolsIds.ModId,
            "MatchRecords.Replay.CardVisualLifecycle",
            new AuraCardPresentationSubscription
            {
                Priority = int.MaxValue,
                Reset = context => Observe(
                    "card-presentation-reset",
                    () => MatchReplayRecorder.ObserveCardPresentationReset(context))
            }));
        Register("card-actions", AuraCardActionTransactionRouter.Register(
            modConfig,
            AuraToolsIds.ModId,
            "MatchRecords.Replay.Actions",
            new AuraCardActionSubscription
            {
                Phases = AuraCardActionPhase.NativeStarted | AuraCardActionPhase.Completed | AuraCardActionPhase.Aborted,
                Handler = context =>
                {
                    if (context.Phase == AuraCardActionPhase.NativeStarted)
                        Observe("card-before", () => MatchReplayRecorder.BeginCardAction(context.Card));
                    else if (context.Phase == AuraCardActionPhase.Completed)
                        Observe("card-after", () => MatchReplayRecorder.EndCardAction(context.Card));
                    else if (context.Phase == AuraCardActionPhase.Aborted)
                        Observe("card-aborted", () => MatchReplayRecorder.AbortCardAction(context.Card, context.AbortReason));
                }
            },
            AuraToolsLog.Debug,
            AuraToolsLog.Warn));
        Register("skill-lifecycle", AuraSkillActionTransactionRouter.Register(
            modConfig,
            AuraToolsIds.ModId,
            "MatchRecords.Replay.Skills",
            new AuraSkillActionSubscription
            {
                Phases = AuraSkillActionPhase.NativeStarted | AuraSkillActionPhase.Completed | AuraSkillActionPhase.Aborted,
                Handler = context =>
                {
                    if (context.Phase == AuraSkillActionPhase.NativeStarted)
                        Observe("skill-before", () => MatchReplayRecorder.BeginCardAction(context.Skill));
                    else if (context.Phase == AuraSkillActionPhase.Completed)
                        Observe("skill-after", () => MatchReplayRecorder.EndCardAction(context.Skill));
                    else if (context.Phase == AuraSkillActionPhase.Aborted)
                        Observe("skill-aborted", () => MatchReplayRecorder.AbortCardAction(context.Skill, context.AbortReason));
                }
            },
            AuraToolsLog.Debug,
            AuraToolsLog.Warn));
        Register("lifecycle", AuraBattleLifecycleRouter.Register(
            modConfig,
            AuraToolsIds.ModId,
            "MatchRecords.Replay",
            new AuraBattleLifecycleSubscription
            {
                BattleMaterialized = _ => Observe("battle-materialized", MatchReplayRecorder.CommitMaterializedBaseline),
                FightStartSignaled = _ => Observe("fight-start", MatchReplayRecorder.SignalFightStart),
                PlayerRoundReady = _ => Observe("round-ready", MatchReplayRecorder.StartTurn),
                BattleRestarting = _ => MatchReplayRecorder.Abort(),
                BattleSettling = outcome => Observe("battle-settling", () => MatchReplayRecorder.PrepareCompletion(
                    DamageMeterSettlementRuntime.FightResult(outcome.NativeContext))),
                BattleFinalized = _ => Observe("battle-finalized", () => MatchReplayRecorder.CompleteAfterCleanup("Ended"))
            },
            AuraToolsLog.Debug,
            AuraToolsLog.Warn));
        AuraToolsLog.Info("[MatchRecords] replay capture hooks enabled.");
    }

    private static void Register(string key, IDisposable registration)
    {
        Hooks[key] = registration;
    }

    private static void Observe(string stage, Action capture)
    {
        try
        {
            capture();
        }
        catch (Exception ex)
        {
            MatchReplayRecorder.MarkCaptureFailure(stage, ex);
            AuraToolsLog.Error("[MatchRecords] replay observer failed at " + stage, ex);
        }
    }

    private static void Release()
    {
        foreach (var hook in Hooks.Values)
        {
            try
            {
                hook.Dispose();
            }
            catch (Exception ex)
            {
                AuraToolsLog.Warn("[MatchRecords] replay hook release failed: " + ex.Message);
            }
        }

        if (Hooks.Count > 0)
        {
            AuraToolsLog.Info("[MatchRecords] replay capture hooks disabled.");
        }

        Hooks.Clear();
        MatchReplayRecorder.Abort();
    }
}
