using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using AuraCg.Shared;
using AuraShared.Core;
using AuraToolsExp.Dll.Config;
using AuraToolsExp.Dll.Infrastructure;
using UnityEngine;
using Witch;
using Witch.Core;
using Witch.Mod;
using Witch.UI.Window;
using Object = UnityEngine.Object;

namespace AuraToolsExp.Dll.Features.Feast;

public static class AuraToolsFeastRuntime
{
    public const string FeastKind = "feast";
    private const string DriverObjectName = "AuraTools.Feast.Driver";
    private const string FeastCardId = "AuraTools.Feast";
    private const int FoodsPerFrame = 8;
    private static AuraToolsFeastDriver? driver;
    private static MethodInfo? eatFoodMethod;
    private static bool batchQueued;
    private static bool batchEating;
    private static long actionSequence;
    private static string cachedRoleId = "";
    private static readonly HashSet<string> DiagnosticKeys = new(StringComparer.OrdinalIgnoreCase);

    public static void Initialize(ModConfig modConfig)
    {
        SkillCgArbiterRuntime.Initialize(modConfig, AuraToolsIds.ModId, new SkillCgArbiterOptions
        {
            MaxQueueLength = 4,
            MaxRequestAgeSeconds = 8f,
            DuplicateWindowSeconds = 1.25f
        });

        driver = EnsureDriver();
        RegisterAfter(modConfig, "FoodItem.EatFood", OnFoodEaten);
        RegisterAfter(modConfig, "GameEntryUI.ChangeRole", CaptureCurrentRole);
        RegisterBefore(modConfig, "GameEntryUI.StartGame", CaptureCurrentRole);
        RegisterAfter(modConfig, "NormalMapManager.InitRoleTable", CaptureCurrentRole);
        RegisterAfter(modConfig, "SlotMachineManager.InitRoleTable", CaptureCurrentRole);
        RegisterAfter(modConfig, "SublimationManager.InitRoleTable", CaptureCurrentRole);
        RegisterAfter(modConfig, "TeachMapManager.InitRoleTable", CaptureCurrentRole);
        RegisterAfter(modConfig, "FightManager.CmdChangeCareer", CaptureCurrentRole);
        RegisterAfter(modConfig, "FightManager.RpcChangeCareer", CaptureCurrentRole);
        AuraToolsConfigService.Changed += Reconfigure;
    }

    public static int RegisteredFeastCgCount()
    {
        return GetFeastEntries().Count;
    }

    public static IReadOnlyList<FeastCgCandidate> BuildCandidateCgsForRole(string roleId)
    {
        var normalizedRole = RoleCatalog.NormalizeRoleId(roleId);
        if (string.IsNullOrWhiteSpace(normalizedRole))
        {
            return Array.Empty<FeastCgCandidate>();
        }

        return GetFeastEntries()
            .Where(entry => TargetsRole(entry, normalizedRole))
            .Where(entry => AuraCgActivationRuntime.CanConsumerPlay(entry, AuraToolsIds.ModId))
            .Select(CreateCandidate)
            .Where(candidate => !string.IsNullOrWhiteSpace(candidate.ImageResource))
            .OrderByDescending(candidate => candidate.Priority)
            .ThenBy(candidate => candidate.OwnerModId, StringComparer.OrdinalIgnoreCase)
            .ThenBy(candidate => candidate.CgId, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public static FeastRoleSettings EnsureRoleSettings(string roleId, string displayName = "")
    {
        var normalizedRole = RoleCatalog.NormalizeRoleId(roleId);
        var settings = AuraToolsConfigService.MatchExperience.Feast;
        settings.Normalize();
        if (!settings.Roles.TryGetValue(normalizedRole, out var roleSettings) || roleSettings == null)
        {
            roleSettings = new FeastRoleSettings
            {
                Enabled = true,
                RoleId = normalizedRole,
                DisplayName = string.IsNullOrWhiteSpace(displayName) ? normalizedRole : displayName
            };
            settings.Roles[normalizedRole] = roleSettings;
        }
        else if (string.IsNullOrWhiteSpace(roleSettings.DisplayName))
        {
            roleSettings.DisplayName = string.IsNullOrWhiteSpace(displayName) ? normalizedRole : displayName;
        }

        roleSettings.Normalize(normalizedRole, settings.DefaultPresentation);
        return roleSettings;
    }

    public static void SetRoleEnabled(string roleId, bool enabled)
    {
        var role = EnsureRoleSettings(roleId, RoleCatalog.GetDisplayName(roleId));
        role.Enabled = enabled;
        AuraToolsConfigService.SaveMatchExperience();
    }

    public static void SelectCgForRole(string roleId, string qualifiedCgId)
    {
        var role = EnsureRoleSettings(roleId, RoleCatalog.GetDisplayName(roleId));
        role.SelectedCgId = (qualifiedCgId ?? "").Trim();
        AuraToolsConfigService.SaveMatchExperience();
    }

    public static void ClearSelectedCgForRole(string roleId)
    {
        var role = EnsureRoleSettings(roleId, RoleCatalog.GetDisplayName(roleId));
        role.SelectedCgId = "";
        AuraToolsConfigService.SaveMatchExperience();
    }

    public static string ConfiguredSelectedCgIdForRole(string roleId)
    {
        var normalizedRole = RoleCatalog.NormalizeRoleId(roleId);
        var settings = AuraToolsConfigService.MatchExperience.Feast;
        settings.Normalize();
        return settings.Roles.TryGetValue(normalizedRole, out var role) ? role.SelectedCgId : "";
    }

    public static FeastCgCandidate? ResolveEffectiveCandidateForPreview(string roleId)
    {
        return ResolveEffectiveCandidate(RoleCatalog.NormalizeRoleId(roleId), out _);
    }

    public static string ResolveCandidateImagePath(FeastCgCandidate? candidate)
    {
        if (candidate == null || string.IsNullOrWhiteSpace(candidate.ImageResource))
        {
            return "";
        }

        return SkillCgArbiterRuntime.ResolveImagePath(
            candidate.OwnerModId,
            candidate.ImageResource,
            AuraToolsConfigService.ResolveConfiguredPath(candidate.ImageResource));
    }

    public static void PreviewCurrentRole()
    {
        PlayFeastForRole(ResolveCurrentRoleId(), force: true);
    }

    public static void PreviewRole(string roleId)
    {
        PlayFeastForRole(roleId, force: true);
    }

    private static void Reconfigure()
    {
        SkillCgArbiterRuntime.Initialize(null, AuraToolsIds.ModId, new SkillCgArbiterOptions
        {
            MaxQueueLength = 4,
            MaxRequestAgeSeconds = 8f,
            DuplicateWindowSeconds = 1.25f
        });
    }

    private static void OnFoodEaten(ModHookContext context)
    {
        try
        {
            CaptureCurrentRole(context);
            if (!IsEnabled())
            {
                return;
            }

            if (driver == null || batchQueued || batchEating)
            {
                return;
            }

            batchQueued = true;
            driver.StartCoroutine(EatRemainingFoodsNextFrame(context.Target));
        }
        catch (Exception ex)
        {
            AuraToolsLog.Warn("[Feast] food hook failed: " + ex.Message);
            batchQueued = false;
        }
    }

    private static IEnumerator EatRemainingFoodsNextFrame(object? triggerFood)
    {
        yield return null;
        var eaten = 0;
        try
        {
            batchEating = true;
            var maxBatch = Math.Max(1, AuraToolsConfigService.MatchExperience.Feast.MaxBatchCount);
            var foods = Object.FindObjectsByType<FoodItem>(FindObjectsSortMode.None)
                .Where(food => food != null)
                .OrderBy(food => food.GetInstanceID())
                .ToList();
            var seen = new HashSet<int>();
            foreach (var food in foods)
            {
                if (food == null || ReferenceEquals(food, triggerFood))
                {
                    continue;
                }

                if (!seen.Add(food.GetInstanceID()))
                {
                    continue;
                }

                if (TryEatFood(food))
                {
                    eaten++;
                    if (eaten >= maxBatch)
                    {
                        break;
                    }

                    if (eaten % FoodsPerFrame == 0)
                    {
                        yield return null;
                    }
                }
            }
        }
        finally
        {
            batchEating = false;
            batchQueued = false;
        }

        if (eaten > 0)
        {
            LogDiagnostic("batch:" + eaten, "[Feast] ate remaining food count=" + eaten + ".");
        }

        PlayFeastForRole(ResolveCurrentRoleId(), force: false);
    }

    private static bool TryEatFood(FoodItem food)
    {
        try
        {
            eatFoodMethod ??= typeof(FoodItem).GetMethod(
                "EatFood",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (eatFoodMethod == null)
            {
                LogDiagnostic("eat-method-missing", "[Feast] FoodItem.EatFood method not found.");
                return false;
            }

            eatFoodMethod.Invoke(food, Array.Empty<object>());
            return true;
        }
        catch (Exception ex)
        {
            AuraToolsLog.Warn("[Feast] failed to eat food item: " + AuraSharedReflection.UnwrapMessage(ex));
            return false;
        }
    }

    private static void PlayFeastForRole(string roleId, bool force)
    {
        if (!force && !AuraToolsConfigService.MatchExperience.Feast.PlayCg)
        {
            return;
        }

        var normalizedRole = RoleCatalog.NormalizeRoleId(roleId);
        if (string.IsNullOrWhiteSpace(normalizedRole))
        {
            LogDiagnostic("no-role", "[Feast] skipped CG: role is unknown.");
            return;
        }

        var candidate = ResolveEffectiveCandidate(normalizedRole, out var presentation);
        if (candidate == null)
        {
            LogDiagnostic("no-candidate:" + normalizedRole, "[Feast] skipped CG: no registered feast CG for role=" + normalizedRole + ".");
            return;
        }

        var imagePath = ResolveCandidateImagePath(candidate);
        if (string.IsNullOrWhiteSpace(imagePath) || !File.Exists(imagePath))
        {
            AuraToolsLog.Warn("[Feast] CG image missing: " + candidate.ImageResource);
            return;
        }

        SkillCgArbiterRuntime.RequestCg(AuraToolsIds.ModId, new SkillCgRequest
        {
            ProviderId = AuraToolsIds.ModId + ".Feast." + SafeProviderSegment(candidate.OwnerModId) + "." + SafeProviderSegment(candidate.CgId),
            OwnerModId = AuraToolsIds.ModId,
            CardId = FeastCardId,
            OwnerInstanceId = normalizedRole,
            ImagePath = imagePath,
            ImageResource = candidate.ImageResource,
            Priority = candidate.Priority,
            FadeIn = presentation.FadeIn,
            Hold = presentation.Hold,
            FadeOut = presentation.FadeOut,
            PresentationMode = presentation.Mode,
            FitMode = presentation.Fit,
            FocusX = presentation.FocusX,
            FocusY = presentation.FocusY,
            SafeScale = presentation.SafeScale,
            CreatedAt = Time.unscaledTime,
            ActionSequence = ++actionSequence,
            DisableSync = true
        });
    }

    private static FeastCgCandidate? ResolveEffectiveCandidate(string roleId, out SkillCgPresentationSettings presentation)
    {
        var settings = AuraToolsConfigService.MatchExperience.Feast;
        settings.Normalize();
        var normalizedRole = RoleCatalog.NormalizeRoleId(roleId);
        var candidates = BuildCandidateCgsForRole(normalizedRole);
        settings.Roles.TryGetValue(normalizedRole, out var roleSettings);
        roleSettings?.Normalize(normalizedRole, settings.DefaultPresentation);
        presentation = roleSettings?.EffectivePresentation ?? settings.DefaultPresentation;
        if (roleSettings != null && !roleSettings.Enabled)
        {
            return null;
        }

        var selected = roleSettings?.SelectedCgId ?? "";
        if (!string.IsNullOrWhiteSpace(selected))
        {
            var match = candidates.FirstOrDefault(candidate =>
                string.Equals(candidate.QualifiedCgId, selected, StringComparison.OrdinalIgnoreCase)
                || string.Equals(candidate.CgId, selected, StringComparison.OrdinalIgnoreCase));
            if (match != null)
            {
                return match;
            }
        }

        var fallback = candidates.FirstOrDefault();
        if (fallback != null && roleSettings == null)
        {
            presentation = fallback.Presentation;
        }

        return fallback;
    }

    private static List<AuraCgRegistryEntry> GetFeastEntries()
    {
        try
        {
            return AuraCgRegistryRuntime.GetRegisteredEntries()
                .Where(entry => string.Equals(entry.Kind, FeastKind, StringComparison.OrdinalIgnoreCase))
                .Where(entry => string.Equals(entry.Media.Type, "image", StringComparison.OrdinalIgnoreCase))
                .ToList();
        }
        catch (Exception ex)
        {
            AuraToolsLog.Warn("[Feast] failed to read CG registry: " + ex.Message);
            return new List<AuraCgRegistryEntry>();
        }
    }

    private static FeastCgCandidate CreateCandidate(AuraCgRegistryEntry entry)
    {
        var image = string.IsNullOrWhiteSpace(entry.Media.Resource)
            ? entry.Media.FallbackImage
            : entry.Media.Resource;
        return new FeastCgCandidate
        {
            OwnerModId = entry.OwnerModId,
            CgId = entry.CgId,
            QualifiedCgId = entry.QualifiedCgId,
            DisplayName = string.IsNullOrWhiteSpace(entry.DisplayName) ? entry.QualifiedCgId : entry.DisplayName,
            ImageResource = image,
            Priority = entry.Priority,
            Presentation = new SkillCgPresentationSettings
            {
                Mode = entry.DefaultPresentation.Mode,
                Fit = entry.DefaultPresentation.Fit,
                FadeIn = entry.DefaultPresentation.FadeIn,
                Hold = entry.DefaultPresentation.Hold,
                FadeOut = entry.DefaultPresentation.FadeOut,
                FocusX = entry.DefaultPresentation.FocusX,
                FocusY = entry.DefaultPresentation.FocusY,
                SafeScale = entry.DefaultPresentation.SafeScale
            }.Resolve(FeastSettings.CreateDefaultPresentation())
        };
    }

    private static bool TargetsRole(AuraCgRegistryEntry entry, string roleId)
    {
        var targets = entry.TargetRoleIds ?? new List<string>();
        if (targets.Count == 0)
        {
            return false;
        }

        foreach (var target in targets)
        {
            if (string.Equals(target, "*", StringComparison.Ordinal)
                || string.Equals(RoleCatalog.NormalizeRoleId(target), roleId, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsEnabled()
    {
        return AuraToolsConfigService.Root.MatchExperience.Enabled
               && AuraToolsConfigService.MatchExperience.Feast.Enabled;
    }

    private static void CaptureCurrentRole(ModHookContext context)
    {
        try
        {
            var selected = AuraSharedIdentity.SelectRoleId(
                ExtractRoleId(context.Arguments),
                ExtractRoleId(context.Target),
                ExtractRoleId(RoleTable.Instance),
                ReadDataId(RoleTable.Instance?.Career),
                ReadDataId(GameEntryUI.career),
                cachedRoleId);
            selected = PreferRegisteredFeastRole(selected, cachedRoleId);
            if (!string.IsNullOrWhiteSpace(selected))
            {
                cachedRoleId = selected;
            }
        }
        catch
        {
        }
    }

    private static string ResolveCurrentRoleId()
    {
        var selected = AuraSharedIdentity.SelectRoleId(
            ExtractRoleId(RoleTable.Instance),
            ReadDataId(RoleTable.Instance?.Career),
            ReadDataId(GameEntryUI.career),
            cachedRoleId);
        selected = PreferRegisteredFeastRole(selected, cachedRoleId);
        if (!string.IsNullOrWhiteSpace(selected))
        {
            cachedRoleId = selected;
        }

        return selected;
    }

    private static string PreferRegisteredFeastRole(string selected, string fallback)
    {
        var normalized = RoleCatalog.NormalizeRoleId(selected);
        if (!string.IsNullOrWhiteSpace(normalized) && BuildCandidateCgsForRole(normalized).Count > 0)
        {
            return normalized;
        }

        var fallbackRole = RoleCatalog.NormalizeRoleId(fallback);
        if (!string.IsNullOrWhiteSpace(fallbackRole) && BuildCandidateCgsForRole(fallbackRole).Count > 0)
        {
            LogDiagnostic(
                "role-fallback:" + normalized + ":" + fallbackRole,
                "[Feast] preferred cached registered role over unresolved/default role: selected="
                + normalized
                + ", cached="
                + fallbackRole
                + ".");
            return fallbackRole;
        }

        return normalized;
    }

    private static string ExtractRoleId(object? value)
    {
        if (value == null)
        {
            return "";
        }

        if (value is string text)
        {
            return text;
        }

        if (value is IDataConfig dataConfig)
        {
            return ReadDataId(dataConfig);
        }

        if (value is IEnumerable enumerable && value is not string)
        {
            foreach (var item in enumerable)
            {
                var roleId = ExtractRoleId(item);
                if (!string.IsNullOrWhiteSpace(roleId))
                {
                    return roleId;
                }
            }
        }

        var direct = ReflectionUtil.ReadString(value, "RoleId", "roleId", "CareerId", "careerId", "Id", "id");
        if (!string.IsNullOrWhiteSpace(direct))
        {
            return direct;
        }

        return ExtractRoleId(ReflectionUtil.GetMemberValue(value, "Career")
                             ?? ReflectionUtil.GetMemberValue(value, "career")
                             ?? ReflectionUtil.GetMemberValue(value, "Role")
                             ?? ReflectionUtil.GetMemberValue(value, "role"));
    }

    private static string ReadDataId(IDataConfig? data)
    {
        try
        {
            if (data?.data != null && data.data.TryGetValue("Id", out var id))
            {
                return id ?? "";
            }

            return data?.InstanceID ?? "";
        }
        catch
        {
            return "";
        }
    }

    private static string SafeProviderSegment(string value)
    {
        var text = (value ?? "").Trim();
        if (string.IsNullOrWhiteSpace(text))
        {
            return "unknown";
        }

        var chars = text.Select(ch => char.IsLetterOrDigit(ch) || ch == '_' || ch == '-' ? ch : '_').ToArray();
        return new string(chars);
    }

    private static void RegisterAfter(ModConfig modConfig, string target, Action<ModHookContext> action)
    {
        AuraToolsHookRegistry.After(modConfig, target, action, "Feast");
    }

    private static void RegisterBefore(ModConfig modConfig, string target, Action<ModHookContext> action)
    {
        AuraToolsHookRegistry.Before(modConfig, target, action, "Feast");
    }

    private static AuraToolsFeastDriver EnsureDriver()
    {
        var existing = GameObject.Find(DriverObjectName);
        if (existing != null)
        {
            var component = existing.GetComponent<AuraToolsFeastDriver>();
            if (component != null)
            {
                return component;
            }
        }

        var gameObject = new GameObject(DriverObjectName);
        Object.DontDestroyOnLoad(gameObject);
        return gameObject.AddComponent<AuraToolsFeastDriver>();
    }

    private static void LogDiagnostic(string key, string message)
    {
        if (DiagnosticKeys.Add(key))
        {
            AuraToolsLog.Debug(message);
        }
    }
}

public sealed class FeastCgCandidate
{
    public string OwnerModId { get; set; } = "";

    public string CgId { get; set; } = "";

    public string QualifiedCgId { get; set; } = "";

    public string DisplayName { get; set; } = "";

    public string ImageResource { get; set; } = "";

    public int Priority { get; set; }

    public SkillCgPresentationSettings Presentation { get; set; } = FeastSettings.CreateDefaultPresentation();
}

public sealed class AuraToolsFeastDriver : MonoBehaviour
{
}
