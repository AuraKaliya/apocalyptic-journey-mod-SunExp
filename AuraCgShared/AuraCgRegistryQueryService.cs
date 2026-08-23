using System;
using System.Collections.Generic;
using System.Linq;
using AuraShared.Core;

namespace AuraCg.Shared;

internal static class AuraCgRegistryQueryService
{
    public static bool IsRegisteredEntry(AuraCgRegistryEntry? entry, string kind)
    {
        return entry != null
               && entry.Enabled
               && string.Equals(entry.Kind, kind, StringComparison.OrdinalIgnoreCase)
               && (string.Equals(entry.Media.Type, SkillCgMediaTypes.Image, StringComparison.OrdinalIgnoreCase)
                   || string.Equals(entry.Media.Type, SkillCgMediaTypes.Sequence, StringComparison.OrdinalIgnoreCase));
    }

    public static bool MatchesTrigger(
        AuraCgRegistryEntry entry,
        string kind,
        SkillCgTriggerContext context,
        bool consumerCanPlay)
    {
        return IsRegisteredEntry(entry, kind)
               && MatchesTriggerKind(kind, context.TriggerKind)
               && MatchesRole(entry, context.OwnerRoleId)
               && MatchesTarget(entry, kind, context)
               && MatchesAction(context.Action)
               && consumerCanPlay;
    }

    public static bool MatchesRole(AuraCgRegistryEntry entry, string roleId)
    {
        return AuraCgTargetMatcher.MatchesRole(entry, roleId);
    }

    public static bool MatchesCard(AuraCgRegistryEntry entry, string cardId)
    {
        foreach (var value in entry.CardIds ?? new List<string>())
        {
            if (AuraSharedContentId.Matches(value, cardId, entry.OwnerModId, "careercard_"))
            {
                return true;
            }
        }

        return false;
    }

    public static bool MatchesSkill(AuraCgRegistryEntry entry, string skillId)
    {
        var ids = entry.SkillIds != null && entry.SkillIds.Count > 0
            ? entry.SkillIds
            : entry.CardIds;
        foreach (var value in ids ?? new List<string>())
        {
            if (AuraSharedContentId.Matches(value, skillId, entry.OwnerModId, "careercard_"))
            {
                return true;
            }
        }

        return false;
    }

    private static bool MatchesTarget(
        AuraCgRegistryEntry entry,
        string kind,
        SkillCgTriggerContext context)
    {
        return string.Equals(kind, "skill", StringComparison.OrdinalIgnoreCase)
            ? MatchesSkill(entry, string.IsNullOrWhiteSpace(context.SkillId) ? context.CardId : context.SkillId)
            : MatchesCard(entry, context.CardId);
    }

    private static bool MatchesTriggerKind(string registeredKind, string triggerKind)
    {
        if (string.IsNullOrWhiteSpace(triggerKind)) return true;
        if (string.Equals(registeredKind, "skill", StringComparison.OrdinalIgnoreCase))
            return string.Equals(triggerKind, "skill", StringComparison.OrdinalIgnoreCase);
        if (string.Equals(registeredKind, "cardUse", StringComparison.OrdinalIgnoreCase))
            return string.Equals(triggerKind, "card", StringComparison.OrdinalIgnoreCase);
        return true;
    }

    public static bool MatchesAction(string action)
    {
        return true;
    }

    public static string ResolveImageResource(AuraCgRegistryEntry entry)
    {
        return string.IsNullOrWhiteSpace(entry.Media.Resource)
            ? entry.Media.FallbackImage
            : entry.Media.Resource;
    }

    public static SkillCgRequest CreateRequest(
        AuraCgRegistryEntry entry,
        string imageResource,
        string imagePath,
        SkillCgTriggerContext context,
        bool disableSync,
        float createdAt)
    {
        return new SkillCgRequest
        {
            ProviderId = entry.OwnerModId + ".SkillCG." + entry.CgId,
            OwnerModId = entry.OwnerModId,
            TriggerKind = context.TriggerKind,
            CardId = string.IsNullOrWhiteSpace(context.SkillId)
                ? (string.IsNullOrWhiteSpace(context.CardId) ? "*" : context.CardId)
                : context.SkillId,
            OwnerInstanceId = context.OwnerInstanceId,
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
            CreatedAt = createdAt,
            ActionSequence = context.ActionSequence,
            EventToken = context.EventToken,
            DisableSync = disableSync
        };
    }
}
