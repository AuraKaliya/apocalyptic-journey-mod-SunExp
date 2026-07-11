using System;
using SunExp.Dll.Hooks.Ui;
using SunExp.Dll.Infrastructure;
using SunExp.Dll.Mechanics;
using UnityEngine;
using Witch.Core;
using Witch.Mod;
using Witch.UI;

namespace SunExp.Dll.Hooks;

public static class StarScoreHudRuntime
{
    private const int MaxHostRetryCount = 30;
    private static StarScoreHudView? activeView;
    private static StarScoreDisplaySnapshot? pendingSnapshot;
    private static int hostRetryCount;

    public static void Initialize(ModConfig modConfig)
    {
        SunExpBattleLifecycleRouter.Register("StarScoreHud", new SunExpBattleLifecycleSubscription
        {
            FightStarted = OnFightBoundary,
            FightEnding = OnFightBoundary
        });
        RegisterAfter(modConfig, SunExpHookTargets.FightWinInit, OnFightBoundary);
        RegisterAfter(modConfig, SunExpHookTargets.FightEscapeInit, OnFightBoundary);

        StarScoreService.Changed -= OnStarScoreChanged;
        StarScoreService.Changed += OnStarScoreChanged;
        SunExpLog.Info("Star score HUD runtime initialized");
    }

    private static void RegisterAfter(ModConfig config, string target, Action<ModHookContext> action)
    {
        SunExpHookRegistry.After(config, target, action, "StarScoreHud");
    }

    private static void OnFightBoundary(ModHookContext context)
    {
        Close(context.Target?.GetType().Name ?? "fight-boundary");
    }

    private static void OnStarScoreChanged(StarScoreDisplaySnapshot snapshot)
    {
        try
        {
            if (!IsLocalOwner(snapshot))
            {
                return;
            }

            if (!snapshot.HasNotes)
            {
                Close("StarScoreHudRuntime.EmptySnapshot");
                return;
            }

            pendingSnapshot = snapshot;
            var view = EnsureView();
            if (view == null)
            {
                ScheduleHostRetry();
                return;
            }

            pendingSnapshot = null;
            hostRetryCount = 0;
            view.ApplySnapshot(snapshot);
        }
        catch (Exception ex)
        {
            SunExpLog.Error("Star score HUD refresh failed", ex);
        }
    }

    private static bool IsLocalOwner(StarScoreDisplaySnapshot snapshot)
    {
        var ownerId = snapshot.OwnerStatusId;
        if (string.IsNullOrWhiteSpace(ownerId))
        {
            return true;
        }

        var localId = FightPlayer.Instance?.Status?.InstanceId ?? "";
        return string.IsNullOrWhiteSpace(localId) || string.Equals(ownerId, localId, StringComparison.Ordinal);
    }

    private static StarScoreHudView? EnsureView()
    {
        if (activeView != null)
        {
            return activeView;
        }

        if (!BattleHudHost.TryGet(out var parent))
        {
            return null;
        }

        activeView = StarScoreHudView.Create(parent);
        SunExpTransientUiRegistry.Register("StarScoreHud", Close);
        return activeView;
    }

    public static void Close(string source)
    {
        pendingSnapshot = null;
        hostRetryCount = 0;
        if (activeView == null)
        {
            SunExpTransientUiRegistry.Unregister("StarScoreHud");
            return;
        }

        if (string.IsNullOrWhiteSpace(source) || string.Equals(source, "StarScoreHudRuntime.Close", StringComparison.Ordinal))
        {
            activeView.Close("StarScoreHudRuntime.Close");
        }
        else
        {
            activeView.Close(source);
        }

        activeView = null;
        SunExpTransientUiRegistry.Unregister("StarScoreHud");
    }

    private static void ScheduleHostRetry()
    {
        if (hostRetryCount >= MaxHostRetryCount)
        {
            SunExpLog.WarnOnce("StarScoreHud.FightUiUnavailable",
                "Star score HUD skipped after waiting for FightUI; a later score update can retry.");
            return;
        }

        hostRetryCount++;
        SunExpFrameScheduler.RunOnceAfterFrames("StarScoreHud.WaitForFightUI", 2, RetryPendingSnapshot);
    }

    private static void RetryPendingSnapshot()
    {
        var snapshot = pendingSnapshot;
        if (snapshot != null)
        {
            OnStarScoreChanged(snapshot);
        }
    }
}
