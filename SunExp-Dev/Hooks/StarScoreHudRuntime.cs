using System;
using AuraShared.Core;
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
        RegisterAfter(modConfig, "Fight_Start.Init", OnFightBoundary);
        RegisterAfter(modConfig, "Fight_Win.Init", OnFightBoundary);
        RegisterAfter(modConfig, "Fight_Loss.Init", OnFightBoundary);
        RegisterAfter(modConfig, "Fight_Escape.Init", OnFightBoundary);

        StarScoreService.Changed -= OnStarScoreChanged;
        StarScoreService.Changed += OnStarScoreChanged;
        SunExpLog.Info("Star score HUD runtime initialized");
    }

    private static void RegisterAfter(ModConfig config, string target, Action<ModHookContext> action)
    {
        AuraSharedHooks.RegisterAfter(config, target, action, SunExpLog.Debug, message => SunExpLog.Warn("Star score HUD " + message));
    }

    private static void OnFightBoundary(ModHookContext context)
    {
        Close();
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
                Close();
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
        return activeView;
    }

    private static void Close()
    {
        if (activeView == null)
        {
            return;
        }

        activeView.Close("StarScoreHudRuntime.Close");
        activeView = null;
    }
}
