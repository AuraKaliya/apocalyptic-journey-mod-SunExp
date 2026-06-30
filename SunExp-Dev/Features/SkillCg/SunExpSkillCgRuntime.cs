using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using AuraCg.Shared;
using AuraShared.Core;
using SunExp.Dll.Infrastructure;
using UnityEngine;
using Witch;
using Witch.Core;
using Witch.Mod;
using Witch.UI.Window;

namespace SunExp.Dll.Features.SkillCg;

public static class SunExpSkillCgRuntime
{
    private static long actionSequence;
    private static readonly HashSet<string> DiagnosticKeys = new(StringComparer.OrdinalIgnoreCase);

    public static void Initialize(ModConfig modConfig)
    {
        SkillCgArbiterRuntime.Initialize(modConfig, SunExpIds.ModId);
        RegisterBefore(modConfig, "FightUI.CallActionAnimation", BeforeCallActionAnimation);
        RegisterAfter(modConfig, "Fight_Start.Init", OnFightStart);
        RegisterAfter(modConfig, "FightInit.Init", OnFightStart);
        RegisterBefore(modConfig, "Fight_Win.ResetStates", OnFightEnding);
        RegisterBefore(modConfig, "Fight_Escape.ResetStates", OnFightEnding);
        RegisterBefore(modConfig, "Fight_Loss.Init", OnFightEnding);
        RegisterAfter(modConfig, "Fight_Win.ResetStates", OnFightEnded);
        RegisterAfter(modConfig, "Fight_Escape.ResetStates", OnFightEnded);
        RegisterAfter(modConfig, "Fight_Loss.Init", OnFightEnded);
    }

    private static void BeforeCallActionAnimation(ModHookContext context)
    {
        try
        {
            var trigger = BuildTriggerContext(context.Arguments != null && context.Arguments.Length > 0
                ? context.Arguments[0] as IScriptExecutor
                : null);
            if (trigger == null)
            {
                return;
            }

            foreach (var request in BuildRequests(trigger))
            {
                SkillCgArbiterRuntime.RequestCg(SunExpIds.ModId, request);
            }

            foreach (var request in SkillCgArbiterRuntime.BuildRegisteredCardUseRequests(SunExpIds.ModId, trigger, SunExpIds.ModId))
            {
                SkillCgArbiterRuntime.RequestCg(SunExpIds.ModId, request);
            }
        }
        catch (Exception ex)
        {
            SunExpLog.Warn("[SkillCG] trigger failed: " + ex.Message);
        }
    }

    private static SkillCgTriggerContext? BuildTriggerContext(IScriptExecutor? scriptExecutor)
    {
        var dataConfig = scriptExecutor?.dataConfig;
        if (dataConfig == null || dataConfig.Type != DataType.Card || dataConfig.data == null)
        {
            return null;
        }

        var cardId = ReadData(dataConfig, "Id");
        if (string.IsNullOrWhiteSpace(cardId))
        {
            cardId = dataConfig.InstanceID ?? "";
        }

        if (string.IsNullOrWhiteSpace(cardId))
        {
            return null;
        }

        var owner = scriptExecutor?.Self as StatusManager;
        return new SkillCgTriggerContext
        {
            ActionSequence = ++actionSequence,
            Action = ReadData(dataConfig, "Action"),
            CardId = cardId,
            OwnerInstanceId = owner?.InstanceId ?? "",
            OwnerRoleId = ReadStatusRoleId(owner),
            CreatedAt = Time.unscaledTime
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

    private static SkillCgRequest BuildRequest(
        AuraCgRegistryEntry entry,
        string imageResource,
        string imagePath,
        SkillCgTriggerContext trigger)
    {
        return new SkillCgRequest
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
            CreatedAt = Time.unscaledTime,
            ActionSequence = trigger.ActionSequence
        };
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

    private static string ReadData(IDataConfig dataConfig, string key)
    {
        try
        {
            return dataConfig.data.TryGetValue(key, out var value) ? value ?? "" : "";
        }
        catch
        {
            return "";
        }
    }

    private static string ReadStatusRoleId(StatusManager? status)
    {
        var current = ReadCurrentCareerId();
        var fatherId = "";
        try
        {
            var father = status?.fatherObject;
            fatherId = AuraSharedReflection.ReadString(father, "Id", "id");
        }
        catch
        {
        }

        var selected = AuraSharedIdentity.SelectRoleId(fatherId, current);
        if (!string.IsNullOrWhiteSpace(fatherId)
            && !string.Equals(selected, AuraSharedIdentity.NormalizeRoleId(fatherId), StringComparison.OrdinalIgnoreCase))
        {
            LogDiagnostic("role-fallback:" + fatherId + ":" + selected,
                "[SkillCG] ignored runtime owner id while resolving role: ownerId=" + fatherId
                + ", fallbackRole=" + selected);
        }

        return selected;
    }

    private static string ReadCurrentCareerId()
    {
        return ReadDataId(RoleTable.Instance?.Career ?? GameEntryUI.career);
    }

    private static string ReadDataId(IDataConfig? data)
    {
        try
        {
            if (data?.data != null && data.data.TryGetValue("Id", out var id))
            {
                return id ?? "";
            }
        }
        catch
        {
        }

        return "";
    }

    private static string NormalizeId(string value)
    {
        return (value ?? "").Trim();
    }

    private static void LogDiagnostic(string key, string message)
    {
        if (DiagnosticKeys.Add(key))
        {
            SunExpLog.Info(message);
        }
    }

    private static void OnFightStart(ModHookContext context)
    {
        actionSequence = 0;
        SkillCgArbiterRuntime.Clear(SunExpIds.ModId, "fight start");
        try
        {
            SkillCgArbiterRuntime.PreloadRegisteredCardUseCg(SunExpIds.ModId, SunExpIds.ModId);
        }
        catch (Exception ex)
        {
            SunExpLog.Warn("[SkillCG] preload failed: " + ex.Message);
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

    private static IEnumerable<SkillCgRequest> BuildWarmupRequests()
    {
        foreach (var entry in AuraCgRegistryRuntime.GetRegisteredEntries(SunExpIds.ModId))
        {
            if (!entry.Enabled
                || !string.Equals(entry.Kind, "skill", StringComparison.OrdinalIgnoreCase)
                || !IsSupportedMediaType(entry.Media.Type)
                || !AuraCgActivationRuntime.CanConsumerPlay(entry, SunExpIds.ModId))
            {
                continue;
            }

            var imageResource = ResolveImageResource(entry);
            var imagePath = SkillCgArbiterRuntime.ResolveImagePath(SunExpIds.ModId, imageResource);
            if (!MediaExists(entry.Media.Type, imagePath)
                && string.IsNullOrWhiteSpace(entry.Media.BundlePath))
            {
                continue;
            }

            yield return BuildRequest(entry, imageResource, imagePath, new SkillCgTriggerContext
            {
                CardId = entry.CardIds.FirstOrDefault() ?? "",
                CreatedAt = Time.unscaledTime
            });
        }
    }

    private static void RegisterBefore(ModConfig modConfig, string target, Action<ModHookContext> action)
    {
        AuraSharedHooks.RegisterBefore(modConfig, target, action, SunExpLog.Debug, SunExpLog.Warn);
    }

    private static void RegisterAfter(ModConfig modConfig, string target, Action<ModHookContext> action)
    {
        AuraSharedHooks.RegisterAfter(modConfig, target, action, SunExpLog.Debug, SunExpLog.Warn);
    }
}
