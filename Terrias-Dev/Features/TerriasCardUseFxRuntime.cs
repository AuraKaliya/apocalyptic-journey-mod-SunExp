using System;
using System.Linq;
using AuraCardUseFx.Shared;
using SunExp.Dll.Hooks;
using SunExp.Dll.Hooks.Visual;
using SunExp.Dll.Infrastructure;
using SunExp.Dll.Mechanics;
using Witch.Core;
using Witch.Mod;

namespace SunExp.Dll.Features;

public static class SunExpCardUseFxRuntime
{
    private static bool initialized;

    public static void Initialize(ModConfig modConfig)
    {
        if (initialized)
        {
            return;
        }

        initialized = true;
        if (!AuraCardUseFxRegistryRuntime.RegisterManifest(modConfig, SunExpIds.ModId))
        {
            SunExpLog.Warn("[CardUseFx] SunExp manifest registration was rejected; runtime will stay inactive.");
        }

        AuraCardUseFxRuntime.Initialize(modConfig);
        AuraCardUseFxRuntime.Triggered -= OnTriggered;
        AuraCardUseFxRuntime.Triggered += OnTriggered;
        SunExpBattleLifecycleRouter.Register("CardUseFx", new SunExpBattleLifecycleSubscription
        {
            FightStarted = _ => Clear("fight-start"),
            FightEnding = _ => Clear("fight-end")
        });
        SunExpLog.Info("[CardUseFx] Stellar Overture card-use FX runtime initialized.");
    }

    public static void Clear(string source)
    {
        StarScoreArrivalCueService.Clear();
        StarScoreCardUseFxPresenter.Clear(source);
    }

    private static void OnTriggered(AuraCardUseFxTrigger trigger)
    {
        try
        {
            if (!string.Equals(trigger.Entry.OwnerModId, SunExpIds.ModId, StringComparison.OrdinalIgnoreCase)
                || !string.Equals(trigger.Entry.EffectId, SunExpIds.StellarOvertureCardUseFxId, StringComparison.OrdinalIgnoreCase)
                || trigger.Channel != AuraCardUseFxTriggerChannel.LocalCommitted)
            {
                return;
            }

            var cues = StarScoreArrivalCueService.Consume(trigger.CardConfig);
            if (cues.Count == 0 || !IsLocalOwner(cues[0].OwnerStatusId))
            {
                SunExpLog.Debug("[CardUseFx] no local note cues matched useSequence=" + trigger.UseSequence + "; presentation skipped.");
                return;
            }

            var visible = cues.Take(StarScoreArrivalCueService.MaxVisibleRibbonCount).ToList();
            var overflow = Math.Max(0, cues.Count - visible.Count);
            StarScoreCardUseFxPresenter.Play(trigger.SourceSnapshot, visible, overflow, trigger.Entry.VisualEffectId);
        }
        catch (Exception ex)
        {
            SunExpLog.Error("[CardUseFx] trigger handling failed", ex);
        }
    }

    private static bool IsLocalOwner(string ownerStatusId)
    {
        if (string.IsNullOrWhiteSpace(ownerStatusId))
        {
            return true;
        }

        var localId = FightPlayer.Instance?.Status?.InstanceId ?? "";
        return string.IsNullOrWhiteSpace(localId)
            || string.Equals(ownerStatusId, localId, StringComparison.Ordinal);
    }
}
