using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using AuraCg.Shared;
using AuraShared.Core;
using AuraToolsExp.Dll.Config;
using AuraToolsExp.Dll.Infrastructure;
using UnityEngine;
using UnityEngine.UI;
using Witch;
using Witch.Core;
using Witch.Mod;
using Witch.UI.Window;
using Settings = AuraToolsExp.Dll.Features.Settings;

namespace AuraToolsExp.Dll.Features.SkillCg;

public static class AuraToolsSkillCgRuntime
{
    private static readonly List<IDisposable> HookRegistrations = new();
    private static readonly HashSet<string> DiagnosticKeys = new(StringComparer.OrdinalIgnoreCase);
    private static ModConfig? modConfig;
    private static bool hooksRegistered;
    private static int hookFailureCount;
    private static bool safeModeDisabled;
    private static int adventurePreloadSequence;

    public static void Initialize(ModConfig modConfig)
    {
        AuraToolsSkillCgRuntime.modConfig = modConfig;
        SkillCgArbiterRuntime.Initialize(modConfig, AuraToolsIds.ModId, new SkillCgArbiterOptions
        {
            MaxQueueLength = AuraToolsConfigService.SkillCg.MaxQueueLength,
            MaxRequestAgeSeconds = AuraToolsConfigService.SkillCg.MaxRequestAgeSeconds,
            DuplicateWindowSeconds = AuraToolsConfigService.SkillCg.DuplicateWindowSeconds
        });
        SkillCgArbiterRuntime.RegisterProvider(modConfig, AuraToolsIds.ModId, new AuraToolsSkillCgProvider());

        AuraToolsConfigService.Changed += Reconfigure;
        EnsureHooksMatchConfig();
    }

    private static void Reconfigure()
    {
        hookFailureCount = 0;
        safeModeDisabled = false;
        AuraToolsSkillCgProvider.ClearPathCache();
        SkillCgArbiterRuntime.Initialize(null, AuraToolsIds.ModId, new SkillCgArbiterOptions
        {
            MaxQueueLength = AuraToolsConfigService.SkillCg.MaxQueueLength,
            MaxRequestAgeSeconds = AuraToolsConfigService.SkillCg.MaxRequestAgeSeconds,
            DuplicateWindowSeconds = AuraToolsConfigService.SkillCg.DuplicateWindowSeconds
        });
        EnsureHooksMatchConfig();
    }

    private static bool HooksEnabled => AuraToolsConfigService.Root.SkillCg.Enabled
                                        && !safeModeDisabled
                                        && (AuraToolsConfigService.SkillCg.Enabled
                                            || AuraToolsConfigService.SkillCg.CardUseCg.Enabled);

    private static void EnsureHooksMatchConfig()
    {
        if (HooksEnabled)
        {
            EnsureHooksRegistered();
            return;
        }

        ReleaseHooks();
    }

    private static void EnsureHooksRegistered()
    {
        if (hooksRegistered || modConfig == null)
        {
            return;
        }

        HookRegistrations.Add(AuraCombatActionRouter.RegisterBefore(
            modConfig,
            AuraToolsIds.ModId + ".SkillCG",
            BeforeCombatAction,
            warn: AuraToolsLog.Warn));
        RegisterBefore("GameEntryUI.StartGame", OnAdventureStart);
        RegisterAfter("Fight_Start.Init", OnFightStart);
        RegisterAfter("FightInit.Init", OnFightStart);
        RegisterBefore("Fight_Win.ResetStates", OnFightEnding);
        RegisterBefore("Fight_Escape.ResetStates", OnFightEnding);
        RegisterBefore("Fight_Loss.Init", OnFightEnding);
        RegisterAfter("Fight_Win.ResetStates", OnFightEnded);
        RegisterAfter("Fight_Escape.ResetStates", OnFightEnded);
        RegisterAfter("Fight_Loss.Init", OnFightEnded);
        hooksRegistered = true;
        AuraToolsLog.Info("[SkillCG] routed hooks enabled.");
    }

    private static void ReleaseHooks()
    {
        if (!hooksRegistered && HookRegistrations.Count == 0)
        {
            return;
        }

        for (var i = HookRegistrations.Count - 1; i >= 0; i--)
        {
            try
            {
                HookRegistrations[i].Dispose();
            }
            catch
            {
            }
        }

        HookRegistrations.Clear();
        hooksRegistered = false;
        SkillCgArbiterRuntime.Clear(AuraToolsIds.ModId, "disabled");
        AuraToolsSkillCgProvider.ClearOwnerRoles();
        AuraToolsLog.Info("[SkillCG] routed hooks disabled.");
    }

    private static void BeforeCombatAction(AuraCombatActionContext context)
    {
        RunHook("card action", () =>
        {
            if (!AuraToolsConfigService.Root.SkillCg.Enabled
                || (!AuraToolsConfigService.SkillCg.Enabled && !AuraToolsConfigService.SkillCg.CardUseCg.Enabled)
                || !context.IsCardAction)
            {
                return;
            }

            var trigger = BuildTriggerContext(context);
            if (trigger == null || !ShouldEmitLocalRequest(trigger))
            {
                return;
            }

            SkillCgArbiterRuntime.Trigger(AuraToolsConfigService.SkillCg, AuraToolsIds.ModId, trigger);
        });
    }

    private static SkillCgTriggerContext? BuildTriggerContext(AuraCombatActionContext context)
    {
        if (!context.IsCardAction || string.IsNullOrWhiteSpace(context.CardId))
        {
            return null;
        }

        AuraToolsSkillCgProvider.RememberOwnerRole(context.OwnerInstanceId, context.OwnerRoleId);
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

    private static bool ShouldEmitLocalRequest(SkillCgTriggerContext trigger)
    {
        if (PlayerManager.Instance == null)
        {
            return true;
        }

        var localStatusId = FightPlayer.Instance?.Status?.InstanceId ?? "";
        if (string.IsNullOrWhiteSpace(trigger.OwnerInstanceId)
            || string.IsNullOrWhiteSpace(localStatusId)
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

    internal static string ReadCurrentCareerId()
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

    private static void OnFightStart(ModHookContext context)
    {
        RunHook("fight start", () =>
        {
            AuraToolsSkillCgProvider.ClearOwnerRoles();
            SkillCgArbiterRuntime.Clear(AuraToolsIds.ModId, "fight start");
        });
    }

    private static void OnAdventureStart(ModHookContext context)
    {
        RunHook("adventure preload", PreloadAdventureCg);
    }

    private static void PreloadAdventureCg()
    {
        if (!AuraToolsConfigService.Root.SkillCg.Enabled)
        {
            return;
        }

        var key = "AuraToolsExp.Adventure." + (++adventurePreloadSequence).ToString();
        if (AuraToolsConfigService.SkillCg.Enabled)
        {
            SkillCgArbiterRuntime.EnsureAdventurePreloaded(
                AuraToolsIds.ModId,
                "",
                key + ".registered",
                new[] { SkillCgArbiterRuntime.SkillCgKind, SkillCgArbiterRuntime.FeastCgKind });
            SkillCgArbiterRuntime.PreloadCg(AuraToolsIds.ModId, AuraToolsSkillCgProvider.BuildConfiguredPreloadRequests());
        }

        if (AuraToolsConfigService.SkillCg.CardUseCg.Enabled)
        {
            SkillCgArbiterRuntime.EnsureAdventurePreloaded(
                AuraToolsIds.ModId,
                "",
                key + ".card-use",
                new[] { SkillCgArbiterRuntime.CardUseCgKind });
        }
    }

    private static void OnFightEnded(ModHookContext context)
    {
        RunHook("fight ended", () =>
        {
            SkillCgArbiterRuntime.Clear(AuraToolsIds.ModId, "fight ended");
            AuraToolsSkillCgProvider.ClearOwnerRoles();
        });
    }

    private static void OnFightEnding(ModHookContext context)
    {
        RunHook("fight ending", () =>
        {
            SkillCgArbiterRuntime.Clear(AuraToolsIds.ModId, "fight ending");
            AuraToolsSkillCgProvider.ClearOwnerRoles();
        });
    }

    private static void RegisterBefore(string target, Action<ModHookContext> action)
    {
        if (modConfig == null)
        {
            return;
        }

        HookRegistrations.Add(AuraSharedHooks.RegisterBeforeRouted(
            modConfig,
            target,
            action,
            warn: AuraToolsLog.Warn,
            safeInvoke: true));
    }

    private static void RegisterAfter(string target, Action<ModHookContext> action)
    {
        if (modConfig == null)
        {
            return;
        }

        HookRegistrations.Add(AuraSharedHooks.RegisterAfterRouted(
            modConfig,
            target,
            action,
            warn: AuraToolsLog.Warn,
            safeInvoke: true));
    }

    private static void RunHook(string source, Action action)
    {
        if (safeModeDisabled)
        {
            return;
        }

        try
        {
            action();
            hookFailureCount = 0;
        }
        catch (Exception ex)
        {
            hookFailureCount++;
            var maxFailures = AuraToolsConfigService.SkillCg.MaxHookFailures;
            AuraToolsLog.Warn("[SkillCG] hook failed. source=" + source
                              + ", failures=" + hookFailureCount + "/" + maxFailures
                              + ", error=" + ex.Message);
            if (!AuraToolsConfigService.SkillCg.DisableAfterFailures || hookFailureCount < maxFailures)
            {
                return;
            }

            safeModeDisabled = true;
            SkillCgArbiterRuntime.Clear(AuraToolsIds.ModId, "safe-mode-disabled");
            AuraToolsSkillCgProvider.ClearOwnerRoles();
            AuraToolsLog.Warn("[SkillCG] safe mode disabled hooks after repeated failures. source="
                              + source + ", failures=" + hookFailureCount + ".");
        }
    }

    internal static void LogDiagnostic(string key, string message)
    {
        if (DiagnosticKeys.Add(key))
        {
            AuraToolsLog.Debug(message);
        }
    }
}

public sealed class AuraToolsSkillCgProvider
{
    private const float ImagePathCacheSeconds = 2f;
    private static readonly Dictionary<string, string> OwnerRoleIds = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<string, CachedImagePath> ImagePathCache = new(StringComparer.OrdinalIgnoreCase);

    public string ProviderId => AuraToolsIds.ModId + ".SkillCG.Provider";

    public string OwnerModId => AuraToolsIds.ModId;

    public int Priority => 0;

    public static void RememberOwnerRole(string ownerInstanceId, string roleId)
    {
        var normalized = AuraSharedIdentity.SelectRoleId(roleId);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return;
        }

        if (!string.IsNullOrWhiteSpace(ownerInstanceId))
        {
            OwnerRoleIds[ownerInstanceId] = normalized;
        }
    }

    public static void ClearOwnerRoles()
    {
        OwnerRoleIds.Clear();
    }

    public static void ClearPathCache()
    {
        ImagePathCache.Clear();
    }

    public static IReadOnlyList<SkillCgRequest> BuildConfiguredPreloadRequests()
    {
        var requests = new List<SkillCgRequest>();
        foreach (var role in AuraToolsConfigService.SkillCg.Roles.Values)
        {
            if (role == null || !role.Enabled)
            {
                continue;
            }

            foreach (var rule in role.Rules)
            {
                if (rule == null || !rule.Enabled || string.IsNullOrWhiteSpace(rule.Image))
                {
                    continue;
                }

                var imagePath = AuraToolsConfigService.ResolveConfiguredPath(rule.Image);
                if (!File.Exists(imagePath))
                {
                    AuraToolsLog.Warn("[SkillCG] preload image missing: " + rule.Image);
                    continue;
                }

                var presentation = rule.EffectivePresentation;
                requests.Add(new SkillCgRequest
                {
                    ProviderId = string.IsNullOrWhiteSpace(rule.ProviderId)
                        ? AuraToolsIds.ModId + ".SkillCG." + role.RoleId + "." + (string.IsNullOrWhiteSpace(rule.CardId) ? "*" : rule.CardId)
                        : rule.ProviderId,
                    OwnerModId = AuraToolsIds.ModId,
                    CardId = string.IsNullOrWhiteSpace(rule.CardId) ? "*" : rule.CardId,
                    ImagePath = imagePath,
                    ImageResource = rule.Image,
                    Priority = rule.Priority,
                    FadeIn = presentation.FadeIn,
                    Hold = presentation.Hold,
                    FadeOut = presentation.FadeOut,
                    PresentationMode = presentation.Mode,
                    FitMode = presentation.Fit,
                    FocusX = presentation.FocusX,
                    FocusY = presentation.FocusY,
                    SafeScale = presentation.SafeScale,
                    CreatedAt = Time.unscaledTime,
                    DisableSync = true
                });
            }
        }

        return requests;
    }

    public IEnumerable<SkillCgRequest> BuildRequests(object context)
    {
        if (context is not SkillCgTriggerContext trigger)
        {
            yield break;
        }

        if (!AuraToolsConfigService.Root.SkillCg.Enabled
            || (!AuraToolsConfigService.SkillCg.Enabled && !AuraToolsConfigService.SkillCg.CardUseCg.Enabled))
        {
            yield break;
        }

        var roleId = ResolveRoleId(trigger);
        var emitted = false;
        if (AuraToolsConfigService.SkillCg.CardUseCg.Enabled)
        {
            foreach (var request in SkillCgArbiterRuntime.BuildRegisteredCardUseRequests(
                         AuraToolsIds.ModId,
                         trigger,
                         disableSync: !AuraToolsConfigService.SkillCg.SyncRemote))
            {
                emitted = true;
                yield return request;
            }
        }

        if (!AuraToolsConfigService.SkillCg.Enabled)
        {
            yield break;
        }

        foreach (var role in MatchingRoles(roleId))
        {
            foreach (var rule in role.Rules)
            {
                if (!RuleMatches(rule, trigger))
                {
                    continue;
                }

                if (!string.IsNullOrWhiteSpace(rule.SourceOwnerModId)
                    && !string.IsNullOrWhiteSpace(rule.SourceCgId)
                    && !AuraCgActivationRuntime.CanConsumerPlay(rule.SourceOwnerModId, rule.SourceCgId, AuraToolsIds.ModId))
                {
                    AuraToolsSkillCgRuntime.LogDiagnostic(
                        "activation-skip:" + rule.SourceOwnerModId + ":" + rule.SourceCgId,
                        "[SkillCG] registered CG skipped by activation: source="
                        + rule.SourceOwnerModId + ":" + rule.SourceCgId
                        + ", consumer=" + AuraToolsIds.ModId
                        + ", role=" + roleId
                        + ", card=" + trigger.CardId);
                    continue;
                }

                var imagePath = AuraToolsConfigService.ResolveConfiguredPath(rule.Image);
                if (!ImageExists(rule.Image, imagePath))
                {
                    AuraToolsLog.Warn("[SkillCG] image missing: " + rule.Image);
                    continue;
                }

                var requestCardId = trigger.CardId;
                var presentation = rule.EffectivePresentation;
                emitted = true;
                AuraToolsSkillCgRuntime.LogDiagnostic(
                    "match:" + role.RoleId + ":" + rule.ProviderId + ":" + requestCardId,
                    "[SkillCG] matched rule: provider="
                    + (string.IsNullOrWhiteSpace(rule.ProviderId) ? "<auto>" : rule.ProviderId)
                    + ", role=" + role.RoleId
                    + ", card=" + requestCardId
                    + ", image=" + rule.Image);
                yield return new SkillCgRequest
                {
                    ProviderId = string.IsNullOrWhiteSpace(rule.ProviderId)
                        ? AuraToolsIds.ModId + ".SkillCG." + role.RoleId + "." + requestCardId
                        : rule.ProviderId,
                    OwnerModId = AuraToolsIds.ModId,
                    CardId = requestCardId,
                    OwnerInstanceId = trigger.OwnerInstanceId,
                    ImagePath = imagePath,
                    ImageResource = rule.Image,
                    Priority = rule.Priority,
                    FadeIn = presentation.FadeIn,
                    Hold = presentation.Hold,
                    FadeOut = presentation.FadeOut,
                    PresentationMode = presentation.Mode,
                    FitMode = presentation.Fit,
                    FocusX = presentation.FocusX,
                    FocusY = presentation.FocusY,
                    SafeScale = presentation.SafeScale,
                    CreatedAt = Time.unscaledTime,
                    ActionSequence = trigger.ActionSequence,
                    EventToken = trigger.EventToken,
                    DisableSync = !AuraToolsConfigService.SkillCg.SyncRemote
                };
            }
        }

        if (!emitted)
        {
            AuraToolsSkillCgRuntime.LogDiagnostic(
                "no-match:" + roleId + ":" + trigger.CardId,
                "[SkillCG] no AuraTools rule emitted: role=" + roleId + ", card=" + trigger.CardId);
        }
    }

    private static IEnumerable<SkillCgRoleSettings> MatchingRoles(string roleId)
    {
        var normalizedRoleId = RoleCatalog.NormalizeRoleId(roleId);
        foreach (var pair in AuraToolsConfigService.SkillCg.Roles)
        {
            var role = pair.Value;
            if (role == null || !role.Enabled)
            {
                continue;
            }

            if (string.Equals(role.RoleId, "*", StringComparison.Ordinal)
                || string.Equals(pair.Key, "*", StringComparison.Ordinal)
                || string.Equals(RoleCatalog.NormalizeRoleId(role.RoleId), normalizedRoleId, StringComparison.OrdinalIgnoreCase)
                || string.Equals(RoleCatalog.NormalizeRoleId(pair.Key), normalizedRoleId, StringComparison.OrdinalIgnoreCase))
            {
                yield return role;
            }
        }
    }

    private static string ResolveRoleId(SkillCgTriggerContext trigger)
    {
        var triggerRole = AuraSharedIdentity.SelectRoleId(trigger.OwnerRoleId);
        if (!string.IsNullOrWhiteSpace(triggerRole))
        {
            return triggerRole;
        }

        if (!string.IsNullOrWhiteSpace(trigger.OwnerInstanceId)
            && OwnerRoleIds.TryGetValue(trigger.OwnerInstanceId, out var roleId)
            && !string.IsNullOrWhiteSpace(roleId))
        {
            return AuraSharedIdentity.SelectRoleId(roleId);
        }

        return AuraSharedIdentity.SelectRoleId(AuraToolsSkillCgRuntime.ReadCurrentCareerId());
    }

    private static bool RuleMatches(SkillCgRuleSettings rule, SkillCgTriggerContext trigger)
    {
        if (!rule.Enabled || string.IsNullOrWhiteSpace(rule.Image))
        {
            return false;
        }

        return Matches(rule.CardId, trigger.CardId)
               && Matches(rule.Action, trigger.Action);
    }

    private static bool Matches(string pattern, string value)
    {
        var normalizedPattern = (pattern ?? "").Trim();
        var normalizedValue = (value ?? "").Trim();
        return string.Equals(normalizedPattern, "*", StringComparison.Ordinal)
               || string.Equals(normalizedPattern, normalizedValue, StringComparison.OrdinalIgnoreCase)
               || string.Equals(normalizedPattern.TrimStart('*'), normalizedValue.TrimStart('*'), StringComparison.OrdinalIgnoreCase);
    }

    private static bool ImageExists(string resource, string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        var key = (resource ?? "") + "|" + path;
        var now = Time.unscaledTime;
        if (ImagePathCache.TryGetValue(key, out var cached) && now <= cached.ExpiresAt)
        {
            return cached.Exists;
        }

        var exists = File.Exists(path);
        ImagePathCache[key] = new CachedImagePath(exists, now + ImagePathCacheSeconds);
        if (ImagePathCache.Count > 256)
        {
            foreach (var staleKey in ImagePathCache
                         .Where(pair => now > pair.Value.ExpiresAt)
                         .Select(pair => pair.Key)
                         .ToList())
            {
                ImagePathCache.Remove(staleKey);
            }
        }

        return exists;
    }

    private readonly struct CachedImagePath
    {
        public CachedImagePath(bool exists, float expiresAt)
        {
            Exists = exists;
            ExpiresAt = expiresAt;
        }

        public bool Exists { get; }

        public float ExpiresAt { get; }
    }
}

public static class AuraToolsSkillCgEditor
{
    private const float SkillCgRuleBlockHeight = 166f;
    private static Transform? roleContent;
    private static Text? hintText;

    public static void Show(Transform parent)
    {
        var window = Settings.AuraToolsUi.CreateOverlay("AuraTools.SkillCgEditor", parent, "技能CG配置", RefreshAndSave);
        var toolbar = Settings.AuraToolsUi.CreateLayout("Toolbar", window.transform);
        Settings.AuraToolsUi.SetFixedHeight(toolbar, Settings.AuraToolsUi.ToolbarHeight);
        var toolbarLayout = toolbar.AddComponent<HorizontalLayoutGroup>();
        toolbarLayout.spacing = 10f;
        toolbarLayout.childControlWidth = true;
        toolbarLayout.childControlHeight = true;
        toolbarLayout.childForceExpandWidth = false;
        toolbarLayout.childForceExpandHeight = false;
        hintText = Settings.AuraToolsUi.AddText(toolbar.transform, "提示：图片会复制到 ModsData/AuraShared/CG/Roles/{角色ID}/ 下。", 14, TextAnchor.MiddleLeft, Settings.AuraToolsUi.MutedText, 34f, 1f);
        Settings.AuraToolsUi.AddButton(toolbar.transform, "扫描角色", () => RefreshRoles(true), 92f);
        Settings.AuraToolsUi.AddButton(toolbar.transform, "保存", RefreshAndSave, 78f);

        roleContent = Settings.AuraToolsUi.CreateScroll(window.transform, "SkillCgRoles");
        RefreshRoles(false);
    }

    private static void RefreshRoles(bool forceScan)
    {
        EnsureRoleEntries(forceScan);
        RefreshRows();
    }

    private static void EnsureRoleEntries(bool forceScan)
    {
        foreach (var role in RoleCatalog.GetRoles(forceScan))
        {
            if (AuraToolsConfigService.SkillCg.Roles.ContainsKey(role.Id))
            {
                var existing = AuraToolsConfigService.SkillCg.Roles[role.Id];
                if (string.IsNullOrWhiteSpace(existing.DisplayName)
                    || IsRuleDisplayName(existing.DisplayName, existing.Rules))
                {
                    existing.DisplayName = role.DisplayName;
                }

                continue;
            }

            AuraToolsConfigService.SkillCg.Roles[role.Id] = new SkillCgRoleSettings
            {
                Enabled = false,
                RoleId = role.Id,
                DisplayName = role.DisplayName
            };
        }
    }

    private static void RefreshRows()
    {
        if (roleContent == null)
        {
            return;
        }

        Settings.AuraToolsUi.ClearChildren(roleContent);
        foreach (var pair in AuraToolsConfigService.SkillCg.Roles.OrderBy(pair => RoleDisplayName(pair.Value)).ThenBy(pair => pair.Key))
        {
            CreateRoleRow(pair.Key, pair.Value);
        }
    }

    private static void CreateRoleRow(string key, SkillCgRoleSettings role)
    {
        var box = Settings.AuraToolsUi.CreateLayout("Role-" + key, roleContent!);
        Settings.AuraToolsUi.AddPanelImage(box, Settings.AuraToolsUi.Panel);
        var boxElement = Settings.AuraToolsUi.EnsureLayoutElement(box);
        boxElement.minHeight = 112f;
        boxElement.flexibleHeight = 0f;
        var layout = box.AddComponent<VerticalLayoutGroup>();
        layout.padding = new RectOffset(10, 10, 8, 8);
        layout.spacing = 6f;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;

        var header = Settings.AuraToolsUi.CreateLayout("Header", box.transform);
        Settings.AuraToolsUi.SetFixedHeight(header, Settings.AuraToolsUi.ModuleHeaderHeight);
        var headerLayout = header.AddComponent<HorizontalLayoutGroup>();
        headerLayout.spacing = 8f;
        headerLayout.childControlWidth = true;
        headerLayout.childControlHeight = true;
        headerLayout.childForceExpandWidth = false;
        headerLayout.childForceExpandHeight = false;
        Settings.AuraToolsUi.AddToggle(header.transform, role.Enabled, value => role.Enabled = value);
        Settings.AuraToolsUi.AddText(header.transform, RoleDisplayName(role), Settings.AuraToolsUi.ModuleTitleFontSize, TextAnchor.MiddleLeft, Settings.AuraToolsUi.Accent, Settings.AuraToolsUi.TextMinHeight, 1f);
        Settings.AuraToolsUi.AddButton(header.transform, "本地目录", () => FileResourceUtil.OpenDirectory(FileResourceUtil.RoleSkillCgDirectory(role.RoleId)), 92f, 30f);
        Settings.AuraToolsUi.AddButton(header.transform, "添加规则", () =>
        {
            var dir = FileResourceUtil.RoleSkillCgDirectory(role.RoleId);
            var relative = AuraToolsConfigService.ToDataRelativePath(Path.Combine(dir, "skill_cg_" + (role.Rules.Count + 1) + ".png"));
            var defaultSkill = DefaultActiveSkillForNewRule(role);
            role.Rules.Add(new SkillCgRuleSettings
            {
                Enabled = true,
                CardId = defaultSkill,
                Action = "*",
                Image = relative,
                ProviderId = AuraToolsIds.ModId + ".SkillCG." + FileResourceUtil.SafeFolderName(role.RoleId) + "." + (role.Rules.Count + 1)
            });
            RefreshRows();
        }, 92f, 30f);

        foreach (var rule in role.Rules)
        {
            CreateRuleBlock(box.transform, role, rule);
        }
    }

    private static string RoleDisplayName(SkillCgRoleSettings role)
    {
        var displayName = string.IsNullOrWhiteSpace(role.DisplayName)
            ? RoleCatalog.GetDisplayName(role.RoleId)
            : role.DisplayName.Trim();
        var catalogName = RoleCatalog.GetDisplayName(role.RoleId);
        if (!string.IsNullOrWhiteSpace(catalogName)
            && !string.Equals(catalogName, role.RoleId, StringComparison.OrdinalIgnoreCase))
        {
            return catalogName;
        }

        return string.IsNullOrWhiteSpace(displayName) ? role.RoleId : displayName;
    }

    private static bool IsRuleDisplayName(string displayName, IEnumerable<SkillCgRuleSettings>? rules)
    {
        var value = displayName?.Trim() ?? "";
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        return (rules ?? Enumerable.Empty<SkillCgRuleSettings>())
            .Any(rule => string.Equals(rule.DisplayName, value, StringComparison.OrdinalIgnoreCase));
    }

    private static void CreateRuleBlock(Transform parent, SkillCgRoleSettings role, SkillCgRuleSettings rule)
    {
        rule.Action = "*";
        rule.Presentation ??= SkillCgPresentationSettings.CreateInherited();
        var block = Settings.AuraToolsUi.CreateLayout("RuleBlock-" + rule.ProviderId, parent);
        Settings.AuraToolsUi.SetFixedHeight(block, SkillCgRuleBlockHeight);
        Settings.AuraToolsUi.AddImage(block, Settings.AuraToolsUi.Row);
        var blockLayout = block.AddComponent<VerticalLayoutGroup>();
        blockLayout.padding = new RectOffset(8, 8, 5, 5);
        blockLayout.spacing = 6f;
        blockLayout.childControlWidth = true;
        blockLayout.childControlHeight = true;
        blockLayout.childForceExpandWidth = true;
        blockLayout.childForceExpandHeight = false;

        var top = Settings.AuraToolsUi.CreateLayout("RuleTop", block.transform);
        Settings.AuraToolsUi.SetFixedHeight(top, Settings.AuraToolsUi.ButtonHeight);
        var topLayout = top.AddComponent<HorizontalLayoutGroup>();
        topLayout.spacing = 8f;
        topLayout.childControlWidth = true;
        topLayout.childControlHeight = true;
        topLayout.childForceExpandWidth = false;
        topLayout.childForceExpandHeight = false;

        Settings.AuraToolsUi.AddToggle(top.transform, rule.Enabled, value => rule.Enabled = value);
        Settings.AuraToolsUi.AddText(top.transform, RuleDisplayName(rule), Settings.AuraToolsUi.HintFontSize, TextAnchor.MiddleLeft, Settings.AuraToolsUi.Text, Settings.AuraToolsUi.TextMinHeight, 1f, 150f);
        Settings.AuraToolsUi.AddText(top.transform, "\u6280\u80fd", Settings.AuraToolsUi.HintFontSize, TextAnchor.MiddleCenter, Settings.AuraToolsUi.MutedText, Settings.AuraToolsUi.TextMinHeight, 0f, 42f);
        var skillOptions = BuildSkillOptions(role, rule);
        Settings.AuraToolsUi.AddSelectButton(top.transform, skillOptions.Select(option => option.Label).ToList(), SelectedOptionIndex(skillOptions, rule.CardId), index =>
        {
            if (index >= 0 && index < skillOptions.Count)
            {
                rule.CardId = skillOptions[index].Id;
                rule.Action = "*";
            }
        }, 300f);
        Settings.AuraToolsUi.AddText(top.transform, "\u4f18\u5148", Settings.AuraToolsUi.HintFontSize, TextAnchor.MiddleCenter, Settings.AuraToolsUi.MutedText, Settings.AuraToolsUi.TextMinHeight, 0f, 52f);
        Settings.AuraToolsUi.AddInput(top.transform, rule.Priority.ToString(), value =>
        {
            if (int.TryParse(value, out var priority))
            {
                rule.Priority = priority;
            }
        }, 80f);
        Settings.AuraToolsUi.AddButton(top.transform, "\u5220\u9664", () =>
        {
            role.Rules.Remove(rule);
            RefreshRows();
        }, 72f, 30f);

        var presentationRow = Settings.AuraToolsUi.CreateLayout("RulePresentation", block.transform);
        Settings.AuraToolsUi.SetFixedHeight(presentationRow, Settings.AuraToolsUi.ButtonHeight);
        var presentationLayout = presentationRow.AddComponent<HorizontalLayoutGroup>();
        presentationLayout.spacing = 8f;
        presentationLayout.childControlWidth = true;
        presentationLayout.childControlHeight = true;
        presentationLayout.childForceExpandWidth = false;
        presentationLayout.childForceExpandHeight = false;

        var effective = rule.EffectivePresentation;
        Settings.AuraToolsUi.AddText(presentationRow.transform, "表现", Settings.AuraToolsUi.HintFontSize, TextAnchor.MiddleCenter, Settings.AuraToolsUi.MutedText, Settings.AuraToolsUi.TextMinHeight, 0f, 42f);
        var modeOptions = BuildPresentationModeOptions();
        Settings.AuraToolsUi.AddSelectButton(presentationRow.transform, modeOptions.Select(option => option.Label).ToList(), SelectedOptionIndex(modeOptions, effective.Mode), index =>
        {
            if (index >= 0 && index < modeOptions.Count)
            {
                rule.Presentation.Mode = modeOptions[index].Id;
            }
        }, 156f);
        var fitOptions = BuildFitOptions();
        Settings.AuraToolsUi.AddSelectButton(presentationRow.transform, fitOptions.Select(option => option.Label).ToList(), SelectedOptionIndex(fitOptions, effective.Fit), index =>
        {
            if (index >= 0 && index < fitOptions.Count)
            {
                rule.Presentation.Fit = fitOptions[index].Id;
            }
        }, 132f);
        Settings.AuraToolsUi.AddText(presentationRow.transform, "焦点X", Settings.AuraToolsUi.HintFontSize, TextAnchor.MiddleCenter, Settings.AuraToolsUi.MutedText, Settings.AuraToolsUi.TextMinHeight, 0f, 52f);
        Settings.AuraToolsUi.AddInput(presentationRow.transform, FormatFloat(effective.FocusX), value => rule.Presentation.FocusX = ParseClampedFloat(value, rule.Presentation.FocusX, 0f, 1f), 70f);
        Settings.AuraToolsUi.AddText(presentationRow.transform, "焦点Y", Settings.AuraToolsUi.HintFontSize, TextAnchor.MiddleCenter, Settings.AuraToolsUi.MutedText, Settings.AuraToolsUi.TextMinHeight, 0f, 52f);
        Settings.AuraToolsUi.AddInput(presentationRow.transform, FormatFloat(effective.FocusY), value => rule.Presentation.FocusY = ParseClampedFloat(value, rule.Presentation.FocusY, 0f, 1f), 70f);
        Settings.AuraToolsUi.AddText(presentationRow.transform, "缩放", Settings.AuraToolsUi.HintFontSize, TextAnchor.MiddleCenter, Settings.AuraToolsUi.MutedText, Settings.AuraToolsUi.TextMinHeight, 0f, 42f);
        Settings.AuraToolsUi.AddInput(presentationRow.transform, FormatFloat(effective.SafeScale), value => rule.Presentation.SafeScale = ParseClampedFloat(value, rule.Presentation.SafeScale, 1f, 3f), 70f);

        var bottom = Settings.AuraToolsUi.CreateLayout("RuleBottom", block.transform);
        Settings.AuraToolsUi.SetFixedHeight(bottom, Settings.AuraToolsUi.ButtonHeight);
        var bottomLayout = bottom.AddComponent<HorizontalLayoutGroup>();
        bottomLayout.spacing = 8f;
        bottomLayout.childControlWidth = true;
        bottomLayout.childControlHeight = true;
        bottomLayout.childForceExpandWidth = false;
        bottomLayout.childForceExpandHeight = false;

        Settings.AuraToolsUi.AddText(bottom.transform, "\u56fe\u7247", Settings.AuraToolsUi.HintFontSize, TextAnchor.MiddleCenter, Settings.AuraToolsUi.MutedText, Settings.AuraToolsUi.TextMinHeight, 0f, 42f);
        Settings.AuraToolsUi.AddInput(bottom.transform, rule.Image, value => ApplyRuleImagePath(role, rule, value, false, false), 560f);
        Settings.AuraToolsUi.AddButton(bottom.transform, "选择图片", () => PickRuleImage(role, rule), 92f);
        Settings.AuraToolsUi.AddButton(bottom.transform, "图片目录", () => OpenRuleImageDirectory(role, rule), 92f);
    }

    private static void CreateRuleRow(Transform parent, SkillCgRoleSettings role, SkillCgRuleSettings rule)
    {
        var row = Settings.AuraToolsUi.CreateLayout("Rule-" + rule.ProviderId, parent);
        var rowElement = row.AddComponent<LayoutElement>();
        rowElement.minHeight = Settings.AuraToolsUi.RoleRowHeight;
        rowElement.preferredHeight = Settings.AuraToolsUi.RoleRowHeight;
        Settings.AuraToolsUi.AddImage(row, Settings.AuraToolsUi.Row);
        var layout = row.AddComponent<HorizontalLayoutGroup>();
        layout.padding = new RectOffset(8, 8, 4, 4);
        layout.spacing = 8f;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = false;
        Settings.AuraToolsUi.AddToggle(row.transform, rule.Enabled, value => rule.Enabled = value);
        Settings.AuraToolsUi.AddText(row.transform, "卡牌", 12, TextAnchor.MiddleCenter, Settings.AuraToolsUi.MutedText, 28f, 0f, 34f);
        Settings.AuraToolsUi.AddInput(row.transform, rule.CardId, value => rule.CardId = string.IsNullOrWhiteSpace(value) ? "*" : value.Trim(), 160f);
        Settings.AuraToolsUi.AddText(row.transform, "动作", 12, TextAnchor.MiddleCenter, Settings.AuraToolsUi.MutedText, 28f, 0f, 34f);
        Settings.AuraToolsUi.AddInput(row.transform, rule.Action, value => rule.Action = string.IsNullOrWhiteSpace(value) ? "*" : value.Trim(), 100f);
        Settings.AuraToolsUi.AddText(row.transform, "图片", 12, TextAnchor.MiddleCenter, Settings.AuraToolsUi.MutedText, 28f, 0f, 34f);
        Settings.AuraToolsUi.AddInput(row.transform, rule.Image, value => ApplyRuleImagePath(role, rule, value, false, false), 320f);
        Settings.AuraToolsUi.AddText(row.transform, "优先", 12, TextAnchor.MiddleCenter, Settings.AuraToolsUi.MutedText, 28f, 0f, 42f);
        Settings.AuraToolsUi.AddInput(row.transform, rule.Priority.ToString(), value =>
        {
            if (int.TryParse(value, out var priority))
            {
                rule.Priority = priority;
            }
        }, 80f);
        Settings.AuraToolsUi.AddButton(row.transform, "删除", () =>
        {
            role.Rules.Remove(rule);
            RefreshRows();
        }, 60f, 28f);
    }

    private sealed class SkillDropdownOption
    {
        public string Id { get; set; } = "";

        public string Label { get; set; } = "";
    }

    private static List<SkillDropdownOption> BuildPresentationModeOptions()
    {
        return new List<SkillDropdownOption>
        {
            new() { Id = "slide", Label = "从右到左" },
            new() { Id = "fullscreenFade", Label = "全屏淡入淡出" },
            new() { Id = "centerFade", Label = "居中淡入淡出" }
        };
    }

    private static List<SkillDropdownOption> BuildFitOptions()
    {
        return new List<SkillDropdownOption>
        {
            new() { Id = "contain", Label = "完整显示" },
            new() { Id = "cover", Label = "全屏裁切" },
            new() { Id = "stretch", Label = "拉伸填充" }
        };
    }

    private static string DefaultActiveSkillForNewRule(SkillCgRoleSettings role)
    {
        var used = new HashSet<string>(
            role.Rules
                .Select(rule => rule.CardId),
            StringComparer.OrdinalIgnoreCase);
        foreach (var skill in RoleCatalog.GetRoleSkills(role.RoleId))
        {
            if (!used.Contains(skill.Id))
            {
                return skill.Id;
            }
        }

        return RoleCatalog.GetRoleSkills(role.RoleId).FirstOrDefault()?.Id ?? "*";
    }

    private static List<SkillDropdownOption> BuildSkillOptions(SkillCgRoleSettings role, SkillCgRuleSettings rule)
    {
        var options = new List<SkillDropdownOption>
        {
            new()
            {
                Id = "*",
                Label = "任意技能"
            }
        };

        foreach (var skill in RoleCatalog.GetRoleSkills(role.RoleId))
        {
            if (options.Any(option => string.Equals(option.Id, skill.Id, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            options.Add(new SkillDropdownOption
            {
                Id = skill.Id,
                Label = SkillLabel(skill.Id, skill.DisplayName)
            });
        }

        var current = rule.CardId?.Trim() ?? "";
        if (!string.IsNullOrWhiteSpace(current)
            && !options.Any(option => string.Equals(option.Id, current, StringComparison.OrdinalIgnoreCase)))
        {
            options.Add(new SkillDropdownOption
            {
                Id = current,
                Label = "自定义技能"
            });
        }

        return options;
    }

    private static int SelectedOptionIndex(IReadOnlyList<SkillDropdownOption> options, string value)
    {
        for (var i = 0; i < options.Count; i++)
        {
            if (string.Equals(options[i].Id, value, StringComparison.OrdinalIgnoreCase))
            {
                return i;
            }
        }

        return 0;
    }

    private static string SkillLabel(string id, string displayName)
    {
        var name = string.IsNullOrWhiteSpace(displayName) ? id : displayName;
        return name;
    }

    private static string RuleDisplayName(SkillCgRuleSettings rule)
    {
        if (!string.IsNullOrWhiteSpace(rule.DisplayName))
        {
            return rule.DisplayName.Trim();
        }

        return string.IsNullOrWhiteSpace(rule.CardId) || string.Equals(rule.CardId, "*", StringComparison.Ordinal)
            ? "自定义CG"
            : rule.CardId.Trim();
    }

    private static string FormatFloat(float value)
    {
        return value.ToString("0.###", CultureInfo.InvariantCulture);
    }

    private static float ParseClampedFloat(string value, float fallback, float min, float max)
    {
        var text = (value ?? "").Trim().Replace(',', '.');
        if (!float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed))
        {
            return fallback;
        }

        return Mathf.Clamp(parsed, min, max);
    }

    private static void OpenRuleImageDirectory(SkillCgRoleSettings role, SkillCgRuleSettings rule)
    {
        var directory = FileResourceUtil.RoleSkillCgDirectory(role.RoleId);
        try
        {
            if (!string.IsNullOrWhiteSpace(rule.Image))
            {
                var path = AuraToolsConfigService.ResolveConfiguredPath(rule.Image);
                if (File.Exists(path))
                {
                    directory = Path.GetDirectoryName(path) ?? directory;
                }
                else if (Directory.Exists(path))
                {
                    directory = path;
                }
                else
                {
                    var parent = Path.GetDirectoryName(path);
                    if (!string.IsNullOrWhiteSpace(parent))
                    {
                        directory = parent;
                    }
                }
            }
        }
        catch
        {
        }

        FileResourceUtil.OpenDirectory(directory);
    }

    private static void PickRuleImage(SkillCgRoleSettings role, SkillCgRuleSettings rule)
    {
        var directory = FileResourceUtil.RoleSkillCgDirectory(role.RoleId);
        SetHint("正在打开图片选择器...");
        OptionalFileDialog.PickImageFileAsync(directory, result =>
        {
            if (result.Selected)
            {
                ApplyRuleImagePath(role, rule, result.Path, true, true);
                return;
            }

            if (result.Status == OptionalFileDialogStatus.Cancelled)
            {
                SetHint("已取消选择图片。");
                return;
            }

            AuraToolsLog.Warn("[SkillCG] image picker unavailable: " + result.Message);
            SetHint("无法打开系统文件选择器；请使用路径输入框修改，或先把图片放进角色目录。");
        });
    }

    private static void ApplyRuleImagePath(SkillCgRoleSettings role, SkillCgRuleSettings rule, string path, bool refresh, bool save)
    {
        var trimmed = path?.Trim() ?? "";
        if (string.IsNullOrWhiteSpace(trimmed))
        {
            rule.Image = "";
            SetHint("已清空图片路径。");
        }
        else
        {
            var imported = FileResourceUtil.ImportImagePath(
                trimmed,
                FileResourceUtil.RoleSkillCgDirectory(role.RoleId),
                RuleImageBaseName(role, rule),
                out var message);
            rule.Image = string.IsNullOrWhiteSpace(imported) ? trimmed : imported;
            if (string.IsNullOrWhiteSpace(imported))
            {
                AuraToolsLog.Warn("[SkillCG] image path kept as typed: " + message);
                SetHint(message + " 已保留输入路径。");
            }
            else
            {
                role.Enabled = true;
                rule.Enabled = true;
                SetHint(message + " " + rule.Image);
            }
        }

        if (save)
        {
            AuraToolsConfigService.SkillCg.Normalize();
            AuraToolsConfigService.SaveSkillCg();
        }

        if (refresh)
        {
            RefreshRows();
        }
    }

    private static string RuleImageBaseName(SkillCgRoleSettings role, SkillCgRuleSettings rule)
    {
        var index = role.Rules.IndexOf(rule);
        return index >= 0 ? "skill_cg_" + (index + 1) : "skill_cg";
    }

    private static void SetHint(string message)
    {
        if (hintText != null)
        {
            hintText.text = message;
        }
    }

    private static void RefreshAndSave()
    {
        foreach (var role in AuraToolsConfigService.SkillCg.Roles.Values)
        {
            foreach (var rule in role.Rules)
            {
                rule.Action = "*";
            }
        }

        AuraToolsConfigService.SkillCg.Normalize();
        AuraToolsConfigService.SaveSkillCg();
        SetHint("已保存技能CG配置。");
    }
}
