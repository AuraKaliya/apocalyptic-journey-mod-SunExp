using System;
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
    private static readonly DamageMeterHookRegistrationSet HookRegistrations = new();
    private static ModConfig? modConfig;

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
        if (modConfig == null)
        {
            return;
        }

        var countBefore = HookRegistrations.Count;
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

        RegisterBefore("StatusManager.Hit", context => WithObservation(DamageMeterHookContextMapper.MapHit(context), DamageCaptureCoordinator.BeforeHit));
        RegisterAfter("StatusManager.Hit", context => WithObservation(DamageMeterHookContextMapper.MapStatus(context), DamageCaptureCoordinator.AfterHit));
        RegisterAfter("DamageText.Create", AfterDamageTextCreate);
        if (AuraToolsPerformanceSettings.DiagnosticsEnabled)
        {
            RegisterAfter("DamageText.InternalExecute", AfterDamageTextInternalExecute);
            RegisterAfter("FightUI.EnqueueDamageText", AfterFightUiEnqueueDamageText);
        }
        else
        {
            HookRegistrations.DisposeWhere(
                key => key == "after:DamageText.InternalExecute"
                       || key == "after:FightUI.EnqueueDamageText",
                (key, ex) => AuraToolsLog.Warn(
                    "[DamageMeter] diagnostic hook release failed for "
                    + key
                    + ": "
                    + ex.Message));
        }
        RegisterBefore("ScriptExecutor.PureChangeHp", context => WithObservation(DamageMeterHookContextMapper.MapPureHp(context), DamageCaptureCoordinator.BeforePureChangeHp));
        RegisterAfter("ScriptExecutor.PureChangeHp", context => WithObservation(DamageMeterHookContextMapper.MapPureHp(context), DamageCaptureCoordinator.AfterPureChangeHp));
        RegisterBefore("StatusManager.set_CurHp", context => WithObservation(DamageMeterHookContextMapper.MapStatus(context), DamageCaptureCoordinator.BeforeSetCurHp));
        RegisterAfter("StatusManager.set_CurHp", context => WithObservation(DamageMeterHookContextMapper.MapStatus(context), DamageCaptureCoordinator.AfterSetCurHp));
        RegisterBefore("ScriptExecutor.AddBuff", context => WithObservation(DamageMeterHookContextMapper.MapScriptBuff(context), DamageCaptureCoordinator.BeforeScriptAddBuff));
        RegisterAfter("ScriptExecutor.AddBuff", context => WithObservation(DamageMeterHookContextMapper.MapScriptBuff(context), DamageCaptureCoordinator.AfterScriptAddBuff));
        RegisterBefore("StatusManager.AddBuff", context => WithObservation(DamageMeterHookContextMapper.MapStatusBuff(context), DamageCaptureCoordinator.BeforeStatusAddBuff));
        RegisterAfter("StatusManager.AddBuff", context => WithObservation(DamageMeterHookContextMapper.MapStatusBuff(context), DamageCaptureCoordinator.AfterStatusAddBuff));
        RegisterAfter("BuffItemConfig.set_Level", context => WithObservation(DamageMeterHookContextMapper.MapBuffLevel(context), DamageCaptureCoordinator.AfterBuffLevelChanged));
        RegisterAfter("StatusManager.RemoveBuff", context => WithObservation(DamageMeterHookContextMapper.MapStatusBuff(context), DamageCaptureCoordinator.AfterRemoveBuff));
        RegisterAfter("FightManager.OnEnable", _ => DamageCaptureCoordinator.AttachBuffBroadcastListener());
        RegisterBefore("FightManager.OnDisable", _ => DamageCaptureCoordinator.DetachBuffBroadcastListener());

        HookRegistrations.Register("lifecycle", () => AuraBattleLifecycleRouter.Register(
                modConfig,
                AuraToolsIds.ModId,
                "DamageMeter",
                new AuraBattleLifecycleSubscription
                {
                    BattleInitializing = context => DamageMeterLifecycleCoordinator.OnFightInitStarting(
                        DamageMeterAvailabilityRuntime.IsSupportedDamageMeterContext(context, allowMapManagerFallback: true)),
                    FightStartSignaled = _ => DamageMeterLifecycleCoordinator.OnFightStartFallback(),
                    PlayerRoundReady = context => DamageMeterLifecycleCoordinator.OnPlayerRoundStart(
                        DamageMeterHookContextMapper.MapRoundUnit(context)),
                    BattleRestarting = _ => DamageMeterLifecycleCoordinator.OnFightRestarting(),
                    BattleSettling = outcome => DamageMeterLifecycleCoordinator.OnFightEnding(
                        DamageMeterSettlementRuntime.FightResult(outcome.NativeContext)),
                    BattleEnded = _ => DamageMeterLifecycleCoordinator.OnFightEnded()
                },
                AuraToolsLog.Debug,
                AuraToolsLog.Warn));

        if (HookRegistrations.Count > countBefore)
        {
            AuraToolsLog.Info("[DamageMeter] routed hooks enabled.");
        }
    }

    internal static void ReleaseHooks()
    {
        if (HookRegistrations.Count == 0)
        {
            return;
        }

        var failed = HookRegistrations.DisposeAll((key, ex) =>
            AuraToolsLog.Warn("[DamageMeter] hook release failed for " + key + ": " + ex.Message));
        DamageCaptureCoordinator.DetachBuffBroadcastListener();
        DamageMeterLifecycleCoordinator.ResetCaptureServices();
        DamageMeterNetworkRuntime.EndFight("disabled");
        AuraToolsLog.Info(failed == 0
            ? "[DamageMeter] routed hooks disabled."
            : "[DamageMeter] routed hook release incomplete; retained " + failed + " handle(s) for retry.");
    }
    private static void RegisterBefore(string target, Action<ModHookContext> action)
    {
        if (modConfig == null)
        {
            return;
        }

        HookRegistrations.Register("before:" + target, () => AuraToolsHookRegistry.BeforeRouted(
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

        HookRegistrations.Register("after:" + target, () => AuraToolsHookRegistry.AfterRouted(
                modConfig,
                target,
                action,
                "DamageMeter"));
    }

    private static void WithObservation<T>(T? observation, Action<T> action) where T : class
    {
        if (observation != null)
        {
            action(observation);
        }
    }

    private static void AfterDamageTextCreate(ModHookContext context)
    {
        WithObservation(DamageMeterHookContextMapper.MapDamageText(context), DamageCaptureCoordinator.AfterDamageTextCreate);
    }

    private static void AfterDamageTextInternalExecute(ModHookContext context)
    {
        DamageCaptureCoordinator.AfterDamageTextInternalExecute();
    }

    private static void AfterFightUiEnqueueDamageText(ModHookContext context)
    {
        DamageCaptureCoordinator.AfterFightUiEnqueueDamageText();
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
