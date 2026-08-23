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
using AuraToolsExp.Dll.Modules;
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
    private const string FeastCardId = "AuraTools.Feast";
    private const int FoodsPerFrame = 8;
    private static MethodInfo? eatFoodMethod;
    private static bool batchQueued;
    private static bool batchEating;
    private static long actionSequence;
    private static string cachedRoleId = "";
    private static bool catalogLoaded;
    private static bool scopeSubscribed;
    private static IReadOnlyList<AuraCgCatalogResource> catalogResources = Array.Empty<AuraCgCatalogResource>();
    private static readonly HashSet<string> DiagnosticKeys = new(StringComparer.OrdinalIgnoreCase);

    public static void Initialize(ModConfig modConfig)
    {
        SkillCgArbiterRuntime.Initialize(modConfig, AuraToolsIds.ModId, new SkillCgArbiterOptions
        {
            MaxQueueLength = 4,
            MaxRequestAgeSeconds = 8f,
            DuplicateWindowSeconds = 1.25f
        });

        RegisterAfter(modConfig, "FoodItem.EatFood", OnFoodEaten);
        RegisterAfter(modConfig, "GameEntryUI.ChangeRole", CaptureCurrentRole);
        RegisterBefore(modConfig, "GameEntryUI.StartGame", CaptureCurrentRole);
        RegisterAfter(modConfig, "NormalMapManager.InitRoleTable", CaptureCurrentRole);
        RegisterAfter(modConfig, "SlotMachineManager.InitRoleTable", CaptureCurrentRole);
        RegisterAfter(modConfig, "SublimationManager.InitRoleTable", CaptureCurrentRole);
        RegisterAfter(modConfig, "TeachMapManager.InitRoleTable", CaptureCurrentRole);
        RegisterAfter(modConfig, "FightManager.CmdChangeCareer", CaptureCurrentRole);
        RegisterAfter(modConfig, "FightManager.RpcChangeCareer", CaptureCurrentRole);
        AuraToolsConfigService.SubscribeModule(
            AuraToolModuleIds.Feast,
            Reconfigure);
        AuraToolsConfigService.SubscribeModule(
            AuraToolModuleIds.FeastCg,
            Reconfigure);
        ApplyModuleActivation(
            AuraToolsConfigService.MatchExperience.Feast.Enabled);
    }

    internal static void ApplyModuleActivation(bool enabled)
    {
        if (!enabled)
        {
            if (scopeSubscribed)
            {
                AuraSharedResourceProtocol.ScopeChanged -=
                    OnSharedScopeChanged;
                scopeSubscribed = false;
            }
            batchQueued = false;
            batchEating = false;
            return;
        }

        if (!scopeSubscribed)
        {
            AuraSharedResourceProtocol.ScopeChanged += OnSharedScopeChanged;
            scopeSubscribed = true;
        }
        RefreshCatalog();
    }

    public static int RegisteredFeastCgCount()
    {
        return GetCatalogResources()
            .Count(resource => IsRoleResource(resource) && !IsToolProvidedDefault(resource));
    }

    public static IReadOnlyList<string> RegisteredRoleIds()
    {
        return GetCatalogResources()
            .Where(IsRoleResource)
            .Select(resource => RoleCatalog.NormalizeRoleId(resource.ScopeId))
            .Where(roleId => !string.IsNullOrWhiteSpace(roleId))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(roleId => roleId, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public static void RefreshCatalog()
    {
        try
        {
            var snapshot = AuraCgCatalogQueryService.QueryRegisteredResources(
                AuraToolsIds.ModId,
                "Feast");
            catalogResources = snapshot.Entries;
            catalogLoaded = true;
        }
        catch (Exception ex)
        {
            catalogResources = Array.Empty<AuraCgCatalogResource>();
            catalogLoaded = true;
            AuraToolsLog.Warn("[Feast] failed to query v4 catalog: " + ex.Message);
        }
    }

    public static IReadOnlyList<FeastCgCandidate> BuildCandidateCgsForRole(string roleId)
    {
        var normalizedRole = RoleCatalog.NormalizeRoleId(roleId);
        if (string.IsNullOrWhiteSpace(normalizedRole))
        {
            return Array.Empty<FeastCgCandidate>();
        }

        var resources = GetCatalogResources();
        var registered = resources
            .Where(IsRoleResource)
            .Where(resource => !IsToolProvidedDefault(resource))
            .Where(resource => RoleCatalog.MatchesRole(normalizedRole, resource.ScopeId, resource.ScopeAliases))
            .Where(resource => string.Equals(resource.MediaType, "image", StringComparison.OrdinalIgnoreCase))
            .Select(resource => CreateCatalogCandidate(resource,
                string.Equals(resource.OriginKind, AuraSharedOriginKinds.UserManual, StringComparison.Ordinal)
                    ? FeastCgSourceKind.Manual
                    : FeastCgSourceKind.Registered))
            .Where(candidate => !string.IsNullOrWhiteSpace(candidate.ImageResource))
            .ToList();

        var candidates = new List<FeastCgCandidate>(registered);
        if (registered.Count == 0)
        {
            var toolDefault = resources
                .Where(IsToolProvidedDefault)
                .Where(resource =>
                    string.Equals(resource.ScopeType, "Global", StringComparison.OrdinalIgnoreCase)
                    || IsRoleResource(resource)
                    && RoleCatalog.MatchesRole(normalizedRole, resource.ScopeId, resource.ScopeAliases))
                .Where(resource => string.Equals(resource.MediaType, "image", StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(resource => IsRoleResource(resource))
                .ThenByDescending(resource => resource.Priority)
                .ThenBy(resource => resource.ResourceId, StringComparer.OrdinalIgnoreCase)
                .FirstOrDefault();
            if (toolDefault != null)
            {
                candidates.Add(CreateCatalogCandidate(toolDefault, FeastCgSourceKind.Default));
            }
        }

        var settings = EnsureRoleSettings(normalizedRole, RoleCatalog.GetDisplayName(normalizedRole));
        var candidateIds = candidates.Select(candidate => candidate.QualifiedCgId).ToArray();
        if (settings.MigrateLegacyCandidateSelection(candidateIds))
        {
            SaveRoleSettings(settings);
        }

        return candidates
            .OrderByDescending(candidate => candidate.Priority)
            .ThenBy(candidate => candidate.SourceKind)
            .ThenBy(candidate => candidate.OwnerModId, StringComparer.OrdinalIgnoreCase)
            .ThenBy(candidate => candidate.CgId, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public static FeastRoleSettings EnsureRoleSettings(string roleId, string displayName = "")
    {
        var normalizedRole = RoleCatalog.NormalizeRoleId(roleId);
        var settings = AuraToolsConfigService.MatchExperience.Feast.Cg;
        settings.Normalize();
        if (!settings.Roles.TryGetValue(normalizedRole, out var roleSettings) || roleSettings == null)
        {
            roleSettings = new FeastRoleSettings
            {
                Enabled = true,
                RoleId = normalizedRole,
                DisplayName = string.IsNullOrWhiteSpace(displayName) ? normalizedRole : displayName,
                SelectionSchemaVersion = 2
            };
            settings.Roles[normalizedRole] = roleSettings;
        }
        else if (string.IsNullOrWhiteSpace(roleSettings.DisplayName))
        {
            roleSettings.DisplayName = string.IsNullOrWhiteSpace(displayName) ? normalizedRole : displayName;
        }

        roleSettings.Normalize(normalizedRole, settings.DefaultPresentation);
        ApplySharedSelection(roleSettings);
        return roleSettings;
    }

    public static void SetRoleEnabled(string roleId, bool enabled)
    {
        var role = EnsureRoleSettings(roleId, RoleCatalog.GetDisplayName(roleId));
        role.Enabled = enabled;
        SaveRoleSettings(role);
    }

    public static void SetSelectionModeForRole(string roleId, string selectionMode)
    {
        var role = EnsureRoleSettings(roleId, RoleCatalog.GetDisplayName(roleId));
        role.SelectionMode = AuraCgSelectionModes.Normalize(selectionMode);
        SaveRoleSettings(role);
    }

    public static void SetCandidateEnabledForRole(
        string roleId,
        string qualifiedCgId,
        bool enabled,
        IEnumerable<string> currentCandidates)
    {
        var role = EnsureRoleSettings(roleId, RoleCatalog.GetDisplayName(roleId));
        role.SetCandidateEnabled(qualifiedCgId, enabled, currentCandidates);
        SaveRoleSettings(role);
    }

    public static FeastCgCandidate? ResolveEffectiveCandidateForPreview(string roleId)
    {
        return ResolveEffectiveCandidate(RoleCatalog.NormalizeRoleId(roleId), Math.Max(1, actionSequence + 1), out _);
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

    private static void OnSharedScopeChanged(string scopeKey, long revision)
    {
        catalogLoaded = false;
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

            if (batchQueued || batchEating)
            {
                return;
            }

            batchQueued = true;
            if (!AuraSharedFrameScheduler.StartCoroutine(
                    "AuraTools.Feast.Batch",
                    EatRemainingFoodsNextFrame(context.Target)))
            {
                batchQueued = false;
            }
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
        if (!IsEnabled())
        {
            batchQueued = false;
            yield break;
        }
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
                if (!IsEnabled()) break;
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
        if (!force && !IsCgEffective())
        {
            return;
        }

        var normalizedRole = RoleCatalog.NormalizeRoleId(roleId);
        if (string.IsNullOrWhiteSpace(normalizedRole))
        {
            LogDiagnostic("no-role", "[Feast] skipped CG: role is unknown.");
            return;
        }

        var nextSequence = actionSequence + 1;
        var candidate = ResolveEffectiveCandidate(normalizedRole, nextSequence, out var presentation);
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
            TriggerKind = "feast",
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
            ActionSequence = nextSequence,
            DisableSync = true
        });
        actionSequence = nextSequence;
    }

    private static FeastCgCandidate? ResolveEffectiveCandidate(
        string roleId,
        long selectionSequence,
        out SkillCgPresentationSettings presentation)
    {
        var settings = AuraToolsConfigService.MatchExperience.Feast.Cg;
        settings.Normalize();
        var normalizedRole = RoleCatalog.NormalizeRoleId(roleId);
        var candidates = BuildCandidateCgsForRole(normalizedRole);
        settings.Roles.TryGetValue(normalizedRole, out var roleSettings);
        if (roleSettings != null)
        {
            roleSettings.Normalize(normalizedRole, settings.DefaultPresentation);
            ApplySharedSelection(roleSettings);
        }
        presentation = roleSettings?.EffectivePresentation ?? settings.DefaultPresentation;
        if (roleSettings != null && !roleSettings.Enabled)
        {
            return null;
        }

        var enabledCandidates = roleSettings == null
            ? candidates
            : candidates.Where(candidate => roleSettings.IsCandidateEnabled(candidate.QualifiedCgId)).ToList();
        var fallback = AuraCgCandidateSelector.Select(
            enabledCandidates,
            roleSettings?.SelectionMode ?? AuraCgSelectionModes.Priority,
            normalizedRole,
            selectionSequence);
        if (fallback != null && roleSettings == null)
        {
            presentation = fallback.Presentation;
        }

        return fallback;
    }

    private static void ApplySharedSelection(FeastRoleSettings role)
    {
        var document = AuraSharedResourceProtocol.ReadUserOverride(
            AuraToolsIds.ModId,
            FeastScope(role.RoleId));
        if (document.Revision <= 0 || document.Override == null)
        {
            return;
        }

        var local = document.Override;
        if (local.Enabled.HasValue)
        {
            role.Enabled = local.Enabled.Value;
        }
        role.SelectionMode = AuraCgSelectionModes.Normalize(local.SelectionMode);
        role.ResourceOverrides = new Dictionary<string, bool>(
            local.ResourceOverrides ?? new Dictionary<string, bool>(),
            StringComparer.OrdinalIgnoreCase);

        var values = local.Values ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (values.TryGetValue("manualResources", out var manualJson))
        {
            role.ManualResources = AuraSharedJson.Deserialize<List<FeastManualResourceSettings>>(manualJson)
                                   ?? new List<FeastManualResourceSettings>();
        }
        if (values.TryGetValue("candidateSelectionConfigured", out var configured)
            && bool.TryParse(configured, out var parsedConfigured))
        {
            role.CandidateSelectionConfigured = parsedConfigured;
        }
        if (values.TryGetValue("enabledCgIds", out var enabledJson))
        {
            role.EnabledCgIds = AuraSharedJson.Deserialize<List<string>>(enabledJson) ?? new List<string>();
        }
        role.Normalize(role.RoleId, AuraToolsConfigService.MatchExperience.Feast.Cg.DefaultPresentation);
    }

    public static void SaveRoleSettings(FeastRoleSettings role)
    {
        role.Normalize(role.RoleId, AuraToolsConfigService.MatchExperience.Feast.Cg.DefaultPresentation);
        AuraToolsConfigService.SaveFeastCg();
        var scope = FeastScope(role.RoleId);
        var current = AuraSharedResourceProtocol.ReadUserOverride(AuraToolsIds.ModId, scope);
        var local = CreateLocalOverride(role, current);
        var written = AuraSharedResourceProtocol.WriteUserOverride(
            AuraToolsIds.ModId,
            scope,
            local,
            current.Revision);
        if (written.Conflict)
        {
            current = AuraSharedResourceProtocol.ReadUserOverride(AuraToolsIds.ModId, scope);
            local = CreateLocalOverride(role, current);
            written = AuraSharedResourceProtocol.WriteUserOverride(
                AuraToolsIds.ModId,
                scope,
                local,
                current.Revision);
        }
        if (!written.Success)
        {
            AuraToolsLog.Warn("[Feast] failed to persist shared role selection for " + role.RoleId + ": " + written.Message);
        }
    }

    private static AuraSharedLocalOverrideV4 CreateLocalOverride(
        FeastRoleSettings role,
        AuraSharedUserOverrideDocumentV4 current)
    {
        var values = current.Override?.Values == null
            ? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, string>(current.Override.Values, StringComparer.OrdinalIgnoreCase);
        values["selectionMode"] = role.SelectionMode;
        values["resourceOverrides"] = AuraSharedJson.Serialize(role.ResourceOverrides);
        values["manualResources"] = AuraSharedJson.Serialize(role.ManualResources);
        values.Remove("candidateSelectionConfigured");
        values.Remove("enabledCgIds");
        return new AuraSharedLocalOverrideV4
        {
            Enabled = role.Enabled,
            SelectionMode = role.SelectionMode,
            ResourceOverrides = new Dictionary<string, bool>(role.ResourceOverrides, StringComparer.OrdinalIgnoreCase),
            Values = values
        };
    }

    private static AuraSharedScopeKey FeastScope(string roleId)
    {
        return new AuraSharedScopeKey
        {
            ModuleId = AuraSharedSystems.Cg,
            FeatureId = "Feast",
            ScopeType = "Role",
            ScopeId = RoleCatalog.NormalizeRoleId(roleId)
        };
    }

    private static IReadOnlyList<AuraCgCatalogResource> GetCatalogResources()
    {
        if (!catalogLoaded)
        {
            RefreshCatalog();
        }

        return catalogResources;
    }

    private static bool IsRoleResource(AuraCgCatalogResource resource)
    {
        return string.Equals(resource.ScopeType, "Role", StringComparison.OrdinalIgnoreCase)
               && !string.IsNullOrWhiteSpace(resource.ScopeId);
    }

    private static bool IsToolProvidedDefault(AuraCgCatalogResource resource)
    {
        return string.Equals(resource.OriginKind, AuraSharedOriginKinds.ToolDefault, StringComparison.Ordinal)
               && (IsRoleResource(resource)
                   || string.Equals(resource.ScopeType, "Global", StringComparison.OrdinalIgnoreCase)
                   && string.Equals(resource.ScopeId, "all", StringComparison.OrdinalIgnoreCase));
    }

    private static FeastCgCandidate CreateCatalogCandidate(
        AuraCgCatalogResource resource,
        FeastCgSourceKind sourceKind)
    {
        return new FeastCgCandidate
        {
            SourceKind = sourceKind,
            OwnerModId = resource.OwnerModId,
            CgId = resource.ResourceId,
            QualifiedCgId = resource.QualifiedResourceId,
            DisplayName = string.IsNullOrWhiteSpace(resource.DisplayName)
                ? resource.QualifiedResourceId
                : resource.DisplayName,
            ImageResource = resource.CanonicalResource,
            Priority = resource.Priority,
            Presentation = new SkillCgPresentationSettings
            {
                Mode = resource.Presentation.Mode,
                Fit = resource.Presentation.Fit,
                FadeIn = resource.Presentation.FadeIn,
                Hold = resource.Presentation.Hold,
                FadeOut = resource.Presentation.FadeOut,
                FocusX = resource.Presentation.FocusX,
                FocusY = resource.Presentation.FocusY,
                SafeScale = resource.Presentation.SafeScale
            }.Resolve(FeastSettings.CreateDefaultPresentation())
        };
    }

    private static FeastCgCandidate CreateManualCandidate(
        string roleId,
        FeastManualResourceSettings manual)
    {
        return new FeastCgCandidate
        {
            SourceKind = FeastCgSourceKind.Manual,
            OwnerModId = "LocalUser",
            CgId = manual.ManualId,
            QualifiedCgId = FeastRoleResourceIdentity.ManualId(roleId, manual.ManualId),
            DisplayName = manual.DisplayName,
            ImageResource = manual.Resource,
            Priority = manual.Priority,
            Presentation = FeastSettings.CreateDefaultPresentation()
        };
    }

    private static bool IsEnabled()
    {
        return AuraToolsConfigService.MatchExperience.Feast.Enabled;
    }

    public static bool IsCgEffective()
    {
        var settings = AuraToolsConfigService.MatchExperience.Feast;
        return settings.IsCgEffective;
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
    public FeastCgSourceKind SourceKind { get; set; }

    public string OwnerModId { get; set; } = "";

    public string CgId { get; set; } = "";

    public string QualifiedCgId { get; set; } = "";

    public string DisplayName { get; set; } = "";

    public string ImageResource { get; set; } = "";

    public int Priority { get; set; }

    public SkillCgPresentationSettings Presentation { get; set; } = FeastSettings.CreateDefaultPresentation();
}

public enum FeastCgSourceKind
{
    Registered = 0,
    Default = 1,
    Manual = 2
}
