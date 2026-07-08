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
    private static StarScoreHudView? activeView;

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

            var view = EnsureView();
            if (view == null)
            {
                return;
            }

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
            activeView.transform.SetAsLastSibling();
            return activeView;
        }

        var parent = UIManager.Instance?.canvasTf ?? UIManager.Instance?.upperCanvasTf;
        if (parent == null)
        {
            SunExpLog.Warn("Star score HUD skipped: UI canvas unavailable.");
            return null;
        }

        activeView = StarScoreHudView.Create(parent);
        SunExpTransientUiRegistry.Register("StarScoreHud", Close);
        return activeView;
    }

    public static void Close(string source)
    {
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
}
