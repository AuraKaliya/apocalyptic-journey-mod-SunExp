using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using AuraCg.Shared;
using AuraShared.Core;
using SunExp.Dll.GameApi;
using SunExp.Dll.Hooks;
using SunExp.Dll.Infrastructure;
using UnityEngine;
using Witch;
using Witch.Core;
using Witch.Mod;

namespace SunExp.Dll.Features.SkillCg;

public static class SunExpSkillCgRuntime
{
    private static readonly HashSet<string> DiagnosticKeys = new(StringComparer.OrdinalIgnoreCase);
    private const string AdventurePreloadContentKey = "SunExp.Adventure.SkillCg.content";

    public static void Initialize(ModConfig modConfig)
    {
        SkillCgArbiterRuntime.Initialize(modConfig, SunExpIds.ModId, new SkillCgArbiterOptions
        {
            DuplicateWindowSeconds = 1.25f
        });
        AuraCombatActionRouter.RegisterBefore(
            modConfig,
            SunExpIds.ModId + ".SkillCG",
            BeforeCombatAction,
            SunExpLog.Debug,
            SunExpLog.Warn);
        SunExpBattleLifecycleRouter.Register("SkillCG", new SunExpBattleLifecycleSubscription
        {
            AdventureStarting = OnAdventureStart,
            FightStarted = OnFightStart,
            FightEnding = OnFightEnding,
            FightEnded = OnFightEnded
        });
    }

    private static void BeforeCombatAction(AuraCombatActionContext context)
    {
        try
        {
            var trigger = BuildTriggerContext(context);
            if (trigger == null || !ShouldEmitLocalRequest(trigger))
            {
                return;
            }

            foreach (var request in BuildRequests(trigger))
            {
                SkillCgArbiterRuntime.RequestCg(SunExpIds.ModId, request, syncRemote: true);
            }

            foreach (var request in SkillCgArbiterRuntime.BuildRegisteredCardUseRequests(
                         SunExpIds.ModId,
                         trigger,
                         SunExpIds.ModId,
                         disableSync: false))
            {
                PrepareRequestForPlayback(request, trigger);
                SkillCgArbiterRuntime.RequestCg(SunExpIds.ModId, request, syncRemote: true);
            }
        }
        catch (Exception ex)
        {
            SunExpLog.Warn("[SkillCG] trigger failed: " + ex.Message);
        }
    }

    private static SkillCgTriggerContext? BuildTriggerContext(AuraCombatActionContext context)
    {
        if (!context.IsCardAction || string.IsNullOrWhiteSpace(context.CardId))
        {
            return null;
        }

        return new SkillCgTriggerContext
        {
            ActionSequence = context.ActionSequence,
            EventToken = context.EventToken,
            Action = context.Action,
            CardId = context.CardId,
            OwnerInstanceId = context.OwnerInstanceId,
            OwnerRoleId = context.OwnerRoleId,
            CreatedAt = context.CreatedAt
        };
    }

    private static IEnumerable<SkillCgRequest> BuildRequests(SkillCgTriggerContext trigger)
    {
        var matched = false;
        foreach (var entry in AuraCgRegistryRuntime.GetRegisteredEntries(SunExpIds.ModId))
        {
            var skipReason = EntrySkipReason(entry, trigger);
            if (!string.IsNullOrWhiteSpace(skipReason))
            {
                LogDiagnostic("skip:" + entry.QualifiedCgId + ":" + trigger.OwnerRoleId + ":" + trigger.CardId + ":" + skipReason,
                    "[SkillCG] skipped " + entry.QualifiedCgId
                    + ": reason=" + skipReason
                    + ", role=" + trigger.OwnerRoleId
                    + ", card=" + trigger.CardId);
                continue;
            }

            var imageResource = ResolveImageResource(entry);
            var imagePath = SkillCgArbiterRuntime.ResolveImagePath(SunExpIds.ModId, imageResource);
            if (!MediaExists(entry.Media.Type, imagePath))
            {
                SunExpLog.Warn("[SkillCG] media missing: " + imageResource);
                continue;
            }

            matched = true;
            LogDiagnostic("match:" + entry.QualifiedCgId + ":" + trigger.OwnerRoleId + ":" + trigger.CardId,
                "[SkillCG] matched " + entry.QualifiedCgId
                + ": role=" + trigger.OwnerRoleId
                + ", card=" + trigger.CardId
                + ", image=" + imageResource);

            yield return BuildRequest(entry, imageResource, imagePath, trigger);
        }

        if (!matched)
        {
            LogDiagnostic("none:" + trigger.OwnerRoleId + ":" + trigger.CardId,
                "[SkillCG] no CG request matched: role=" + trigger.OwnerRoleId + ", card=" + trigger.CardId);
        }
    }

    private static bool ShouldEmitLocalRequest(SkillCgTriggerContext trigger)
    {
        var multiplayer = IsMultiplayerSession();
        if (multiplayer && string.IsNullOrWhiteSpace(trigger.OwnerInstanceId))
        {
            LogDiagnostic("missing-owner:" + trigger.CardId,
                "[SkillCG] local request skipped: owner instance id is empty in multiplayer. card="
                + trigger.CardId);
            return false;
        }

        if (PlayerManager.Instance == null)
        {
            return true;
        }

        var localStatusId = PlayerApi.LocalPlayerStatusId();
        if (string.IsNullOrWhiteSpace(localStatusId))
        {
            if (!multiplayer)
            {
                return true;
            }

            LogDiagnostic("missing-local-status:" + trigger.OwnerInstanceId + ":" + trigger.CardId,
                "[SkillCG] local request skipped: local status id is empty. owner="
                + trigger.OwnerInstanceId
                + ", card="
                + trigger.CardId);
            return false;
        }

        if (string.IsNullOrWhiteSpace(trigger.OwnerInstanceId)
            || string.Equals(trigger.OwnerInstanceId, localStatusId, StringComparison.Ordinal))
        {
            return true;
        }

        LogDiagnostic("remote-owner:" + trigger.OwnerInstanceId + ":" + trigger.CardId,
            "[SkillCG] local request skipped for remote owner: owner="
            + trigger.OwnerInstanceId
            + ", local="
            + localStatusId
            + ", card="
            + trigger.CardId);
        return false;
    }

    private static SkillCgRequest BuildRequest(
        AuraCgRegistryEntry entry,
        string imageResource,
        string imagePath,
        SkillCgTriggerContext trigger)
    {
        return PrepareRequestForPlayback(new SkillCgRequest
        {
            ProviderId = SunExpIds.ModId + ".SkillCG." + entry.CgId,
            OwnerModId = SunExpIds.ModId,
            CardId = trigger.CardId,
            OwnerInstanceId = trigger.OwnerInstanceId,
            ImagePath = imagePath,
            ImageResource = imageResource,
            BundlePath = entry.Media.BundlePath,
            BundleAssetPrefix = entry.Media.BundleAssetPrefix,
            MediaType = entry.Media.Type,
            FrameSeconds = entry.Media.FrameSeconds,
            AlphaMode = entry.Media.AlphaMode,
            KeyThreshold = entry.Media.KeyThreshold,
            KeySoftness = entry.Media.KeySoftness,
            FlashAtSeconds = entry.Media.FlashAtSeconds,
            FlashDuration = entry.Media.FlashDuration,
            FlashMode = entry.Media.FlashMode,
            FlashStartFrame = entry.Media.FlashStartFrame,
            FlashEndFrame = entry.Media.FlashEndFrame,
            FlashPulseEveryFrames = entry.Media.FlashPulseEveryFrames,
            FlashStrength = entry.Media.FlashStrength,
            Priority = entry.Priority,
            FadeIn = entry.DefaultPresentation.FadeIn,
            Hold = entry.DefaultPresentation.Hold,
            FadeOut = entry.DefaultPresentation.FadeOut,
            PresentationMode = entry.DefaultPresentation.Mode,
            FitMode = entry.DefaultPresentation.Fit,
            FocusX = entry.DefaultPresentation.FocusX,
            FocusY = entry.DefaultPresentation.FocusY,
            SafeScale = entry.DefaultPresentation.SafeScale,
            CreatedAt = Time.unscaledTime
        }, trigger);
    }

    private static SkillCgRequest PrepareRequestForPlayback(SkillCgRequest request, SkillCgTriggerContext trigger)
    {
        request.ActionSequence = trigger.ActionSequence;
        request.EventToken = trigger.EventToken;
        request.OwnerInstanceId = trigger.OwnerInstanceId;
        request.CardId = string.IsNullOrWhiteSpace(request.CardId) ? trigger.CardId : request.CardId;
        request.DisableSync = false;
        return request;
    }

    private static string EntrySkipReason(AuraCgRegistryEntry entry, SkillCgTriggerContext trigger)
    {
        if (!string.Equals(entry.Kind, "skill", StringComparison.OrdinalIgnoreCase))
        {
            return "kind";
        }

        if (!IsSupportedMediaType(entry.Media.Type))
        {
            return "media";
        }

        if (!AuraCgActivationRuntime.CanConsumerPlay(entry, SunExpIds.ModId))
        {
            return "activation";
        }

        if (!EntryMatchesRole(entry, trigger.OwnerRoleId))
        {
            return "role";
        }

        if (!EntryMatchesCard(entry, trigger.CardId))
        {
            return "card";
        }

        return "";
    }

    private static bool EntryMatchesRole(AuraCgRegistryEntry entry, string roleId)
    {
        var normalizedRole = NormalizeId(roleId);
        if (string.IsNullOrWhiteSpace(normalizedRole))
        {
            return true;
        }

        foreach (var target in entry.TargetRoleIds ?? new List<string>())
        {
            var normalizedTarget = NormalizeId(target);
            if (string.Equals(normalizedTarget, "*", StringComparison.Ordinal)
                || string.Equals(normalizedTarget, normalizedRole, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static bool EntryMatchesCard(AuraCgRegistryEntry entry, string cardId)
    {
        var normalizedCard = NormalizeId(cardId).TrimStart('*');
        foreach (var value in entry.CardIds ?? new List<string>())
        {
            var pattern = (value ?? "").Trim();
            if (string.Equals(pattern, "*", StringComparison.Ordinal)
                || string.Equals(pattern, cardId, StringComparison.OrdinalIgnoreCase)
                || string.Equals(pattern.TrimStart('*'), normalizedCard, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsSupportedMediaType(string type)
    {
        return string.Equals(type, SkillCgMediaTypes.Image, StringComparison.OrdinalIgnoreCase)
               || string.Equals(type, SkillCgMediaTypes.Sequence, StringComparison.OrdinalIgnoreCase);
    }

    private static string ResolveImageResource(AuraCgRegistryEntry entry)
    {
        return string.IsNullOrWhiteSpace(entry.Media.Resource)
            ? entry.Media.FallbackImage
            : entry.Media.Resource;
    }

    private static bool MediaExists(string mediaType, string path)
    {
        if (string.Equals(mediaType, SkillCgMediaTypes.Sequence, StringComparison.OrdinalIgnoreCase))
        {
            return Directory.Exists(path)
                   || File.Exists(path);
        }

        return File.Exists(path);
    }

    private static string NormalizeId(string value)
    {
        return (value ?? "").Trim();
    }

    private static bool IsMultiplayerSession()
    {
        var manager = PlayerManager.Instance;
        if (manager != null && (manager.isClient || manager.isServer))
        {
            return true;
        }

        try
        {
            return (GameServer.Instance?.LobbyInfo?.AddedPlayers?.Count ?? 0) > 1;
        }
        catch
        {
            return false;
        }
    }

    private static void LogDiagnostic(string key, string message)
    {
        if (DiagnosticKeys.Add(key))
        {
            SunExpLog.Debug(message);
        }
    }

    private static void OnFightStart(ModHookContext context)
    {
        SkillCgArbiterRuntime.BeginFightSession(SunExpIds.ModId, "fight start");
    }

    private static void OnAdventureStart(ModHookContext context)
    {
        try
        {
            SkillCgArbiterRuntime.EnsureAdventurePreloaded(
                SunExpIds.ModId,
                SunExpIds.ModId,
                AdventurePreloadContentKey,
                new[] { SkillCgArbiterRuntime.SkillCgKind, SkillCgArbiterRuntime.CardUseCgKind });
        }
        catch (Exception ex)
        {
            SunExpLog.Warn("[SkillCG] adventure preload failed: " + ex.Message);
        }
    }

    private static void OnFightEnded(ModHookContext context)
    {
        SkillCgArbiterRuntime.Clear(SunExpIds.ModId, "fight ended");
    }

    private static void OnFightEnding(ModHookContext context)
    {
        SkillCgArbiterRuntime.Clear(SunExpIds.ModId, "fight ending");
    }

}
