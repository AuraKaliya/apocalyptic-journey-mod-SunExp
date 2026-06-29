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
    private static readonly Stack<PendingCard> Pending = new();
    private static bool handlerRegistered;

    public static void Initialize(ModConfig modConfig)
    {
        EnsureHandlerRegistered();
        AuraSharedHooks.RegisterAfter(modConfig, "Fight_Start.Init", OnFightStart, SunExpLog.Debug, message => SunExpLog.Warn("Loneer " + message));
    }

    private static void OnFightStart(ModHookContext context)
    {
        Pending.Clear();
        if (!LoneerMiracleService.IsActive())
        {
            LoneerCombatStateStore.ClearAll();
            return;
        }

        SunExpActionEventRouter.ResetForFight("Loneer.Fight_Start.Init");
    }

    private static void EnsureHandlerRegistered()
    {
        if (handlerRegistered)
        {
            return;
        }

        SunExpActionEventRouter.RegisterHandler("Loneer", OnAction, OnActionAfter);
        handlerRegistered = true;
    }

    private static void OnAction(SunExpActionEventContext context)
    {
        try
        {
            if (!LoneerMiracleService.IsActive())
            {
                return;
            }

            var config = context.Config;
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
            if (!LoneerMiracleService.IsActive())
            {
                Pending.Clear();
                return;
            }

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
