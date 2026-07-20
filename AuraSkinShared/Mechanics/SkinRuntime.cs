using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using AuraSkin.Shared.GameApi;
using AuraSkin.Shared.Infrastructure;
using AuraSkin.Shared.Models;
using AuraSkin.Shared.Services;
using UnityEngine;

namespace AuraSkin.Shared.Mechanics;

public static class SkinRuntime
{
    private static readonly string[] AnimationStates =
    {
        "Idle", "Attack", "Hit", "Buff", "Debuff", "Skill", "Special", "Special1", "Special2", "Defend"
    };

    private static readonly Dictionary<string, string> AppliedAnimationSkin = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<string, SkinSelectionSnapshot> RemoteSelections = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<string, SkinSelectionResolveResult> RemoteStatuses = new(StringComparer.OrdinalIgnoreCase);

    public static bool FeatureEnabled { get; private set; } = true;

    public static bool EntryPanelEnabled { get; private set; } = true;

    public static event Action<SkinSelectionSnapshot>? LocalSelectionChanged;

    public static void Initialize()
    {
        Reload();
    }

    public static void Reload()
    {
        ResourceRedirectApi.RestoreAll();
        AppliedAnimationSkin.Clear();
        RemoteSelections.Clear();
        RemoteStatuses.Clear();
        SkinRegistry.Reload();
        SkinSelectionStore.Load();
    }

    public static IReadOnlyList<SkinDefinition> GetSkins(string careerId) => SkinRegistry.GetForCareer(careerId);

    public static IReadOnlyList<SkinDefinition> GetAllSkins(string careerId) => SkinRegistry.GetAllForCareer(careerId);

    public static IReadOnlyList<SkinDefinition> GetAllSkinCandidates() => SkinRegistry.GetAll();

    public static void ConfigureCandidates(bool configured, IEnumerable<string>? enabledQualifiedSkinIds)
    {
        SkinRegistry.ConfigureCandidateEnablement(configured, enabledQualifiedSkinIds);
        ApplyAllKnownSelections();
    }

    public static void ConfigureCandidateOverrides(IEnumerable<KeyValuePair<string, bool>>? overrides)
    {
        SkinRegistry.ConfigureCandidateOverrides(overrides);
        ApplyAllKnownSelections();
    }

    public static void ConfigurePresentation(bool featureEnabled, bool entryPanelEnabled)
    {
        FeatureEnabled = featureEnabled;
        EntryPanelEnabled = entryPanelEnabled;
        if (!FeatureEnabled)
        {
            ResourceRedirectApi.RestoreAll();
            AppliedAnimationSkin.Clear();
        }
    }

    public static SkinDefinition? GetSelectedSkin(string careerId, string instanceId = "")
    {
        if (!FeatureEnabled)
        {
            return null;
        }

        var remote = GetRemoteSelection(instanceId);
        if (remote != null
            && string.Equals(remote.CareerId, CareerConfigApi.NormalizeId(careerId), StringComparison.OrdinalIgnoreCase))
        {
            var remoteReference = string.IsNullOrWhiteSpace(remote.QualifiedSkinId)
                ? remote.SkinId
                : remote.QualifiedSkinId;
            return SkinRegistry.ResolveReference(
                remote.CareerId,
                remoteReference,
                string.IsNullOrWhiteSpace(remote.QualifiedSkinId) ? remote.OwnerModId : "",
                remote.ContentHash);
        }

        return SkinRegistry.ResolveReference(careerId, SkinSelectionStore.Get(careerId));
    }

    public static string GetSelectedSkinId(string careerId, string instanceId = "")
    {
        return GetSelectedSkin(careerId, instanceId)?.SkinId ?? "";
    }

    public static string GetSelectedQualifiedSkinId(string careerId, string instanceId = "")
    {
        return GetSelectedSkin(careerId, instanceId)?.QualifiedSkinId ?? "";
    }

    public static void Select(DataConfig career, string skinId)
    {
        if (!FeatureEnabled)
        {
            return;
        }

        var careerId = CareerId(career);
        if (string.IsNullOrWhiteSpace(careerId))
        {
            return;
        }

        var skin = SkinRegistry.ResolveReference(careerId, skinId);
        SkinSelectionStore.Set(careerId, skin?.QualifiedSkinId ?? "");
        ApplyAnimation(career, true);
        LocalSelectionChanged?.Invoke(CreateLocalSelectionSnapshot(career, "", ""));
    }

    public static bool TryRemapSelection(string careerId, string oldSkinId, string newSkinId)
    {
        var replacement = SkinRegistry.ResolveReference(careerId, newSkinId, effectiveOnly: false);
        var changed = SkinSelectionStore.TryRemapSelection(
            careerId,
            oldSkinId,
            replacement?.QualifiedSkinId ?? newSkinId);
        if (changed)
        {
            ApplyAllKnownSelections();
        }

        return changed;
    }

    public static void EnsureAnimation(DataConfig? career)
    {
        if (FeatureEnabled && career != null)
        {
            ApplyAnimation(career, false);
        }
    }

    public static void EnsureAnimation(DataConfig? career, string instanceId)
    {
        if (FeatureEnabled && career != null)
        {
            ApplyAnimation(career, false, instanceId);
        }
    }

    public static void ApplyAllKnownSelections()
    {
        var careerIds = SkinRegistry.CareerIds.Concat(SkinSelectionStore.CareerIds)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        foreach (var careerId in careerIds)
        {
            var normalizedCareerId = CareerConfigApi.NormalizeId(careerId);
            if (!CareerConfigApi.TryCreate(careerId, out var career) || career == null)
            {
                if (!string.IsNullOrWhiteSpace(normalizedCareerId))
                {
                    SkinLog.Warn("Could not apply saved skin for missing career " + normalizedCareerId);
                }

                continue;
            }

            try
            {
                ApplyAnimation(career, false);
            }
            catch (Exception ex)
            {
                SkinLog.Warn("Could not apply saved skin for career " + normalizedCareerId + ": " + ex.Message);
            }
        }
    }

    public static Sprite? LoadSprite(DataConfig? career, string field, string instanceId = "")
    {
        if (!FeatureEnabled || career?.data == null || !career.data.TryGetValue(field, out var defaultPath))
        {
            return null;
        }

        var resourcePath = ResolveResourcePath(career, field, defaultPath, instanceId);
        if (string.IsNullOrWhiteSpace(resourcePath))
        {
            return null;
        }

        try
        {
            return ResourceLoader.Load<Sprite>(resourcePath, true);
        }
        catch (Exception ex)
        {
            SkinLog.Warn("Failed to load " + field + " for " + CareerId(career) + ": " + ex.Message);
            return null;
        }
    }

    public static Sprite? LoadPreview(DataConfig? career)
    {
        if (career == null)
        {
            return null;
        }

        var skin = GetSelectedSkin(CareerId(career));
        var path = skin?.PreviewPath;
        if (string.IsNullOrWhiteSpace(path))
        {
            return LoadSprite(career, "CareerImage");
        }

        try
        {
            return ResourceLoader.Load<Sprite>(SkinPaths.ToRawResourcePath(path ?? ""), true);
        }
        catch
        {
            return LoadSprite(career, "CareerImage");
        }
    }

    public static string CareerId(DataConfig? career)
    {
        return career?.data != null && career.data.TryGetValue("Id", out var id)
            ? CareerConfigApi.NormalizeId(id)
            : "";
    }

    public static SkinSelectionSnapshot CreateLocalSelectionSnapshot(DataConfig? career, string playerId, string playerName)
    {
        var careerId = CareerId(career);
        var skin = GetSelectedSkin(careerId);
        return new SkinSelectionSnapshot
        {
            PlayerId = playerId?.Trim() ?? "",
            PlayerName = playerName?.Trim() ?? "",
            CareerId = careerId,
            SkinId = skin?.SkinId ?? "",
            QualifiedSkinId = skin?.QualifiedSkinId ?? "",
            ContentHash = skin?.ContentHash ?? "",
            PackageId = skin?.PackageId ?? "",
            PackageVersion = skin?.PackageVersion ?? 0,
            OwnerModId = skin?.OwnerModId ?? ""
        };
    }

    public static SkinSelectionResolveResult ApplyRemoteSelection(SkinSelectionSnapshot? snapshot)
    {
        var result = ResolveRemoteSelection(snapshot);
        var playerId = snapshot?.PlayerId?.Trim() ?? "";
        if (string.IsNullOrWhiteSpace(playerId))
        {
            return result;
        }

        RemoteStatuses[playerId] = result;
        if (!result.Success || result.DefaultSkin)
        {
            RemoteSelections.Remove(playerId);
            if (!string.IsNullOrWhiteSpace(result.Warning))
            {
                SkinLog.Warn(result.Warning);
            }

            return result;
        }

        var normalized = CloneSnapshot(snapshot!);
        normalized.CareerId = CareerConfigApi.NormalizeId(normalized.CareerId);
        normalized.SkinId = normalized.SkinId.Trim();
        normalized.QualifiedSkinId = normalized.QualifiedSkinId.Trim();
        RemoteSelections[playerId] = normalized;
        if (CareerConfigApi.TryCreate(normalized.CareerId, out var career) && career != null)
        {
            ApplyAnimation(career, true, playerId);
        }

        return result;
    }

    public static string[] RemoteStatusLines()
    {
        return RemoteStatuses.Values
            .OrderBy(item => item.PlayerId, StringComparer.OrdinalIgnoreCase)
            .Select(item => string.IsNullOrWhiteSpace(item.Warning) ? item.Status : item.Status + " / " + item.Warning)
            .ToArray();
    }

    private static string ResolveResourcePath(DataConfig career, string field, string defaultPath, string instanceId)
    {
        var skin = GetSelectedSkin(CareerId(career), instanceId);
        var assetPath = skin?.Assets.Get(field) ?? "";
        return string.IsNullOrWhiteSpace(assetPath) ? defaultPath : SkinPaths.ToRawResourcePath(assetPath);
    }

    private static void ApplyAnimation(DataConfig career, bool force, string instanceId = "")
    {
        var careerId = CareerId(career);
        if (string.IsNullOrWhiteSpace(careerId) || career.data == null)
        {
            return;
        }

        var skin = GetSelectedSkin(careerId, instanceId);
        var selectedId = skin?.QualifiedSkinId ?? "";
        if (!force
            && AppliedAnimationSkin.TryGetValue(careerId, out var applied)
            && string.Equals(applied, selectedId, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        ResourceRedirectApi.RestoreCareer(careerId);
        AppliedAnimationSkin[careerId] = selectedId;
        if (skin == null
            || string.IsNullOrWhiteSpace(skin.Assets.Animation)
            || !career.data.TryGetValue("Animation", out var defaultAnimation)
            || string.IsNullOrWhiteSpace(defaultAnimation))
        {
            return;
        }

        var redirectedCount = 0;
        foreach (var state in AnimationStates)
        {
            var replacementDirectory = Path.Combine(skin.Assets.Animation, state);
            if (!Directory.Exists(replacementDirectory))
            {
                continue;
            }

            if (ResourceRedirectApi.TryRedirect(
                    careerId,
                    defaultAnimation.TrimEnd('/', '\\') + "/" + state,
                    SkinPaths.ToRawResourcePath(replacementDirectory)))
            {
                redirectedCount++;
            }
        }

        SkinLog.Info("Applied skin " + skin.SkinId + " to " + careerId + " with " + redirectedCount + " animation state(s)");
    }

    private static SkinSelectionSnapshot? GetRemoteSelection(string instanceId)
    {
        if (string.IsNullOrWhiteSpace(instanceId))
        {
            return null;
        }

        return RemoteSelections.TryGetValue(instanceId.Trim(), out var remote) ? remote : null;
    }

    private static SkinSelectionResolveResult ResolveRemoteSelection(SkinSelectionSnapshot? snapshot)
    {
        var playerId = snapshot?.PlayerId?.Trim() ?? "";
        var careerId = CareerConfigApi.NormalizeId(snapshot?.CareerId);
        var skinId = snapshot?.SkinId?.Trim() ?? "";
        var qualifiedSkinId = snapshot?.QualifiedSkinId?.Trim() ?? "";
        var prefix = "player=" + (string.IsNullOrWhiteSpace(playerId) ? "<unknown>" : playerId)
                     + ", career=" + (string.IsNullOrWhiteSpace(careerId) ? "<missing>" : careerId)
                     + ", skin=" + (string.IsNullOrWhiteSpace(skinId) ? "<default>" : skinId);
        var result = new SkinSelectionResolveResult
        {
            PlayerId = playerId,
            CareerId = careerId,
            SkinId = skinId,
            QualifiedSkinId = qualifiedSkinId
        };

        if (string.IsNullOrWhiteSpace(playerId) || string.IsNullOrWhiteSpace(careerId))
        {
            result.Warning = "[SkinSync] Invalid remote skin selection. " + prefix + ". Fallback to default skin.";
            result.Status = prefix + " / fallback default";
            return result;
        }

        if (string.IsNullOrWhiteSpace(skinId))
        {
            result.Success = true;
            result.DefaultSkin = true;
            result.Status = prefix + " / default";
            return result;
        }

        var reference = string.IsNullOrWhiteSpace(qualifiedSkinId) ? skinId : qualifiedSkinId;
        var skin = SkinRegistry.ResolveReference(
            careerId,
            reference,
            string.IsNullOrWhiteSpace(qualifiedSkinId) ? snapshot?.OwnerModId ?? "" : "",
            snapshot?.ContentHash ?? "");
        if (skin == null)
        {
            result.Warning = "[SkinSync] Missing remote skin resource. " + prefix + ". Fallback to default skin.";
            result.Status = prefix + " / missing resource";
            return result;
        }

        var incomingHash = snapshot?.ContentHash ?? "";
        if (!string.IsNullOrWhiteSpace(incomingHash)
            && !string.IsNullOrWhiteSpace(skin.ContentHash)
            && !string.Equals(incomingHash, skin.ContentHash, StringComparison.OrdinalIgnoreCase))
        {
            result.Warning = "[SkinSync] Remote skin hash mismatch. " + prefix + ". Fallback to default skin.";
            result.Status = prefix + " / hash mismatch";
            return result;
        }

        result.Success = true;
        result.QualifiedSkinId = skin.QualifiedSkinId;
        result.Status = prefix + " / synced";
        return result;
    }

    private static SkinSelectionSnapshot CloneSnapshot(SkinSelectionSnapshot snapshot)
    {
        return new SkinSelectionSnapshot
        {
            SchemaVersion = snapshot.SchemaVersion,
            PlayerId = snapshot.PlayerId ?? "",
            PlayerName = snapshot.PlayerName ?? "",
            CareerId = snapshot.CareerId ?? "",
            SkinId = snapshot.SkinId ?? "",
            QualifiedSkinId = snapshot.QualifiedSkinId ?? "",
            ContentHash = snapshot.ContentHash ?? "",
            PackageId = snapshot.PackageId ?? "",
            PackageVersion = snapshot.PackageVersion,
            OwnerModId = snapshot.OwnerModId ?? ""
        };
    }

}
