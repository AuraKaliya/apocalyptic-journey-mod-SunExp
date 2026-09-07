using System;
using System.Linq;
using AuraCardUseFx.Shared;
using Terrias.Dll.Hooks;
using Terrias.Dll.Hooks.Visual;
using Terrias.Dll.Infrastructure;
using Terrias.Dll.Mechanics;
using Witch.Core;
using Witch.Mod;

namespace Terrias.Dll.Hooks.Visual;

public static class TerriasCardUseFxRuntime
{
    private static bool initialized;

    public static void Initialize(ModConfig modConfig)
    {
        if (initialized)
        {
            return;
        }

        initialized = true;
        if (!AuraCardUseFxRegistryRuntime.RegisterManifest(modConfig, TerriasIds.ModId))
        {
            TerriasLog.Warn("[CardUseFx] Terrias manifest registration was rejected; runtime will stay inactive.");
        }

        AuraCardUseFxRuntime.Initialize(modConfig);
        AuraCardUseFxRuntime.Triggered -= OnTriggered;
        AuraCardUseFxRuntime.Triggered += OnTriggered;
        TerriasBattleLifecycleRouter.Register("CardUseFx", new TerriasBattleLifecycleSubscription
        {
            BattleOpening = _ => Clear("battle-opening"),
            BattleRestarting = _ => Clear("battle-restarting"),
            BattleSettling = _ => Clear("battle-settling")
        });
        TerriasLog.Info("[CardUseFx] Stellar Overture card-use FX runtime initialized.");
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
            if (!string.Equals(trigger.Entry.OwnerModId, TerriasIds.ModId, StringComparison.OrdinalIgnoreCase)
                || !string.Equals(trigger.Entry.EffectId, TerriasIds.StellarOvertureCardUseFxId, StringComparison.OrdinalIgnoreCase)
                || trigger.Channel != AuraCardUseFxTriggerChannel.LocalCommitted)
            {
                return;
            }

            var cues = StarScoreArrivalCueService.Consume(trigger.CardConfig);
            if (cues.Count == 0 || !IsLocalOwner(cues[0].OwnerStatusId))
            {
                TerriasLog.Debug("[CardUseFx] no local note cues matched useSequence=" + trigger.UseSequence + "; presentation skipped.");
                return;
            }

            var visible = cues.Take(StarScoreArrivalCueService.MaxVisibleRibbonCount).ToList();
            var overflow = Math.Max(0, cues.Count - visible.Count);
            StarScoreCardUseFxPresenter.Play(trigger.SourceSnapshot, visible, overflow, trigger.Entry.VisualEffectId);
        }
        catch (Exception ex)
        {
            TerriasLog.Error("[CardUseFx] trigger handling failed", ex);
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
