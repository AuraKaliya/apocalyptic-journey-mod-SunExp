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
    private static bool terminalBattleSceneEmitted;

    public static void BeginAdventure(ModHookContext context)
    {
        adventureSequence++;
        battleId = "";
        adventureSettlementEmitted = false;
        terminalBattleSceneEmitted = false;
        AuraToolsCgOutcomeReasonService.Reset();
        AuraToolsCgTeamSnapshotService.BeginAdventure();
    }

    public static void BattleOpening(ModHookContext context)
    {
        battleId = ResolveBattleId(context);
        terminalBattleSceneEmitted = false;
        AuraToolsCgOutcomeReasonService.Reset();
        AuraToolsCgTeamSnapshotService.Refresh();
        var settings = Settings();
        var scene = settings.GetScene(AuraToolsEventCgSceneIds.BattleOpening);
        if (scene.Enabled && MatchesBattle(battleId, scene.BattleIds))
        {
            Emit(
                AuraToolsEventCgSceneIds.BattleOpening,
                AuraCgSignals.BattleOpening,
                "opening",
                terminal: false);
        }
    }

    public static void OutcomeEntering(AuraBattleOutcomeContext context)
    {
        var settings = Settings();
        var reason = AuraToolsCgOutcomeReasonService.Consume(context.Outcome);
        if (context.Outcome == AuraBattleOutcome.Win
            || string.Equals(reason, AuraToolsEventCgOutcomeReasons.MidasEscape, StringComparison.OrdinalIgnoreCase))
        {
            var sceneId = AuraToolsEventCgOutcomeReasons.SceneForReason(reason);
            var scene = settings.GetScene(sceneId);
            if (scene.Enabled && Emit(sceneId, AuraCgSignals.BattleVictory, reason, terminal: true))
            {
                terminalBattleSceneEmitted = true;
            }
            return;
        }

        // Ordinary escape is neither victory nor defeat. Content-specific
        // successful escapes are classified before the native outcome arrives.
        if (context.Outcome == AuraBattleOutcome.Escape)
        {
            return;
        }

        var defeat = settings.GetScene(AuraToolsEventCgSceneIds.BattleDefeat);
        if (defeat.Enabled
            && Emit(
                AuraToolsEventCgSceneIds.BattleDefeat,
                AuraCgSignals.BattleDefeat,
                AuraToolsEventCgOutcomeReasons.Defeat,
                terminal: true))
        {
            terminalBattleSceneEmitted = true;
        }
    }

    public static void BattleRestarting()
    {
        terminalBattleSceneEmitted = false;
        AuraToolsCgOutcomeReasonService.Reset();
        AuraToolsCgTeamSnapshotService.Refresh();
    }

    public static void Reset()
    {
        battleId = "";
        adventureSettlementEmitted = false;
        terminalBattleSceneEmitted = false;
        AuraToolsCgOutcomeReasonService.Reset();
        AuraToolsCgTeamSnapshotService.Reset();
    }

    public static void AdventureSettlement(ModHookContext context)
    {
        if (adventureSettlementEmitted || !SkillCgArbiterRuntime.IsAuthoritativeHost())
        {
            return;
        }

        var settings = Settings();
        var scene = settings.GetScene(AuraToolsEventCgSceneIds.AdventureSettlement);
        if (!settings.Enabled
            || !scene.Enabled
            || terminalBattleSceneEmitted && !settings.PlaySettlementAfterBattleScene)
        {
            adventureSettlementEmitted = true;
            return;
        }

        var failed = IsAdventureLoss();
        SkillCgArbiterRuntime.BeginPresentationSession(
            AuraToolsIds.ModId,
            "adventure settlement");
        adventureSettlementEmitted = Emit(
            AuraToolsEventCgSceneIds.AdventureSettlement,
            AuraCgSignals.AdventureSettlementEntering,
            failed ? "failed" : "completed",
            terminal: true);
    }

    public static bool Preview(string sceneId, int participantCount) => Preview(sceneId, participantCount, 0);

    internal static bool Preview(string sceneId, int participantCount, int roleOffset)
    {
        var request = BuildPreviewRequest(sceneId, participantCount, roleOffset);
        if (request == null)
        {
            return false;
        }

        SkillCgArbiterRuntime.BeginPresentationSession(AuraToolsIds.ModId, "event CG preview");
        SkillCgArbiterRuntime.RequestCg(AuraToolsIds.ModId, request);
        return true;
    }

    public static SkillCgRequest? BuildPreviewRequest(string sceneId, int participantCount) =>
        BuildPreviewRequest(sceneId, participantCount, 0);

    internal static SkillCgRequest? BuildPreviewRequest(string sceneId, int participantCount, int roleOffset)
    {
        var normalizedSceneId = AuraToolsEventCgSceneIds.Normalize(sceneId);
        var settings = Settings();
        var scene = settings.GetScene(normalizedSceneId);
        if (!scene.Enabled)
        {
            return null;
        }

        var source = AuraToolsCgTeamSnapshotService.BuildPreviewSource(
            normalizedSceneId,
            "event-cg-preview:" + normalizedSceneId,
            participantCount,
            roleOffset);
        if (source == null)
        {
            return null;
        }

        var signalId = SignalForScene(normalizedSceneId);
        var reason = OutcomeReasonForScene(normalizedSceneId);
        var terminal = !string.Equals(
            normalizedSceneId,
            AuraToolsEventCgSceneIds.BattleOpening,
            StringComparison.OrdinalIgnoreCase);
        var signal = CreateSignal(normalizedSceneId, signalId, reason, terminal, source);
        signal.ConfigureResolvedRequest = request =>
        {
            ConfigureRequest(request, scene, terminal);
            request.DisableSync = true;
        };
        var candidates = SkillCgArbiterRuntime.BuildRegisteredSignalRequests(
            AuraToolsIds.ModId,
            signal,
            disableSync: true);
        if (candidates.Count == 0)
        {
            return null;
        }

        return candidates[0];
    }

    private static bool Emit(
        string sceneId,
        string signalId,
        string outcomeReason,
        bool terminal)
    {
        var settings = Settings();
        var scene = settings.GetScene(sceneId);
        if (!settings.Enabled || !scene.Enabled || !SkillCgArbiterRuntime.IsAuthoritativeHost())
        {
            return false;
        }

        var sequence = ++signalSequence;
        var eventToken = "event-cg:"
                         + adventureSequence.ToString(CultureInfo.InvariantCulture)
                         + ":" + AuraBattleLifecycleRouter.CurrentBattleSessionId.ToString(CultureInfo.InvariantCulture)
                         + ":" + sceneId
                         + ":" + sequence.ToString(CultureInfo.InvariantCulture);
        var sceneSource = AuraToolsCgTeamSnapshotService.BuildSource(sceneId, eventToken);
        if (sceneSource == null)
        {
            AuraToolsLog.Warn("[CG] event scene skipped: no participating roles. scene=" + sceneId + ".");
            return false;
        }

        var signal = CreateSignal(sceneId, signalId, outcomeReason, terminal, sceneSource);
        signal.ActionSequence = sequence;
        signal.EventToken = eventToken;
        signal.ConfigureResolvedRequest = request => ConfigureRequest(request, scene, terminal);
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

    private static AuraCgSignalContext CreateSignal(
        string sceneId,
        string signalId,
        string outcomeReason,
        bool terminal,
        AuraCgSceneSourceSnapshot source)
    {
        var modeId = ResolveModeId();
        var subjectId = !string.IsNullOrWhiteSpace(battleId)
            ? battleId
            : string.IsNullOrWhiteSpace(modeId) ? "adventure" : modeId;
        return new AuraCgSignalContext
        {
            SignalId = signalId,
            SubjectType = AuraCgSubjectTypes.Event,
            SubjectId = subjectId,
            BattleId = battleId,
            ModeId = modeId,
            Outcome = outcomeReason,
            CreatedAt = Time.unscaledTime,
            SceneSource = source,
            Facts = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["resolvedCgId"] = AuraToolsEventCgSceneIds.CgId(sceneId),
                ["sceneId"] = sceneId,
                ["outcomeReason"] = outcomeReason,
                ["terminal"] = terminal ? "true" : "false"
            }
        };
    }

    private static void ConfigureRequest(
        SkillCgRequest request,
        AuraToolsEventCgSceneSettings scene,
        bool terminal)
    {
        var settings = Settings();
        request.DisableSync = !settings.SyncRemote;
        request.FadeIn = scene.EffectiveFadeIn;
        request.Hold = scene.EffectiveHold;
        request.FadeOut = scene.EffectiveFadeOut;
        request.PresentationMode = SkillCgPresentationModes.FullscreenFade;
        request.FitMode = SkillCgFitModes.Cover;
        request.Exclusive = terminal || request.Exclusive;
        if (request.ScenePlan != null)
        {
            request.ScenePlan.LogicalWidth = scene.EffectiveBaseWidth;
            request.ScenePlan.LogicalHeight = scene.EffectiveBaseHeight;
            request.ScenePlan.MotionEnabled = scene.MotionEnabled;
            request.ScenePlan.PresentationProfileId = scene.SceneId;
            request.ScenePlan.Normalize();
        }
    }

    private static AuraToolsEventCgSettings Settings()
    {
        var settings = AuraToolsConfigService.SkillCg.EventCg;
        settings.Normalize();
        return settings;
    }

    private static bool MatchesBattle(string id, IEnumerable<string> configuredIds)
    {
        var configured = (configuredIds ?? Array.Empty<string>())
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .ToList();
        if (configured.Count == 0)
        {
            return true;
        }

        var normalized = (id ?? "").Trim();
        return normalized.Length > 0 && configured.Any(value =>
            string.Equals(value, "*", StringComparison.Ordinal)
            || string.Equals(value, normalized, StringComparison.OrdinalIgnoreCase));
    }

    private static string SignalForScene(string sceneId)
    {
        if (AuraToolsEventCgSceneIds.IsVictory(sceneId)) return AuraCgSignals.BattleVictory;
        if (string.Equals(sceneId, AuraToolsEventCgSceneIds.BattleOpening, StringComparison.OrdinalIgnoreCase)) return AuraCgSignals.BattleOpening;
        if (string.Equals(sceneId, AuraToolsEventCgSceneIds.BattleDefeat, StringComparison.OrdinalIgnoreCase)) return AuraCgSignals.BattleDefeat;
        return AuraCgSignals.AdventureSettlementEntering;
    }

    private static string OutcomeReasonForScene(string sceneId)
    {
        if (string.Equals(sceneId, AuraToolsEventCgSceneIds.VictoryMidas, StringComparison.OrdinalIgnoreCase)) return AuraToolsEventCgOutcomeReasons.MidasEscape;
        if (string.Equals(sceneId, AuraToolsEventCgSceneIds.VictoryRitual, StringComparison.OrdinalIgnoreCase)) return AuraToolsEventCgOutcomeReasons.RitualVictory;
        if (string.Equals(sceneId, AuraToolsEventCgSceneIds.VictoryCurse, StringComparison.OrdinalIgnoreCase)) return AuraToolsEventCgOutcomeReasons.CurseVictory;
        if (string.Equals(sceneId, AuraToolsEventCgSceneIds.BattleDefeat, StringComparison.OrdinalIgnoreCase)) return AuraToolsEventCgOutcomeReasons.Defeat;
        return AuraToolsEventCgOutcomeReasons.StandardVictory;
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
