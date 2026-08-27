using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using AuraCg.Shared;
using AuraGameData.Shared.GameApi;
using AuraShared.Core;
using AuraToolsExp.Dll.Config;
using AuraToolsExp.Dll.Features.SkillCg;
using AuraToolsExp.Dll.Infrastructure;
using UnityEngine;
using Witch.Core;

namespace AuraToolsExp.Dll.Features.Cg;

public static class AuraToolsRoleCgChannels
{
    public const string Skill = "skill";
    public const string Feast = "feast";
    public const string LowHealth = "low-health";

    public static string Signal(string channel)
    {
        if (string.Equals(channel, Feast, StringComparison.OrdinalIgnoreCase)) return AuraCgSignals.RoleFeastCompleted;
        if (string.Equals(channel, LowHealth, StringComparison.OrdinalIgnoreCase)) return AuraCgSignals.RoleLowHealthEntered;
        return AuraCgSignals.RoleSkillCommitted;
    }
}

public sealed class AuraToolsRoleCgCandidate
{
    public string QualifiedCgId { get; set; } = "";
    public string OwnerModId { get; set; } = "";
    public string CgId { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string RoleId { get; set; } = "";
    public string Channel { get; set; } = AuraToolsRoleCgChannels.Skill;
    public string SkillId { get; set; } = "";
    public string Resource { get; set; } = "";
    public int Priority { get; set; }
    public bool Enabled { get; set; }
    public bool Manual { get; set; }
}

public static class AuraToolsRoleCgCatalog
{
    public const string LocalContributionId = "local-role-cg";
    public const string NoSelectionCgId = AuraToolsRoleCgContextKeys.NoneSelectionCgId;

    public static IReadOnlyList<AuraToolsRoleCgCandidate> Query(
        string roleId,
        string channel,
        string skillId = "")
    {
        var normalizedRole = RoleCatalog.NormalizeRoleId(roleId);
        var signal = AuraToolsRoleCgChannels.Signal(channel);
        var entries = AuraCgRegistryRuntime.GetRegisteredEntries()
            .Where(entry => string.Equals(entry.SubjectType, AuraCgSubjectTypes.Role, StringComparison.OrdinalIgnoreCase))
            .Where(entry => entry.Signals.Contains(signal, StringComparer.OrdinalIgnoreCase))
            .Where(entry => MatchesRole(entry, normalizedRole))
            .Where(entry => !string.Equals(channel, AuraToolsRoleCgChannels.Skill, StringComparison.OrdinalIgnoreCase)
                            || MatchesSkill(entry, skillId))
            .ToList();
        var configured = AuraToolsConfigService.SkillCg.GetRoleSelection(
            normalizedRole,
            channel,
            skillId);
        var selected = string.Equals(configured, NoSelectionCgId, StringComparison.OrdinalIgnoreCase)
            ? ""
            : entries.Any(entry => string.Equals(
                    entry.QualifiedCgId,
                    configured,
                    StringComparison.OrdinalIgnoreCase))
                ? configured
                : entries.FirstOrDefault(entry => entry.DefaultActivation.Enabled)?.QualifiedCgId ?? "";
        return entries
            .Select(entry => Project(
                entry,
                normalizedRole,
                channel,
                skillId,
                string.Equals(entry.QualifiedCgId, selected, StringComparison.OrdinalIgnoreCase)))
            .OrderByDescending(candidate => candidate.Enabled)
            .ThenByDescending(candidate => candidate.Priority)
            .ThenBy(candidate => candidate.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(candidate => candidate.QualifiedCgId, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public static IReadOnlyList<RoleSkillInfo> SkillOptions(string roleId)
    {
        var normalized = RoleCatalog.NormalizeRoleId(roleId);
        var result = RoleCatalog.GetRoleSkills(normalized).ToList();
        foreach (var entry in AuraCgRegistryRuntime.GetRegisteredEntries()
                     .Where(entry => entry.Signals.Contains(AuraCgSignals.RoleSkillCommitted, StringComparer.OrdinalIgnoreCase))
                     .Where(entry => MatchesRole(entry, normalized)))
        {
            foreach (var skillId in MatchValues(entry, "skillId"))
            {
                if (string.Equals(skillId, "*", StringComparison.Ordinal)
                    || AuraToolsRoleSkillIdentity.ContainsEquivalent(
                        result.Select(skill => skill.Id),
                        skillId,
                        entry.OwnerModId))
                {
                    continue;
                }

                var canonicalId = AuraGameDataHostApi.ResolveId(
                    DataType.Card,
                    new[]
                    {
                        skillId,
                        AuraSharedContentId.NormalizeProtocolMarkers(skillId)
                    });
                if (string.IsNullOrWhiteSpace(canonicalId)
                    || AuraToolsRoleSkillIdentity.ContainsEquivalent(
                        result.Select(skill => skill.Id),
                        canonicalId,
                        entry.OwnerModId))
                {
                    continue;
                }

                result.Add(new RoleSkillInfo
                {
                    Id = canonicalId,
                    DisplayName = Settings.AuraToolsPlayerDisplay.CardName(canonicalId),
                    Slot = result.Count + 1
                });
            }
        }

        return result
            .OrderBy(skill => skill.Slot)
            .ThenBy(skill => skill.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public static void Select(
        string roleId,
        string channel,
        string skillId,
        string qualifiedCgId)
    {
        AuraToolsConfigService.SkillCg.SetRoleSelection(
            roleId,
            channel,
            skillId,
            qualifiedCgId);
        SaveAndApply();
    }

    public static RoleCgEntryOverrideSettings GetOrCreateOverride(string qualifiedCgId)
    {
        var key = (qualifiedCgId ?? "").Trim();
        var settings = AuraToolsConfigService.SkillCg;
        if (!settings.RoleEntries.TryGetValue(key, out var value) || value == null)
        {
            value = new RoleCgEntryOverrideSettings();
            settings.RoleEntries[key] = value;
        }
        value.Normalize();
        return value;
    }

    public static void ResetContext(string roleId, string channel, string skillId)
    {
        var candidates = Query(roleId, channel, skillId);
        foreach (var candidate in candidates)
        {
            AuraToolsConfigService.SkillCg.RoleEntries.Remove(candidate.QualifiedCgId);
        }

        AuraToolsConfigService.SkillCg.ResetRoleSelection(roleId, channel, skillId);

        var signal = AuraToolsRoleCgChannels.Signal(channel);
        var normalizedRole = RoleCatalog.NormalizeRoleId(roleId);
        AuraToolsConfigService.SkillCg.ManualRoleEntries.RemoveAll(entry =>
            RoleCatalog.MatchesRole(normalizedRole, entry.RoleId)
            && string.Equals(entry.SignalId, signal, StringComparison.OrdinalIgnoreCase)
            && (!string.Equals(channel, AuraToolsRoleCgChannels.Skill, StringComparison.OrdinalIgnoreCase)
                || SkillIdEquals(entry.SkillId, skillId, AuraToolsIds.ModId)));
        SaveAndApply();
    }

    public static string ResolveSelectedCgId(string roleId, string channel, string skillId = "")
    {
        return Query(roleId, channel, skillId)
                   .FirstOrDefault(candidate => candidate.Enabled)
                   ?.QualifiedCgId
               ?? NoSelectionCgId;
    }

    public static void RemoveManual(string qualifiedCgId)
    {
        var key = (qualifiedCgId ?? "").Trim();
        var prefix = AuraToolsIds.ModId + ":";
        var cgId = key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
            ? key.Substring(prefix.Length)
            : key;
        AuraToolsConfigService.SkillCg.ManualRoleEntries.RemoveAll(entry =>
            string.Equals(entry.CgId, cgId, StringComparison.OrdinalIgnoreCase));
        AuraToolsConfigService.SkillCg.RoleEntries.Remove(key);
        foreach (var contextKey in AuraToolsConfigService.SkillCg.RoleSelections
                     .Where(pair => string.Equals(pair.Value, key, StringComparison.OrdinalIgnoreCase))
                     .Select(pair => pair.Key)
                     .ToList())
        {
            AuraToolsConfigService.SkillCg.RoleSelections.Remove(contextKey);
        }
        SaveAndApply();
    }

    public static bool Import(
        string roleId,
        string channel,
        string skillId,
        string sourcePath,
        out string message)
    {
        var normalizedRole = RoleCatalog.NormalizeRoleId(roleId);
        var cgId = "user.role."
                   + SafeSegment(normalizedRole) + "."
                   + SafeSegment(channel) + "."
                   + Guid.NewGuid().ToString("N");
        var imported = FileResourceUtil.ImportImagePath(
            sourcePath,
            FileResourceUtil.RoleSkillCgDirectory(normalizedRole),
            cgId,
            out message);
        if (string.IsNullOrWhiteSpace(imported))
        {
            return false;
        }

        var displayName = Path.GetFileNameWithoutExtension(sourcePath);
        var entry = new RoleCgManualEntrySettings
        {
            CgId = cgId,
            DisplayName = string.IsNullOrWhiteSpace(displayName) ? "玩家自定义 CG" : displayName,
            RoleId = normalizedRole,
            SignalId = AuraToolsRoleCgChannels.Signal(channel),
            SkillId = string.Equals(channel, AuraToolsRoleCgChannels.Skill, StringComparison.OrdinalIgnoreCase)
                ? skillId
                : "",
            Resource = imported,
            Priority = 1000,
            Presentation = AuraToolsConfigService.SkillCg.DefaultPresentation.Resolve(
                SkillCgPresentationSettings.CreateDefault())
        };
        entry.Normalize();
        AuraToolsConfigService.SkillCg.ManualRoleEntries.Add(entry);
        SaveAndApply();
        Select(normalizedRole, channel, skillId, AuraToolsIds.ModId + ":" + entry.CgId);
        message += " 已加入当前角色的候选资源。";
        return true;
    }

    public static bool Preview(AuraToolsRoleCgCandidate candidate)
    {
        var entry = AuraCgRegistryRuntime.GetRegisteredEntries(candidate.OwnerModId)
            .FirstOrDefault(value => string.Equals(value.CgId, candidate.CgId, StringComparison.OrdinalIgnoreCase));
        if (entry == null)
        {
            return false;
        }

        var resource = string.IsNullOrWhiteSpace(entry.Media.Resource)
            ? entry.Media.FallbackImage
            : entry.Media.Resource;
        var path = SkillCgArbiterRuntime.ResolveImagePath(entry.OwnerModId, resource);
        if (string.IsNullOrWhiteSpace(path) && !string.Equals(entry.Media.Type, SkillCgMediaTypes.Scene, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var request = new SkillCgRequest
        {
            ProviderId = entry.OwnerModId + ".SkillCG." + entry.CgId,
            OwnerModId = entry.OwnerModId,
            SignalId = AuraToolsRoleCgChannels.Signal(candidate.Channel),
            SubjectType = AuraCgSubjectTypes.Role,
            SubjectId = candidate.RoleId,
            TriggerKind = candidate.Channel,
            CardId = candidate.SkillId,
            OwnerInstanceId = "role-cg-preview",
            ImagePath = path,
            ImageResource = resource,
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
            ActionSequence = -Math.Abs(DateTime.UtcNow.Ticks),
            EventToken = "role-cg-preview:" + entry.QualifiedCgId,
            DisableSync = true
        };
        AuraToolsSkillCgRuntime.ApplyRegisteredSkillPresentationOverride(request);
        SkillCgArbiterRuntime.RequestCg(AuraToolsIds.ModId, request);
        return true;
    }

    public static string ResolveImagePath(AuraToolsRoleCgCandidate candidate)
    {
        return SkillCgArbiterRuntime.ResolveImagePath(candidate.OwnerModId, candidate.Resource);
    }

    public static void SynchronizeManualContribution()
    {
        var entries = AuraToolsConfigService.SkillCg.ManualRoleEntries
            .Where(entry => entry != null && entry.IsValid)
            .Select(ToRegistryEntry)
            .ToList();
        AuraCgRegistryRuntime.RegisterContribution(AuraToolsIds.ModId, LocalContributionId, entries);
    }

    private static AuraCgRegistryEntry ToRegistryEntry(RoleCgManualEntrySettings source)
    {
        source.Normalize();
        var entry = new AuraCgRegistryEntry
        {
            CgId = source.CgId,
            DisplayName = source.DisplayName,
            SubjectType = AuraCgSubjectTypes.Role,
            SubjectIds = new List<string> { source.RoleId },
            Signals = new List<string> { source.SignalId },
            Media = new AuraCgMediaSpec
            {
                Type = SkillCgMediaTypes.Image,
                Resource = source.Resource,
                FallbackImage = source.Resource
            },
            DefaultPresentation = new AuraCgPresentationSpec
            {
                Mode = source.Presentation.Mode,
                Fit = source.Presentation.Fit,
                FadeIn = source.Presentation.FadeIn,
                Hold = source.Presentation.Hold,
                FadeOut = source.Presentation.FadeOut,
                FocusX = source.Presentation.FocusX,
                FocusY = source.Presentation.FocusY,
                SafeScale = source.Presentation.SafeScale
            },
            DefaultActivation = new AuraCgDefaultActivationSpec
            {
                Enabled = true,
                ConsumerMode = AuraCgConsumerModes.ToolManaged,
                ConsumerModId = AuraToolsIds.ModId
            },
            Priority = source.Priority,
            Tags = new List<string> { "role-cg", "user-manual" },
            Enabled = true
        };
        if (!string.IsNullOrWhiteSpace(source.SkillId))
        {
            entry.Match.Facts["skillId"] = new List<string> { source.SkillId };
        }
        return entry;
    }

    private static AuraToolsRoleCgCandidate Project(
        AuraCgRegistryEntry entry,
        string roleId,
        string channel,
        string skillId,
        bool enabled)
    {
        return new AuraToolsRoleCgCandidate
        {
            QualifiedCgId = entry.QualifiedCgId,
            OwnerModId = entry.OwnerModId,
            CgId = entry.CgId,
            DisplayName = string.IsNullOrWhiteSpace(entry.DisplayName) ? entry.CgId : entry.DisplayName,
            RoleId = roleId,
            Channel = channel,
            SkillId = skillId,
            Resource = string.IsNullOrWhiteSpace(entry.Media.Resource)
                ? entry.Media.FallbackImage
                : entry.Media.Resource,
            Priority = entry.Priority,
            Enabled = enabled,
            Manual = string.Equals(entry.OwnerModId, AuraToolsIds.ModId, StringComparison.OrdinalIgnoreCase)
                     && string.Equals(entry.RegistrationSourceId, LocalContributionId, StringComparison.OrdinalIgnoreCase)
        };
    }

    private static bool MatchesRole(AuraCgRegistryEntry entry, string roleId)
    {
        return entry.SubjectIds.Contains("*", StringComparer.Ordinal)
               || entry.SubjectIds.Any(candidate => RoleCatalog.MatchesRole(roleId, candidate));
    }

    private static bool MatchesSkill(AuraCgRegistryEntry entry, string skillId)
    {
        var configured = MatchValues(entry, "skillId").ToList();
        return configured.Count == 0
               || configured.Contains("*", StringComparer.Ordinal)
               || configured.Any(candidate => SkillIdEquals(candidate, skillId, entry.OwnerModId));
    }

    private static IEnumerable<string> MatchValues(AuraCgRegistryEntry entry, string key)
    {
        return entry.Match?.Facts != null && entry.Match.Facts.TryGetValue(key, out var values)
            ? values
            : Array.Empty<string>();
    }

    private static bool SkillIdEquals(string candidate, string actual, string ownerModId)
    {
        if (string.IsNullOrWhiteSpace(actual)) return string.Equals(candidate, "*", StringComparison.Ordinal);
        return string.Equals(candidate, actual, StringComparison.OrdinalIgnoreCase)
               || AuraSharedContentId.Matches(candidate, actual, ownerModId, "careercard_");
    }

    private static string SafeSegment(string value)
    {
        var chars = (value ?? "").Trim()
            .Select(character => char.IsLetterOrDigit(character) || character == '_' || character == '-'
                ? char.ToLowerInvariant(character)
                : '-')
            .ToArray();
        var result = new string(chars).Trim('-');
        return string.IsNullOrWhiteSpace(result) ? "item" : result;
    }

    private static void SaveAndApply()
    {
        AuraToolsConfigService.SkillCg.Normalize();
        AuraToolsConfigService.SaveSkillCg();
        SynchronizeManualContribution();
        AuraToolsSkillCgRuntime.ApplyRoleCgConfiguration();
    }
}
