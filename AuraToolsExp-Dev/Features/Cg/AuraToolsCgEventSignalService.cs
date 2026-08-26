using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using AuraCg.Shared;
using AuraMode.Shared;
using AuraShared.Core;
using AuraToolsExp.Dll.Config;
using AuraToolsExp.Dll.Infrastructure;
using UnityEngine;
using Witch.Core;
using Witch.Mod;
using Witch.UI.Window;

namespace AuraToolsExp.Dll.Features.Cg;

internal static class AuraToolsCgEventSignalService
{
    private static long adventureSequence;
    private static long signalSequence;
    private static string battleId = "";
    private static bool adventureSettlementEmitted;
    private static bool terminalSceneEmitted;
    private static float lastSpecialVictoryAt = -1000f;

    public static void BeginAdventure(ModHookContext context)
    {
        adventureSequence++;
        battleId = "";
        adventureSettlementEmitted = false;
        terminalSceneEmitted = false;
        lastSpecialVictoryAt = -1000f;
        AuraToolsCgTeamSnapshotService.BeginAdventure();
    }

    public static void BattleOpening(ModHookContext context)
    {
        battleId = ResolveBattleId(context);
        AuraToolsCgTeamSnapshotService.Refresh();
        var settings = Settings();
        if (settings.SpecialOpeningEnabled && IsSpecialBattle(battleId, settings.SpecialBattleIds))
        {
            Emit(
                AuraCgSignals.BattleOpening,
                "opening",
                specialBattle: true,
                terminal: false);
        }
    }

    public static void OutcomeEntering(AuraBattleOutcomeContext context)
    {
        var settings = Settings();
        if (context.Outcome == AuraBattleOutcome.Win)
        {
            if (settings.SpecialVictoryEnabled && IsSpecialBattle(battleId, settings.SpecialBattleIds))
            {
                if (Emit(
                        AuraCgSignals.BattleVictory,
                        "victory",
                        specialBattle: true,
                        terminal: true))
                {
                    lastSpecialVictoryAt = Time.unscaledTime;
                }
            }

            return;
        }

        if (settings.BattleDefeatEnabled
            && Emit(
                AuraCgSignals.BattleDefeat,
                context.Outcome == AuraBattleOutcome.Escape ? "escape" : "defeat",
                specialBattle: IsSpecialBattle(battleId, settings.SpecialBattleIds),
                terminal: true))
        {
            terminalSceneEmitted = true;
        }
    }

    public static void BattleRestarting()
    {
        terminalSceneEmitted = false;
        lastSpecialVictoryAt = -1000f;
        AuraToolsCgTeamSnapshotService.Refresh();
    }

    public static void Reset()
    {
        battleId = "";
        adventureSettlementEmitted = false;
        terminalSceneEmitted = false;
        lastSpecialVictoryAt = -1000f;
        AuraToolsCgTeamSnapshotService.Reset();
    }

    public static void AdventureSettlement(ModHookContext context)
    {
        if (adventureSettlementEmitted || !SkillCgArbiterRuntime.IsAuthoritativeHost())
        {
            return;
        }

        adventureSettlementEmitted = true;
        var settings = Settings();
        if (!settings.Enabled || terminalSceneEmitted || Time.unscaledTime - lastSpecialVictoryAt <= 3f)
        {
            return;
        }

        var failed = IsAdventureLoss();
        SkillCgArbiterRuntime.BeginPresentationSession(
            AuraToolsIds.ModId,
            "adventure settlement");
        if (failed && settings.BattleDefeatEnabled)
        {
            terminalSceneEmitted = Emit(
                AuraCgSignals.BattleDefeat,
                "adventure-defeat",
                specialBattle: false,
                terminal: true);
            return;
        }

        if (settings.AdventureSettlementEnabled)
        {
            terminalSceneEmitted = Emit(
                AuraCgSignals.AdventureSettlementEntering,
                failed ? "failed" : "completed",
                specialBattle: false,
                terminal: true);
        }
    }

    private static bool Emit(
        string signalId,
        string outcome,
        bool specialBattle,
        bool terminal)
    {
        var settings = Settings();
        if (!settings.Enabled || !SkillCgArbiterRuntime.IsAuthoritativeHost())
        {
            return false;
        }

        var sequence = ++signalSequence;
        var modeId = ResolveModeId();
        var subjectId = !string.IsNullOrWhiteSpace(battleId)
            ? battleId
            : string.IsNullOrWhiteSpace(modeId) ? "adventure" : modeId;
        var eventToken = "event-cg:"
                         + adventureSequence.ToString(CultureInfo.InvariantCulture)
                         + ":" + AuraBattleLifecycleRouter.CurrentBattleSessionId.ToString(CultureInfo.InvariantCulture)
                         + ":" + signalId
                         + ":" + sequence.ToString(CultureInfo.InvariantCulture);
        var sceneSource = AuraToolsCgTeamSnapshotService.BuildSource(
            terminal ? "team-terminal" : "team-event",
            eventToken);
        if (sceneSource == null)
        {
            AuraToolsLog.Warn("[CG] event scene skipped: no participating roles. signal=" + signalId + ".");
            return false;
        }

        var signal = new AuraCgSignalContext
        {
            SignalId = signalId,
            SubjectType = AuraCgSubjectTypes.Event,
            SubjectId = subjectId,
            BattleId = battleId,
            ModeId = modeId,
            Outcome = outcome,
            ActionSequence = sequence,
            EventToken = eventToken,
            CreatedAt = Time.unscaledTime,
            SceneSource = sceneSource,
            Facts = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["specialBattle"] = specialBattle ? "true" : "false",
                ["terminal"] = terminal ? "true" : "false"
            },
            ConfigureResolvedRequest = request => ConfigureRequest(request, settings, terminal)
        };
        var candidates = SkillCgArbiterRuntime.BuildRegisteredSignalRequests(
            AuraToolsIds.ModId,
            signal,
            disableSync: !settings.SyncRemote);
        if (candidates.Count == 0)
        {
            return false;
        }

        SkillCgArbiterRuntime.EmitSignal(settings, AuraToolsIds.ModId, signal);
        return true;
    }

    private static void ConfigureRequest(
        SkillCgRequest request,
        AuraToolsEventCgSettings settings,
        bool terminal)
    {
        request.DisableSync = !settings.SyncRemote;
        request.FadeIn = settings.FadeIn;
        request.Hold = settings.Hold;
        request.FadeOut = settings.FadeOut;
        request.PresentationMode = SkillCgPresentationModes.FullscreenFade;
        request.FitMode = SkillCgFitModes.Cover;
        request.Exclusive = terminal || request.Exclusive;
        if (request.ScenePlan != null)
        {
            request.ScenePlan.LogicalWidth = settings.BaseWidth;
            request.ScenePlan.LogicalHeight = settings.BaseHeight;
            request.ScenePlan.PresentationProfileId = terminal ? "terminal" : "event";
            request.ScenePlan.Normalize();
        }
    }

    private static AuraToolsEventCgSettings Settings()
    {
        var settings = AuraToolsConfigService.SkillCg.EventCg;
        settings.Normalize();
        return settings;
    }

    private static bool IsSpecialBattle(string id, IEnumerable<string> configuredIds)
    {
        var normalized = (id ?? "").Trim();
        return normalized.Length > 0 && (configuredIds ?? Array.Empty<string>()).Any(value =>
            string.Equals(value, "*", StringComparison.Ordinal)
            || string.Equals(value, normalized, StringComparison.OrdinalIgnoreCase));
    }

    private static string ResolveModeId()
    {
        try
        {
            return (AuraModeRuntime.Current(AuraToolsIds.ModId)?.ModeId ?? "").Trim();
        }
        catch
        {
            return "";
        }
    }

    private static bool IsAdventureLoss()
    {
        try
        {
            return GameExitUI.loss;
        }
        catch
        {
            return false;
        }
    }

    private static string ResolveBattleId(ModHookContext context)
    {
        foreach (var candidate in new[] { context?.Target, FightManager.Instance }
                     .Concat(context?.Arguments ?? Array.Empty<object>()))
        {
            var value = ReadIdentity(candidate);
            if (!string.IsNullOrWhiteSpace(value)) return value;
        }

        var session = AuraBattleLifecycleRouter.CurrentBattleSessionId;
        return session > 0
            ? "battle-session-" + session.ToString(CultureInfo.InvariantCulture)
            : "battle";
    }

    private static string ReadIdentity(object? source)
    {
        if (source == null) return "";
        var type = source.GetType();
        foreach (var name in new[] { "BattleId", "FightId", "LevelId", "MapId", "DataId" })
        {
            try
            {
                var property = type.GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                var propertyValue = property?.GetValue(source)?.ToString()?.Trim() ?? "";
                if (propertyValue.Length > 0) return propertyValue;
                var field = type.GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                var fieldValue = field?.GetValue(source)?.ToString()?.Trim() ?? "";
                if (fieldValue.Length > 0) return fieldValue;
            }
            catch
            {
            }
        }

        return "";
    }
}
