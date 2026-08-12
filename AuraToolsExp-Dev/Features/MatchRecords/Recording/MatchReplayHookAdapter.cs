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
            context => MatchReplayRecorder.Start(context.Arguments),
            "MatchRecords.Replay"));
        RegisterCommand("ActionCommandBase.Execute");
        RegisterCommand("ClientCommandBase.Execute");
        RegisterCommand("ObjTargetBase.Execute");
        RegisterCommand("StatusDataTransfer.Populate");
        Register("lifecycle", AuraBattleLifecycleRouter.Register(
            modConfig,
            AuraToolsIds.ModId,
            "MatchRecords.Replay",
            new AuraBattleLifecycleSubscription
            {
                FightStarting = _ => MatchReplayRecorder.StartFromCurrentFight(),
                FightInitialized = _ => MatchReplayRecorder.StartFromCurrentFight(),
                PlayerRoundStarted = _ => MatchReplayRecorder.StartTurn(),
                FightRestarting = _ => MatchReplayRecorder.Complete("Restarted"),
                FightEnding = context => MatchReplayRecorder.Complete(DamageMeterSettlementRuntime.FightResult(context)),
                FightEnded = _ => MatchReplayRecorder.Complete("Ended")
            },
            AuraToolsLog.Debug,
            AuraToolsLog.Warn));
        AuraToolsLog.Info("[MatchRecords] replay capture hooks enabled.");
    }

    private static void RegisterCommand(string target)
    {
        Register("before:" + target, AuraToolsHookRegistry.BeforeRouted(
            modConfig!,
            target,
            context => MatchReplayRecorder.Record(context.Target),
            "MatchRecords.Replay"));
    }

    private static void Register(string key, IDisposable registration)
    {
        Hooks[key] = registration;
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
