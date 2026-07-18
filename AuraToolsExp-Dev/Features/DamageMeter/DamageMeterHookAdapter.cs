using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using AuraMode.Shared;
using AuraShared.Core;
using AuraToolsExp.Dll.Config;
using AuraToolsExp.Dll.Features.DamageMeter.Capture;
using AuraToolsExp.Dll.Features.DamageMeter.Model;
using AuraToolsExp.Dll.Features.DamageMeter.Network;
using AuraToolsExp.Dll.Features.DamageMeter.Resolution;
using AuraToolsExp.Dll.Features.DamageMeter.SettlementCg;
using AuraToolsExp.Dll.Infrastructure;
using Data.Save;
using UnityEngine;
using Witch;
using Witch.Core;
using Witch.Mod;
using Witch.UI.Window;

namespace AuraToolsExp.Dll.Features.DamageMeter;

internal static class DamageMeterHookAdapter
{
    private static readonly List<IDisposable> HookRegistrations = new();
    private static ModConfig? modConfig;
    private static bool hooksRegistered;

    internal static void Initialize(ModConfig config)
    {
        modConfig = config;
        EnsureHooksMatchConfig();
    }

    internal static void EnsureHooksMatchConfig()
    {
        if (AuraToolsDamageMeterRuntime.Enabled)
        {
            EnsureHooksRegistered();
            AuraToolsDamageMeterUi.EnsureDriver();
            return;
        }

        ReleaseHooks();
        AuraToolsDamageMeterUi.ReleaseDriver();
    }

    private static void EnsureHooksRegistered()
    {
        if (hooksRegistered || modConfig == null)
        {
            return;
        }

        RegisterAfter("GameEntryUI.Init", DamageMeterAvailabilityRuntime.HideForEntryUi);
        RegisterAfter("GameEntryUI.Outlobby", DamageMeterAvailabilityRuntime.HideForEntryUi);
        RegisterAfter("GameEntryUI.ReturnHouse", DamageMeterAvailabilityRuntime.HideForEntryUi);
        RegisterAfter("GameEntryUI.ShowCareer", DamageMeterAvailabilityRuntime.ShowForPreparationUi);
        RegisterAfter("GameEntryUI.ShowDetail", DamageMeterAvailabilityRuntime.ShowForPreparationUi);
        RegisterAfter("GameEntryUI.ChangeRole", DamageMeterAvailabilityRuntime.ShowForPreparationUi);
        RegisterBefore("GameEntryUI.StartGame", DamageMeterAvailabilityRuntime.ShowForStartGame);
        RegisterBefore("GameApp.GameOver", DamageMeterSettlementRuntime.OnAdventureSettlement);
        RegisterBefore("PlayerManager.GameOver", DamageMeterSettlementRuntime.OnAdventureSettlement);
        RegisterAfter("GameExitUI.Start", DamageMeterSettlementRuntime.OnAdventureSettlement);
        RegisterAfter("NormalMapManager.InitRoleTable", DamageMeterAvailabilityRuntime.ShowForAdventureUi);
        RegisterAfter("SublimationManager.InitRoleTable", DamageMeterAvailabilityRuntime.ShowForAdventureUi);
        RegisterAfter("SlotMachineManager.InitRoleTable", DamageMeterAvailabilityRuntime.ShowForAdventureUi);
        RegisterAfter("TopBarUI.Awake", DamageMeterAvailabilityRuntime.ShowForAdventureUi);
        RegisterAfter("TopBarUI.Start", DamageMeterAvailabilityRuntime.ShowForAdventureUi);
        RegisterAfter("TopBarUI.ShowLeftUp", DamageMeterAvailabilityRuntime.ShowForAdventureUi);
        RegisterAfter("MapSelectUI.Start", DamageMeterAvailabilityRuntime.ShowForAdventureUi);
        RegisterAfter("MapSelectUI.ReadyToSelect", DamageMeterAvailabilityRuntime.ShowForAdventureUi);
        RegisterAfter("MapSelectUI.ShowMap", DamageMeterAvailabilityRuntime.ShowForAdventureUi);
        RegisterAfter("MapSelectUI.MapAnimation", DamageMeterAvailabilityRuntime.ShowForAdventureUi);

        RegisterBefore("StatusManager.Hit", DamageCaptureCoordinator.BeforeHit);
        RegisterAfter("StatusManager.Hit", DamageCaptureCoordinator.AfterHit);
        RegisterAfter("DamageText.Create", DamageCaptureCoordinator.AfterDamageTextCreate);
        if (AuraToolsPerformanceSettings.DiagnosticsEnabled)
        {
            RegisterAfter("DamageText.InternalExecute", DamageCaptureCoordinator.AfterDamageTextInternalExecute);
            RegisterAfter("FightUI.EnqueueDamageText", DamageCaptureCoordinator.AfterFightUiEnqueueDamageText);
        }
        RegisterBefore("ScriptExecutor.PureChangeHp", DamageCaptureCoordinator.BeforePureChangeHp);
        RegisterAfter("ScriptExecutor.PureChangeHp", DamageCaptureCoordinator.AfterPureChangeHp);
        RegisterBefore("StatusManager.set_CurHp", DamageCaptureCoordinator.BeforeSetCurHp);
        RegisterAfter("StatusManager.set_CurHp", DamageCaptureCoordinator.AfterSetCurHp);
        RegisterBefore("ScriptExecutor.AddBuff", DamageCaptureCoordinator.BeforeScriptAddBuff);
        RegisterAfter("ScriptExecutor.AddBuff", DamageCaptureCoordinator.AfterScriptAddBuff);
        RegisterBefore("StatusManager.AddBuff", DamageCaptureCoordinator.BeforeStatusAddBuff);
        RegisterAfter("StatusManager.AddBuff", DamageCaptureCoordinator.AfterStatusAddBuff);
        RegisterAfter("BuffItemConfig.set_Level", DamageCaptureCoordinator.AfterBuffLevelChanged);
        RegisterAfter("StatusManager.RemoveBuff", DamageCaptureCoordinator.AfterRemoveBuff);
        RegisterAfter("FightManager.OnEnable", DamageCaptureCoordinator.AttachBuffBroadcastListener);
        RegisterBefore("FightManager.OnDisable", DamageCaptureCoordinator.DetachBuffBroadcastListener);

        HookRegistrations.Add(AuraBattleLifecycleRouter.Register(
            modConfig,
            AuraToolsIds.ModId,
            "DamageMeter",
            new AuraBattleLifecycleSubscription
            {
                FightStarting = DamageMeterLifecycleCoordinator.OnFightInitStarting,
                FightStarted = DamageMeterLifecycleCoordinator.OnFightStartFallback,
                PlayerRoundStarted = DamageMeterLifecycleCoordinator.OnPlayerRoundStart,
                FightEnding = DamageMeterLifecycleCoordinator.OnFightEnding,
                FightEnded = DamageMeterLifecycleCoordinator.OnFightEnded
            },
            AuraToolsLog.Debug,
            AuraToolsLog.Warn));

        hooksRegistered = true;
        AuraToolsLog.Info("[DamageMeter] routed hooks enabled.");
    }

    internal static void ReleaseHooks()
    {
        if (!hooksRegistered && HookRegistrations.Count == 0)
        {
            return;
        }

        for (var i = HookRegistrations.Count - 1; i >= 0; i--)
        {
            try
            {
                HookRegistrations[i].Dispose();
            }
            catch
            {
            }
        }

        HookRegistrations.Clear();
        hooksRegistered = false;
        DamageCaptureCoordinator.DetachBuffBroadcastListener(null);
        DamageCaptureCoordinator.ResetCaptureState();
        DamageMeterNetworkRuntime.EndFight("disabled");
        AuraToolsLog.Info("[DamageMeter] routed hooks disabled.");
    }
    private static void RegisterBefore(string target, Action<ModHookContext> action)
    {
        if (modConfig == null)
        {
            return;
        }

        HookRegistrations.Add(AuraToolsHookRegistry.BeforeRouted(
            modConfig,
            target,
            action,
            "DamageMeter"));
    }

    private static void RegisterAfter(string target, Action<ModHookContext> action)
    {
        if (modConfig == null)
        {
            return;
        }

        HookRegistrations.Add(AuraToolsHookRegistry.AfterRouted(
            modConfig,
            target,
            action,
            "DamageMeter"));
    }

    internal static void RunHook(string name, Action action)
    {
        try
        {
            action();
        }
        catch (Exception ex)
        {
            AuraToolsLog.Warn("[DamageMeter] " + name + " failed: " + ex.Message);
        }
    }
}
