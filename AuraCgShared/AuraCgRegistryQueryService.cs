using System;
using System.Collections.Generic;
using System.Linq;
using AuraShared.Core;

namespace AuraCg.Shared;

internal static class AuraCgRegistryQueryService
{
    public static bool IsRegisteredEntry(AuraCgRegistryEntry? entry, string kind)
    {
        if (entry == null)
        {
            return false;
        }

        entry.Normalize(entry.OwnerModId);
        if (!IsRegisteredSignalEntry(entry))
        {
            return false;
        }

        var requiredSignal = SignalForLegacyKind(kind);
        return entry.Signals.Any(signal => string.Equals(signal, requiredSignal, StringComparison.OrdinalIgnoreCase));
    }

    public static bool IsRegisteredSignalEntry(AuraCgRegistryEntry? entry)
    {
        if (entry == null)
        {
            return false;
        }

        entry.Normalize(entry.OwnerModId);
        var supportedMedia = string.Equals(entry.Media.Type, SkillCgMediaTypes.Image, StringComparison.OrdinalIgnoreCase)
                             || string.Equals(entry.Media.Type, SkillCgMediaTypes.Sequence, StringComparison.OrdinalIgnoreCase)
                             || string.Equals(entry.Media.Type, SkillCgMediaTypes.Scene, StringComparison.OrdinalIgnoreCase);
        return entry.Enabled
               && supportedMedia
               && entry.SubjectIds.Count > 0
               && entry.Signals.Count > 0
               && (!string.Equals(entry.Media.Type, SkillCgMediaTypes.Scene, StringComparison.OrdinalIgnoreCase)
                   || entry.Scene != null);
    }

    public static bool MatchesSignal(
        AuraCgRegistryEntry entry,
        AuraCgSignalContext context,
        bool consumerCanPlay)
    {
        if (entry == null || context == null || !consumerCanPlay)
        {
            return false;
        }

        entry.Normalize(entry.OwnerModId);
        context.Normalize();
        return IsRegisteredSignalEntry(entry)
               && string.Equals(entry.SubjectType, context.SubjectType, StringComparison.OrdinalIgnoreCase)
               && entry.Signals.Any(signal => string.Equals(signal, context.SignalId, StringComparison.OrdinalIgnoreCase))
               && MatchesSubject(entry, context.SubjectId)
               && MatchesConditions(entry, context);
    }

    public static bool MatchesResolvedIdentity(
        AuraCgRegistryEntry entry,
        string signalId,
        string subjectType,
        string subjectId)
    {
        if (entry == null)
        {
            return false;
        }

        entry.Normalize(entry.OwnerModId);
        var normalizedSignal = (signalId ?? "").Trim().ToLowerInvariant();
        var normalizedSubjectType = AuraCgSubjectTypes.Normalize(subjectType);
        return IsRegisteredSignalEntry(entry)
               && string.Equals(entry.SubjectType, normalizedSubjectType, StringComparison.OrdinalIgnoreCase)
               && entry.Signals.Any(signal => string.Equals(signal, normalizedSignal, StringComparison.OrdinalIgnoreCase))
               && MatchesSubject(entry, subjectId);
    }

    public static bool MatchesTrigger(
        AuraCgRegistryEntry entry,
        string kind,
        SkillCgTriggerContext context,
        bool consumerCanPlay)
    {
        var signal = AuraCgSignalContext.FromLegacy(context);
        return string.Equals(signal.SignalId, SignalForLegacyKind(kind), StringComparison.OrdinalIgnoreCase)
               && MatchesSignal(entry, signal, consumerCanPlay);
    }

    public static bool MatchesRole(AuraCgRegistryEntry entry, string roleId)
    {
        entry.Normalize(entry.OwnerModId);
        if (string.Equals(entry.SubjectType, AuraCgSubjectTypes.Role, StringComparison.OrdinalIgnoreCase))
        {
            return MatchesContentIds(
                entry.SubjectIds,
                roleId,
                entry.OwnerModId,
                AuraSharedIdentity.OfficialCareerPrefix);
        }

        return AuraCgTargetMatcher.MatchesRole(entry, roleId);
    }

    public static bool MatchesCard(AuraCgRegistryEntry entry, string cardId)
    {
        entry.Normalize(entry.OwnerModId);
        var ids = string.Equals(entry.SubjectType, AuraCgSubjectTypes.Card, StringComparison.OrdinalIgnoreCase)
            ? entry.SubjectIds
            : entry.CardIds;
        return ids.Any(value => AuraSharedContentId.Matches(value, cardId, entry.OwnerModId, "careercard_"));
    }

    public static bool MatchesSkill(AuraCgRegistryEntry entry, string skillId)
    {
        entry.Normalize(entry.OwnerModId);
        var ids = entry.SkillIds != null && entry.SkillIds.Count > 0
            ? entry.SkillIds
            : entry.Match.Facts.TryGetValue("skillId", out var configured)
                ? configured
                : new List<string>();
        return ids.Any(value => AuraSharedContentId.Matches(value, skillId, entry.OwnerModId, "careercard_"));
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
        return CreateRequest(
            entry,
            imageResource,
            imagePath,
            AuraCgSignalContext.FromLegacy(context),
            disableSync,
            createdAt);
    }

    public static SkillCgRequest CreateRequest(
        AuraCgRegistryEntry entry,
        string imageResource,
        string imagePath,
        AuraCgSignalContext context,
        bool disableSync,
        float createdAt)
    {
        context.Normalize();
        var scene = string.Equals(entry.Media.Type, SkillCgMediaTypes.Scene, StringComparison.OrdinalIgnoreCase);
        return new SkillCgRequest
        {
            ProviderId = entry.OwnerModId + ".SkillCG." + entry.CgId,
            OwnerModId = entry.OwnerModId,
            SignalId = context.SignalId,
            SubjectType = context.SubjectType,
            SubjectId = context.SubjectId,
            TriggerKind = context.SubjectType,
            CardId = ResolveLegacyActionId(context),
            OwnerInstanceId = context.OwnerInstanceId,
            ImagePath = scene ? "" : imagePath,
            ImageResource = scene ? "" : imageResource,
            BundlePath = scene ? "" : entry.Media.BundlePath,
            BundleAssetPrefix = scene ? "" : entry.Media.BundleAssetPrefix,
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
            DisableSync = disableSync,
            Exclusive = scene && (entry.Scene?.Exclusive ?? true),
            ScenePlan = scene ? context.ScenePlan : null
        };
    }

    private static bool MatchesSubject(AuraCgRegistryEntry entry, string subjectId)
    {
        if (entry.SubjectIds.Contains("*", StringComparer.Ordinal))
        {
            return true;
        }

        if (string.IsNullOrWhiteSpace(subjectId))
        {
            return false;
        }

        if (string.Equals(entry.SubjectType, AuraCgSubjectTypes.Role, StringComparison.OrdinalIgnoreCase))
        {
            return MatchesContentIds(
                entry.SubjectIds,
                subjectId,
                entry.OwnerModId,
                AuraSharedIdentity.OfficialCareerPrefix);
        }

        if (string.Equals(entry.SubjectType, AuraCgSubjectTypes.Card, StringComparison.OrdinalIgnoreCase))
        {
            return entry.SubjectIds.Any(value => AuraSharedContentId.Matches(
                value,
                subjectId,
                entry.OwnerModId,
                "careercard_"));
        }

        return entry.SubjectIds.Any(value => string.Equals(value, subjectId, StringComparison.OrdinalIgnoreCase));
    }

    private static bool MatchesConditions(AuraCgRegistryEntry entry, AuraCgSignalContext context)
    {
        entry.Match.Normalize();
        foreach (var pair in entry.Match.Facts)
        {
            if (!context.Facts.TryGetValue(pair.Key, out var actual))
            {
                return false;
            }

            if (string.Equals(pair.Key, "roleId", StringComparison.OrdinalIgnoreCase))
            {
                if (!MatchesContentIds(pair.Value, actual, entry.OwnerModId, AuraSharedIdentity.OfficialCareerPrefix))
                {
                    return false;
                }

                continue;
            }

            if (string.Equals(pair.Key, "cardId", StringComparison.OrdinalIgnoreCase)
                || string.Equals(pair.Key, "skillId", StringComparison.OrdinalIgnoreCase))
            {
                if (!pair.Value.Any(value => AuraSharedContentId.Matches(
                        value,
                        actual,
                        entry.OwnerModId,
                        "careercard_")))
                {
                    return false;
                }

                continue;
            }

            if (!pair.Value.Any(value => string.Equals(value, "*", StringComparison.Ordinal)
                                         || string.Equals(value, actual, StringComparison.OrdinalIgnoreCase)))
            {
                return false;
            }
        }

        foreach (var pair in entry.Match.MinimumMetrics)
        {
            if (!context.Metrics.TryGetValue(pair.Key, out var actual) || actual < pair.Value)
            {
                return false;
            }
        }

        foreach (var pair in entry.Match.MaximumMetrics)
        {
            if (!context.Metrics.TryGetValue(pair.Key, out var actual) || actual > pair.Value)
            {
                return false;
            }
        }

        return true;
    }

    private static bool MatchesContentIds(
        IEnumerable<string> configuredIds,
        string actualId,
        string ownerModId,
        string officialPrefix)
    {
        var actual = (actualId ?? "").Trim();
        if (actual.Length == 0)
        {
            return false;
        }

        return (configuredIds ?? Array.Empty<string>())
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Any(value => string.Equals(value.Trim(), "*", StringComparison.Ordinal)
                          || AuraSharedContentId.Resolve(
                              value,
                              new[] { actual },
                              ownerModId,
                              officialPrefix).Success);
    }

    private static string ResolveLegacyActionId(AuraCgSignalContext context)
    {
        if (!string.IsNullOrWhiteSpace(context.SkillId)) return context.SkillId;
        if (!string.IsNullOrWhiteSpace(context.CardId)) return context.CardId;
        if (!string.IsNullOrWhiteSpace(context.SubjectId)) return context.SubjectId;
        return "*";
    }

    private static string SignalForLegacyKind(string kind)
    {
        if (string.Equals(kind, SkillCgArbiterRuntime.CardUseCgKind, StringComparison.OrdinalIgnoreCase))
        {
            return AuraCgSignals.CardUsePresentationCommitted;
        }

        if (string.Equals(kind, SkillCgArbiterRuntime.FeastCgKind, StringComparison.OrdinalIgnoreCase))
        {
            return AuraCgSignals.RoleFeastCompleted;
        }

        return AuraCgSignals.RoleSkillCommitted;
    }
}
