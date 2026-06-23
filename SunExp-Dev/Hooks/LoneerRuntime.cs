using System;
using System.Collections.Generic;
using AuraShared.Core;
using SunExp.Dll.GameApi;
using SunExp.Dll.Infrastructure;
using SunExp.Dll.Mechanics;
using Witch.Core;
using Witch.Mod;

namespace SunExp.Dll.Hooks;

public static class LoneerRuntime
{
    private static readonly object EventOwner = new();
    private static readonly Stack<PendingCard> Pending = new();
    private static string? registeredStatusId;

    public static void Initialize(ModConfig modConfig)
    {
        AuraSharedHooks.RegisterAfter(modConfig, "Fight_Start.Init", OnFightStart, SunExpLog.Debug, message => SunExpLog.Warn("Loneer " + message));
    }

    private static void OnFightStart(ModHookContext context)
    {
        Pending.Clear();
        registeredStatusId = null;
        if (!LoneerMiracleService.IsActive())
        {
            LoneerCombatStateStore.ClearAll();
            return;
        }

        TryRegisterForPlayer();
    }

    private static void TryRegisterForPlayer()
    {
        try
        {
            var statusId = FightPlayer.Instance?.Status?.InstanceId;
            if (string.IsNullOrWhiteSpace(statusId) || registeredStatusId == statusId)
            {
                return;
            }

            EventCenter.Instance.Clear(EventOwner);
            EventCenter.Instance.AddEventListener("Action" + statusId, new Action<object>(OnAction), EventOwner, EventDispose.OnFightEnd);
            EventCenter.Instance.AddEventListener("ActionAfter" + statusId, new Action(OnActionAfter), EventOwner, EventDispose.OnFightEnd);
            registeredStatusId = statusId;
            SunExpLog.Info("Registered Loneer player Action listeners: statusId=" + statusId);
        }
        catch (Exception ex)
        {
            SunExpLog.Error("Failed to register Loneer Action listeners", ex);
        }
    }

    private static void OnAction(object payload)
    {
        try
        {
            var config = CardConfigApi.FromActionPayload(payload);
            if (config?.scriptExecutor is ScriptExecutor executor)
            {
                Pending.Push(new PendingCard(config, executor));
            }
        }
        catch (Exception ex)
        {
            SunExpLog.Error("Loneer Action listener failed", ex);
        }
    }

    private static void OnActionAfter()
    {
        try
        {
            if (Pending.Count == 0)
            {
                return;
            }

            var pending = Pending.Pop();
            LoneerMiracleService.OnCardActionAfter(pending.Executor, pending.Config);
        }
        catch (Exception ex)
        {
            SunExpLog.Error("Loneer ActionAfter listener failed", ex);
        }
    }

    private readonly struct PendingCard
    {
        public PendingCard(IDataConfig config, ScriptExecutor executor)
        {
            Config = config;
            Executor = executor;
        }

        public IDataConfig Config { get; }

        public ScriptExecutor Executor { get; }
    }
}
