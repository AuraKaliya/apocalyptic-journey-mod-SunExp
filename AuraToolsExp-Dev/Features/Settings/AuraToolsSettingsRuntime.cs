using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using AuraShared.Core;
using AuraUi.Shared;
using AuraToolsExp.Dll.Config;
using AuraToolsExp.Dll.Features.Audio;
using AuraToolsExp.Dll.Features.AutoBattle;
using AuraToolsExp.Dll.Features.DamageMeter;
using AuraToolsExp.Dll.Features.Feast;
using AuraToolsExp.Dll.Features.Logging;
using AuraToolsExp.Dll.Features.SafeBox;
using AuraToolsExp.Dll.Features.Skin;
using AuraToolsExp.Dll.Features.SkillCg;
using AuraToolsExp.Dll.Features.StarterDeck;
using AuraToolsExp.Dll.Infrastructure;
using Michsky.MUIP;
using StarterDeckArbiter.Shared;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using Witch.Core;
using Witch.Mod;
using Witch.UI.Window;
using Object = UnityEngine.Object;

namespace AuraToolsExp.Dll.Features.Settings;

public static class AuraToolsSettingsRuntime
{
    private const string AuraTabButtonName = "AuraToolsSettingsTabButton";
    private const string AuraPanelName = "AuraToolsSettingsPanel";
    private const float AuraTabHeight = 60f;
    private const float AuraTabTextSize = 20f;
    private const float AuraTabMinimumTextSize = 18f;
    private static GameObject? activePanel;
    private static Transform? activePanelHost;
    private static Transform? activeTabParent;
    private static bool panelBuilt;
    private static bool panelBuilding;
    private static bool autoBattleSnapshotSubscribed;
    private static long autoBattleSnapshotRevisionBuilt = -1;
    private static bool AutoBattleAdvancedTrainingExpanded;
    private static bool AutoBattleEvolutionView;
    private static readonly Dictionary<string, bool> FoldoutStates = new(StringComparer.Ordinal);
    private static bool loggedHookRegistration;
    private static bool loggedInjectionSuccess;
    private static bool loggedNoTabParent;
    private static bool loggedNativeTabCloneFallback;

    public static void Initialize(ModConfig modConfig)
    {
        if (!autoBattleSnapshotSubscribed)
        {
            autoBattleSnapshotSubscribed = true;
            AuraToolsAutoBattleUiSnapshotRuntime.Changed +=
                OnAutoBattleUiSnapshotChanged;
        }
        RegisterAfter(modConfig, "SettingUI.Start", InjectSettings);
        RegisterAfter(modConfig, "SettingUI.OnEnable", InjectSettings);
        RegisterAfter(modConfig, "SettingUI.Load", InjectSettings);
        RegisterAfter(modConfig, "SettingUI.OnDestroy", ClearPanel);
        if (!loggedHookRegistration)
        {
            loggedHookRegistration = true;
            AuraToolsLog.Info("[Settings] hooks registered.");
        }
    }

    [HookAfter(typeof(SettingUI), nameof(SettingUI.OnEnable))]
    public static void AfterSettingOnEnable(SettingUI __instance)
    {
        InjectSettings(__instance, "attribute:OnEnable");
    }

    internal static void HideActivePanel()
    {
        if (activePanel != null)
        {
            activePanel.SetActive(false);
        }
    }

    private static void InjectSettings(ModHookContext context)
    {
        try
        {
            if (context.Target is not SettingUI setting)
            {
                return;
            }

            InjectSettings(setting, "dynamic");
        }
        catch (Exception ex)
        {
            AuraToolsLog.Error("[Settings] inject failed", ex);
        }
    }

    private static void InjectSettings(SettingUI setting, string source)
    {
        try
        {
            var parent = ResolveTabParent(setting);
            activeTabParent = parent;
            var panelHost = ResolvePanelHost(setting, parent);
            EnsureTabButton(setting, parent);
            BindNativeTabsToHide(parent);
            EnsurePanel(setting, parent, panelHost);
            var autoBattle =
                AuraToolsConfigService.MatchExperience.AutoBattle;
            AuraToolsAutoBattleUiSnapshotRuntime.RequestRefresh(
                autoBattle.Profile,
                autoBattle.SelectedModelId);
            if (!loggedInjectionSuccess)
            {
                loggedInjectionSuccess = true;
                AuraToolsLog.Info("[Settings] injected from " + source
                                  + "; tabParent=" + DescribeTransform(parent)
                                  + "; panelHost=" + DescribeTransform(panelHost)
                                  + "; hostRect=" + DescribeRect(panelHost)
                                  + "; keyButtonParent=" + DescribeTransform(setting.KeyButton == null ? null : setting.KeyButton.transform.parent)
                                  + "; buttonParent=" + DescribeTransform(setting.ButtonParent)
                                  + "; buttonParentRect=" + DescribeRect(setting.ButtonParent));
            }
        }
        catch (Exception ex)
        {
            AuraToolsLog.Error("[Settings] inject failed from " + source, ex);
        }
    }

    private static void ClearPanel(ModHookContext context)
    {
        activePanel = null;
        activePanelHost = null;
        activeTabParent = null;
        panelBuilt = false;
        panelBuilding = false;
        autoBattleSnapshotRevisionBuilt = -1;
    }

    private static void RegisterAfter(ModConfig config, string target, Action<ModHookContext> action)
    {
        AuraToolsHookRegistry.After(config, target, action, "Settings");
    }

    private static Transform? ResolveTabParent(SettingUI setting)
    {
        if (setting.KeyButton != null && setting.KeyButton.transform.parent != null)
        {
            return setting.KeyButton.transform.parent;
        }

        if (setting.ButtonParent != null)
        {
            return setting.ButtonParent;
        }

        var found = FindLikelyButtonRow(setting.transform);
        if (found == null && !loggedNoTabParent)
        {
            loggedNoTabParent = true;
            AuraToolsLog.Warn("[Settings] could not resolve tab parent; fallback tab will be created under SettingUI root.");
        }

        return found ?? setting.transform;
    }

    private static void EnsureTabButton(SettingUI setting, Transform? tabParent)
    {
        if (tabParent == null)
        {
            return;
        }

        var template = setting.KeyButton;
        var existing = tabParent.Find(AuraTabButtonName);
        if (existing != null && template != null && AuraUiNativeButtonCloneAdapter.IsOwnedClone(template, existing.gameObject))
        {
            var configured = AuraUiNativeButtonCloneAdapter.TryConfigureClone(
                template,
                existing.gameObject,
                AuraToolsIds.SettingsTabName,
                ShowAuraPanel,
                AuraTabTextSize,
                AuraTabMinimumTextSize);
            if (configured.Success)
            {
                existing.SetAsLastSibling();
                AdjustTabSize(existing.gameObject);
                existing.gameObject.SetActive(true);
                return;
            }

            RejectUnsafeTabClone(existing.gameObject, configured.FailureReason);
            existing = null;
        }

        if (existing != null && existing.GetComponent<ButtonManager>() != null)
        {
            RejectUnsafeTabClone(existing.gameObject, "existing native-style button has no matching ownership marker");
            existing = null;
        }

        GameObject buttonObject;
        if (existing != null)
        {
            buttonObject = existing.gameObject;
            ConfigureTabButton(buttonObject);
        }
        else
        {
            AuraUiNativeButtonCloneResult? cloneResult = null;
            if (template != null)
            {
                cloneResult = AuraUiNativeButtonCloneAdapter.TryClone(new AuraUiNativeButtonCloneRequest
                {
                    Template = template,
                    Parent = tabParent,
                    CloneName = AuraTabButtonName,
                    Label = AuraToolsIds.SettingsTabName,
                    OnClick = ShowAuraPanel,
                    TextSizeOverride = AuraTabTextSize,
                    MinimumTextSizeOverride = AuraTabMinimumTextSize
                });
            }

            if (cloneResult != null && cloneResult.Success && cloneResult.Root != null)
            {
                buttonObject = cloneResult.Root;
            }
            else
            {
                LogNativeTabCloneFallback(cloneResult?.FailureReason ?? "SettingUI.KeyButton is unavailable");
                buttonObject = CreatePlainTabButton(tabParent);
                ConfigureTabButton(buttonObject);
            }
        }

        buttonObject.transform.SetAsLastSibling();
        AdjustTabSize(buttonObject);
        buttonObject.SetActive(true);
    }

    private static void ConfigureTabButton(GameObject buttonObject)
    {
        var button = buttonObject.GetComponent<Button>();
        if (button != null)
        {
            button.onClick.RemoveAllListeners();
            button.onClick.AddListener(ShowAuraPanel);
        }
        else
        {
            var image = buttonObject.GetComponent<Image>() ?? buttonObject.AddComponent<Image>();
            button = buttonObject.AddComponent<Button>();
            button.targetGraphic = image;
            button.onClick.AddListener(ShowAuraPanel);
        }

        if (button.targetGraphic != null)
        {
            AuraUiButtonFeedback.Apply(button, button.targetGraphic, AuraToolsUi.Accent);
        }

        RemoveTextChildren(buttonObject.transform);
        AuraToolsUi.AddFillText(buttonObject.transform, AuraToolsIds.SettingsTabName, AuraToolsUi.TabFontSize, TextAnchor.MiddleCenter, AuraToolsUi.Accent);
    }

    private static void RejectUnsafeTabClone(GameObject buttonObject, string reason)
    {
        LogNativeTabCloneFallback(reason);
        buttonObject.SetActive(false);
        buttonObject.name = AuraTabButtonName + "-Rejected";
        Object.Destroy(buttonObject);
    }

    private static void LogNativeTabCloneFallback(string reason)
    {
        if (loggedNativeTabCloneFallback)
        {
            return;
        }

        loggedNativeTabCloneFallback = true;
        AuraToolsLog.Warn("[Settings] native KeyButton style clone rejected; using Aura fallback. reason=" + reason);
    }

    private static void BindNativeTabsToHide(Transform? tabParent)
    {
        if (tabParent == null)
        {
            return;
        }

        foreach (Transform child in tabParent)
        {
            if (child == null || child.name == AuraTabButtonName || child.GetComponent<AuraToolsNativeTabRelay>() != null)
            {
                continue;
            }

            child.gameObject.AddComponent<AuraToolsNativeTabRelay>();
        }
    }

    private static Transform? FindLikelyButtonRow(Transform root)
    {
        foreach (var button in root.GetComponentsInChildren<ButtonManager>(true))
        {
            var parent = button.transform.parent;
            if (parent != null && parent.childCount >= 3)
            {
                return parent;
            }
        }

        foreach (var button in root.GetComponentsInChildren<Button>(true))
        {
            var parent = button.transform.parent;
            if (parent != null && parent.childCount >= 3)
            {
                return parent;
            }
        }

        return null;
    }

    private static GameObject CreatePlainTabButton(Transform parent)
    {
        var go = AuraToolsUi.CreateRect(
            AuraTabButtonName,
            parent,
            new Vector2(0f, 1f),
            new Vector2(0f, 1f),
            new Vector2(0f, 1f),
            new Vector2(124f, AuraTabHeight));
        AuraToolsUi.AddButtonImage(go, new Color(0.08f, 0.07f, 0.16f, 0.98f));
        go.AddComponent<Button>();
        AuraToolsUi.AddFillText(go.transform, AuraToolsIds.SettingsTabName, AuraToolsUi.TabFontSize, TextAnchor.MiddleCenter, AuraToolsUi.Accent);
        return go;
    }

    private static void AdjustTabSize(GameObject buttonObject)
    {
        if (buttonObject.transform is RectTransform rect)
        {
            rect.sizeDelta = new Vector2(Mathf.Max(rect.sizeDelta.x, 118f), Mathf.Max(rect.sizeDelta.y, AuraTabHeight));
        }

        var layout = buttonObject.GetComponent<LayoutElement>() ?? buttonObject.AddComponent<LayoutElement>();
        layout.minWidth = Mathf.Max(layout.minWidth, 112f);
        layout.preferredWidth = Mathf.Max(layout.preferredWidth, 118f);
        layout.minHeight = Mathf.Max(layout.minHeight, AuraTabHeight);
        layout.preferredHeight = Mathf.Max(layout.preferredHeight, AuraTabHeight);
        layout.flexibleHeight = 0f;
    }

    private static void RemoveTextChildren(Transform root)
    {
        foreach (var text in root.GetComponentsInChildren<Text>(true))
        {
            if (text.transform != root)
            {
                Object.Destroy(text);
            }
        }

        foreach (var component in root.GetComponentsInChildren<UnityEngine.Component>(true))
        {
            if (component == null || !component.GetType().FullName.Contains("TMPro"))
            {
                continue;
            }

            var property = component.GetType().GetProperty("text");
            if (property != null && property.CanWrite)
            {
                try
                {
                    Object.Destroy(component);
                }
                catch
                {
                    // Text component compatibility fallback only.
                }
            }
        }
    }

    private static string DescribeTransform(Transform? transform)
    {
        return transform == null ? "<null>" : transform.name + " children=" + transform.childCount;
    }

    private static string DescribeRect(Transform? transform)
    {
        if (transform is not RectTransform rect)
        {
            return "<no-rect>";
        }

        return rect.name
               + " size=" + Mathf.RoundToInt(rect.rect.width) + "x" + Mathf.RoundToInt(rect.rect.height)
               + " anchor=" + FormatVector(rect.anchorMin) + "-" + FormatVector(rect.anchorMax)
               + " offset=" + FormatVector(rect.offsetMin) + "/" + FormatVector(rect.offsetMax);
    }

    private static string FormatVector(Vector2 vector)
    {
        return "(" + vector.x.ToString("0.##") + "," + vector.y.ToString("0.##") + ")";
    }

    private static void EnsurePanel(SettingUI setting, Transform? tabParent, Transform panelHost)
    {
        activePanelHost = panelHost;
        activeTabParent = tabParent;
        var existing = panelHost.Find(AuraPanelName);
        if (existing != null)
        {
            activePanel = existing.gameObject;
            PositionPanelInHost(activePanel, panelHost, tabParent);
            return;
        }

        activePanel = AuraToolsUi.CreateRect(AuraPanelName, panelHost, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
        PositionPanelInHost(activePanel, panelHost, tabParent);
        activePanel.SetActive(false);
        AuraToolsUi.AddImage(activePanel, AuraToolsUi.Background);
    }

    private static Transform ResolvePanelHost(SettingUI setting, Transform? tabParent)
    {
        var common = FindNearestCommonAncestor(tabParent, setting.ButtonParent);
        var contentHost = FindContentHostUnderCommonAncestor(common, setting.ButtonParent, tabParent);
        if (contentHost != null)
        {
            return contentHost;
        }

        if (setting.ButtonParent?.parent != null)
        {
            return setting.ButtonParent.parent;
        }

        return setting.transform;
    }

    private static Transform? FindContentHostUnderCommonAncestor(Transform? common, Transform? contentDescendant, Transform? tabParent)
    {
        if (common == null || contentDescendant == null)
        {
            return null;
        }

        Transform? best = null;
        foreach (Transform child in common)
        {
            if (child == null
                || child == tabParent
                || child == contentDescendant
                || IsAncestorOrSelf(child, tabParent)
                || !IsAncestorOrSelf(child, contentDescendant))
            {
                continue;
            }

            if (IsReasonablePanelHost(child))
            {
                best = child;
                break;
            }
        }

        return best;
    }

    private static bool IsReasonablePanelHost(Transform candidate)
    {
        if (candidate.name == "setting" || candidate.name == "Setting" || candidate.name == "Content")
        {
            return true;
        }

        if (candidate is not RectTransform rect)
        {
            return false;
        }

        return Mathf.Abs(rect.rect.width) >= 360f && Mathf.Abs(rect.rect.height) >= 260f;
    }

    private static Transform? FindNearestCommonAncestor(Transform? first, Transform? second)
    {
        if (first == null || second == null)
        {
            return null;
        }

        var ancestors = new List<Transform>();
        var current = first;
        while (current != null)
        {
            ancestors.Add(current);
            current = current.parent;
        }

        current = second;
        while (current != null)
        {
            if (ancestors.Contains(current))
            {
                return current;
            }

            current = current.parent;
        }

        return null;
    }

    private static bool IsAncestorOrSelf(Transform? ancestor, Transform? item)
    {
        if (ancestor == null || item == null)
        {
            return false;
        }

        var current = item;
        while (current != null)
        {
            if (current == ancestor)
            {
                return true;
            }

            current = current.parent;
        }

        return false;
    }

    private static void PositionPanelInHost(GameObject panel, Transform panelHost, Transform? tabParent)
    {
        if (panel.transform is not RectTransform rect)
        {
            return;
        }

        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = Vector2.zero;
        rect.sizeDelta = Vector2.zero;
        rect.offsetMin = new Vector2(20f, 18f);
        rect.offsetMax = new Vector2(-20f, -ResolveTopInset(panelHost, tabParent));
    }

    private static float ResolveTopInset(Transform panelHost, Transform? tabParent)
    {
        if (!IsAncestorOrSelf(panelHost, tabParent))
        {
            return 18f;
        }

        if (panelHost is RectTransform hostRect && tabParent is RectTransform)
        {
            var bounds = RectTransformUtility.CalculateRelativeRectTransformBounds(hostRect, tabParent);
            var topInset = hostRect.rect.yMax - bounds.min.y + 6f;
            if (!float.IsNaN(topInset) && !float.IsInfinity(topInset) && topInset > 24f && topInset < hostRect.rect.height * 0.5f)
            {
                return Mathf.Clamp(topInset, 44f, 92f);
            }
        }

        return 58f;
    }

    private static void ShowAuraPanel()
    {
        if (activePanel == null)
        {
            return;
        }

        activePanel.SetActive(true);
        activePanel.transform.SetAsLastSibling();
        var autoBattle = AuraToolsConfigService.MatchExperience.AutoBattle;
        AuraToolsAutoBattleUiSnapshotRuntime.RequestRefresh(
            autoBattle.Profile,
            autoBattle.SelectedModelId);
        AuraToolsAutoBattleFoundationRuntime.BeginReadinessRefresh();
        if (!panelBuilt)
        {
            BeginInitialPanelBuild(activePanel);
        }
    }

    private static void BeginInitialPanelBuild(GameObject panel)
    {
        if (panelBuilding)
        {
            return;
        }
        panelBuilding = true;
        var driver = panel.GetComponent<AuraToolsPanelBuildDriver>()
                     ?? panel.AddComponent<AuraToolsPanelBuildDriver>();
        driver.Begin(panel.transform);
    }

    internal static IEnumerator BuildPanelAcrossFrames(Transform panel)
    {
        AuraToolsUi.ClearChildren(panel);
        var layout = panel.gameObject.GetComponent<VerticalLayoutGroup>();
        if (layout == null)
        {
            layout = panel.gameObject.AddComponent<VerticalLayoutGroup>();
        }
        layout.padding = new RectOffset(10, 10, 8, 8);
        layout.spacing = 8f;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;

        var content = AuraToolsUi.CreateScroll(panel, "AuraToolsSettings");
        yield return null;
        CreateDataDirectorySection(content);
        yield return null;
        CreateSkinSection(content);
        yield return null;
        CreateAudioSection(content);
        yield return null;
        CreateGameParametersSection(content);
        yield return null;
        CreateMatchExperienceSection(content);
        yield return null;
        CreateLoggingSection(content);
        panelBuilt = true;
        panelBuilding = false;
        var autoBattle = AuraToolsConfigService.MatchExperience.AutoBattle;
        autoBattleSnapshotRevisionBuilt =
            AuraToolsAutoBattleUiSnapshotRuntime
                .Snapshot(autoBattle.Profile, autoBattle.SelectedModelId)
                .Revision;
    }

    private static void RebuildPanel(Transform panel)
    {
        var existingScroll = panel.GetComponentInChildren<ScrollRect>(true);
        var scrollPosition = existingScroll == null
            ? 1f
            : existingScroll.verticalNormalizedPosition;
        AuraToolsUi.ClearChildren(panel);

        var layout = panel.gameObject.GetComponent<VerticalLayoutGroup>();
        if (layout == null)
        {
            layout = panel.gameObject.AddComponent<VerticalLayoutGroup>();
        }

        layout.padding = new RectOffset(10, 10, 8, 8);
        layout.spacing = 8f;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;

        var content = AuraToolsUi.CreateScroll(panel, "AuraToolsSettings");
        CreateDataDirectorySection(content);
        CreateSkinSection(content);
        CreateAudioSection(content);
        CreateGameParametersSection(content);
        CreateMatchExperienceSection(content);
        CreateLoggingSection(content);
        panelBuilt = true;
        panelBuilding = false;
        var autoBattle = AuraToolsConfigService.MatchExperience.AutoBattle;
        autoBattleSnapshotRevisionBuilt =
            AuraToolsAutoBattleUiSnapshotRuntime
                .Snapshot(autoBattle.Profile, autoBattle.SelectedModelId)
                .Revision;
        var rebuiltScroll = panel.GetComponentInChildren<ScrollRect>(true);
        if (rebuiltScroll != null)
        {
            rebuiltScroll.verticalNormalizedPosition = scrollPosition;
        }
    }

    private static void CreateDataDirectorySection(Transform parent)
    {
        CreateSectionLabel(parent, "数据目录");
        var row = CreateInlineRow(parent, "DataDirectoryRow");
        AuraToolsUi.AddText(row.transform, "配置与用户资源目录：" + AuraToolsConfigService.DataRootDirectory, AuraToolsUi.BodyFontSize, TextAnchor.MiddleLeft, AuraToolsUi.Text, AuraToolsUi.TextMinHeight, 1f);
        AuraToolsUi.AddButton(row.transform, "打开目录", () => FileResourceUtil.OpenDirectory(AuraToolsConfigService.DataRootDirectory), 92f);
    }

    private static void CreateAudioSection(Transform parent)
    {
        CreateSectionLabel(parent, "音频");
        CreateSubmodule(parent, "战斗背景音乐", AuraToolsConfigService.Audio.BattleBgm.Enabled, value =>
        {
            AuraToolsConfigService.Audio.BattleBgm.Enabled = value;
            AuraToolsConfigService.SaveAudio();
            AuraToolsAudioRuntime.RegisterProviders();
        }, content =>
        {
            CreateModeRow(content, AuraToolsConfigService.Audio.BattleBgm, true);
            CreateAudioCommonRow(content, AuraToolsConfigService.Audio.BattleBgm, true);
            AuraToolsUi.AddText(content, "仅替换战斗时背景音乐；高级模式可为每个角色指定独立的背景音乐。", AuraToolsUi.HintFontSize, TextAnchor.MiddleLeft, AuraToolsUi.MutedText, AuraToolsUi.TextMinHeight, 1f);
        });

        CreateSubmodule(parent, "出牌音效", AuraToolsConfigService.Audio.CardUse.Enabled, value =>
        {
            AuraToolsConfigService.Audio.CardUse.Enabled = value;
            AuraToolsConfigService.SaveAudio();
            AuraToolsAudioRuntime.RegisterProviders();
        }, content =>
        {
            CreateModeRow(content, AuraToolsConfigService.Audio.CardUse, false);
            CreateAudioCommonRow(content, AuraToolsConfigService.Audio.CardUse, false);
            AuraToolsUi.AddText(content, "仅替换出牌音效；联机时由主机同步出牌事件，各端仍按自己的开关和音频文件决定是否播放。", AuraToolsUi.HintFontSize, TextAnchor.MiddleLeft, AuraToolsUi.MutedText, AuraToolsUi.TextMinHeight, 1f);
        });
    }

    private static void CreateSkinSection(Transform parent)
    {
        CreateSectionLabel(parent, "角色皮肤");
        CreateSubmodule(parent, "共享皮肤管理", AuraToolsConfigService.Skin.Enabled, value =>
        {
            AuraToolsConfigService.Skin.Enabled = value;
            AuraToolsConfigService.SaveSkin();
            if (value)
            {
                AuraToolsSkinRuntime.RegisterBundledPackage();
                AuraToolsSkinRuntime.Reload();
            }
        }, content =>
        {
            var statusRow = CreateInlineRow(content, "SkinStatusRow");
            AuraToolsConfigService.Skin.AutoInstallBundledSkins = true;
            var skinCandidates = AuraToolsSkinRuntime.CandidateDefinitions();
            var enabledCandidates = skinCandidates.Count(candidate =>
                AuraToolsConfigService.Skin.IsCandidateEnabled(candidate.QualifiedSkinId));
            AuraToolsUi.AddText(
                statusRow.transform,
                "ManualSelection · 角色 "
                + AuraToolsSkinRuntime.CandidateCareerIds().Count
                + " · 待选 " + enabledCandidates + "/" + skinCandidates.Count,
                AuraToolsUi.BodyFontSize,
                TextAnchor.MiddleLeft,
                AuraToolsUi.Text,
                AuraToolsUi.TextMinHeight,
                1f);
            AuraToolsUi.AddButton(statusRow.transform, "管理皮肤", () =>
                AuraToolsSkinEditor.Show(activePanel!.transform), 104f);

            var installPolicyRow = CreateInlineRow(content, "SkinAutoInstallInfoRow");
            AuraToolsUi.AddText(
                installPolicyRow.transform,
                "内置角色皮肤会自动安装并补齐到共享皮肤目录。",
                AuraToolsUi.HintFontSize,
                TextAnchor.MiddleLeft,
                AuraToolsUi.MutedText,
                AuraToolsUi.TextMinHeight,
                1f);

            var toggles = CreateInlineRow(content, "SkinToggleRow");
            AuraToolsUi.AddToggle(toggles.transform, AuraToolsConfigService.Skin.SyncRemote, value =>
            {
                AuraToolsConfigService.Skin.SyncRemote = value;
                AuraToolsConfigService.SaveSkin();
                RebuildPanel(activePanel!.transform);
            });
            AuraToolsUi.AddText(toggles.transform, "联机同步皮肤选择", AuraToolsUi.BodyFontSize, TextAnchor.MiddleLeft, AuraToolsUi.Text, AuraToolsUi.TextMinHeight, 1f);

            var entryRow = CreateInlineRow(content, "SkinEntryUiRow");
            AuraToolsUi.AddToggle(entryRow.transform, AuraToolsConfigService.Skin.ShowEntrySkinButton, value =>
            {
                AuraToolsConfigService.Skin.ShowEntrySkinButton = value;
                AuraToolsConfigService.SaveSkin();
                RebuildPanel(activePanel!.transform);
            });
            AuraToolsUi.AddText(entryRow.transform, "在角色选择界面显示皮肤按钮", AuraToolsUi.BodyFontSize, TextAnchor.MiddleLeft, AuraToolsUi.Text, AuraToolsUi.TextMinHeight, 1f);

        });
    }

    private static void CreateMatchExperienceSection(Transform parent)
    {
        CreateSectionLabel(parent, "对局体验");
        var starterDeckEnabled = AuraToolsConfigService.MatchExperience.StarterDeck.Enabled;
        CreateSubmodule(parent,
            "【世界推演】开局卡组配置：" + (starterDeckEnabled ? "已启用" : "未启用"),
            starterDeckEnabled,
            value =>
        {
            AuraToolsConfigService.MatchExperience.StarterDeck.Enabled = value;
            AuraToolsConfigService.SaveMatchExperience();
        }, content =>
        {
            var settings = AuraToolsConfigService.MatchExperience.StarterDeck;
            var profileCount = StarterDeckArbiterRuntime.GetRegisteredProfiles(AuraToolsIds.ModId).Count;
            var row = CreateInlineRow(content, "StarterDeckConfigRow");
            AuraToolsUi.AddText(row.transform,
                "模式：" + (settings.Mode == StarterDeckModes.RoleSpecific ? "按角色" : "全局")
                + "；全局：" + settings.GlobalProfile.CardIds.Count + "/" + settings.GlobalProfile.DeckSize
                + "；角色本地：" + settings.Roles.Count
                + "；MOD注册：" + profileCount,
                AuraToolsUi.BodyFontSize,
                TextAnchor.MiddleLeft,
                AuraToolsUi.Text,
                AuraToolsUi.TextMinHeight,
                1f);
            AuraToolsUi.AddButton(row.transform, settings.Mode == StarterDeckModes.RoleSpecific ? "切到全局" : "切到按角色", () =>
            {
                settings.Mode = settings.Mode == StarterDeckModes.RoleSpecific ? StarterDeckModes.Global : StarterDeckModes.RoleSpecific;
                AuraToolsConfigService.SaveMatchExperience();
                RebuildPanel(activePanel!.transform);
            }, 96f);
            AuraToolsUi.AddButton(row.transform, "全局配置", () => AuraToolsStarterDeckEditor.ShowGlobal(activePanel!.transform), 96f);
            AuraToolsUi.AddButton(row.transform, "角色配置", () => AuraToolsStarterDeckRoleManager.Show(activePanel!.transform), 96f);

            var policyRow = CreateInlineRow(content, "StarterDeckPolicyRow");
            settings.PreferRoleModProfile = true;
            AuraToolsUi.AddText(policyRow.transform, "说明：没有本地角色卡组时，会自动使用角色所属 MOD 注册的推荐开局卡组；没有推荐时再回退到全局卡组。", AuraToolsUi.HintFontSize, TextAnchor.MiddleLeft, AuraToolsUi.MutedText, AuraToolsUi.TextMinHeight, 1f);
        }, starterDeckEnabled ? new Color(0.58f, 0.94f, 0.62f, 1f) : AuraToolsUi.MutedText);

        CreateToggleModule(parent, "卡牌刷新", AuraToolsConfigService.MatchExperience.CardRefresh.Enabled, value =>
        {
            AuraToolsConfigService.MatchExperience.CardRefresh.Enabled = value;
            AuraToolsConfigService.SaveMatchExperience();
        }, AuraToolsConfigService.MatchExperience.CardRefresh.Enabled ? AuraToolsUi.SuccessText : AuraToolsUi.MutedText);

        var autoBattle = AuraToolsConfigService.MatchExperience.AutoBattle;
        CreateSectionLabel(parent, "训练参数");
        CreateSubmodule(parent, "战斗策略实验室（工具）", autoBattle.Enabled, value =>
        {
            autoBattle.Enabled = value;
            AuraToolsConfigService.SaveMatchExperience();
        }, content =>
        {
            var profileRow = CreateInlineRow(content, "AutoBattleProfileRow");
            AuraToolsUi.AddText(
                profileRow.transform,
                "决策风格：" + AutoBattleProfileLabel(autoBattle.Profile),
                AuraToolsUi.BodyFontSize,
                TextAnchor.MiddleLeft,
                AuraToolsUi.Text,
                AuraToolsUi.TextMinHeight,
                1f);
            var profileButton = AuraToolsUi.AddButton(profileRow.transform, "切换风格", () =>
            {
                autoBattle.Profile = NextAutoBattleProfile(autoBattle.Profile);
                autoBattle.Normalize();
                AuraToolsConfigService.SaveMatchExperience();
                RebuildPanel(activePanel!.transform);
            }, 96f);
            AttachAutoBattleWorkLock(profileRow, profileButton);

            var policyRow = CreateInlineRow(content, "AutoBattleUnknownPolicyRow");
            AuraToolsUi.AddText(
                policyRow.transform,
                "未知动作：" + AutoBattleUnknownPolicyLabel(autoBattle.UnknownActionPolicy),
                AuraToolsUi.BodyFontSize,
                TextAnchor.MiddleLeft,
                AuraToolsUi.Text,
                AuraToolsUi.TextMinHeight,
                1f);
            var unknownPolicyButton = AuraToolsUi.AddButton(policyRow.transform, "切换策略", () =>
            {
                autoBattle.UnknownActionPolicy = NextAutoBattleUnknownPolicy(autoBattle.UnknownActionPolicy);
                autoBattle.Normalize();
                AuraToolsConfigService.SaveMatchExperience();
                RebuildPanel(activePanel!.transform);
            }, 96f);
            AttachAutoBattleWorkLock(policyRow, unknownPolicyButton);

            var datasetRow = CreateInlineRow(content, "AutoBattleDatasetExportRow");
            var datasetStatus = AuraToolsUi.AddText(
                content,
                "导出当前游戏版本已加载的卡牌、Buff、敌人、关卡、遗物与祝福",
                AuraToolsUi.HintFontSize,
                TextAnchor.MiddleLeft,
                AuraToolsUi.MutedText,
                AuraToolsUi.TextMinHeight,
                1f);
            AuraToolsUi.AddText(
                datasetRow.transform,
                "游戏数据集",
                AuraToolsUi.BodyFontSize,
                TextAnchor.MiddleLeft,
                AuraToolsUi.Text,
                AuraToolsUi.TextMinHeight,
                1f);
            AuraToolsUi.AddButton(
                datasetRow.transform,
                "一键导出游戏数据集",
                () =>
                {
                    if (AuraToolsCombatKnowledgeRuntime.TryExportBaseGameTables(
                            out var exportedPath,
                            out var exportMessage))
                    {
                        datasetStatus.text = exportMessage + "："
                                             + Path.GetFileName(exportedPath);
                        datasetStatus.color = AuraToolsUi.SuccessText;
                    }
                    else
                    {
                        datasetStatus.text = exportMessage;
                        datasetStatus.color = AuraToolsUi.WarningText;
                    }
                },
                172f);
            AuraToolsUi.AddButton(
                datasetRow.transform,
                "打开目录",
                AuraToolsCombatKnowledgeRuntime.OpenBaseGameTableExportDirectory,
                88f);

            AuraToolsUi.AddText(
                content,
                "① 实战采集：正常完成【世界推演】。每场战斗、奖励选择、卡组成长和最终结局会自动归入同一次旅程。",
                AuraToolsUi.BodyFontSize,
                TextAnchor.MiddleLeft,
                AuraToolsUi.Text,
                AuraToolsUi.TextMinHeight,
                1f);
            CreateAutoBattleToggleRow(content, "进入战斗时自动接管", autoBattle.StartActive, value =>
            {
                autoBattle.StartActive = value;
                AuraToolsConfigService.SaveMatchExperience();
            });
            CreateAutoBattleToggleRow(content, "自动记录实战与完整旅程", autoBattle.CaptureTrainingSamples, value =>
            {
                autoBattle.CaptureTrainingSamples = value;
                AuraToolsConfigService.SaveMatchExperience();
            });
            var journeyCaptureText = AuraToolsUi.AddText(
                content,
                "",
                AuraToolsUi.HintFontSize,
                TextAnchor.MiddleLeft,
                AuraToolsUi.MutedText,
                AuraToolsUi.TextMinHeight,
                1f);
            journeyCaptureText.gameObject
                .AddComponent<AuraToolsAutoBattleJourneyStatusView>()
                .Configure(journeyCaptureText);
            CreateAutoBattleToggleRow(content, "显示 AI 预测标记", autoBattle.ShowPredictionMarkers, value =>
            {
                autoBattle.ShowPredictionMarkers = value;
                AuraToolsConfigService.SaveMatchExperience();
            });
            var trainingModeRow = CreateInlineRow(content, "AutoBattleTrainingModeRow");
            AuraToolsUi.AddText(
                trainingModeRow.transform,
                "训练采集：" + AutoBattleTrainingModeLabel(autoBattle.TrainingMode),
                AuraToolsUi.BodyFontSize,
                TextAnchor.MiddleLeft,
                AuraToolsUi.Text,
                AuraToolsUi.TextMinHeight,
                1f);
            var trainingModeButton = AuraToolsUi.AddButton(trainingModeRow.transform, "切换模式", () =>
            {
                autoBattle.TrainingMode = NextAutoBattleTrainingMode(autoBattle.TrainingMode);
                autoBattle.Normalize();
                AuraToolsConfigService.SaveMatchExperience();
                RebuildPanel(activePanel!.transform);
            }, 96f);
            AttachAutoBattleWorkLock(trainingModeRow, trainingModeButton);

            var trainingPresetRow = CreateInlineRow(content, "AutoBattleTrainingPresetRow");
            AuraToolsUi.AddText(
                trainingPresetRow.transform,
                "训练预设",
                AuraToolsUi.BodyFontSize,
                TextAnchor.MiddleLeft,
                AuraToolsUi.Text,
                AuraToolsUi.TextMinHeight,
                0f,
                96f);
            var trainingPresetButton = AuraToolsUi.AddSelectButton(
                trainingPresetRow.transform,
                new[] { "稳健", "标准", "强适应", "自定义" },
                AutoBattleTrainingPresetIndex(autoBattle.Training.Preset),
                index =>
                {
                    if (index < 3)
                    {
                        autoBattle.Training.ApplyPreset(index switch
                        {
                            1 => AutoBattleTrainingSettings.StandardPreset,
                            2 => AutoBattleTrainingSettings.AdaptivePreset,
                            _ => AutoBattleTrainingSettings.SteadyPreset
                        });
                        AuraToolsConfigService.SaveMatchExperience();
                        RebuildPanel(activePanel!.transform);
                    }
                    else
                    {
                        autoBattle.Training.MarkCustom();
                        AuraToolsConfigService.SaveMatchExperience();
                        RebuildPanel(activePanel!.transform);
                    }
                },
                160f);
            AttachAutoBattleWorkLock(trainingPresetRow, trainingPresetButton);
            AuraToolsUi.AddText(
                trainingPresetRow.transform,
                AutoBattleTrainingPresetSummary(autoBattle.Training),
                AuraToolsUi.HintFontSize,
                TextAnchor.MiddleLeft,
                AuraToolsUi.MutedText,
                AuraToolsUi.TextMinHeight,
                1f);

            var advancedToggleRow = CreateInlineRow(content, "AutoBattleAdvancedTrainingToggleRow");
            AuraToolsUi.AddToggle(advancedToggleRow.transform, AutoBattleAdvancedTrainingExpanded, value =>
            {
                AutoBattleAdvancedTrainingExpanded = value;
                RebuildPanel(activePanel!.transform);
            });
            AuraToolsUi.AddText(
                advancedToggleRow.transform,
                "显示高级参数与自定义场景工具",
                AuraToolsUi.BodyFontSize,
                TextAnchor.MiddleLeft,
                AuraToolsUi.Text,
                AuraToolsUi.TextMinHeight,
                1f);
            if (AutoBattleAdvancedTrainingExpanded)
            {
                CreateAutoBattleTrainingParameterRows(content, autoBattle);
            }

            AuraToolsUi.AddText(
                content,
                "② 训练底模：直接用固定七层世界推演生成训练轨迹；训练、竞技场和最终验证种子完全隔离。",
                AuraToolsUi.BodyFontSize,
                TextAnchor.MiddleLeft,
                AuraToolsUi.Text,
                AuraToolsUi.TextMinHeight,
                1f);
            var foundationRow = CreateInlineRow(content, "AutoBattleFoundationTrainingRow");
            var foundationStatusText = AuraToolsUi.AddText(
                foundationRow.transform,
                "",
                AuraToolsUi.HintFontSize,
                TextAnchor.MiddleLeft,
                AuraToolsUi.MutedText,
                AuraToolsUi.TextMinHeight,
                1f);
            var foundationButton = AuraToolsUi.AddButton(
                foundationRow.transform,
                "训练底模",
                () =>
                {
                    if (!AuraToolsAutoBattleFoundationRuntime.Queue(
                            autoBattle,
                            out var foundationMessage))
                    {
                        AuraToolsLog.Warn("[AutoBattle][Foundation] " + foundationMessage);
                    }
                },
                104f);
            var cancelFoundationButton = AuraToolsUi.AddButton(
                foundationRow.transform,
                "取消",
                AuraToolsAutoBattleFoundationRuntime.Cancel,
                66f);
            var openFoundationButton = AuraToolsUi.AddButton(
                foundationRow.transform,
                "打开报告",
                AuraToolsAutoBattleFoundationRuntime.OpenResultDirectory,
                88f);
            AuraToolsUi.AddButton(
                foundationRow.transform,
                "外部训练控制台",
                () =>
                {
                    if (AuraToolsFoundationWorkerRuntime.LaunchControlCenter(
                            out var controllerMessage))
                    {
                        AuraToolsLog.Info(
                            "[AutoBattle][FoundationController] "
                            + controllerMessage);
                    }
                    else
                    {
                        AuraToolsLog.Warn(
                            "[AutoBattle][FoundationController] "
                            + controllerMessage);
                    }
                },
                126f);
            foundationRow.AddComponent<AuraToolsAutoBattleFoundationStatusView>().Configure(
                foundationStatusText,
                foundationButton,
                cancelFoundationButton,
                openFoundationButton);
            var foundationSettings = autoBattle.FoundationTraining;
            var foundationPerformanceRow = CreateInlineRow(
                content,
                "AutoBattleFoundationPerformanceRow");
            AddAutoBattleSimulationInt(
                foundationPerformanceRow.transform,
                "CPU 并行线程",
                foundationSettings.Parallelism,
                1,
                32,
                value => foundationSettings.Parallelism = value,
                autoBattle);
            AuraToolsUi.AddText(
                foundationPerformanceRow.transform,
                "独立 .NET 8 训练器同时用于世界推演和确定性并行 minibatch；训练阶段支持心跳、提前停止与断点续训",
                AuraToolsUi.HintFontSize,
                TextAnchor.MiddleLeft,
                AuraToolsUi.MutedText,
                AuraToolsUi.TextMinHeight,
                1f);
            if (AutoBattleAdvancedTrainingExpanded)
            {
                var foundationSeedRow = CreateInlineRow(
                    content,
                    "AutoBattleFoundationSeedRow");
                AuraToolsUi.AddToggle(
                    foundationSeedRow.transform,
                    foundationSettings.RandomizeRunSeed,
                    value =>
                    {
                        foundationSettings.RandomizeRunSeed = value;
                        autoBattle.Normalize();
                        AuraToolsConfigService.SaveMatchExperience();
                    });
                AuraToolsUi.AddText(
                    foundationSeedRow.transform,
                    "每次训练生成新 RunSeed",
                    AuraToolsUi.HintFontSize,
                    TextAnchor.MiddleLeft,
                    AuraToolsUi.Text,
                    AuraToolsUi.TextMinHeight,
                    0f,
                    146f);
                AddAutoBattleFoundationUlong(
                    foundationSeedRow.transform,
                    "复现 RunSeed",
                    foundationSettings.RunSeed,
                    value => foundationSettings.RunSeed = value,
                    autoBattle);

                var foundationStrategyRow = CreateInlineRow(
                    content,
                    "AutoBattleFoundationStrategyRow");
                AuraToolsUi.AddToggle(
                    foundationStrategyRow.transform,
                    foundationSettings.EnableCurriculum,
                    value =>
                    {
                        foundationSettings.EnableCurriculum = value;
                        autoBattle.Normalize();
                        AuraToolsConfigService.SaveMatchExperience();
                    });
                AuraToolsUi.AddText(
                    foundationStrategyRow.transform,
                    "课程难度",
                    AuraToolsUi.HintFontSize,
                    TextAnchor.MiddleLeft,
                    AuraToolsUi.Text,
                    AuraToolsUi.TextMinHeight,
                    0f,
                    78f);
                AuraToolsUi.AddToggle(
                    foundationStrategyRow.transform,
                    foundationSettings.EnableStratifiedReplay,
                    value =>
                    {
                        foundationSettings.EnableStratifiedReplay = value;
                        autoBattle.Normalize();
                        AuraToolsConfigService.SaveMatchExperience();
                    });
                AuraToolsUi.AddText(
                    foundationStrategyRow.transform,
                    "普通/高级分层回放",
                    AuraToolsUi.HintFontSize,
                    TextAnchor.MiddleLeft,
                    AuraToolsUi.Text,
                    AuraToolsUi.TextMinHeight,
                    1f);

                var foundationAdaptiveRow = CreateInlineRow(
                    content,
                    "AutoBattleFoundationAdaptiveRow");
                AuraToolsUi.AddToggle(
                    foundationAdaptiveRow.transform,
                    foundationSettings.EnableArenaRecovery,
                    value =>
                    {
                        foundationSettings.EnableArenaRecovery = value;
                        autoBattle.Normalize();
                        AuraToolsConfigService.SaveMatchExperience();
                    });
                AuraToolsUi.AddText(
                    foundationAdaptiveRow.transform,
                    "竞技场恢复",
                    AuraToolsUi.HintFontSize,
                    TextAnchor.MiddleLeft,
                    AuraToolsUi.Text,
                    AuraToolsUi.TextMinHeight,
                    0f,
                    90f);
                AuraToolsUi.AddToggle(
                    foundationAdaptiveRow.transform,
                    foundationSettings.EnableTuningArena,
                    value =>
                    {
                        foundationSettings.EnableTuningArena = value;
                        autoBattle.Normalize();
                        AuraToolsConfigService.SaveMatchExperience();
                    });
                AuraToolsUi.AddText(
                    foundationAdaptiveRow.transform,
                    "Top-K 调参",
                    AuraToolsUi.HintFontSize,
                    TextAnchor.MiddleLeft,
                    AuraToolsUi.Text,
                    AuraToolsUi.TextMinHeight,
                    1f);

                var foundationAcceptanceRow = CreateInlineRow(
                    content,
                    "AutoBattleFoundationAcceptanceRow");
                AddAutoBattleFoundationDouble(
                    foundationAcceptanceRow.transform,
                    "普通验收率",
                    foundationSettings.NormalAcceptanceRate,
                    0.5d,
                    1d,
                    value => foundationSettings.NormalAcceptanceRate = value,
                    autoBattle);
                AddAutoBattleFoundationDouble(
                    foundationAcceptanceRow.transform,
                    "高级验收率",
                    foundationSettings.AdvancedAcceptanceRate,
                    0.1d,
                    1d,
                    value => foundationSettings.AdvancedAcceptanceRate = value,
                    autoBattle);

                var foundationSuccessArchiveRow = CreateInlineRow(
                    content,
                    "AutoBattleFoundationSuccessArchiveRow");
                AuraToolsUi.AddToggle(
                    foundationSuccessArchiveRow.transform,
                    foundationSettings.EnableSuccessCaseArchive,
                    value =>
                    {
                        foundationSettings.EnableSuccessCaseArchive = value;
                        autoBattle.Normalize();
                        AuraToolsConfigService.SaveMatchExperience();
                    });
                AuraToolsUi.AddText(
                    foundationSuccessArchiveRow.transform,
                    "成功案例库",
                    AuraToolsUi.HintFontSize,
                    TextAnchor.MiddleLeft,
                    AuraToolsUi.Text,
                    AuraToolsUi.TextMinHeight,
                    0f,
                    90f);
                AddAutoBattleFoundationDouble(
                    foundationSuccessArchiveRow.transform,
                    "教师回放占比",
                    foundationSettings.SuccessExpertReplayShare,
                    0d,
                    0.4d,
                    value =>
                        foundationSettings.SuccessExpertReplayShare = value,
                    autoBattle);

                var foundationModelRow = CreateInlineRow(
                    content,
                    "AutoBattleFoundationModelTrainingRow");
                AddAutoBattleSimulationInt(
                    foundationModelRow.transform,
                    "底模最大 Epoch",
                    foundationSettings.ModelEpochs,
                    5,
                    200,
                    value => foundationSettings.ModelEpochs = value,
                    autoBattle);
                AddAutoBattleSimulationInt(
                    foundationModelRow.transform,
                    "提前停止耐心",
                    foundationSettings.ModelEarlyStoppingPatience,
                    1,
                    30,
                    value =>
                        foundationSettings.ModelEarlyStoppingPatience = value,
                    autoBattle);
                var foundationBatchRow = CreateInlineRow(
                    content,
                    "AutoBattleFoundationBatchTrainingRow");
                AddAutoBattleSimulationInt(
                    foundationBatchRow.transform,
                    "Minibatch",
                    foundationSettings.ModelBatchSize,
                    8,
                    512,
                    value => foundationSettings.ModelBatchSize = value,
                    autoBattle);
                AddAutoBattleSimulationInt(
                    foundationBatchRow.transform,
                    "Replay 战斗上限",
                    foundationSettings.ModelReplayEpisodeLimit,
                    64,
                    20000,
                    value =>
                        foundationSettings.ModelReplayEpisodeLimit = value,
                    autoBattle);
                var foundationExplorationRow = CreateInlineRow(
                    content,
                    "AutoBattleFoundationExplorationRow");
                AddAutoBattleFoundationDouble(
                    foundationExplorationRow.transform,
                    "自博弈探索率",
                    foundationSettings.SelfPlayExplorationProbability,
                    0d,
                    0.5d,
                    value =>
                        foundationSettings.SelfPlayExplorationProbability = value,
                    autoBattle);
                AddAutoBattleFoundationDouble(
                    foundationExplorationRow.transform,
                    "探索温度",
                    foundationSettings.SelfPlayExplorationTemperature,
                    0.1d,
                    5d,
                    value =>
                        foundationSettings.SelfPlayExplorationTemperature = value,
                    autoBattle);
            }
            AuraToolsUi.AddText(
                content,
                "默认训练 "
                + foundationSettings.Iterations
                + " 轮 × "
                + foundationSettings.TrainingCampaignsPerIteration
                + " 次冒险；最终隔离验证为普通 "
                + foundationSettings.NormalValidationCampaigns
                + " 次、高级 "
                + foundationSettings.AdvancedValidationCampaigns
                + " 次。必须分别达到 "
                + (int)Math.Ceiling(
                    foundationSettings.NormalValidationCampaigns
                    * foundationSettings.NormalAcceptanceRate)
                + "/"
                + foundationSettings.NormalValidationCampaigns
                + " 与至少 "
                + (int)Math.Ceiling(
                    foundationSettings.AdvancedValidationCampaigns
                    * foundationSettings.AdvancedAcceptanceRate)
                + "/"
                + foundationSettings.AdvancedValidationCampaigns
                + "，才保存为 career_1 正式底模；0% 胜率只记为课程检查点。",
                AuraToolsUi.HintFontSize,
                TextAnchor.MiddleLeft,
                AuraToolsUi.MutedText,
                AuraToolsUi.TextMinHeight,
                1f);

            AuraToolsUi.AddText(
                content,
                "③ 训练玩家候选：底模达标后，再读取新的实战样本和完整旅程进行角色策略微调。",
                AuraToolsUi.BodyFontSize,
                TextAnchor.MiddleLeft,
                AuraToolsUi.Text,
                AuraToolsUi.TextMinHeight,
                1f);
            var modelRow = CreateInlineRow(content, "AutoBattleModelActionRow");
            var trainingStatusText = AuraToolsUi.AddText(
                modelRow.transform,
                "",
                AuraToolsUi.HintFontSize,
                TextAnchor.MiddleLeft,
                AuraToolsUi.MutedText,
                AuraToolsUi.TextMinHeight,
                1f);
            var generateButton = AuraToolsUi.AddButton(modelRow.transform, "训练候选", () =>
            {
                if (!AuraToolsAutoBattleModelRuntime.QueueGenerateCandidate(autoBattle.Profile))
                {
                    AuraToolsLog.Warn("[AutoBattle][Training] 本地训练任务正在运行或未能提交");
                }
            }, 112f);
            var cancelTrainingButton = AuraToolsUi.AddButton(
                modelRow.transform,
                "取消",
                () => AuraToolsAutoBattleModelRuntime.CancelTraining(autoBattle.Profile),
                66f);
            AuraToolsUi.AddText(
                content,
                "候选只保存在本机；正式模拟评估不回流训练集。每批训练会保留快照，世界推演会另存构筑与终局战斗报告。",
                AuraToolsUi.HintFontSize,
                TextAnchor.MiddleLeft,
                AuraToolsUi.MutedText,
                AuraToolsUi.TextMinHeight,
                1f);
            var clearDataRow = CreateInlineRow(content, "AutoBattleClearDataRow");
            AuraToolsUi.AddText(
                clearDataRow.transform,
                "重训前清场（不可恢复）：删除玩家样本、训练快照、候选/冠军/模型库、底模任务、断点、案例库及报告；保留知识包与规则配置。",
                AuraToolsUi.HintFontSize,
                TextAnchor.MiddleLeft,
                AuraToolsUi.WarningText,
                AuraToolsUi.TextMinHeight,
                1f);
            AuraToolsUi.AddButton(clearDataRow.transform, "清空全部训练数据", () =>
            {
                if (AuraToolsAutoBattleModelRuntime.TryClearAllCombatLearningData(
                        out var clearMessage))
                {
                    AuraToolsLog.Info("[AutoBattle][Clear] " + clearMessage);
                }
                else
                {
                    AuraToolsLog.Warn("[AutoBattle][Clear] " + clearMessage);
                }
                RebuildPanel(activePanel!.transform);
            }, 142f);
            CreateAutoBattleSimulationRows(content, autoBattle);

            AuraToolsUi.AddText(
                content,
                "⑤ 游戏主体验证：在真实 FightManager、RoleTable 与卡牌脚本上自动挑战四名最终首领；默认隐藏战斗画面，只保留一小轮主体验收。",
                AuraToolsUi.BodyFontSize,
                TextAnchor.MiddleLeft,
                AuraToolsUi.Text,
                AuraToolsUi.TextMinHeight,
                1f);
            var gameValidationRow = CreateInlineRow(
                content,
                "AutoBattleGameValidationRow");
            var gameValidationStatusText = AuraToolsUi.AddText(
                gameValidationRow.transform,
                "",
                AuraToolsUi.HintFontSize,
                TextAnchor.MiddleLeft,
                AuraToolsUi.MutedText,
                AuraToolsUi.TextMinHeight,
                1f);
            var runGameValidationButton = AuraToolsUi.AddButton(
                gameValidationRow.transform,
                "实机验证",
                () =>
                {
                    if (!AuraToolsAutoBattleGameValidationRuntime.Queue(
                            autoBattle,
                            out var validationMessage))
                    {
                        AuraToolsLog.Warn(
                            "[AutoBattle][GameValidation] " + validationMessage);
                    }
                },
                92f);
            var cancelGameValidationButton = AuraToolsUi.AddButton(
                gameValidationRow.transform,
                "取消",
                AuraToolsAutoBattleGameValidationRuntime.Cancel,
                66f);
            var openGameValidationButton = AuraToolsUi.AddButton(
                gameValidationRow.transform,
                "打开回执",
                AuraToolsAutoBattleGameValidationRuntime.OpenResultDirectory,
                88f);
            gameValidationRow
                .AddComponent<AuraToolsAutoBattleGameValidationStatusView>()
                .Configure(
                    gameValidationStatusText,
                    runGameValidationButton,
                    cancelGameValidationButton,
                    openGameValidationButton);

            var gameValidationSettings = autoBattle.GameValidation;
            var gameValidationOptionsRow = CreateInlineRow(
                content,
                "AutoBattleGameValidationOptionsRow");
            AuraToolsUi.AddToggle(
                gameValidationOptionsRow.transform,
                gameValidationSettings.HidePresentation,
                value =>
                {
                    gameValidationSettings.HidePresentation = value;
                    autoBattle.Normalize();
                    AuraToolsConfigService.SaveMatchExperience();
                });
            AuraToolsUi.AddText(
                gameValidationOptionsRow.transform,
                "隐藏战斗画面",
                AuraToolsUi.HintFontSize,
                TextAnchor.MiddleLeft,
                AuraToolsUi.Text,
                AuraToolsUi.TextMinHeight,
                0f,
                112f);
            AddAutoBattleSimulationInt(
                gameValidationOptionsRow.transform,
                "每名最终首领场次",
                gameValidationSettings.RepetitionsPerFinalBoss,
                1,
                20,
                value => gameValidationSettings.RepetitionsPerFinalBoss = value,
                autoBattle);
            AuraToolsUi.AddText(
                content,
                "验证使用游戏主体真实运行时，不把结果回流训练集；角色状态会在每场后还原。回执绑定模型工件、游戏版本、规则集与权威程序包，任一变化都必须重验。",
                AuraToolsUi.HintFontSize,
                TextAnchor.MiddleLeft,
                AuraToolsUi.MutedText,
                AuraToolsUi.TextMinHeight,
                1f);

            AuraToolsUi.AddText(
                content,
                "⑥ 保存与应用：标准评估和游戏主体验证门禁都通过后，才可把玩家候选保存为新冠军；底模训练成功后先保持关闭。",
                AuraToolsUi.BodyFontSize,
                TextAnchor.MiddleLeft,
                AuraToolsUi.Text,
                AuraToolsUi.TextMinHeight,
                1f);
            var modelModeRow = CreateInlineRow(content, "AutoBattleModelModeRow");
            AuraToolsUi.AddText(
                modelModeRow.transform,
                "当前应用状态：" + AutoBattleModelModeLabel(autoBattle.TrainedModelMode),
                AuraToolsUi.BodyFontSize,
                TextAnchor.MiddleLeft,
                AuraToolsUi.Text,
                AuraToolsUi.TextMinHeight,
                1f);
            var modelModeButton = AuraToolsUi.AddButton(modelModeRow.transform, "切换状态", () =>
            {
                autoBattle.TrainedModelMode = NextAutoBattleModelMode(autoBattle.TrainedModelMode);
                autoBattle.Normalize();
                AuraToolsConfigService.SaveMatchExperience();
                RebuildPanel(activePanel!.transform);
            }, 96f);
            var promotionRow = CreateInlineRow(content, "AutoBattlePromotionActionRow");
            AuraToolsUi.AddText(
                promotionRow.transform,
                "晋级操作",
                AuraToolsUi.HintFontSize,
                TextAnchor.MiddleLeft,
                AuraToolsUi.MutedText,
                AuraToolsUi.TextMinHeight,
                1f);
            var importButton = AuraToolsUi.AddButton(promotionRow.transform, "保存新冠军", () =>
            {
                if (!AuraToolsAutoBattleModelRuntime.QueueImportCandidate(autoBattle.Profile))
                {
                    AuraToolsLog.Warn("[AutoBattle][Import] 保存任务正在运行或候选尚未通过门禁");
                }
            }, 112f);
            var rollbackButton = AuraToolsUi.AddButton(promotionRow.transform, "回退上一冠军", () =>
            {
                if (!AuraToolsAutoBattleModelRuntime.QueueRollbackChampion(autoBattle.Profile))
                {
                    AuraToolsLog.Warn("[AutoBattle][Rollback] 回退任务正在运行或未能提交");
                }
            }, 112f);
            modelRow.AddComponent<AuraToolsAutoBattleTrainingStatusView>().Configure(
                autoBattle.Profile,
                autoBattle.TrainedModelMode,
                trainingStatusText,
                generateButton,
                importButton,
                rollbackButton,
                cancelTrainingButton,
                modelModeButton);
        }, autoBattle.Enabled ? AuraToolsUi.SuccessText : AuraToolsUi.MutedText);

        var feast = AuraToolsConfigService.MatchExperience.Feast;
        CreateSubmodule(parent, "一键美餐", feast.Enabled, value =>
        {
            feast.Enabled = value;
            AuraToolsConfigService.SaveMatchExperience();
        }, content =>
        {
            var row = CreateInlineRow(content, "FeastConfigRow");
            var scannedRoleCount = RoleCatalog.GetRoles().Count;
            AuraToolsUi.AddText(
                row.transform,
                "角色：" + scannedRoleCount
                + "，已配置：" + feast.Roles.Count
                + "，注册CG：" + AuraToolsFeastRuntime.RegisteredFeastCgCount()
                + "，CG仅本地播放",
                AuraToolsUi.BodyFontSize,
                TextAnchor.MiddleLeft,
                AuraToolsUi.Text,
                AuraToolsUi.TextMinHeight,
                1f);
            AuraToolsUi.AddButton(row.transform, "按角色配置", () => AuraToolsFeastRoleEditor.Show(activePanel!.transform), 112f);
            feast.PlayCg = true;
        }, feast.Enabled ? AuraToolsUi.SuccessText : AuraToolsUi.MutedText);

        CreateSubmodule(parent, "随身保险箱", AuraToolsConfigService.MatchExperience.SafeBox.Enabled, value =>
        {
            AuraToolsConfigService.MatchExperience.SafeBox.Enabled = value;
            AuraToolsConfigService.SaveMatchExperience();
        }, content =>
        {
            AuraToolsUi.AddText(content, "开启后在冒险 TopBar 增加随身保险箱入口；功能只提供总开关。", AuraToolsUi.HintFontSize, TextAnchor.MiddleLeft, AuraToolsUi.MutedText, AuraToolsUi.TextMinHeight, 1f);
        });

        CreateSubmodule(parent, "联机MOD配置同步", AuraToolsConfigService.MatchExperience.ModSync.Enabled, value =>
        {
            AuraToolsConfigService.MatchExperience.ModSync.Enabled = value;
            AuraToolsConfigService.SaveMatchExperience();
        }, content =>
        {
            AuraToolsUi.AddText(content, "开启后在联机大厅开始按钮下方显示 MOD 配置入口；非主机玩家可一键同步房主启用状态。", AuraToolsUi.HintFontSize, TextAnchor.MiddleLeft, AuraToolsUi.MutedText, AuraToolsUi.TextMinHeight, 1f);
        }, AuraToolsConfigService.MatchExperience.ModSync.Enabled ? AuraToolsUi.SuccessText : AuraToolsUi.MutedText);

        var damageMeter = AuraToolsConfigService.MatchExperience.DamageMeter;
        CreateSubmodule(parent, "DPS统计模块", damageMeter.Enabled, value =>
        {
            damageMeter.Enabled = value;
            AuraToolsConfigService.SaveMatchExperience();
            AuraToolsDamageMeterRuntime.SetVisible(value && damageMeter.ShowPanelByDefault);
        }, content =>
        {
            CreateDamageMeterToggleRow(content, "只显示友方统计", damageMeter.FriendlyOnly, value =>
            {
                damageMeter.FriendlyOnly = value;
                damageMeter.Normalize();
                AuraToolsConfigService.SaveMatchExperience();
            });

            var historyRow = CreateInlineRow(content, "DamageMeterOutOfRunHistoryRow");
            AuraToolsUi.AddText(
                historyRow.transform,
                "局外历史记录：" + AuraToolsDamageMeterRuntime.OutOfRunHistoryCount + " 条",
                AuraToolsUi.BodyFontSize,
                TextAnchor.MiddleLeft,
                AuraToolsUi.Text,
                AuraToolsUi.TextMinHeight,
                1f);
            AuraToolsUi.AddButton(
                historyRow.transform,
                "查看局外历史",
                AuraToolsDamageMeterRuntime.OpenOutOfRunHistory,
                128f);

            AuraToolsUi.AddText(content, "声明：该模块初始版本代码由【哈基米】提供，后续由【Aura】进行维护和功能开发。", AuraToolsUi.HintFontSize, TextAnchor.MiddleLeft, AuraToolsUi.MutedText, AuraToolsUi.TextMinHeight, 1f);
        }, damageMeter.Enabled ? AuraToolsUi.SuccessText : AuraToolsUi.MutedText);

        CreateSubmodule(parent, "\u6280\u80fdCG\u7279\u6548\u7ba1\u7406", AuraToolsConfigService.SkillCg.Enabled, value =>
        {
            AuraToolsConfigService.SkillCg.Enabled = value;
            AuraToolsConfigService.SaveSkillCg();
        }, content =>
        {
            var row = CreateInlineRow(content, "SkillCgConfigRow");
            var ruleCount = AuraToolsConfigService.SkillCg.Roles.Values.Sum(role => role.Rules.Count);
            AuraToolsUi.AddText(row.transform, "\u672c\u5730\u89c4\u5219\uff1a" + ruleCount + "\uff0c\u8054\u673a\u540c\u6b65\uff1a" + (AuraToolsConfigService.SkillCg.SyncRemote ? "\u5f00" : "\u5173"), AuraToolsUi.BodyFontSize, TextAnchor.MiddleLeft, AuraToolsUi.Text, AuraToolsUi.TextMinHeight, 1f);
            AuraToolsUi.AddButton(row.transform, "\u914d\u7f6e", () => AuraToolsSkillCgEditor.Show(activePanel!.transform), 88f);
            AuraToolsUi.AddButton(row.transform, AuraToolsConfigService.SkillCg.SyncRemote ? "\u5173\u95ed\u540c\u6b65" : "\u5f00\u542f\u540c\u6b65", () =>
            {
                AuraToolsConfigService.SkillCg.SyncRemote = !AuraToolsConfigService.SkillCg.SyncRemote;
                AuraToolsConfigService.SaveSkillCg();
                RebuildPanel(activePanel!.transform);
            }, 96f);
        });

        CreateSubmodule(parent, "\u5361\u724c\u4f7f\u7528CG", AuraToolsConfigService.SkillCg.CardUseCg.Enabled, value =>
        {
            AuraToolsConfigService.SkillCg.CardUseCg.Enabled = value;
            AuraToolsConfigService.SaveSkillCg();
        }, content =>
        {
            var row = CreateInlineRow(content, "CardUseCgConfigRow");
            var registeredCount = AuraCg.Shared.SkillCgArbiterRuntime.GetRegisteredCardUseCgEntries().Count;
            AuraToolsUi.AddText(row.transform, "\u5df2\u6ce8\u518c\uff1a" + registeredCount, AuraToolsUi.BodyFontSize, TextAnchor.MiddleLeft, AuraToolsUi.Text, AuraToolsUi.TextMinHeight, 1f);
            AuraToolsUi.AddButton(row.transform, "\u7ba1\u7406", () => AuraToolsSkillCgManager.Show(activePanel!.transform), 88f);
        });
    }

    private static void CreateGameParametersSection(Transform parent)
    {
        CreateSectionLabel(parent, "游戏参数");
        var autoBattle = AuraToolsConfigService.MatchExperience.AutoBattle;
        autoBattle.Normalize();
        var parameters = autoBattle.GameParameters;
        var preset = parameters.ActivePreset;

        var presetRow = CreateInlineRow(parent, "AutoBattleGamePresetRow");
        AuraToolsUi.AddText(
            presetRow.transform,
            "角色 + 使魔预设",
            AuraToolsUi.BodyFontSize,
            TextAnchor.MiddleLeft,
            AuraToolsUi.Text,
            AuraToolsUi.TextMinHeight,
            0f,
            128f);
        var presetButton = AuraToolsUi.AddSelectButton(
            presetRow.transform,
            parameters.Presets.Select(item => item.DisplayName).ToArray(),
            Math.Max(
                0,
                parameters.Presets.FindIndex(item => string.Equals(
                    item.Id,
                    parameters.SelectedPresetId,
                    StringComparison.OrdinalIgnoreCase))),
            index =>
            {
                if (index < 0 || index >= parameters.Presets.Count)
                {
                    return;
                }
                parameters.SelectedPresetId = parameters.Presets[index].Id;
                autoBattle.Normalize();
                AuraToolsConfigService.SaveMatchExperience();
                RebuildPanel(activePanel!.transform);
            },
            172f);
        var addPresetButton = AuraToolsUi.AddButton(
            presetRow.transform,
            "新增预设",
            () =>
            {
                var number = parameters.Presets.Count + 1;
                var id = "preset-" + number;
                while (parameters.Presets.Any(item => string.Equals(
                           item.Id,
                           id,
                           StringComparison.OrdinalIgnoreCase)))
                {
                    id = "preset-" + ++number;
                }
                var clone = preset.CloneAs(id, "游戏预设 " + number);
                parameters.Presets.Add(clone);
                parameters.SelectedPresetId = clone.Id;
                autoBattle.Normalize();
                AuraToolsConfigService.SaveMatchExperience();
                RebuildPanel(activePanel!.transform);
            },
            88f);
        var deletePresetButton = AuraToolsUi.AddButton(
            presetRow.transform,
            "删除",
            () =>
            {
                if (parameters.Presets.Count <= 1)
                {
                    return;
                }
                parameters.Presets.Remove(preset);
                parameters.SelectedPresetId = parameters.Presets[0].Id;
                autoBattle.Normalize();
                AuraToolsConfigService.SaveMatchExperience();
                RebuildPanel(activePanel!.transform);
            },
            66f);
        deletePresetButton.interactable = parameters.Presets.Count > 1;
        AttachAutoBattleWorkLock(
            presetRow,
            presetButton,
            addPresetButton,
            deletePresetButton);

        var roles = RoleCatalog.GetRoles();
        var partners = PartnerCatalog.GetPartners();
        var identityRow = CreateInlineRow(parent, "AutoBattleGameIdentityRow");
        AuraToolsUi.AddText(
            identityRow.transform,
            "角色",
            AuraToolsUi.BodyFontSize,
            TextAnchor.MiddleLeft,
            AuraToolsUi.Text,
            AuraToolsUi.TextMinHeight,
            0f,
            56f);
        var roleItems = roles.Count > 0
            ? roles.ToList()
            : new List<RoleInfo>
            {
                new() { Id = preset.RoleId, DisplayName = preset.RoleId }
            };
        var roleButton = AuraToolsUi.AddSelectButton(
            identityRow.transform,
            roleItems.Select(item => item.DisplayName).ToArray(),
            Math.Max(
                0,
                roleItems.FindIndex(item => string.Equals(
                    item.Id,
                    preset.RoleId,
                    StringComparison.OrdinalIgnoreCase))),
            index =>
            {
                if (index < 0 || index >= roleItems.Count)
                {
                    return;
                }
                preset.RoleId = roleItems[index].Id;
                preset.ResolvedRoleSkillIds = roleItems[index].Skills
                    .Select(item => item.Id)
                    .ToList();
                preset.ResolvedRoleInitialStatuses =
                    new Dictionary<string, int>(
                        roleItems[index].InitialStatuses,
                        StringComparer.OrdinalIgnoreCase);
                preset.ResolvedRoleSkillCooldownTurns = roleItems[index].Skills
                    .GroupBy(item => item.Id, StringComparer.OrdinalIgnoreCase)
                    .ToDictionary(
                        group => group.Key,
                        group => Math.Max(
                            1,
                            group.First().CooldownTurns),
                        StringComparer.OrdinalIgnoreCase);
                autoBattle.Normalize();
                AuraToolsConfigService.SaveMatchExperience();
                RebuildPanel(activePanel!.transform);
            },
            164f);
        AuraToolsUi.AddText(
            identityRow.transform,
            "使魔",
            AuraToolsUi.BodyFontSize,
            TextAnchor.MiddleLeft,
            AuraToolsUi.Text,
            AuraToolsUi.TextMinHeight,
            0f,
            56f);
        var partnerItems = partners.Count > 0
            ? partners.ToList()
            : new List<PartnerInfo>
            {
                new() { Id = preset.PartnerId, DisplayName = preset.PartnerId }
            };
        var partnerButton = AuraToolsUi.AddSelectButton(
            identityRow.transform,
            partnerItems.Select(item => item.DisplayName).ToArray(),
            Math.Max(
                0,
                partnerItems.FindIndex(item => string.Equals(
                    item.Id,
                    preset.PartnerId,
                    StringComparison.OrdinalIgnoreCase))),
            index =>
            {
                if (index < 0 || index >= partnerItems.Count)
                {
                    return;
                }
                preset.PartnerId = partnerItems[index].Id;
                preset.ResolvedFamiliarBlessingIds =
                    partnerItems[index].BlessingIds.ToList();
                autoBattle.Normalize();
                AuraToolsConfigService.SaveMatchExperience();
                RebuildPanel(activePanel!.transform);
            },
            164f);
        AttachAutoBattleWorkLock(identityRow, roleButton, partnerButton);

        var deckRow = CreateInlineRow(parent, "AutoBattlePreferredDeckSizeRow");
        var deckMinimum = AddAutoBattleSimulationInt(
            deckRow.transform,
            "卡组倾向下限",
            preset.PreferredDeckSizeMinimum,
            1,
            80,
            value => preset.PreferredDeckSizeMinimum = value,
            autoBattle);
        var deckMaximum = AddAutoBattleSimulationInt(
            deckRow.transform,
            "卡组倾向上限",
            preset.PreferredDeckSizeMaximum,
            1,
            80,
            value => preset.PreferredDeckSizeMaximum = value,
            autoBattle);
        AttachAutoBattleWorkLock(deckRow, deckMinimum, deckMaximum);

        const string packFoldoutKey = "AutoBattle.GameParameters.CardPacks";
        var packExpanded = FoldoutStates.TryGetValue(
            packFoldoutKey,
            out var storedExpanded)
            && storedExpanded;
        var packHeader = CreateInlineRow(parent, "AutoBattleRewardCardPackHeader");
        AuraToolsUi.AddText(
            packHeader.transform,
            "奖励卡包范围：" + preset.EnabledRewardCardPackIds.Count + " 个",
            AuraToolsUi.BodyFontSize,
            TextAnchor.MiddleLeft,
            AuraToolsUi.Text,
            AuraToolsUi.TextMinHeight,
            1f);
        var packFoldoutButton = AuraToolsUi.AddButton(
            packHeader.transform,
            packExpanded ? "收起" : "展开",
            () =>
            {
                FoldoutStates[packFoldoutKey] = !packExpanded;
                RebuildPanel(activePanel!.transform);
            },
            72f);
        AttachAutoBattleWorkLock(packHeader, packFoldoutButton);
        if (!packExpanded)
        {
            return;
        }

        foreach (var pack in AuraToolsAutoBattleGameParameterRuntime
                     .GetRewardCardPacks())
        {
            var packRow = CreateInlineRow(
                parent,
                "AutoBattleRewardCardPack-" + pack.Id);
            var enabled = preset.EnabledRewardCardPackIds.Contains(
                pack.Id,
                StringComparer.OrdinalIgnoreCase);
            var packToggle = AuraToolsUi.AddToggle(
                packRow.transform,
                enabled,
                value =>
                {
                    preset.EnabledRewardCardPackIds.RemoveAll(item =>
                        string.Equals(
                            item,
                            pack.Id,
                            StringComparison.OrdinalIgnoreCase));
                    if (value || pack.Required)
                    {
                        preset.EnabledRewardCardPackIds.Add(pack.Id);
                    }
                    autoBattle.Normalize();
                    AuraToolsConfigService.SaveMatchExperience();
                });
            packToggle.interactable = !pack.Required;
            AuraToolsUi.AddText(
                packRow.transform,
                pack.DisplayName
                + "  ["
                + pack.Id
                + "]"
                + (pack.Required ? "（基础包，固定开启）" : ""),
                AuraToolsUi.BodyFontSize,
                TextAnchor.MiddleLeft,
                pack.Required ? AuraToolsUi.MutedText : AuraToolsUi.Text,
                AuraToolsUi.TextMinHeight,
                1f);
            AttachAutoBattleWorkLock(packRow, packToggle);
        }
    }

    private static void CreateLoggingSection(Transform parent)
    {
        CreateSectionLabel(parent, "日志文件");
        CreateSubmodule(parent, "文件日志", AuraToolsConfigService.Logging.Enabled, value =>
        {
            AuraToolsConfigService.Logging.Enabled = value;
            AuraToolsConfigService.SaveLogging();
        }, content =>
        {
            var settings = AuraToolsConfigService.Logging;
            var diagnosticsRow = CreateInlineRow(content, "PerformanceDiagnosticsRow");
            AuraToolsUi.AddToggle(diagnosticsRow.transform, settings.PerformanceDiagnostics, value =>
            {
                settings.PerformanceDiagnostics = value;
                AuraToolsConfigService.SaveLogging();
            });
            AuraToolsUi.AddText(
                diagnosticsRow.transform,
                "性能诊断（重启后生效；会启用高频计数与基准钩子）",
                AuraToolsUi.BodyFontSize,
                TextAnchor.MiddleLeft,
                AuraToolsUi.Text,
                AuraToolsUi.TextMinHeight,
                1f);

            var row = CreateInlineRow(content, "LoggingRow");
            var levelLabels = new List<string> { "Debug", "Info", "Warning", "Error" };
            AuraToolsUi.AddText(row.transform, "最低等级", AuraToolsUi.BodyFontSize, TextAnchor.MiddleLeft, AuraToolsUi.Text, AuraToolsUi.TextMinHeight, 0f, 90f);
            AuraToolsUi.AddSelectButton(row.transform, levelLabels, SelectedLoggingLevelIndex(settings.MinimumLevel), index =>
            {
                if (index >= 0 && index < levelLabels.Count)
                {
                    settings.MinimumLevel = levelLabels[index];
                    settings.Normalize();
                    AuraToolsConfigService.SaveLogging();
                    RebuildPanel(activePanel!.transform);
                }
            }, 180f);
            AuraToolsUi.AddText(row.transform, "队列 " + settings.MaxQueueLength + " / Flush " + settings.FlushIntervalMs + "ms", AuraToolsUi.BodyFontSize, TextAnchor.MiddleLeft, AuraToolsUi.MutedText, AuraToolsUi.TextMinHeight, 1f);

            var mirrorRow = CreateInlineRow(content, "LoggingMirrorRow");
            AuraToolsUi.AddToggle(mirrorRow.transform, settings.MirrorUnityLog, value =>
            {
                settings.MirrorUnityLog = value;
                AuraToolsConfigService.SaveLogging();
            });
            AuraToolsUi.AddText(mirrorRow.transform, "镜像 Unity 日志", AuraToolsUi.BodyFontSize, TextAnchor.MiddleLeft, AuraToolsUi.Text, AuraToolsUi.TextMinHeight, 1f);
            AuraToolsUi.AddToggle(mirrorRow.transform, settings.MirrorCommandsLog, value =>
            {
                settings.MirrorCommandsLog = value;
                AuraToolsConfigService.SaveLogging();
            });
            AuraToolsUi.AddText(mirrorRow.transform, "镜像 Commands 日志", AuraToolsUi.BodyFontSize, TextAnchor.MiddleLeft, AuraToolsUi.Text, AuraToolsUi.TextMinHeight, 1f);

            var sourceRow = CreateInlineRow(content, "LoggingSourceRow");
            AuraToolsUi.AddText(sourceRow.transform, "来源", AuraToolsUi.BodyFontSize, TextAnchor.MiddleLeft, AuraToolsUi.Text, AuraToolsUi.TextMinHeight, 0f, 48f);
            CreateLoggingListToggle(sourceRow.transform, settings.EnabledSources, "AuraTools");
            CreateLoggingListToggle(sourceRow.transform, settings.EnabledSources, "Unity");
            CreateLoggingListToggle(sourceRow.transform, settings.EnabledSources, "Command");

            var unityRow = CreateInlineRow(content, "LoggingUnityTypesRow");
            AuraToolsUi.AddText(unityRow.transform, "Unity 类型", AuraToolsUi.BodyFontSize, TextAnchor.MiddleLeft, AuraToolsUi.Text, AuraToolsUi.TextMinHeight, 0f, 82f);
            foreach (var type in new[] { "Log", "Warning", "Error", "Exception", "Assert" })
            {
                CreateLoggingListToggle(unityRow.transform, settings.UnityLogTypes, type);
            }

            var stackRow = CreateInlineRow(content, "LoggingStackTraceRow");
            AuraToolsUi.AddText(stackRow.transform, "堆栈", AuraToolsUi.BodyFontSize, TextAnchor.MiddleLeft, AuraToolsUi.Text, AuraToolsUi.TextMinHeight, 0f, 60f);
            var stackLabels = new List<string> { "关闭", "仅错误", "全部" };
            var stackValues = new List<string> { LoggingStackTraceModes.Off, LoggingStackTraceModes.ErrorsOnly, LoggingStackTraceModes.All };
            AuraToolsUi.AddSelectButton(stackRow.transform, stackLabels, SelectedLoggingStackIndex(settings.StackTraceMode), index =>
            {
                if (index >= 0 && index < stackValues.Count)
                {
                    settings.StackTraceMode = stackValues[index];
                    AuraToolsConfigService.SaveLogging();
                    RebuildPanel(activePanel!.transform);
                }
            }, 180f);
            AuraToolsUi.AddText(stackRow.transform, "Command tag 可在 JSON 的 includedCommandTags / excludedCommandTags 中长期配置。", AuraToolsUi.HintFontSize, TextAnchor.MiddleLeft, AuraToolsUi.MutedText, AuraToolsUi.TextMinHeight, 1f);

            var queueRow = CreateInlineRow(content, "LoggingQueueRow");
            AuraToolsUi.AddText(queueRow.transform, "队列上限", AuraToolsUi.BodyFontSize, TextAnchor.MiddleLeft, AuraToolsUi.Text, AuraToolsUi.TextMinHeight, 0f, 72f);
            AuraToolsUi.AddInput(queueRow.transform, settings.MaxQueueLength.ToString(), value =>
            {
                if (int.TryParse(value, out var parsed))
                {
                    settings.MaxQueueLength = parsed;
                    settings.Normalize();
                    AuraToolsConfigService.SaveLogging();
                }
            }, 110f);
            AuraToolsUi.AddText(queueRow.transform, "Flush ms", AuraToolsUi.BodyFontSize, TextAnchor.MiddleLeft, AuraToolsUi.Text, AuraToolsUi.TextMinHeight, 0f, 72f);
            AuraToolsUi.AddInput(queueRow.transform, settings.FlushIntervalMs.ToString(), value =>
            {
                if (int.TryParse(value, out var parsed))
                {
                    settings.FlushIntervalMs = parsed;
                    settings.Normalize();
                    AuraToolsConfigService.SaveLogging();
                }
            }, 110f);
            AuraToolsUi.AddText(row.transform, "默认开启；日志目录：" + AuraToolsConfigService.LogsDirectory, AuraToolsUi.BodyFontSize, TextAnchor.MiddleLeft, AuraToolsUi.Text, AuraToolsUi.TextMinHeight, 1f);
            AuraToolsUi.AddButton(row.transform, "打开目录", () => FileResourceUtil.OpenDirectory(AuraToolsConfigService.LogsDirectory), 92f);
        });
    }

    private static void CreateModeRow(Transform parent, AudioFeatureSettings settings, bool battleBgm)
    {
        var row = CreateInlineRow(parent, "ModeRow");
        AuraToolsUi.AddText(row.transform, "模式：" + (settings.Mode == AudioModes.Advanced ? "高级（按角色）" : "通用"), AuraToolsUi.BodyFontSize, TextAnchor.MiddleLeft, AuraToolsUi.Text, AuraToolsUi.TextMinHeight, 1f);
        AuraToolsUi.AddButton(row.transform, settings.Mode == AudioModes.Advanced ? "切到通用" : "切到高级", () =>
        {
            settings.Mode = settings.Mode == AudioModes.Advanced ? AudioModes.Common : AudioModes.Advanced;
            AuraToolsConfigService.SaveAudio();
            AuraToolsAudioRuntime.RegisterProviders();
            RebuildPanel(activePanel!.transform);
        }, 96f);
        AuraToolsUi.AddButton(row.transform, "高级配置", () => AuraToolsAudioRoleEditor.Show(activePanel!.transform, battleBgm), 96f);
    }

    private static void CreateAudioCommonRow(Transform parent, AudioFeatureSettings settings, bool battleBgm)
    {
        CreateAudioCommonRows(parent, settings, battleBgm);
        return;
    }

    private static void CreateAudioCommonRows(Transform parent, AudioFeatureSettings settings, bool battleBgm)
    {
        var pathRow = CreateInlineRow(parent, "CommonAudioPathRow");
        AuraToolsUi.AddText(pathRow.transform, "通用音频", AuraToolsUi.BodyFontSize, TextAnchor.MiddleLeft, AuraToolsUi.Text, AuraToolsUi.TextMinHeight, 0f, 86f);
        AuraToolsUi.AddInput(pathRow.transform, settings.Common.RelativePath, value =>
        {
            ApplyCommonAudioPath(settings, battleBgm, value, false);
        }, 620f);

        var actionRow = CreateInlineRow(parent, "CommonAudioActionRow");
        AuraToolsUi.AddText(actionRow.transform, DescribeAudioPathStatus(settings.Common.RelativePath) + " / 优先级 " + settings.Common.Priority, AuraToolsUi.BodyFontSize, TextAnchor.MiddleLeft, AuraToolsUi.MutedText, AuraToolsUi.TextMinHeight, 1f);
        AuraToolsUi.AddButton(actionRow.transform, "选择音频", () =>
        {
            OptionalFileDialog.PickAudioFileAsync(FileResourceUtil.CommonAudioDirectory(), result =>
            {
                if (result.Selected)
                {
                    ApplyCommonAudioPath(settings, battleBgm, result.Path, true);
                    return;
                }

                if (result.Status != OptionalFileDialogStatus.Cancelled)
                {
                    AuraToolsLog.Warn("[Settings] audio picker unavailable: " + result.Message);
                }
            });
        }, 88f);
        AuraToolsUi.AddButton(actionRow.transform, "打开目录", () => FileResourceUtil.OpenDirectory(FileResourceUtil.CommonAudioDirectory()), 88f);
    }

    private static void ApplyCommonAudioPath(AudioFeatureSettings settings, bool battleBgm, string path, bool rebuild)
    {
        var trimmed = path?.Trim() ?? "";
        if (string.IsNullOrWhiteSpace(trimmed))
        {
            settings.Common.RelativePath = "";
            AuraToolsConfigService.SaveAudio();
            AuraToolsAudioRuntime.RegisterProviders();
            if (rebuild && activePanel != null)
            {
                RebuildPanel(activePanel.transform);
            }

            return;
        }

        var baseName = battleBgm ? "battle_bgm" : "card_use";
        var imported = FileResourceUtil.ImportAudioPath(trimmed, FileResourceUtil.CommonAudioDirectory(), baseName, out var message);
        if (string.IsNullOrWhiteSpace(imported))
        {
            AuraToolsLog.Warn("[Settings] common audio import rejected; current configuration preserved: " + message);
            if (rebuild && activePanel != null)
            {
                RebuildPanel(activePanel.transform);
            }

            return;
        }

        settings.Common.RelativePath = imported;
        FileResourceUtil.RegisterManualDirectory(
            AuraSharedSystems.Audio,
            "LocalAudio",
            "Global",
            "all",
            AuraToolsIds.ModId,
            "user-imports",
            FileResourceUtil.CommonAudioDirectory(),
            out _);
        AuraToolsConfigService.SaveAudio();
        AuraToolsAudioRuntime.RegisterProviders();
        if (rebuild && activePanel != null)
        {
            RebuildPanel(activePanel.transform);
        }
    }

    private static string DescribeAudioPathStatus(string relativeOrAbsolute)
    {
        if (string.IsNullOrWhiteSpace(relativeOrAbsolute))
        {
            return "未设置音频";
        }

        return File.Exists(AuraToolsConfiguredResourceResolver.ResolveAudioPath(relativeOrAbsolute))
            ? "文件存在"
            : "文件缺失";
    }

    private static void CreateSectionLabel(Transform parent, string title)
    {
        var label = AuraToolsUi.CreateLayout("Section-" + title, parent);
        var labelElement = label.AddComponent<LayoutElement>();
        labelElement.minHeight = AuraToolsUi.SectionHeight;
        labelElement.preferredHeight = AuraToolsUi.SectionHeight;
        AuraToolsUi.AddImage(label, AuraToolsUi.Header);
        var layout = label.AddComponent<HorizontalLayoutGroup>();
        layout.padding = new RectOffset(12, 12, 3, 3);
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        AuraToolsUi.AddText(label.transform, title, AuraToolsUi.SectionFontSize, TextAnchor.MiddleLeft, AuraToolsUi.Accent, AuraToolsUi.TextMinHeight, 1f);
    }

    private static void CreateSubmodule(Transform parent, string title, bool enabled, Action<bool> setEnabled, Action<Transform> buildContent, Color? titleColor = null)
    {
        var box = AuraToolsUi.CreateLayout("Submodule-" + title, parent);
        AuraToolsUi.AddPanelImage(box, AuraToolsUi.Panel);
        var boxLayout = box.AddComponent<VerticalLayoutGroup>();
        boxLayout.padding = new RectOffset(8, 8, 6, 6);
        boxLayout.spacing = 6f;
        boxLayout.childControlWidth = true;
        boxLayout.childControlHeight = true;
        boxLayout.childForceExpandWidth = true;
        boxLayout.childForceExpandHeight = false;

        var header = AuraToolsUi.CreateLayout("Header", box.transform);
        var headerElement = header.AddComponent<LayoutElement>();
        headerElement.minHeight = AuraToolsUi.ModuleHeaderHeight;
        headerElement.preferredHeight = AuraToolsUi.ModuleHeaderHeight;
        var headerImage = AuraToolsUi.AddImage(header, new Color(0f, 0f, 0f, 0.01f));
        var headerLayout = header.AddComponent<HorizontalLayoutGroup>();
        headerLayout.spacing = 8f;
        headerLayout.childControlWidth = true;
        headerLayout.childControlHeight = true;
        headerLayout.childForceExpandWidth = false;
        headerLayout.childForceExpandHeight = false;
        AuraToolsUi.AddToggle(header.transform, enabled, value =>
        {
            setEnabled(value);
            RebuildPanel(activePanel!.transform);
        });
        AuraToolsUi.AddText(header.transform, title, AuraToolsUi.ModuleTitleFontSize, TextAnchor.MiddleLeft, titleColor ?? AuraToolsUi.Text, AuraToolsUi.TextMinHeight, 1f);
        var content = AuraToolsUi.CreateLayout("Content", box.transform);
        var contentLayout = content.AddComponent<VerticalLayoutGroup>();
        contentLayout.padding = new RectOffset(34, 6, 0, 4);
        contentLayout.spacing = 6f;
        contentLayout.childControlWidth = true;
        contentLayout.childControlHeight = true;
        contentLayout.childForceExpandWidth = true;
        contentLayout.childForceExpandHeight = false;
        var state = box.AddComponent<AuraToolsFoldoutState>();
        state.Expanded = FoldoutStates.TryGetValue(title, out var expanded) && expanded;
        var contentBuilt = false;
        void EnsureContentBuilt()
        {
            if (contentBuilt)
            {
                return;
            }
            contentBuilt = true;
            buildContent(content.transform);
        }
        if (state.Expanded)
        {
            EnsureContentBuilt();
        }
        AuraToolsUi.SetFoldoutExpanded(content, state.Expanded, box.transform);
        Text? foldoutLabel = null;
        void UpdateFoldoutLabel()
        {
            if (foldoutLabel != null)
            {
                foldoutLabel.text = state.Expanded ? "收起" : "展开";
            }
        }

        void ToggleFoldout()
        {
            state.Expanded = !state.Expanded;
            FoldoutStates[title] = state.Expanded;
            if (state.Expanded)
            {
                EnsureContentBuilt();
            }
            AuraToolsUi.SetFoldoutExpanded(content, state.Expanded, box.transform);
            UpdateFoldoutLabel();
        }

        var headerButton = header.AddComponent<Button>();
        AuraUiButtonFeedback.Apply(headerButton, headerImage, AuraToolsUi.Accent);
        headerButton.onClick.AddListener(ToggleFoldout);
        var foldoutButton = AuraToolsUi.AddButton(header.transform, state.Expanded ? "收起" : "展开", ToggleFoldout, AuraToolsUi.ButtonMinWidth, AuraToolsUi.ButtonHeight);
        foldoutLabel = foldoutButton.GetComponentInChildren<Text>();
        UpdateFoldoutLabel();

    }

    private static void OnAutoBattleUiSnapshotChanged()
    {
        if (activePanel == null
            || !activePanel.activeInHierarchy
            || !panelBuilt)
        {
            return;
        }
        var autoBattle = AuraToolsConfigService.MatchExperience.AutoBattle;
        var snapshot = AuraToolsAutoBattleUiSnapshotRuntime.Snapshot(
            autoBattle.Profile,
            autoBattle.SelectedModelId);
        if (snapshot.Revision == autoBattleSnapshotRevisionBuilt)
        {
            return;
        }
        RebuildPanel(activePanel.transform);
    }

    private static void CreateToggleModule(Transform parent, string title, bool enabled, Action<bool> setEnabled, Color? titleColor = null)
    {
        var box = AuraToolsUi.CreateLayout("ToggleModule-" + title, parent);
        AuraToolsUi.AddPanelImage(box, AuraToolsUi.Panel);
        var element = box.AddComponent<LayoutElement>();
        element.minHeight = AuraToolsUi.ModuleHeaderHeight + 12f;
        element.preferredHeight = AuraToolsUi.ModuleHeaderHeight + 12f;

        var layout = box.AddComponent<HorizontalLayoutGroup>();
        layout.padding = new RectOffset(8, 8, 6, 6);
        layout.spacing = 8f;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = false;
        layout.childForceExpandHeight = false;

        AuraToolsUi.AddToggle(box.transform, enabled, value =>
        {
            setEnabled(value);
            RebuildPanel(activePanel!.transform);
        });
        AuraToolsUi.AddText(box.transform, title, AuraToolsUi.ModuleTitleFontSize, TextAnchor.MiddleLeft, titleColor ?? AuraToolsUi.Text, AuraToolsUi.TextMinHeight, 1f);
    }

    private static GameObject CreateInlineRow(Transform parent, string name)
    {
        var row = AuraToolsUi.CreateLayout(name, parent);
        var rowElement = row.AddComponent<LayoutElement>();
        rowElement.minHeight = AuraToolsUi.InlineRowHeight;
        rowElement.preferredHeight = AuraToolsUi.InlineRowHeight;
        var layout = row.AddComponent<HorizontalLayoutGroup>();
        layout.spacing = 8f;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = false;
        return row;
    }

    private static void SetSegmentActive(Button button, bool active)
    {
        var label = button.GetComponentInChildren<Text>(true);
        if (label != null)
        {
            label.color = active ? AuraToolsUi.Accent : AuraToolsUi.Text;
        }
        var image = button.GetComponent<Image>();
        if (image != null && active)
        {
            image.color = new Color(0.22f, 0.17f, 0.32f, 1f);
        }
    }

    private static void AttachAutoBattleWorkLock(
        GameObject host,
        params Selectable[] controls)
    {
        host.AddComponent<AuraToolsAutoBattleWorkLockView>().Configure(controls);
    }

    private static int SelectedLoggingLevelIndex(string level)
    {
        var normalized = LoggingLevelNames.Normalize(level);
        if (string.Equals(normalized, LoggingLevelNames.Debug, StringComparison.OrdinalIgnoreCase))
        {
            return 0;
        }

        if (string.Equals(normalized, LoggingLevelNames.Warning, StringComparison.OrdinalIgnoreCase))
        {
            return 2;
        }

        return string.Equals(normalized, LoggingLevelNames.Error, StringComparison.OrdinalIgnoreCase) ? 3 : 1;
    }

    private static int SelectedLoggingStackIndex(string mode)
    {
        var normalized = LoggingStackTraceModes.Normalize(mode);
        if (string.Equals(normalized, LoggingStackTraceModes.Off, StringComparison.OrdinalIgnoreCase))
        {
            return 0;
        }

        return string.Equals(normalized, LoggingStackTraceModes.All, StringComparison.OrdinalIgnoreCase) ? 2 : 1;
    }

    private static void CreateLoggingListToggle(Transform parent, List<string> values, string value)
    {
        var enabled = values.Any(item => string.Equals(item, value, StringComparison.OrdinalIgnoreCase));
        AuraToolsUi.AddToggle(parent, enabled, selected =>
        {
            SetLoggingListValue(values, value, selected);
            AuraToolsConfigService.Logging.Normalize();
            AuraToolsConfigService.SaveLogging();
            RebuildPanel(activePanel!.transform);
        });
        AuraToolsUi.AddText(parent, value, AuraToolsUi.BodyFontSize, TextAnchor.MiddleLeft, AuraToolsUi.Text, AuraToolsUi.TextMinHeight, 0f, 82f);
    }

    private static void SetLoggingListValue(List<string> values, string value, bool selected)
    {
        values.RemoveAll(item => string.Equals(item, value, StringComparison.OrdinalIgnoreCase));
        if (selected)
        {
            values.Add(value);
        }
    }

    private static void CreateDamageMeterToggleRow(Transform parent, string label, bool value, Action<bool> changed)
    {
        var row = CreateInlineRow(parent, "DamageMeterToggle-" + label);
        AuraToolsUi.AddToggle(row.transform, value, changed);
        AuraToolsUi.AddText(row.transform, label, AuraToolsUi.BodyFontSize, TextAnchor.MiddleLeft, AuraToolsUi.Text, AuraToolsUi.TextMinHeight, 1f);
    }

    private static Toggle CreateAutoBattleToggleRow(
        Transform parent,
        string label,
        bool value,
        Action<bool> changed)
    {
        var row = CreateInlineRow(parent, "AutoBattleToggle-" + label);
        var toggle = AuraToolsUi.AddToggle(row.transform, value, changed);
        AuraToolsUi.AddText(row.transform, label, AuraToolsUi.BodyFontSize, TextAnchor.MiddleLeft, AuraToolsUi.Text, AuraToolsUi.TextMinHeight, 1f);
        return toggle;
    }

    private static void CreateAutoBattleTrainingParameterRows(
        Transform parent,
        AutoBattleSettings autoBattle)
    {
        var first = CreateInlineRow(parent, "AutoBattleTrainingParameters1");
        AddAutoBattleTrainingInt(
            first.transform,
            "训练轮数",
            autoBattle.Training.Epochs,
            value => autoBattle.Training.Epochs = Math.Max(20, Math.Min(300, value)),
            autoBattle);
        AddAutoBattleTrainingDouble(
            first.transform,
            "学习率",
            autoBattle.Training.LearningRate,
            value => autoBattle.Training.LearningRate = Math.Max(0.005d, Math.Min(0.1d, value)),
            autoBattle);
        AttachAutoBattleWorkLock(
            first,
            first.GetComponentsInChildren<Selectable>(true));

        var second = CreateInlineRow(parent, "AutoBattleTrainingParameters2");
        AddAutoBattleTrainingDouble(
            second.transform,
            "L2 正则",
            autoBattle.Training.L2,
            value => autoBattle.Training.L2 = Math.Max(0d, Math.Min(0.02d, value)),
            autoBattle);
        AddAutoBattleTrainingDouble(
            second.transform,
            "最大修正",
            autoBattle.Training.MaximumCorrection,
            value => autoBattle.Training.MaximumCorrection = Math.Max(0.25d, Math.Min(2d, value)),
            autoBattle);
        AttachAutoBattleWorkLock(
            second,
            second.GetComponentsInChildren<Selectable>(true));

        var third = CreateInlineRow(parent, "AutoBattleTrainingParameters3");
        AddAutoBattleTrainingInt(
            third.transform,
            "最低偏好对",
            autoBattle.Training.MinimumPreferencePairs,
            value => autoBattle.Training.MinimumPreferencePairs = Math.Max(5, Math.Min(200, value)),
            autoBattle);
        AddAutoBattleTrainingInt(
            third.transform,
            "类别最低样本",
            autoBattle.Training.MinimumCategoryObservations,
            value => autoBattle.Training.MinimumCategoryObservations = Math.Max(3, Math.Min(100, value)),
            autoBattle);
        AttachAutoBattleWorkLock(
            third,
            third.GetComponentsInChildren<Selectable>(true));

        var fourth = CreateInlineRow(parent, "AutoBattleTrainingParameters4");
        AddAutoBattleTrainingInt(
            fourth.transform,
            "完整战斗数",
            autoBattle.Training.MinimumEpisodes,
            value => autoBattle.Training.MinimumEpisodes = Math.Max(2, Math.Min(10000, value)),
            autoBattle);
        AddAutoBattleTrainingInt(
            fourth.transform,
            "网络隐藏维度",
            autoBattle.Training.PolicyValueHiddenDimensions,
            value => autoBattle.Training.PolicyValueHiddenDimensions = Math.Max(8, Math.Min(256, value)),
            autoBattle);
        AttachAutoBattleWorkLock(
            fourth,
            fourth.GetComponentsInChildren<Selectable>(true));
    }

    private static void CreateAutoBattleSimulationRows(
        Transform parent,
        AutoBattleSettings autoBattle)
    {
        var evaluationModelId = string.IsNullOrWhiteSpace(
            autoBattle.EvaluationModelId)
            ? autoBattle.SelectedModelId
            : autoBattle.EvaluationModelId;
        var uiSnapshot = AuraToolsAutoBattleUiSnapshotRuntime.Snapshot(
            autoBattle.Profile,
            evaluationModelId);
        if (!uiSnapshot.Ready)
        {
            AuraToolsAutoBattleUiSnapshotRuntime.RequestRefresh(
                autoBattle.Profile,
                evaluationModelId);
        }
        if (!AutoBattleAdvancedTrainingExpanded)
        {
            AutoBattleEvolutionView = false;
        }
        var fixedCampaignSelected = string.Equals(
            autoBattle.Simulation.ScenarioId,
            "witch.world-simulation.standard-v2",
            StringComparison.OrdinalIgnoreCase);
        if (fixedCampaignSelected)
        {
            AutoBattleEvolutionView = false;
        }
        AuraToolsUi.AddText(
            parent,
            "④ 正式世界推演：career_1 从本体 Normal 真实开局出发，在全离线卡包奖励池中完成 37 战与构筑；普通/高级分别生成验证标记。",
            AuraToolsUi.BodyFontSize,
            TextAnchor.MiddleLeft,
            AuraToolsUi.Text,
            AuraToolsUi.TextMinHeight,
            1f);

        var lockedControls = new List<Selectable>();
        var library = uiSnapshot.Models.ToList();
        var modelRow = CreateInlineRow(parent, "AutoBattleModelLibraryRow");
        AuraToolsUi.AddText(
            modelRow.transform,
            "策略模型",
            AuraToolsUi.HintFontSize,
            TextAnchor.MiddleLeft,
            AuraToolsUi.Text,
            AuraToolsUi.TextMinHeight,
            0f,
            106f);
        var modelLabels = new List<string> { "当前已安装 / 最新候选" };
        modelLabels.AddRange(library.Select(item => item.DisplayName));
        var selectedModelIndex = library.FindIndex(item => string.Equals(
            item.ModelId,
            autoBattle.SelectedModelId,
            StringComparison.Ordinal));
        var modelButton = AuraToolsUi.AddSelectButton(
            modelRow.transform,
            modelLabels,
            selectedModelIndex < 0 ? 0 : selectedModelIndex + 1,
            index =>
            {
                autoBattle.SelectedModelId = index <= 0 || index > library.Count
                    ? ""
                    : library[index - 1].ModelId;
                autoBattle.EvaluationModelId = "";
                autoBattle.Normalize();
                AuraToolsConfigService.SaveMatchExperience();
                AuraToolsAutoBattleRuntime.ReloadModels();
                AuraToolsAutoBattleUiSnapshotRuntime.RequestRefresh(
                    autoBattle.Profile,
                    autoBattle.SelectedModelId,
                    force: true);
                RebuildPanel(activePanel!.transform);
            },
            260f);
        lockedControls.Add(modelButton);
        var renameValue = selectedModelIndex >= 0
            ? library[selectedModelIndex].DisplayName
            : "";
        var renameInput = AuraToolsUi.AddInput(
            modelRow.transform,
            renameValue,
            value => renameValue = value,
            180f);
        renameInput.interactable = selectedModelIndex >= 0;
        var renameButton = AuraToolsUi.AddButton(
            modelRow.transform,
            "改名",
            () =>
            {
                if (AuraToolsAutoBattleModelRuntime.TryRenameLibraryModel(
                        autoBattle.SelectedModelId,
                        renameValue,
                        out var renameMessage))
                {
                    AuraToolsLog.Info("[AutoBattle][ModelLibrary] " + renameMessage);
                }
                else
                {
                    AuraToolsLog.Warn("[AutoBattle][ModelLibrary] " + renameMessage);
                }
                AuraToolsAutoBattleUiSnapshotRuntime.RequestRefresh(
                    autoBattle.Profile,
                    autoBattle.SelectedModelId,
                    force: true);
                RebuildPanel(activePanel!.transform);
            },
            66f);
        renameButton.interactable = selectedModelIndex >= 0;
        AuraToolsUi.AddText(
            parent,
            library.Count == 0
                ? "训练候选通过评估并导入后，会进入 career_1 专属模型库；之后可改名并随时切换。"
                : "模型库共 "
                  + library.Count
                  + " 个 career_1 / 当前风格模型；切换会同时影响实战、世界推演和后续策略进化。",
            AuraToolsUi.HintFontSize,
            TextAnchor.MiddleLeft,
            AuraToolsUi.MutedText,
            AuraToolsUi.TextMinHeight,
            1f);

        var externalEntry =
            AuraToolsAutoBattleModelRuntime.SnapshotExternalValidationModel();
        var externalRow = CreateInlineRow(
            parent,
            "AutoBattleExternalFoundationValidationRow");
        var externalStatusText = AuraToolsUi.AddText(
            externalRow.transform,
            externalEntry == null
                ? "尚未选择外部待验底模"
                : "外部待验底模："
                  + externalEntry.DisplayName
                  + (string.Equals(
                        autoBattle.EvaluationModelId,
                        externalEntry.ModelId,
                        StringComparison.Ordinal)
                      ? "（当前评估目标）"
                      : ""),
            AuraToolsUi.HintFontSize,
            TextAnchor.MiddleLeft,
            AuraToolsUi.MutedText,
            AuraToolsUi.TextMinHeight,
            1f);
        var selectExternalButton = AuraToolsUi.AddButton(
            externalRow.transform,
            "选择底模验证",
            () =>
            {
                OptionalFileDialog.PickFileAsync(
                    "选择外部待验底模包",
                    new[]
                    {
                        new OptionalFileDialogFilter(
                            "Aura 待验底模包",
                            "foundation-model-package-v2.json;*.aura-model.json"),
                        new OptionalFileDialogFilter("JSON 文件", "*.json")
                    },
                    "json",
                    AuraToolsAutoBattleSimulationRuntime.ResultsRootDirectory,
                    result =>
                    {
                        if (!result.Selected)
                        {
                            if (result.Status
                                != OptionalFileDialogStatus.Cancelled)
                            {
                                AuraToolsLog.Warn(
                                    "[AutoBattle][ExternalValidation] "
                                    + result.Message);
                            }
                            return;
                        }
                        if (AuraToolsAutoBattleModelRuntime
                            .TryStageExternalFoundationPackage(
                                result.Path,
                                out var externalModelId,
                                out var stageMessage))
                        {
                            autoBattle.EvaluationModelId = externalModelId;
                            autoBattle.Normalize();
                            AuraToolsConfigService.SaveMatchExperience();
                            AuraToolsAutoBattleUiSnapshotRuntime.RequestRefresh(
                                autoBattle.Profile,
                                externalModelId,
                                force: true);
                            AuraToolsLog.Info(
                                "[AutoBattle][ExternalValidation] "
                                + stageMessage);
                            RebuildPanel(activePanel!.transform);
                        }
                        else
                        {
                            AuraToolsLog.Warn(
                                "[AutoBattle][ExternalValidation] "
                                + stageMessage);
                        }
                    });
            },
            112f);
        var promoteExternalButton = AuraToolsUi.AddButton(
            externalRow.transform,
            "加入模型库",
            () =>
            {
                var targetId = autoBattle.EvaluationModelId;
                if (!AuraToolsAutoBattleSimulationRuntime.CanActivateModel(
                        autoBattle.Profile,
                        targetId,
                        out var gateMessage))
                {
                    AuraToolsLog.Warn(
                        "[AutoBattle][ExternalValidation] 尚不能入库："
                        + gateMessage);
                    return;
                }
                if (AuraToolsAutoBattleModelRuntime
                    .TryPromoteExternalValidationModel(
                        autoBattle.Profile,
                        targetId,
                        out var promotedModelId,
                        out var promoteMessage))
                {
                    autoBattle.SelectedModelId = promotedModelId;
                    autoBattle.EvaluationModelId = "";
                    autoBattle.TrainedModelMode = "off";
                    AuraToolsAutoBattleModelRuntime
                        .ClearExternalValidationModel();
                    AuraToolsConfigService.SaveMatchExperience();
                    AuraToolsAutoBattleRuntime.ReloadModels();
                    AuraToolsAutoBattleUiSnapshotRuntime.RequestRefresh(
                        autoBattle.Profile,
                        promotedModelId,
                        force: true);
                    AuraToolsLog.Info(
                        "[AutoBattle][ExternalValidation] "
                        + promoteMessage);
                    RebuildPanel(activePanel!.transform);
                }
                else
                {
                    AuraToolsLog.Warn(
                        "[AutoBattle][ExternalValidation] "
                        + promoteMessage);
                }
            },
            96f);
        var clearExternalButton = AuraToolsUi.AddButton(
            externalRow.transform,
            "清除待验",
            () =>
            {
                AuraToolsAutoBattleModelRuntime.ClearExternalValidationModel();
                autoBattle.EvaluationModelId = "";
                AuraToolsConfigService.SaveMatchExperience();
                RebuildPanel(activePanel!.transform);
            },
            80f);
        externalRow
            .AddComponent<AuraToolsAutoBattleExternalValidationStatusView>()
            .Configure(
                autoBattle.Profile,
                externalStatusText,
                selectExternalButton,
                promoteExternalButton,
                clearExternalButton);
        AuraToolsUi.AddText(
            parent,
            "外部底模只进入待验证区，不会替换当前实战模型。请依次完成普通/高级正式世界推演和游戏主体验证，通过后再加入模型库。",
            AuraToolsUi.HintFontSize,
            TextAnchor.MiddleLeft,
            AuraToolsUi.MutedText,
            AuraToolsUi.TextMinHeight,
            1f);

        var modeRow = CreateInlineRow(parent, "AutoBattleSimulationModeRow");
        AuraToolsUi.AddText(
            modeRow.transform,
            "评估方式",
            AuraToolsUi.BodyFontSize,
            TextAnchor.MiddleLeft,
            AuraToolsUi.Text,
            AuraToolsUi.TextMinHeight,
            1f);
        var pairedModeButton = AuraToolsUi.AddButton(
            modeRow.transform,
            "对照评估",
            () =>
            {
                AutoBattleEvolutionView = false;
                RebuildPanel(activePanel!.transform);
            },
            92f);
        Button? evolutionModeButton = null;
        if (AutoBattleAdvancedTrainingExpanded && !fixedCampaignSelected)
        {
            evolutionModeButton = AuraToolsUi.AddButton(
                modeRow.transform,
                "高级：策略进化",
                () =>
                {
                    AutoBattleEvolutionView = true;
                    RebuildPanel(activePanel!.transform);
                },
                128f);
        }
        SetSegmentActive(pairedModeButton, !AutoBattleEvolutionView);
        if (evolutionModeButton != null)
        {
            SetSegmentActive(evolutionModeButton, AutoBattleEvolutionView);
        }
        AuraToolsUi.AddText(
            parent,
            AuraToolsCombatKnowledgeRuntime.DescribeLoadedPackages(),
            AuraToolsUi.HintFontSize,
            TextAnchor.MiddleLeft,
            AuraToolsUi.MutedText,
            AuraToolsUi.TextMinHeight,
            1f);

        var scenarios = uiSnapshot.ScenarioIds.ToList();
        var scenarioLabels = scenarios.Count == 0
            ? new List<string> { "未注册场景" }
            : scenarios;
        var selectedScenario = Math.Max(
            0,
            scenarios.FindIndex(id => string.Equals(
                id,
                autoBattle.Simulation.ScenarioId,
                StringComparison.OrdinalIgnoreCase)));
        var scenarioRow = CreateInlineRow(parent, "AutoBattleSimulationScenarioRow");
        AuraToolsUi.AddText(
            scenarioRow.transform,
            "场景",
            AuraToolsUi.HintFontSize,
            TextAnchor.MiddleLeft,
            AuraToolsUi.Text,
            AuraToolsUi.TextMinHeight,
            0f,
            106f);
        var scenarioButton = AuraToolsUi.AddSelectButton(
            scenarioRow.transform,
            scenarioLabels,
            selectedScenario,
            index =>
            {
                if (index >= 0 && index < scenarios.Count)
                {
                    autoBattle.Simulation.ScenarioId = scenarios[index];
                    autoBattle.Normalize();
                    AuraToolsConfigService.SaveMatchExperience();
                }
            },
            260f);
        scenarioButton.interactable = scenarios.Count > 0;
        if (scenarios.Count > 0)
        {
            lockedControls.Add(scenarioButton);
        }
        AuraToolsUi.AddButton(
            scenarioRow.transform,
            "刷新",
            () => RebuildPanel(activePanel!.transform),
            66f);
        if (AutoBattleAdvancedTrainingExpanded)
        {
            AuraToolsUi.AddButton(
                scenarioRow.transform,
                "高级输入目录",
                AuraToolsAutoBattleSimulationRuntime.OpenInputDirectory,
                112f);
        }
        AuraToolsUi.AddText(
            parent,
            scenarios.Count == 0
                ? "未找到随 MOD 发布的标准评估包。请检查 AuraToolsExp/Config/combat-simulation 是否完整，或重新安装 MOD。"
                : "标准 v2 固定 7 层：前 6 层均为 2普通＋1精英＋2普通＋1首领，第 7 层从勇者卡洛琳、永夜化身、魔王、神圣审判机关中抽取最终首领。只使用游戏主体内容。",
            AuraToolsUi.HintFontSize,
            TextAnchor.MiddleLeft,
            scenarios.Count == 0
                ? new Color(1f, 0.68f, 0.3f, 1f)
                : AuraToolsUi.MutedText,
            AuraToolsUi.TextMinHeight,
            1f);
        if (AutoBattleAdvancedTrainingExpanded && fixedCampaignSelected)
        {
            AuraToolsUi.AddText(
                parent,
                "提示：固定七层战役用于正式对照评估；“策略进化”只对高级输入目录中的单场 *.scenario.json 开放。",
                AuraToolsUi.HintFontSize,
                TextAnchor.MiddleLeft,
                AuraToolsUi.MutedText,
                AuraToolsUi.TextMinHeight,
                1f);
        }

        var difficultyRow = CreateInlineRow(parent, "AutoBattleSimulationDifficultyRow");
        AuraToolsUi.AddText(
            difficultyRow.transform,
            "敌人难度",
            AuraToolsUi.HintFontSize,
            TextAnchor.MiddleLeft,
            AuraToolsUi.Text,
            AuraToolsUi.TextMinHeight,
            0f,
            106f);
        var difficulties = new List<string> { "普通难度（无词条）", "高级难度（本体满词条）" };
        var difficultyButton = AuraToolsUi.AddSelectButton(
            difficultyRow.transform,
            difficulties,
            string.Equals(
                autoBattle.Simulation.DifficultyId,
                "advanced",
                StringComparison.OrdinalIgnoreCase)
                ? 1
                : 0,
            index =>
            {
                autoBattle.Simulation.DifficultyId = index == 1 ? "advanced" : "normal";
                autoBattle.Normalize();
                AuraToolsConfigService.SaveMatchExperience();
                RebuildPanel(activePanel!.transform);
            },
            260f);
        lockedControls.Add(difficultyButton);
        AuraToolsUi.AddText(
            difficultyRow.transform,
            "普通与高级分别产生一枚验证标记，不要求同时通过。",
            AuraToolsUi.HintFontSize,
            TextAnchor.MiddleLeft,
            AuraToolsUi.MutedText,
            AuraToolsUi.TextMinHeight,
            1f);

        if (AutoBattleEvolutionView)
        {
            var evolutionRow = CreateInlineRow(parent, "AutoBattleEvolutionParameterRow");
            lockedControls.Add(AddAutoBattleSimulationInt(
                evolutionRow.transform,
                "进化轮数",
                autoBattle.Simulation.EvolutionIterations,
                1,
                20,
                value => autoBattle.Simulation.EvolutionIterations = value,
                autoBattle));
            lockedControls.Add(AddAutoBattleSimulationInt(
                evolutionRow.transform,
                "每轮训练局",
                autoBattle.Simulation.EvolutionEpisodesPerIteration,
                8,
                10000,
                value => autoBattle.Simulation.EvolutionEpisodesPerIteration = value,
                autoBattle));
            lockedControls.Add(AddAutoBattleSimulationInt(
                evolutionRow.transform,
                "竞技场局数",
                autoBattle.Simulation.EvolutionArenaEpisodes,
                2,
                10000,
                value => autoBattle.Simulation.EvolutionArenaEpisodes = value,
                autoBattle));
            AuraToolsUi.AddText(
                parent,
                "本次工作量："
                + autoBattle.Simulation.EvolutionIterations
                * (autoBattle.Simulation.EvolutionEpisodesPerIteration
                   + autoBattle.Simulation.EvolutionArenaEpisodes * 2)
                + " 场战斗",
                AuraToolsUi.HintFontSize,
                TextAnchor.MiddleLeft,
                AuraToolsUi.MutedText,
                AuraToolsUi.TextMinHeight,
                1f);
        }
        else
        {
            var parameterRow = CreateInlineRow(parent, "AutoBattleSimulationParameterRow");
            lockedControls.Add(AddAutoBattleSimulationInt(
                parameterRow.transform,
                "对照局数",
                autoBattle.Simulation.SimulationCount,
                1,
                100000,
                value => autoBattle.Simulation.SimulationCount = value,
                autoBattle));
            lockedControls.Add(AddAutoBattleSimulationInt(
                parameterRow.transform,
                "并行度",
                autoBattle.Simulation.Parallelism,
                1,
                16,
                value => autoBattle.Simulation.Parallelism = value,
                autoBattle));
            lockedControls.Add(CreateAutoBattleToggleRow(
                parent,
                "保留分歧与失败轨迹",
                autoBattle.Simulation.RetainDivergentTraces,
                value =>
                {
                    autoBattle.Simulation.RetainDivergentTraces = value;
                    AuraToolsConfigService.SaveMatchExperience();
                }));
        }

        var actionRow = CreateInlineRow(parent, "AutoBattleSimulationActionRow");
        var statusText = AuraToolsUi.AddText(
            actionRow.transform,
            "",
            AuraToolsUi.HintFontSize,
            TextAnchor.MiddleLeft,
            AuraToolsUi.MutedText,
            AuraToolsUi.TextMinHeight,
            1f);
        var primaryButton = AuraToolsUi.AddButton(
            actionRow.transform,
            AutoBattleEvolutionView ? "开始进化" : "运行标准评估",
            () =>
            {
                var queued = AutoBattleEvolutionView
                    ? AuraToolsAutoBattleSimulationRuntime.QueueEvolution(
                        autoBattle,
                        out var message)
                    : AuraToolsAutoBattleSimulationRuntime.QueueRun(
                        autoBattle,
                        out message);
                if (!queued)
                {
                    AuraToolsLog.Warn("[AutoBattle][Simulation] " + message);
                }
            },
            92f);
        var cancelButton = AuraToolsUi.AddButton(
            actionRow.transform,
            "取消",
            AuraToolsAutoBattleSimulationRuntime.Cancel,
            66f);
        var resultButton = AuraToolsUi.AddButton(
            actionRow.transform,
            "打开结果",
            () => AuraToolsAutoBattleSimulationRuntime.OpenResultDirectory(
                autoBattle.Profile,
                evaluationModelId),
            84f);
        var operationDetailText = AuraToolsUi.AddText(
            parent,
            "",
            AuraToolsUi.HintFontSize,
            TextAnchor.MiddleLeft,
            AuraToolsUi.MutedText,
            AuraToolsUi.TextMinHeight,
            1f);
        actionRow.AddComponent<AuraToolsAutoBattleSimulationStatusView>().Configure(
            autoBattle.Profile,
            evaluationModelId,
            AutoBattleEvolutionView,
            statusText,
            operationDetailText,
            primaryButton,
            cancelButton,
            resultButton,
            scenarios.Count > 0,
            lockedControls);

        AuraToolsUi.AddText(
            parent,
            "最近结果",
            AuraToolsUi.BodyFontSize,
            TextAnchor.MiddleLeft,
            AuraToolsUi.Text,
            AuraToolsUi.TextMinHeight,
            1f);
        var resultTitle = AuraToolsUi.AddText(
            parent,
            "",
            AuraToolsUi.HintFontSize,
            TextAnchor.MiddleLeft,
            AuraToolsUi.MutedText,
            AuraToolsUi.TextMinHeight,
            1f);
        var resultPrimary = AuraToolsUi.AddText(
            parent,
            "",
            AuraToolsUi.HintFontSize,
            TextAnchor.MiddleLeft,
            AuraToolsUi.Text,
            AuraToolsUi.TextMinHeight,
            1f);
        var resultSecondary = AuraToolsUi.AddText(
            parent,
            "",
            AuraToolsUi.HintFontSize,
            TextAnchor.MiddleLeft,
            AuraToolsUi.MutedText,
            AuraToolsUi.TextMinHeight,
            1f);
        var resultDetail = AuraToolsUi.AddText(
            parent,
            "",
            AuraToolsUi.HintFontSize,
            TextAnchor.MiddleLeft,
            AuraToolsUi.MutedText,
            AuraToolsUi.TextMinHeight,
            1f);
        actionRow.AddComponent<AuraToolsAutoBattleSimulationResultView>().Configure(
            autoBattle.Profile,
            evaluationModelId,
            resultTitle,
            resultPrimary,
            resultSecondary,
            resultDetail);
    }

    private static InputField AddAutoBattleSimulationInt(
        Transform parent,
        string label,
        int value,
        int minimum,
        int maximum,
        Action<int> apply,
        AutoBattleSettings autoBattle)
    {
        AuraToolsUi.AddText(
            parent,
            label,
            AuraToolsUi.HintFontSize,
            TextAnchor.MiddleLeft,
            AuraToolsUi.Text,
            AuraToolsUi.TextMinHeight,
            0f,
            86f);
        InputField? input = null;
        input = AuraToolsUi.AddInput(parent, value.ToString(CultureInfo.InvariantCulture), raw =>
        {
            var parsed = int.TryParse(
                raw,
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out var configured)
                ? configured
                : value;
            parsed = Math.Max(minimum, Math.Min(maximum, parsed));
            apply(parsed);
            autoBattle.Normalize();
            AuraToolsConfigService.SaveMatchExperience();
            if (input != null)
            {
                input.text = parsed.ToString(CultureInfo.InvariantCulture);
            }
        }, 72f);
        input.contentType = InputField.ContentType.IntegerNumber;
        return input;
    }

    private static InputField AddAutoBattleFoundationUlong(
        Transform parent,
        string label,
        ulong value,
        Action<ulong> apply,
        AutoBattleSettings autoBattle)
    {
        AuraToolsUi.AddText(
            parent,
            label,
            AuraToolsUi.HintFontSize,
            TextAnchor.MiddleLeft,
            AuraToolsUi.Text,
            AuraToolsUi.TextMinHeight,
            0f,
            92f);
        InputField? input = null;
        input = AuraToolsUi.AddInput(
            parent,
            value.ToString(CultureInfo.InvariantCulture),
            raw =>
            {
                var parsed = ulong.TryParse(
                    raw,
                    NumberStyles.Integer,
                    CultureInfo.InvariantCulture,
                    out var configured)
                    ? configured
                    : value;
                apply(parsed);
                autoBattle.Normalize();
                AuraToolsConfigService.SaveMatchExperience();
                if (input != null)
                {
                    input.text = parsed.ToString(
                        CultureInfo.InvariantCulture);
                }
            },
            150f);
        input.contentType = InputField.ContentType.IntegerNumber;
        return input;
    }

    private static InputField AddAutoBattleFoundationDouble(
        Transform parent,
        string label,
        double value,
        double minimum,
        double maximum,
        Action<double> apply,
        AutoBattleSettings autoBattle)
    {
        AuraToolsUi.AddText(
            parent,
            label,
            AuraToolsUi.HintFontSize,
            TextAnchor.MiddleLeft,
            AuraToolsUi.Text,
            AuraToolsUi.TextMinHeight,
            0f,
            92f);
        InputField? input = null;
        input = AuraToolsUi.AddInput(
            parent,
            value.ToString("0.###", CultureInfo.InvariantCulture),
            raw =>
            {
                var parsed = double.TryParse(
                    raw,
                    NumberStyles.Float,
                    CultureInfo.InvariantCulture,
                    out var configured)
                    ? configured
                    : value;
                parsed = Math.Max(minimum, Math.Min(maximum, parsed));
                apply(parsed);
                autoBattle.Normalize();
                AuraToolsConfigService.SaveMatchExperience();
                if (input != null)
                {
                    input.text = parsed.ToString(
                        "0.###",
                        CultureInfo.InvariantCulture);
                }
            },
            72f);
        input.contentType = InputField.ContentType.DecimalNumber;
        return input;
    }

    private static void AddAutoBattleTrainingInt(
        Transform parent,
        string label,
        int value,
        Action<int> apply,
        AutoBattleSettings autoBattle)
    {
        AuraToolsUi.AddText(
            parent,
            label,
            AuraToolsUi.HintFontSize,
            TextAnchor.MiddleLeft,
            AuraToolsUi.Text,
            AuraToolsUi.TextMinHeight,
            0f,
            106f);
        var input = AuraToolsUi.AddInput(parent, value.ToString(CultureInfo.InvariantCulture), raw =>
        {
            if (int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
            {
                apply(parsed);
            }
            autoBattle.Training.MarkCustom();
            autoBattle.Normalize();
            AuraToolsConfigService.SaveMatchExperience();
            RebuildPanel(activePanel!.transform);
        }, 86f);
        input.contentType = InputField.ContentType.IntegerNumber;
    }

    private static void AddAutoBattleTrainingDouble(
        Transform parent,
        string label,
        double value,
        Action<double> apply,
        AutoBattleSettings autoBattle)
    {
        AuraToolsUi.AddText(
            parent,
            label,
            AuraToolsUi.HintFontSize,
            TextAnchor.MiddleLeft,
            AuraToolsUi.Text,
            AuraToolsUi.TextMinHeight,
            0f,
            106f);
        var input = AuraToolsUi.AddInput(parent, value.ToString("0.####", CultureInfo.InvariantCulture), raw =>
        {
            if (TryParseTrainingDouble(raw, out var parsed))
            {
                apply(parsed);
            }
            autoBattle.Training.MarkCustom();
            autoBattle.Normalize();
            AuraToolsConfigService.SaveMatchExperience();
            RebuildPanel(activePanel!.transform);
        }, 86f);
        input.contentType = InputField.ContentType.DecimalNumber;
    }

    private static bool TryParseTrainingDouble(string value, out double result)
    {
        return double.TryParse(
                   value,
                   NumberStyles.Float,
                   CultureInfo.InvariantCulture,
                   out result)
               || double.TryParse(
                   value,
                   NumberStyles.Float,
                   CultureInfo.CurrentCulture,
                   out result);
    }

    private static int AutoBattleTrainingPresetIndex(string value)
    {
        return value switch
        {
            AutoBattleTrainingSettings.StandardPreset => 1,
            AutoBattleTrainingSettings.AdaptivePreset => 2,
            AutoBattleTrainingSettings.CustomPreset => 3,
            _ => 0
        };
    }

    private static string AutoBattleTrainingPresetSummary(AutoBattleTrainingSettings settings)
    {
        return "轮数 " + settings.Epochs
               + " · 学习率 " + settings.LearningRate.ToString("0.####", CultureInfo.InvariantCulture)
               + " · 最大修正 " + settings.MaximumCorrection.ToString("0.##", CultureInfo.InvariantCulture)
               + " · 最低偏好对 " + settings.MinimumPreferencePairs;
    }

    private static string NextAutoBattleProfile(string value)
    {
        return value switch
        {
            "aggressive" => "defensive",
            "defensive" => "balanced",
            _ => "aggressive"
        };
    }

    private static string AutoBattleProfileLabel(string value)
    {
        return value switch
        {
            "aggressive" => "进攻",
            "defensive" => "稳健",
            _ => "均衡"
        };
    }

    private static string NextAutoBattleTrainingMode(string value)
    {
        return value switch
        {
            "auto" => "shadow",
            "shadow" => "hybrid",
            _ => "auto"
        };
    }

    private static string AutoBattleTrainingModeLabel(string value)
    {
        return value switch
        {
            "auto" => "自动轨迹采集",
            "shadow" => "人工示范采集",
            _ => "全部轨迹采集"
        };
    }

    private static string NextAutoBattleModelMode(string value)
    {
        return value switch
        {
            "off" => "shadow",
            "shadow" => "active",
            _ => "off"
        };
    }

    private static string AutoBattleModelModeLabel(string value)
    {
        return value switch
        {
            "shadow" => "影子评估",
            "active" => "受限应用",
            _ => "关闭"
        };
    }

    private static string NextAutoBattleUnknownPolicy(string value)
    {
        return value switch
        {
            "allow" => "handoff",
            "handoff" => "conservative",
            _ => "allow"
        };
    }

    private static string AutoBattleUnknownPolicyLabel(string value)
    {
        return value switch
        {
            "allow" => "允许尝试",
            "handoff" => "交还玩家",
            _ => "保守降权"
        };
    }
}

internal sealed class AuraToolsFoldoutState : MonoBehaviour
{
    public bool Expanded = true;
}

internal sealed class AuraToolsAutoBattleWorkLockView : MonoBehaviour
{
    private IReadOnlyList<Selectable> controls = Array.Empty<Selectable>();
    private float nextRefreshAt;

    public void Configure(IReadOnlyList<Selectable> values)
    {
        controls = values ?? Array.Empty<Selectable>();
        Refresh();
    }

    private void Update()
    {
        if (Time.unscaledTime < nextRefreshAt)
        {
            return;
        }
        nextRefreshAt = Time.unscaledTime + 0.2f;
        Refresh();
    }

    private void Refresh()
    {
        var busy = AuraToolsAutoBattleModelRuntime.AnyTrainingBusy()
                   || AuraToolsAutoBattleSimulationRuntime.GetStatus().Busy;
        foreach (var control in controls)
        {
            if (control != null)
            {
                control.interactable = !busy;
            }
        }
    }
}

internal sealed class AuraToolsAutoBattleJourneyStatusView : MonoBehaviour
{
    private Text? text;
    private float nextRefreshAt;

    public void Configure(Text target)
    {
        text = target;
        Refresh();
    }

    private void Update()
    {
        if (Time.unscaledTime < nextRefreshAt)
        {
            return;
        }
        nextRefreshAt = Time.unscaledTime + 0.5f;
        Refresh();
    }

    private void Refresh()
    {
        if (text != null)
        {
            text.text = AuraToolsAutoBattleJourneyRuntime.DescribeCurrentCapture();
        }
    }
}

internal sealed class AuraToolsAutoBattleFoundationStatusView : MonoBehaviour
{
    private Text? statusText;
    private Button? trainButton;
    private Button? cancelButton;
    private Button? openButton;
    private float nextRefreshAt;
    private bool foundationReady;
    private string readinessMessage = "";

    public void Configure(
        Text text,
        Button train,
        Button cancel,
        Button open)
    {
        statusText = text;
        trainButton = train;
        cancelButton = cancel;
        openButton = open;
        foundationReady = AuraToolsAutoBattleFoundationRuntime.CheckReadiness(
            out readinessMessage);
        Refresh();
    }

    private void Update()
    {
        if (Time.unscaledTime < nextRefreshAt)
        {
            return;
        }
        nextRefreshAt = Time.unscaledTime + 0.2f;
        Refresh();
    }

    private void Refresh()
    {
        if (!foundationReady)
        {
            foundationReady = AuraToolsAutoBattleFoundationRuntime.CheckReadiness(
                out readinessMessage);
        }
        var status = AuraToolsAutoBattleFoundationRuntime.GetStatus();
        var otherBusy = AuraToolsAutoBattleSimulationRuntime.GetStatus().Busy
                        || AuraToolsAutoBattleModelRuntime.GetTrainingStatus(
                            AuraToolsConfigService.MatchExperience.AutoBattle.Profile).Busy;
        if (statusText != null)
        {
            var progress = status.RequestedCampaigns <= 0
                ? ""
                : " · 冒险 "
                  + status.CompletedCampaigns
                  + "/"
                  + status.RequestedCampaigns;
            var modelProgress = !string.Equals(
                    status.Phase,
                    "model-training",
                    StringComparison.Ordinal)
                ? ""
                : " · 模型 Epoch "
                  + status.ModelEpoch
                  + "/"
                  + status.ModelTotalEpochs
                  + " · Frame "
                  + status.ModelCompletedFrames
                  + "/"
                  + status.ModelTotalFrames
                  + " · Loss "
                  + status.ModelValidationLoss.ToString("F4")
                  + " · 最佳 "
                  + status.ModelBestEpoch
                  + " · 耐心 "
                  + status.ModelStaleEpochs;
            statusText.text = status.Stage switch
            {
                AutoBattleFoundationStage.Idle => readinessMessage,
                AutoBattleFoundationStage.Queued => "底模训练已排队",
                AutoBattleFoundationStage.Training => status.Message
                                                      + progress
                                                      + modelProgress
                                                      + " · "
                                                      + Math.Max(0, status.ActiveWorkerCount)
                                                      + " active · "
                                                      + Math.Max(0, status.PeakWorkerCount)
                                                      + "/"
                                                      + Math.Max(1, status.WorkerCount)
                                                      + " peak/effective · "
                                                      + Math.Max(0, status.ObservedWorkerThreads)
                                                      + " 线程"
                                                      + " · 深度 "
                                                      + Math.Max(0, status.MaximumActiveBattleDepth)
                                                      + "/"
                                                      + Math.Max(0, status.MaximumCompletedBattleDepth)
                                                      + "/37"
                                                      + (status.SearchSimulationsPerSecond <= 0d
                                                          ? ""
                                                          : " · 搜索 "
                                                            + status.SearchSimulationsPerSecond.ToString("F0")
                                                            + "/秒 · 提前 "
                                                            + status.SearchEarlyStops)
                                                      + (status.CampaignsPerSecond <= 0d
                                                          ? ""
                                                          : " · "
                                                            + status.CampaignsPerSecond.ToString("F2")
                                                            + " 冒险/秒 · 约 "
                                                            + status.BattlesPerSecond.ToString("F1")
                                                            + " 战/秒 · 已用 "
                                                            + FormatDuration(status.ElapsedSeconds)
                                                            + " · ETA "
                                                            + FormatDuration(status.EstimatedRemainingSeconds)),
                AutoBattleFoundationStage.Writing => "正在写入底模与报告",
                AutoBattleFoundationStage.Completed => status.AcceptancePassed
                    ? "底模已达标 · 普通 "
                      + status.NormalWinRate.ToString("P1")
                      + " · 高级 "
                      + status.AdvancedWinRate.ToString("P1")
                    : "底模未达标 · 普通 "
                      + status.NormalWinRate.ToString("P1")
                      + " / 高级 "
                      + status.AdvancedWinRate.ToString("P1"),
                AutoBattleFoundationStage.Cancelling => "正在取消底模训练",
                AutoBattleFoundationStage.Cancelled => "底模训练已取消",
                AutoBattleFoundationStage.Failed => status.Message,
                _ => status.Message
            };
            statusText.color = status.Stage == AutoBattleFoundationStage.Failed
                               || !string.IsNullOrWhiteSpace(
                                   status.ProgressDiagnostic)
                               || (status.Stage == AutoBattleFoundationStage.Idle
                                   && !foundationReady)
                               || (status.Stage == AutoBattleFoundationStage.Completed
                                   && !status.AcceptancePassed)
                ? AuraToolsUi.WarningText
                : status.AcceptancePassed
                    ? AuraToolsUi.SuccessText
                    : AuraToolsUi.MutedText;
        }
        if (trainButton != null)
        {
            var externalBusy =
                AuraToolsFoundationWorkerRuntime.ExternalTrainingActive();
            trainButton.interactable = foundationReady
                                       && !status.Busy
                                       && !otherBusy
                                       && !externalBusy;
            SetButtonLabel(
                trainButton,
                status.Busy
                    ? "训练中..."
                    : externalBusy
                        ? "外部训练中"
                    : foundationReady
                        ? "训练底模"
                        : "知识未就绪");
        }
        if (cancelButton != null)
        {
            cancelButton.interactable = status.Busy
                                        && status.Stage
                                        != AutoBattleFoundationStage.Cancelling;
        }
        if (openButton != null)
        {
            openButton.interactable = !string.IsNullOrWhiteSpace(status.ResultDirectory)
                                      || Directory.Exists(
                                          AuraToolsAutoBattleSimulationRuntime.ResultsRootDirectory);
        }
    }

    private static string FormatDuration(double seconds)
    {
        if (double.IsNaN(seconds)
            || double.IsInfinity(seconds)
            || seconds <= 0d)
        {
            return "--";
        }
        var span = TimeSpan.FromSeconds(seconds);
        return span.TotalHours >= 1d
            ? ((int)span.TotalHours).ToString("00")
              + ":"
              + span.Minutes.ToString("00")
              + ":"
              + span.Seconds.ToString("00")
            : span.Minutes.ToString("00")
              + ":"
              + span.Seconds.ToString("00");
    }

    private static void SetButtonLabel(Button button, string value)
    {
        var label = button.GetComponentInChildren<Text>(true);
        if (label != null)
        {
            label.text = value;
        }
    }
}

internal sealed class AuraToolsAutoBattleExternalValidationStatusView :
    MonoBehaviour
{
    private string profile = "balanced";
    private Text? statusText;
    private Button? selectButton;
    private Button? promoteButton;
    private Button? clearButton;
    private float nextRefreshAt;

    public void Configure(
        string decisionProfile,
        Text text,
        Button select,
        Button promote,
        Button clear)
    {
        profile = string.IsNullOrWhiteSpace(decisionProfile)
            ? "balanced"
            : decisionProfile;
        statusText = text;
        selectButton = select;
        promoteButton = promote;
        clearButton = clear;
        Refresh();
    }

    private void Update()
    {
        if (Time.unscaledTime < nextRefreshAt)
        {
            return;
        }
        nextRefreshAt = Time.unscaledTime + 0.25f;
        Refresh();
    }

    private void Refresh()
    {
        var entry =
            AuraToolsAutoBattleModelRuntime.SnapshotExternalValidationModel();
        var settings = AuraToolsConfigService.MatchExperience.AutoBattle;
        var busy = AuraToolsAutoBattleFoundationRuntime.GetStatus().Busy
                   || AuraToolsAutoBattleSimulationRuntime.GetStatus().Busy
                   || AuraToolsAutoBattleGameValidationRuntime.GetStatus().Busy
                   || AuraToolsAutoBattleModelRuntime.AnyTrainingBusy();
        var selected = entry != null
                       && string.Equals(
                           settings.EvaluationModelId,
                           entry.ModelId,
                           StringComparison.Ordinal);
        var gateReason = "";
        var ready = selected
                    && AuraToolsAutoBattleSimulationRuntime.CanActivateModel(
                        profile,
                        entry!.ModelId,
                        out gateReason);
        if (!selected)
        {
            gateReason = entry == null
                ? "尚未选择外部底模包"
                : "该底模尚未设为当前评估目标";
        }
        if (statusText != null)
        {
            statusText.text = entry == null
                ? "尚未选择外部待验底模"
                : "外部待验底模："
                  + entry.DisplayName
                  + " · "
                  + (ready ? "双门禁已通过，可入库" : gateReason);
            statusText.color = ready
                ? AuraToolsUi.SuccessText
                : entry == null
                    ? AuraToolsUi.MutedText
                    : AuraToolsUi.WarningText;
        }
        if (selectButton != null)
        {
            selectButton.interactable = !busy;
        }
        if (promoteButton != null)
        {
            promoteButton.interactable = !busy && ready;
        }
        if (clearButton != null)
        {
            clearButton.interactable = !busy && entry != null;
        }
    }
}

internal sealed class AuraToolsAutoBattleGameValidationStatusView : MonoBehaviour
{
    private Text? statusText;
    private Button? runButton;
    private Button? cancelButton;
    private Button? openButton;
    private float nextRefreshAt;

    public void Configure(
        Text text,
        Button run,
        Button cancel,
        Button open)
    {
        statusText = text;
        runButton = run;
        cancelButton = cancel;
        openButton = open;
        Refresh();
    }

    private void Update()
    {
        if (Time.unscaledTime < nextRefreshAt)
        {
            return;
        }
        nextRefreshAt = Time.unscaledTime + 0.2f;
        Refresh();
    }

    private void Refresh()
    {
        var status = AuraToolsAutoBattleGameValidationRuntime.GetStatus();
        var otherBusy = AuraToolsAutoBattleFoundationRuntime.GetStatus().Busy
                        || AuraToolsAutoBattleSimulationRuntime.GetStatus().Busy
                        || AuraToolsAutoBattleModelRuntime.GetTrainingStatus(
                            AuraToolsConfigService.MatchExperience.AutoBattle.Profile).Busy;
        if (statusText != null)
        {
            var progress = status.RequestedBattles <= 0
                ? ""
                : " · "
                  + status.CompletedBattles
                  + "/"
                  + status.RequestedBattles
                  + " 战";
            statusText.text = status.Message + progress;
            statusText.color = status.Stage == AutoBattleGameValidationStage.Failed
                               || status.Stage == AutoBattleGameValidationStage.Cancelled
                ? AuraToolsUi.WarningText
                : status.Stage == AutoBattleGameValidationStage.Passed
                    ? AuraToolsUi.SuccessText
                    : AuraToolsUi.MutedText;
        }
        if (runButton != null)
        {
            runButton.interactable = !status.Busy && !otherBusy;
            SetButtonLabel(runButton, status.Busy ? "验证中..." : "实机验证");
        }
        if (cancelButton != null)
        {
            cancelButton.interactable = status.Busy
                                        && status.Stage
                                        != AutoBattleGameValidationStage.Cancelling;
        }
        if (openButton != null)
        {
            openButton.interactable = Directory.Exists(
                AuraToolsAutoBattleGameValidationRuntime.ResultsRootDirectory);
        }
    }

    private static void SetButtonLabel(Button button, string value)
    {
        var label = button.GetComponentInChildren<Text>(true);
        if (label != null)
        {
            label.text = value;
        }
    }
}

internal sealed class AuraToolsAutoBattleTrainingStatusView : MonoBehaviour
{
    private string profile = "balanced";
    private string modelMode = "off";
    private Text? statusText;
    private Button? generateButton;
    private Button? importButton;
    private Button? rollbackButton;
    private Button? cancelButton;
    private Button? modelModeButton;
    private float nextRefreshAt;

    public void Configure(
        string decisionProfile,
        string trainedModelMode,
        Text text,
        Button generate,
        Button import,
        Button rollback,
        Button cancel,
        Button modelModeControl)
    {
        profile = decisionProfile ?? "balanced";
        modelMode = trainedModelMode ?? "off";
        statusText = text;
        generateButton = generate;
        importButton = import;
        rollbackButton = rollback;
        cancelButton = cancel;
        modelModeButton = modelModeControl;
        Refresh();
    }

    private void Update()
    {
        if (Time.unscaledTime < nextRefreshAt)
        {
            return;
        }
        nextRefreshAt = Time.unscaledTime + 0.2f;
        Refresh();
    }

    private void Refresh()
    {
        var status = AuraToolsAutoBattleModelRuntime.GetTrainingStatus(profile);
        var simulationBusy = AuraToolsAutoBattleSimulationRuntime.GetStatus().Busy
                             || AuraToolsAutoBattleFoundationRuntime.GetStatus().Busy;
        var candidateExists =
            AuraToolsAutoBattleModelRuntime.CandidateExists(profile);
        var candidateModelId = candidateExists
            ? AuraToolsAutoBattleModelRuntime.CandidateModelId(profile)
            : "none";
        var promotionReason = candidateExists
            ? "尚未完成标准评估"
            : "请先训练候选";
        var promotionReady = candidateExists
                             && AuraToolsAutoBattleSimulationRuntime.CanActivateModel(
                                 profile,
                                 candidateModelId,
                                 out promotionReason);
        if (statusText != null)
        {
            statusText.text = Describe(status, candidateExists, promotionReady, promotionReason);
            statusText.color = status.Stage == AutoBattleTrainingStage.Failed
                ? new Color(1f, 0.46f, 0.42f, 1f)
                : status.Stage == AutoBattleTrainingStage.CandidateReady
                  && promotionReady
                  || status.Stage == AutoBattleTrainingStage.Imported
                    ? AuraToolsUi.SuccessText
                    : status.Stage == AutoBattleTrainingStage.CandidateReady
                      && !promotionReady
                      ? new Color(1f, 0.78f, 0.32f, 1f)
                    : status.Stage == AutoBattleTrainingStage.Cancelling
                      || status.Stage == AutoBattleTrainingStage.Cancelled
                        ? new Color(1f, 0.78f, 0.32f, 1f)
                    : AuraToolsUi.MutedText;
        }

        if (generateButton != null)
        {
            var generating = status.Busy
                             && status.Stage != AutoBattleTrainingStage.Importing;
            generateButton.interactable = !status.Busy && !simulationBusy;
            SetButtonLabel(generateButton, generating ? "训练中..." : "训练候选");
        }
        if (importButton != null)
        {
            importButton.interactable = !status.Busy
                                        && !simulationBusy
                                        && promotionReady;
            SetButtonLabel(
                importButton,
                status.Stage == AutoBattleTrainingStage.Importing ? "保存中..." : "保存新冠军");
        }
        if (rollbackButton != null)
        {
            rollbackButton.interactable = !status.Busy && !simulationBusy;
        }
        if (cancelButton != null)
        {
            cancelButton.interactable = status.Busy
                                        && status.Stage != AutoBattleTrainingStage.Importing
                                        && status.Stage != AutoBattleTrainingStage.Cancelling;
            SetButtonLabel(
                cancelButton,
                status.Stage == AutoBattleTrainingStage.Cancelling ? "取消中..." : "取消");
        }
        if (modelModeButton != null)
        {
            modelModeButton.interactable = !status.Busy && !simulationBusy;
        }
    }

    private string Describe(
        AutoBattleTrainingStatus status,
        bool candidateExists,
        bool promotionReady,
        string promotionReason)
    {
        var profileLabel = profile switch
        {
            "aggressive" => "进攻",
            "defensive" => "稳健",
            _ => "均衡"
        };
        var counts = status.SampleCount > 0
            ? " · 样本 " + status.SampleCount
              + " / 偏好对 " + status.PreferencePairCount
            : "";
        return status.Stage switch
        {
            AutoBattleTrainingStage.Queued => profileLabel + " · 已排队",
            AutoBattleTrainingStage.ReadingSamples => profileLabel + " · 正在读取样本",
            AutoBattleTrainingStage.Training => profileLabel + " · 训练中" + counts,
            AutoBattleTrainingStage.WritingCandidate => profileLabel + " · 正在写入候选" + counts,
            AutoBattleTrainingStage.Cancelling => profileLabel + " · 正在取消训练",
            AutoBattleTrainingStage.Cancelled => profileLabel + " · 训练已取消",
            AutoBattleTrainingStage.CandidateReady => status.WeightCount > 0
                ? profileLabel + " · 候选已生成 · 偏好对 "
                  + status.PreferencePairCount
                  + " / 权重 " + status.WeightCount
                  + " · "
                  + (promotionReady
                      ? "标准评估已通过"
                      : "下一步："
                        + Compact(promotionReason))
                : profileLabel + " · 检测到可导入的候选模型",
            AutoBattleTrainingStage.Importing => profileLabel + " · 正在导入候选",
            AutoBattleTrainingStage.Imported => profileLabel + " · 已导入"
                                                + ModelModeSuffix(modelMode)
                                                + " · 权重 " + status.WeightCount,
            AutoBattleTrainingStage.Failed => profileLabel + " · " + Compact(status.Message),
            _ => profileLabel + " · " + status.Message
                 + (candidateExists && !promotionReady
                     ? " · 待评估：" + Compact(promotionReason)
                     : "")
        };
    }

    private static string ModelModeSuffix(string mode)
    {
        return mode switch
        {
            "shadow" => "，正在影子评估",
            "active" => "，正在受限应用",
            _ => "，尚未启用"
        };
    }

    private static string Compact(string value)
    {
        var text = string.IsNullOrWhiteSpace(value) ? "操作失败" : value.Trim();
        return text.Length <= 72 ? text : text.Substring(0, 69) + "...";
    }

    private static void SetButtonLabel(Button button, string value)
    {
        var label = button.GetComponentInChildren<Text>(true);
        if (label != null)
        {
            label.text = value;
        }
    }
}

internal sealed class AuraToolsAutoBattleSimulationStatusView : MonoBehaviour
{
    private string profile = "balanced";
    private string modelId = "";
    private bool evolutionView;
    private Text? statusText;
    private Text? operationDetailText;
    private Button? primaryButton;
    private Button? cancelButton;
    private Button? resultButton;
    private bool hasScenario;
    private IReadOnlyList<Selectable> lockedControls = Array.Empty<Selectable>();
    private float nextRefreshAt;

    public void Configure(
        string decisionProfile,
        string selectedModelId,
        bool showEvolution,
        Text text,
        Text operationDetail,
        Button primary,
        Button cancel,
        Button result,
        bool scenarioAvailable,
        IReadOnlyList<Selectable> controls)
    {
        profile = decisionProfile ?? "balanced";
        modelId = selectedModelId ?? "";
        evolutionView = showEvolution;
        statusText = text;
        operationDetailText = operationDetail;
        primaryButton = primary;
        cancelButton = cancel;
        resultButton = result;
        hasScenario = scenarioAvailable;
        lockedControls = controls ?? Array.Empty<Selectable>();
        Refresh();
    }

    private void Update()
    {
        if (Time.unscaledTime < nextRefreshAt)
        {
            return;
        }
        nextRefreshAt = Time.unscaledTime + 0.2f;
        Refresh();
    }

    private void Refresh()
    {
        var status = AuraToolsAutoBattleSimulationRuntime.GetStatus();
        var modelBusy = AuraToolsAutoBattleModelRuntime.AnyTrainingBusy();
        var workBusy = status.Busy || modelBusy;
        if (statusText != null)
        {
            var progress = status.RequestedPairs > 0
                ? " · "
                  + status.CompletedPairs
                  + "/"
                  + status.RequestedPairs
                  + " "
                  + status.ProgressUnit
                : "";
            var message = string.IsNullOrWhiteSpace(status.Message)
                ? "尚未运行模拟评估"
                : status.Message.Trim();
            statusText.text = (message.Length <= 84 ? message : message.Substring(0, 81) + "...")
                              + progress;
            statusText.color = status.Stage == AutoBattleSimulationStage.Failed
                ? new Color(1f, 0.46f, 0.42f, 1f)
                : status.Stage == AutoBattleSimulationStage.Completed && status.GatePassed
                    ? AuraToolsUi.SuccessText
                    : status.Stage == AutoBattleSimulationStage.Cancelling
                      || status.Stage == AutoBattleSimulationStage.Cancelled
                      || status.Stage == AutoBattleSimulationStage.Completed
                        ? new Color(1f, 0.78f, 0.32f, 1f)
                    : AuraToolsUi.MutedText;
        }
        if (operationDetailText != null)
        {
            operationDetailText.text = status.Stage == AutoBattleSimulationStage.Failed
                ? status.Message
                : "";
            operationDetailText.color = status.Stage == AutoBattleSimulationStage.Failed
                ? new Color(1f, 0.46f, 0.42f, 1f)
                : AuraToolsUi.MutedText;
        }
        if (primaryButton != null)
        {
            primaryButton.interactable = !workBusy && hasScenario;
            SetButtonLabel(
                primaryButton,
                status.Busy
                    ? status.Operation == AutoBattleSimulationOperation.PolicyEvolution
                        ? "进化中..."
                        : "评估中..."
                    : evolutionView
                        ? "开始进化"
                        : "运行标准评估");
        }
        if (cancelButton != null)
        {
            cancelButton.interactable = status.Busy
                                        && status.Stage
                                        != AutoBattleSimulationStage.Cancelling;
            SetButtonLabel(
                cancelButton,
                status.Stage == AutoBattleSimulationStage.Cancelling ? "取消中..." : "取消");
        }
        if (resultButton != null)
        {
            resultButton.interactable =
                AuraToolsAutoBattleUiSnapshotRuntime
                    .Snapshot(profile, modelId)
                    .Result
                    .Available;
        }
        foreach (var control in lockedControls)
        {
            if (control != null)
            {
                control.interactable = !workBusy;
            }
        }
    }

    private static void SetButtonLabel(Button button, string value)
    {
        var label = button.GetComponentInChildren<Text>(true);
        if (label != null)
        {
            label.text = value;
        }
    }
}

internal sealed class AuraToolsPanelBuildDriver : MonoBehaviour
{
    private Coroutine? build;

    public void Begin(Transform panel)
    {
        if (build != null)
        {
            StopCoroutine(build);
        }
        build = StartCoroutine(Build(panel));
    }

    private IEnumerator Build(Transform panel)
    {
        yield return AuraToolsSettingsRuntime.BuildPanelAcrossFrames(panel);
        build = null;
    }
}

internal sealed class AuraToolsAutoBattleSimulationResultView : MonoBehaviour
{
    private string profile = "balanced";
    private string modelId = "";
    private Text? title;
    private Text? primary;
    private Text? secondary;
    private Text? detail;
    private float nextRefreshAt;

    public void Configure(
        string decisionProfile,
        string selectedModelId,
        Text titleText,
        Text primaryText,
        Text secondaryText,
        Text detailText)
    {
        profile = decisionProfile ?? "balanced";
        modelId = selectedModelId ?? "";
        title = titleText;
        primary = primaryText;
        secondary = secondaryText;
        detail = detailText;
        AuraToolsAutoBattleUiSnapshotRuntime.RequestRefresh(
            profile,
            modelId);
        Refresh();
    }

    private void Update()
    {
        if (Time.unscaledTime < nextRefreshAt)
        {
            return;
        }
        nextRefreshAt = Time.unscaledTime + 0.75f;
        Refresh();
    }

    private void Refresh()
    {
        var result = AuraToolsAutoBattleUiSnapshotRuntime
            .Snapshot(profile, modelId)
            .Result;
        if (title != null)
        {
            title.text = result.Title;
            title.color = result.Available
                ? result.GatePassed
                    ? AuraToolsUi.SuccessText
                    : new Color(1f, 0.78f, 0.32f, 1f)
                : AuraToolsUi.MutedText;
        }
        if (primary != null)
        {
            primary.text = result.Primary;
        }
        if (secondary != null)
        {
            secondary.text = result.Secondary;
        }
        if (detail != null)
        {
            detail.text = result.Detail;
        }
    }
}

internal sealed class AuraToolsNativeTabRelay : MonoBehaviour, IPointerDownHandler
{
    public void OnPointerDown(PointerEventData eventData)
    {
        AuraToolsSettingsRuntime.HideActivePanel();
    }
}
