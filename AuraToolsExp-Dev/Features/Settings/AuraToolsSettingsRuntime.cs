using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using AuraCombatAi.Shared;
using AuraShared.Core;
using AuraUi.Shared;
using AuraToolsExp.Dll.Config;
using AuraToolsExp.Dll.Features.Audio;
using AuraToolsExp.Dll.Features.AutoBattle;
using AuraToolsExp.Dll.Features.DamageMeter;
using AuraToolsExp.Dll.Features.Feast;
using AuraToolsExp.Dll.Features.Logging;
using AuraToolsExp.Dll.Features.MatchRecords;
using AuraToolsExp.Dll.Features.PixelEmoji;
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
using UiTransitionGuardShared;
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
    private static readonly AuraToolsPanelBuildState PanelBuildState = new();
    private static bool autoBattleSnapshotSubscribed;
    private static long autoBattleSnapshotRevisionBuilt = -1;
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
        RegisterAfter(modConfig, "SettingUI.Close", ClosePanel);
        RegisterAfter(modConfig, "SettingUI.Hide", ClosePanel);
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
        PanelBuildState.CancelBuild();
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
        ClosePanel(context);
        activePanel = null;
        activePanelHost = null;
        activeTabParent = null;
        PanelBuildState.Reset();
        autoBattleSnapshotRevisionBuilt = -1;
    }

    private static void ClosePanel(ModHookContext context)
    {
        HideActivePanel();
        AuraToolsUi.CloseOwnedOverlays("SettingUI disabled");
        UiTransitionGuardRuntime.BeginTransition(
            null,
            AuraToolsIds.ModId,
            "SettingUI disabled",
            6);
        UiTransitionGuardRuntime.ScrubNow(
            null,
            AuraToolsIds.ModId,
            "SettingUI disabled");
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
            var changedPanel = activePanel != existing.gameObject;
            activePanel = existing.gameObject;
            if (changedPanel)
            {
                PanelBuildState.Adopt(
                    existing.GetComponent<AuraToolsPanelBuildMarker>()?.Completed == true);
            }
            PositionPanelInHost(activePanel, panelHost, tabParent);
            return;
        }

        PanelBuildState.Reset();
        activePanel = AuraToolsUi.CreateRect(AuraPanelName, panelHost, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
        activePanel.AddComponent<AuraToolsPanelBuildMarker>();
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
        if (!PanelBuildState.IsBuilt)
        {
            BeginInitialPanelBuild(activePanel);
        }
    }

    private static void BeginInitialPanelBuild(GameObject panel)
    {
        var ticket = PanelBuildState.Begin();
        if (ticket == 0)
        {
            return;
        }

        if (!AuraSharedFrameScheduler.StartCoroutine(
                "AuraTools.Settings.BuildPanel",
                BuildPanelAcrossFrames(panel.transform, ticket)))
        {
            PanelBuildState.Complete(ticket, false);
            AuraToolsLog.Warn("[Settings] persistent scheduler rejected panel build; it will retry on the next tab open.");
        }
    }

    internal static IEnumerator BuildPanelAcrossFrames(Transform panel, int ticket)
    {
        var completed = false;
        try
        {
            if (!CanContinuePanelBuild(panel, ticket))
            {
                yield break;
            }

            var marker = panel.GetComponent<AuraToolsPanelBuildMarker>()
                         ?? panel.gameObject.AddComponent<AuraToolsPanelBuildMarker>();
            marker.Completed = false;
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
            if (!CanContinuePanelBuild(panel, ticket)) yield break;
            CreateDataDirectorySection(content);
            yield return null;
            if (!CanContinuePanelBuild(panel, ticket)) yield break;
            CreateSkinSection(content);
            yield return null;
            if (!CanContinuePanelBuild(panel, ticket)) yield break;
            CreateAudioSection(content);
            yield return null;
            if (!CanContinuePanelBuild(panel, ticket)) yield break;
            CreateMatchExperienceSection(content);
            yield return null;
            if (!CanContinuePanelBuild(panel, ticket)) yield break;
            CreateBattleStrategySection(content);
            yield return null;
            if (!CanContinuePanelBuild(panel, ticket)) yield break;
            CreateLoggingSection(content);
            marker.Completed = true;
            var autoBattle = AuraToolsConfigService.MatchExperience.AutoBattle;
            autoBattleSnapshotRevisionBuilt =
                AuraToolsAutoBattleUiSnapshotRuntime
                    .Snapshot(autoBattle.Profile, autoBattle.SelectedModelId)
                    .Revision;
            completed = true;
        }
        finally
        {
            PanelBuildState.Complete(ticket, completed);
        }
    }

    private static bool CanContinuePanelBuild(Transform panel, int ticket)
    {
        return panel != null
               && panel.gameObject.activeInHierarchy
               && activePanel == panel.gameObject
               && PanelBuildState.IsCurrent(ticket);
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
            });
            AuraToolsUi.AddText(toggles.transform, "联机同步皮肤选择", AuraToolsUi.BodyFontSize, TextAnchor.MiddleLeft, AuraToolsUi.Text, AuraToolsUi.TextMinHeight, 1f);

            var entryRow = CreateInlineRow(content, "SkinEntryUiRow");
            AuraToolsUi.AddToggle(entryRow.transform, AuraToolsConfigService.Skin.ShowEntrySkinButton, value =>
            {
                AuraToolsConfigService.Skin.ShowEntrySkinButton = value;
                AuraToolsConfigService.SaveSkin();
            });
            AuraToolsUi.AddText(entryRow.transform, "在角色选择界面显示皮肤按钮", AuraToolsUi.BodyFontSize, TextAnchor.MiddleLeft, AuraToolsUi.Text, AuraToolsUi.TextMinHeight, 1f);

        });
    }

    private static void CreateMatchExperienceSection(Transform parent)
    {
        CreateSectionLabel(parent, "游戏体验");
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
            Button? starterDeckModeButton = null;
            starterDeckModeButton = AuraToolsUi.AddButton(row.transform, settings.Mode == StarterDeckModes.RoleSpecific ? "切到全局" : "切到按角色", () =>
            {
                settings.Mode = settings.Mode == StarterDeckModes.RoleSpecific ? StarterDeckModes.Global : StarterDeckModes.RoleSpecific;
                AuraToolsConfigService.SaveMatchExperience();
                AuraToolsUi.SetButtonLabel(
                    starterDeckModeButton,
                    settings.Mode == StarterDeckModes.RoleSpecific ? "切到全局" : "切到按角色");
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

        var pixelEmoji = AuraToolsConfigService.PixelEmoji;
        CreateSubmodule(parent, "像素表情工坊", pixelEmoji.Enabled, value =>
        {
            pixelEmoji.Enabled = value;
            AuraToolsConfigService.SavePixelEmoji();
        }, content =>
        {
            var row = CreateInlineRow(content, "PixelEmojiConfigRow");
            var itemCount = PixelEmojiLibraryStore.GetItems().Count;
            AuraToolsUi.AddText(
                row.transform,
                "作品：" + itemCount + "；收藏：" + pixelEmoji.FavoriteIds.Count + "/" + pixelEmoji.MaxFavorites + "；24×24 / 1～8帧 / 0.2秒 / PNG序列",
                AuraToolsUi.BodyFontSize,
                TextAnchor.MiddleLeft,
                AuraToolsUi.Text,
                AuraToolsUi.TextMinHeight,
                1f);
            AuraToolsUi.AddButton(row.transform, "打开工坊", () => PixelEmojiWorkshop.Show(activePanel!.transform), 104f);
            Button? pixelEmojiSyncButton = null;
            pixelEmojiSyncButton = AuraToolsUi.AddButton(row.transform, pixelEmoji.SyncRemote ? "关闭联机展示" : "开启联机展示", () =>
            {
                pixelEmoji.SyncRemote = !pixelEmoji.SyncRemote;
                AuraToolsConfigService.SavePixelEmoji();
                AuraToolsUi.SetButtonLabel(
                    pixelEmojiSyncButton,
                    pixelEmoji.SyncRemote ? "关闭联机展示" : "开启联机展示");
            }, 128f);
            AuraToolsUi.AddText(content, "工坊只从设置页进入；冒险表情列表只在尾部追加已收藏的成品。", AuraToolsUi.HintFontSize, TextAnchor.MiddleLeft, AuraToolsUi.MutedText, AuraToolsUi.TextMinHeight, 1f);
        }, pixelEmoji.Enabled ? AuraToolsUi.SuccessText : AuraToolsUi.MutedText);

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

        var matchRecords = AuraToolsConfigService.MatchExperience.MatchRecords;
        var damageMeter = matchRecords.Statistics;
        CreateSubmodule(parent, "对局记录", matchRecords.Enabled, value =>
        {
            matchRecords.Enabled = value;
            AuraToolsConfigService.SaveMatchExperience();
            AuraToolsDamageMeterRuntime.SetVisible(false);
        }, content =>
        {
            var statisticsToggleRow = CreateInlineRow(content, "MatchRecordStatisticsToggleRow");
            AuraToolsUi.AddText(statisticsToggleRow.transform, "DPT统计", AuraToolsUi.BodyFontSize, TextAnchor.MiddleLeft, AuraToolsUi.Text, AuraToolsUi.TextMinHeight, 1f);
            AuraToolsUi.AddToggle(statisticsToggleRow.transform, damageMeter.Enabled, value =>
            {
                damageMeter.Enabled = value;
                AuraToolsConfigService.SaveMatchExperience();
                AuraToolsDamageMeterRuntime.SetVisible(false);
            });

            var replayToggleRow = CreateInlineRow(content, "MatchRecordReplayToggleRow");
            AuraToolsUi.AddText(replayToggleRow.transform, "自动记录完整战斗回放", AuraToolsUi.BodyFontSize, TextAnchor.MiddleLeft, AuraToolsUi.Text, AuraToolsUi.TextMinHeight, 1f);
            AuraToolsUi.AddToggle(replayToggleRow.transform, matchRecords.Replay.Enabled, value =>
            {
                matchRecords.Replay.Enabled = value;
                AuraToolsConfigService.SaveMatchExperience();
            });

            var replayLimitRow = CreateInlineRow(content, "MatchRecordReplayLimitRow");
            AuraToolsUi.AddText(replayLimitRow.transform, "自动回放保存上限", AuraToolsUi.BodyFontSize, TextAnchor.MiddleLeft, AuraToolsUi.Text, AuraToolsUi.TextMinHeight, 1f);
            AuraToolsUi.AddInput(replayLimitRow.transform, matchRecords.Replay.AutoRecordLimit.ToString(CultureInfo.InvariantCulture), value =>
            {
                if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
                {
                    matchRecords.Replay.AutoRecordLimit = parsed;
                    matchRecords.Replay.Normalize();
                    AuraToolsConfigService.SaveMatchExperience();
                }
            }, 104f);

            var presentationRow = CreateInlineRow(content, "MatchRecordPresentationRow");
            AuraToolsUi.AddText(presentationRow.transform, "回放演出节奏", AuraToolsUi.BodyFontSize, TextAnchor.MiddleLeft, AuraToolsUi.Text, AuraToolsUi.TextMinHeight, 1f);
            Button? presentationButton = null;
            presentationButton = AuraToolsUi.AddButton(presentationRow.transform, matchRecords.Replay.PresentationMode, () =>
            {
                matchRecords.Replay.PresentationMode = matchRecords.Replay.PresentationMode == "Standard"
                    ? "Compact"
                    : matchRecords.Replay.PresentationMode == "Compact" ? "Showcase" : "Standard";
                AuraToolsConfigService.SaveMatchExperience();
                AuraToolsUi.SetButtonLabel(presentationButton, matchRecords.Replay.PresentationMode);
            }, 104f);

            var videoRow = CreateInlineRow(content, "MatchRecordVideoRow");
            AuraToolsUi.AddText(videoRow.transform, "视频导出", AuraToolsUi.BodyFontSize, TextAnchor.MiddleLeft, AuraToolsUi.Text, AuraToolsUi.TextMinHeight, 1f);
            Button? videoQualityButton = null;
            videoQualityButton = AuraToolsUi.AddButton(videoRow.transform, matchRecords.Replay.Video.Quality, () =>
            {
                matchRecords.Replay.Video.Quality = matchRecords.Replay.Video.Quality == "1080p" ? "720p" : "1080p";
                AuraToolsConfigService.SaveMatchExperience();
                AuraToolsUi.SetButtonLabel(videoQualityButton, matchRecords.Replay.Video.Quality);
            }, 86f);
            Button? videoFpsButton = null;
            videoFpsButton = AuraToolsUi.AddButton(videoRow.transform, matchRecords.Replay.Video.FramesPerSecond + " FPS", () =>
            {
                matchRecords.Replay.Video.FramesPerSecond = matchRecords.Replay.Video.FramesPerSecond >= 60 ? 30 : 60;
                AuraToolsConfigService.SaveMatchExperience();
                AuraToolsUi.SetButtonLabel(videoFpsButton, matchRecords.Replay.Video.FramesPerSecond + " FPS");
            }, 86f);
            AuraToolsUi.AddText(videoRow.transform, "UI", AuraToolsUi.HintFontSize, TextAnchor.MiddleRight, AuraToolsUi.MutedText, AuraToolsUi.TextMinHeight, 0f, 28f);
            AuraToolsUi.AddToggle(videoRow.transform, matchRecords.Replay.Video.IncludeUi, value =>
            {
                matchRecords.Replay.Video.IncludeUi = value;
                AuraToolsConfigService.SaveMatchExperience();
            });
            AuraToolsUi.AddText(videoRow.transform, "音频", AuraToolsUi.HintFontSize, TextAnchor.MiddleRight, AuraToolsUi.MutedText, AuraToolsUi.TextMinHeight, 0f, 42f);
            AuraToolsUi.AddToggle(videoRow.transform, matchRecords.Replay.Video.IncludeAudio, value =>
            {
                matchRecords.Replay.Video.IncludeAudio = value;
                AuraToolsConfigService.SaveMatchExperience();
            });
            AuraToolsUi.AddText(videoRow.transform, "优先MP4", AuraToolsUi.HintFontSize, TextAnchor.MiddleRight, AuraToolsUi.MutedText, AuraToolsUi.TextMinHeight, 0f, 70f);
            AuraToolsUi.AddToggle(videoRow.transform, matchRecords.Replay.Video.PreferMp4, value =>
            {
                matchRecords.Replay.Video.PreferMp4 = value;
                AuraToolsConfigService.SaveMatchExperience();
            });

            var displayRow = CreateInlineRow(content, "DamageMeterDisplayModeRow");
            AuraToolsUi.AddText(displayRow.transform, "展示方式", AuraToolsUi.BodyFontSize, TextAnchor.MiddleLeft, AuraToolsUi.Text, AuraToolsUi.TextMinHeight, 1f);
            Button? displayModeButton = null;
            displayModeButton = AuraToolsUi.AddButton(displayRow.transform, damageMeter.DisplayMode == DamageMeterDisplayModes.Bars ? "进度条" : "表格", () =>
            {
                damageMeter.DisplayMode = damageMeter.DisplayMode == DamageMeterDisplayModes.Bars
                    ? DamageMeterDisplayModes.Table
                    : DamageMeterDisplayModes.Bars;
                AuraToolsConfigService.SaveMatchExperience();
                AuraToolsUi.SetButtonLabel(
                    displayModeButton,
                    damageMeter.DisplayMode == DamageMeterDisplayModes.Bars ? "进度条" : "表格");
            }, 96f);

            var scopeRow = CreateInlineRow(content, "DamageMeterDisplayScopeRow");
            AuraToolsUi.AddText(scopeRow.transform, "统计范围", AuraToolsUi.BodyFontSize, TextAnchor.MiddleLeft, AuraToolsUi.Text, AuraToolsUi.TextMinHeight, 1f);
            Button? displayScopeButton = null;
            displayScopeButton = AuraToolsUi.AddButton(scopeRow.transform, damageMeter.DisplayScope == DamageMeterDisplayScopes.Adventure ? "本轮冒险" : "本场战斗", () =>
            {
                damageMeter.DisplayScope = damageMeter.DisplayScope == DamageMeterDisplayScopes.Adventure
                    ? DamageMeterDisplayScopes.Fight
                    : DamageMeterDisplayScopes.Adventure;
                AuraToolsConfigService.SaveMatchExperience();
                AuraToolsUi.SetButtonLabel(
                    displayScopeButton,
                    damageMeter.DisplayScope == DamageMeterDisplayScopes.Adventure ? "本轮冒险" : "本场战斗");
            }, 96f);

            var teamRow = CreateInlineRow(content, "DamageMeterTeamFilterRow");
            AuraToolsUi.AddText(teamRow.transform, "统计阵营", AuraToolsUi.BodyFontSize, TextAnchor.MiddleLeft, AuraToolsUi.Text, AuraToolsUi.TextMinHeight, 1f);
            Button? teamFilterButton = null;
            teamFilterButton = AuraToolsUi.AddButton(teamRow.transform, DamageMeterTeamFilterLabel(damageMeter.TeamFilter), () =>
            {
                damageMeter.TeamFilter = damageMeter.TeamFilter == DamageMeterTeamFilters.All
                    ? DamageMeterTeamFilters.Friendly
                    : damageMeter.TeamFilter == DamageMeterTeamFilters.Friendly
                        ? DamageMeterTeamFilters.Enemy
                        : DamageMeterTeamFilters.All;
                AuraToolsConfigService.SaveMatchExperience();
                AuraToolsUi.SetButtonLabel(teamFilterButton, DamageMeterTeamFilterLabel(damageMeter.TeamFilter));
            }, 96f);

            var libraryRow = CreateInlineRow(content, "MatchRecordLibraryRow");
            AuraToolsUi.AddText(
                libraryRow.transform,
                "自动记录：" + AuraToolsMatchRecordsRuntime.AutoRecordCount
                + "，收藏对局：" + AuraToolsMatchRecordsRuntime.FavoriteRecordCount
                + "，冒险统计：" + AuraToolsDamageMeterRuntime.OutOfRunHistoryCount,
                AuraToolsUi.BodyFontSize,
                TextAnchor.MiddleLeft,
                AuraToolsUi.Text,
                AuraToolsUi.TextMinHeight,
                1f);
            AuraToolsUi.AddButton(libraryRow.transform, "打开对局资料库", () => AuraToolsMatchRecordsRuntime.OpenLibrary(activePanel!.transform), 132f);

            AuraToolsUi.AddText(content, "声明：该模块初始版本代码由【哈基米】提供，后续由【Aura】进行维护和功能开发。", AuraToolsUi.HintFontSize, TextAnchor.MiddleLeft, AuraToolsUi.MutedText, AuraToolsUi.TextMinHeight, 1f);
        }, matchRecords.Enabled ? AuraToolsUi.SuccessText : AuraToolsUi.MutedText);

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
            Button? skillCgSyncButton = null;
            skillCgSyncButton = AuraToolsUi.AddButton(row.transform, AuraToolsConfigService.SkillCg.SyncRemote ? "\u5173\u95ed\u540c\u6b65" : "\u5f00\u542f\u540c\u6b65", () =>
            {
                AuraToolsConfigService.SkillCg.SyncRemote = !AuraToolsConfigService.SkillCg.SyncRemote;
                AuraToolsConfigService.SaveSkillCg();
                AuraToolsUi.SetButtonLabel(
                    skillCgSyncButton,
                    AuraToolsConfigService.SkillCg.SyncRemote ? "\u5173\u95ed\u540c\u6b65" : "\u5f00\u542f\u540c\u6b65");
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

    private static string DamageMeterTeamFilterLabel(string teamFilter)
    {
        return teamFilter == DamageMeterTeamFilters.Friendly
            ? "友方"
            : teamFilter == DamageMeterTeamFilters.Enemy
                ? "敌方"
                : "全部";
    }

    private static void CreateBattleStrategySection(Transform parent)
    {
        var autoBattle = AuraToolsConfigService.MatchExperience.AutoBattle;
        CreateSectionLabel(parent, "战斗策略");
        CreateSubmodule(parent, "战斗策略实验室", autoBattle.Enabled, value =>
        {
            autoBattle.Enabled = value;
            AuraToolsConfigService.SaveMatchExperience();
        }, content =>
        {
            CreateSectionLabel(content, "当前策略");
            CreateAutoBattleModelApplicationRows(content);
            AuraToolsUi.AddText(
                content,
                "应用模式下，可执行的模型决策不会被规则评分、低置信度或质量门禁替换。只有模型未能加载、推理超时或连续无进展时，才会临时使用技术兜底。",
                AuraToolsUi.HintFontSize,
                TextAnchor.MiddleLeft,
                AuraToolsUi.MutedText,
                AuraToolsUi.TextMinHeight,
                1f);
            CreateAutoBattleToggleRow(
                content,
                "完整应用时进入战斗自动接管",
                autoBattle.StartActive,
                value =>
                {
                    autoBattle.StartActive = value;
                    AuraToolsConfigService.SaveMatchExperience();
                });
            CreateAutoBattleToggleRow(
                content,
                "显示 AI 预测标记",
                autoBattle.ShowPredictionMarkers,
                value =>
                {
                    autoBattle.ShowPredictionMarkers = value;
                    AuraToolsConfigService.SaveMatchExperience();
                });

            var modelLibrary = CreateCompactFoldout(content, "模型库与导入", "AutoBattle.ModelLibrary");
            CreateAutoBattleModelManagementSection(modelLibrary, autoBattle);

            var developerTools = CreateCompactFoldout(content, "训练、评估与开发者工具", "AutoBattle.DeveloperTools");
            CreateGameParametersSection(developerTools);
            CreateSectionLabel(developerTools, "玩家适配");
            CreateAutoBattlePlayerAdaptationSection(developerTools, autoBattle);
            CreateSectionLabel(developerTools, "评估与诊断");
            CreateAutoBattleAdvancedDiagnosticsSection(developerTools, autoBattle);
        }, autoBattle.Enabled ? AuraToolsUi.SuccessText : AuraToolsUi.MutedText);
    }

    private static void CreateAutoBattlePlayerAdaptationSection(
        Transform parent,
        AutoBattleSettings autoBattle)
    {
        AuraToolsUi.AddText(
            parent,
            "记录实战决策并在已选底模之上训练玩家偏好残差；底模始终保持冻结。",
            AuraToolsUi.HintFontSize,
            TextAnchor.MiddleLeft,
            AuraToolsUi.MutedText,
            AuraToolsUi.TextMinHeight,
            1f);

        var contentAdapterIds = AuraToolsCombatContentRuntime
            .SnapshotPolicyAdapters(autoBattle.SelectedModelId)
            .Select(item => item.Manifest.AdapterId)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        var activeAdapterIds = AuraToolsAutoBattleModelRuntime
            .SnapshotActiveAdapterIds(
                autoBattle.Profile,
                autoBattle.SelectedModelId);
        var personalAdapterCount = activeAdapterIds.Count(id =>
            !contentAdapterIds.Contains(id, StringComparer.Ordinal));
        AuraToolsUi.AddText(
            parent,
            "适配器链：内容 LoRA/低秩 "
            + contentAdapterIds.Length
            + " · 玩家残差 "
            + personalAdapterCount,
            AuraToolsUi.BodyFontSize,
            TextAnchor.MiddleLeft,
            AuraToolsUi.Text,
            AuraToolsUi.TextMinHeight,
            1f);

        CreateAutoBattleToggleRow(
            parent,
            "自动记录实战与完整旅程",
            autoBattle.CaptureTrainingSamples,
            value =>
            {
                autoBattle.CaptureTrainingSamples = value;
                AuraToolsConfigService.SaveMatchExperience();
            });
        var journeyCaptureText = AuraToolsUi.AddText(
            parent,
            "",
            AuraToolsUi.HintFontSize,
            TextAnchor.MiddleLeft,
            AuraToolsUi.MutedText,
            AuraToolsUi.TextMinHeight,
            1f);
        journeyCaptureText.gameObject
            .AddComponent<AuraToolsAutoBattleJourneyStatusView>()
            .Configure(journeyCaptureText);

        var trainingModeRow = CreateInlineRow(
            parent,
            "AutoBattleTrainingModeRow");
        var trainingModeText = AuraToolsUi.AddText(
            trainingModeRow.transform,
            "采集模式：" + AutoBattleTrainingModeLabel(autoBattle.TrainingMode),
            AuraToolsUi.BodyFontSize,
            TextAnchor.MiddleLeft,
            AuraToolsUi.Text,
            AuraToolsUi.TextMinHeight,
            1f);
        var trainingModeButton = AuraToolsUi.AddButton(
            trainingModeRow.transform,
            "切换模式",
            () =>
            {
                autoBattle.TrainingMode =
                    NextAutoBattleTrainingMode(autoBattle.TrainingMode);
                autoBattle.Normalize();
                AuraToolsConfigService.SaveMatchExperience();
                trainingModeText.text =
                    "采集模式："
                    + AutoBattleTrainingModeLabel(autoBattle.TrainingMode);
            },
            96f);
        AttachAutoBattleWorkLock(trainingModeRow, trainingModeButton);

        var trainingPresetRow = CreateInlineRow(
            parent,
            "AutoBattleTrainingPresetRow");
        AuraToolsUi.AddText(
            trainingPresetRow.transform,
            "残差训练预设",
            AuraToolsUi.HintFontSize,
            TextAnchor.MiddleLeft,
            AuraToolsUi.MutedText,
            AuraToolsUi.TextMinHeight,
            0f,
            112f);
        Text? trainingPresetSummary = null;
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
                }
                else
                {
                    autoBattle.Training.MarkCustom();
                }
                AuraToolsConfigService.SaveMatchExperience();
                if (trainingPresetSummary != null)
                {
                    trainingPresetSummary.text =
                        AutoBattleTrainingPresetSummary(autoBattle.Training);
                }
            },
            144f);
        AttachAutoBattleWorkLock(trainingPresetRow, trainingPresetButton);
        trainingPresetSummary = AuraToolsUi.AddText(
            trainingPresetRow.transform,
            AutoBattleTrainingPresetSummary(autoBattle.Training),
            AuraToolsUi.HintFontSize,
            TextAnchor.MiddleLeft,
            AuraToolsUi.MutedText,
            AuraToolsUi.TextMinHeight,
            1f);

        var parameterContent = CreateCompactFoldout(
            parent,
            "残差训练参数",
            "AutoBattle.PlayerResidualParameters");
        CreateAutoBattleTrainingParameterRows(parameterContent, autoBattle);

        var modelRow = CreateInlineRow(parent, "AutoBattleModelActionRow");
        var trainingStatusText = AuraToolsUi.AddText(
            modelRow.transform,
            "",
            AuraToolsUi.HintFontSize,
            TextAnchor.MiddleLeft,
            AuraToolsUi.MutedText,
            AuraToolsUi.TextMinHeight,
            1f);
        var generateButton = AuraToolsUi.AddButton(
            modelRow.transform,
            "训练玩家残差",
            () =>
            {
                if (!AuraToolsAutoBattleModelRuntime.QueueGenerateCandidate(
                        autoBattle.Profile))
                {
                    AuraToolsLog.Warn(
                        "[AutoBattle][Training] 玩家残差任务正在运行或未能提交");
                }
            },
            128f);
        var cancelTrainingButton = AuraToolsUi.AddButton(
            modelRow.transform,
            "取消",
            () => AuraToolsAutoBattleModelRuntime.CancelTraining(
                autoBattle.Profile),
            66f);

        var promotionRow = CreateInlineRow(
            parent,
            "AutoBattlePromotionActionRow");
        AuraToolsUi.AddText(
            promotionRow.transform,
            "玩家残差版本",
            AuraToolsUi.HintFontSize,
            TextAnchor.MiddleLeft,
            AuraToolsUi.MutedText,
            AuraToolsUi.TextMinHeight,
            1f);
        var importButton = AuraToolsUi.AddButton(
            promotionRow.transform,
            "保存已验证版本",
            () =>
            {
                if (!AuraToolsAutoBattleModelRuntime.QueueImportCandidate(
                        autoBattle.Profile))
                {
                    AuraToolsLog.Warn(
                        "[AutoBattle][Import] 候选尚未通过门禁或保存任务正在运行");
                }
            },
            128f);
        var rollbackButton = AuraToolsUi.AddButton(
            promotionRow.transform,
            "回退上一版本",
            () =>
            {
                if (!AuraToolsAutoBattleModelRuntime.QueueRollbackChampion(
                        autoBattle.Profile))
                {
                    AuraToolsLog.Warn(
                        "[AutoBattle][Rollback] 没有可回退版本或任务未能提交");
                }
            },
            112f);
        parent.gameObject
            .AddComponent<AuraToolsAutoBattleTrainingStatusView>()
            .Configure(
                autoBattle.Profile,
                trainingStatusText,
                generateButton,
                importButton,
                rollbackButton,
                cancelTrainingButton);
    }

    private static void CreateAutoBattleAdvancedDiagnosticsSection(
        Transform parent,
        AutoBattleSettings autoBattle)
    {
        var datasetRow = CreateInlineRow(parent, "AutoBattleDatasetExportRow");
        var datasetStatus = AuraToolsUi.AddText(
            parent,
            "导出当前游戏版本已加载的卡牌、Buff、敌人、关卡、遗物与祝福。",
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
            "导出数据表",
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
            104f);
        AuraToolsUi.AddButton(
            datasetRow.transform,
            "打开导出目录",
            AuraToolsCombatKnowledgeRuntime.OpenBaseGameTableExportDirectory,
            112f);

        var packageRow = CreateInlineRow(parent, "AutoBattleKnowledgePackageRow");
        AuraToolsUi.AddText(
            packageRow.transform,
            "发布版知识包",
            AuraToolsUi.BodyFontSize,
            TextAnchor.MiddleLeft,
            AuraToolsUi.Text,
            AuraToolsUi.TextMinHeight,
            1f);
        AuraToolsUi.AddButton(
            packageRow.transform,
            "导出并安装",
            () =>
            {
                if (AuraToolsCombatKnowledgeRuntime.TryExportAndInstallRuntimeKnowledgePackage(
                        out var installedPath,
                        out var installMessage))
                {
                    datasetStatus.text = installMessage + "：" + Path.GetFileName(installedPath);
                    datasetStatus.color = AuraToolsUi.SuccessText;
                }
                else
                {
                    datasetStatus.text = installMessage;
                    datasetStatus.color = AuraToolsUi.WarningText;
                }
            },
            112f);
        AuraToolsUi.AddButton(
            packageRow.transform,
            "重载知识包",
            () =>
            {
                AuraToolsCombatKnowledgeRuntime.RequestPackageReload();
                datasetStatus.text = "已提交知识包重载；加载结果请查看本页状态或日志";
                datasetStatus.color = AuraToolsUi.SuccessText;
            },
            112f);
        AuraToolsUi.AddButton(
            packageRow.transform,
            "打开知识目录",
            AuraToolsCombatKnowledgeRuntime.OpenKnowledgeDirectory,
            112f);

        CreateAutoBattleEvaluationSection(parent, autoBattle);

        AuraToolsUi.AddText(
            parent,
            "实机验证",
            AuraToolsUi.BodyFontSize,
            TextAnchor.MiddleLeft,
            AuraToolsUi.Text,
            AuraToolsUi.TextMinHeight,
            1f);
        var gameValidationStatusText = AuraToolsUi.AddText(
            parent,
            "",
            AuraToolsUi.HintFontSize,
            TextAnchor.MiddleLeft,
            AuraToolsUi.MutedText,
            AuraToolsUi.TextMinHeight,
            1f);
        var gameValidationRow = CreateInlineRow(
            parent,
            "AutoBattleGameValidationActions");
        var runGameValidationButton = AuraToolsUi.AddButton(
            gameValidationRow.transform,
            "开始验证",
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
            96f);
        var cancelGameValidationButton = AuraToolsUi.AddButton(
            gameValidationRow.transform,
            "取消",
            AuraToolsAutoBattleGameValidationRuntime.Cancel,
            66f);
        var openGameValidationButton = AuraToolsUi.AddButton(
            gameValidationRow.transform,
            "打开回执目录",
            AuraToolsAutoBattleGameValidationRuntime.OpenResultDirectory,
            112f);
        gameValidationRow
            .AddComponent<AuraToolsAutoBattleGameValidationStatusView>()
            .Configure(
                gameValidationStatusText,
                runGameValidationButton,
                cancelGameValidationButton,
                openGameValidationButton);

        var gameValidationSettings = autoBattle.GameValidation;
        var gameValidationOptionsRow = CreateInlineRow(
            parent,
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

        var clearDataRow = CreateInlineRow(parent, "AutoBattleClearDataRow");
        AuraToolsUi.AddText(
            clearDataRow.transform,
            "危险操作：永久清空实战样本、玩家残差与评估结果",
            AuraToolsUi.HintFontSize,
            TextAnchor.MiddleLeft,
            AuraToolsUi.WarningText,
            AuraToolsUi.TextMinHeight,
            1f);
        AuraToolsUi.AddButton(
            clearDataRow.transform,
            "清空玩家训练数据",
            () =>
            {
                if (AuraToolsAutoBattleModelRuntime
                        .TryClearAllCombatLearningData(out var clearMessage))
                {
                    AuraToolsLog.Info("[AutoBattle][Clear] " + clearMessage);
                }
                else
                {
                    AuraToolsLog.Warn("[AutoBattle][Clear] " + clearMessage);
                }
            },
            144f);
    }

    private static void CreateGameParametersSection(Transform parent)
    {
        var host = CreateVerticalStack(
            parent,
            "AutoBattleGameParametersSection");
        RebuildGameParametersSection(host.transform);
    }

    private static void RebuildGameParametersSection(Transform host)
    {
        AuraToolsUi.ClearChildren(host);
        BuildGameParametersSection(host);
    }

    private static void BuildGameParametersSection(Transform parent)
    {
        CreateSectionLabel(parent, "适用游戏主体");
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
                RebuildGameParametersSection(parent);
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
                RebuildGameParametersSection(parent);
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
                RebuildGameParametersSection(parent);
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
                AuraToolsAutoBattleGameParameterRuntime
                    .ResolvePresetReferences(autoBattle);
                autoBattle.Normalize();
                AuraToolsConfigService.SaveMatchExperience();
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
        var packSummaryText = AuraToolsUi.AddText(
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
                RebuildGameParametersSection(parent);
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
                    packSummaryText.text =
                        "奖励卡包范围："
                        + preset.EnabledRewardCardPackIds.Count
                        + " 个";
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
        var modeText = AuraToolsUi.AddText(row.transform, "模式：" + (settings.Mode == AudioModes.Advanced ? "高级（按角色）" : "通用"), AuraToolsUi.BodyFontSize, TextAnchor.MiddleLeft, AuraToolsUi.Text, AuraToolsUi.TextMinHeight, 1f);
        Button? modeButton = null;
        modeButton = AuraToolsUi.AddButton(row.transform, settings.Mode == AudioModes.Advanced ? "切到通用" : "切到高级", () =>
        {
            settings.Mode = settings.Mode == AudioModes.Advanced ? AudioModes.Common : AudioModes.Advanced;
            AuraToolsConfigService.SaveAudio();
            modeText.text = "模式：" + (settings.Mode == AudioModes.Advanced ? "高级（按角色）" : "通用");
            AuraToolsUi.SetButtonLabel(
                modeButton,
                settings.Mode == AudioModes.Advanced ? "切到通用" : "切到高级");
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
        InputField? pathInput = null;

        var actionRow = CreateInlineRow(parent, "CommonAudioActionRow");
        var pathStatus = AuraToolsUi.AddText(actionRow.transform, DescribeAudioPathStatus(settings.Common.RelativePath) + " / 优先级 " + settings.Common.Priority, AuraToolsUi.BodyFontSize, TextAnchor.MiddleLeft, AuraToolsUi.MutedText, AuraToolsUi.TextMinHeight, 1f);
        void RefreshPath()
        {
            if (pathInput != null)
            {
                pathInput.text = settings.Common.RelativePath;
            }
            pathStatus.text = DescribeAudioPathStatus(settings.Common.RelativePath)
                              + " / 优先级 " + settings.Common.Priority;
        }
        pathInput = AuraToolsUi.AddInput(pathRow.transform, settings.Common.RelativePath, value =>
        {
            ApplyCommonAudioPath(settings, battleBgm, value, RefreshPath);
        }, 620f);
        AuraToolsUi.AddButton(actionRow.transform, "选择音频", () =>
        {
            OptionalFileDialog.PickAudioFileAsync(FileResourceUtil.CommonAudioDirectory(), result =>
            {
                if (result.Selected)
                {
                    ApplyCommonAudioPath(settings, battleBgm, result.Path, RefreshPath);
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

    private static void ApplyCommonAudioPath(
        AudioFeatureSettings settings,
        bool battleBgm,
        string path,
        Action refreshed)
    {
        var trimmed = path?.Trim() ?? "";
        if (string.IsNullOrWhiteSpace(trimmed))
        {
            settings.Common.RelativePath = "";
            AuraToolsConfigService.SaveAudio();
            refreshed();
            return;
        }

        var baseName = battleBgm ? "battle_bgm" : "card_use";
        var imported = FileResourceUtil.ImportAudioPath(trimmed, FileResourceUtil.CommonAudioDirectory(), baseName, out var message);
        if (string.IsNullOrWhiteSpace(imported))
        {
            AuraToolsLog.Warn("[Settings] common audio import rejected; current configuration preserved: " + message);
            refreshed();
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
        refreshed();
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
            || !PanelBuildState.IsBuilt)
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
        autoBattleSnapshotRevisionBuilt = snapshot.Revision;
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

    private static GameObject CreateVerticalStack(
        Transform parent,
        string name,
        float spacing = 6f)
    {
        var host = AuraToolsUi.CreateLayout(name, parent);
        var layout = host.AddComponent<VerticalLayoutGroup>();
        layout.spacing = spacing;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;
        return host;
    }

    private static Transform CreateCompactFoldout(
        Transform parent,
        string title,
        string stateKey)
    {
        var box = CreateVerticalStack(
            parent,
            "CompactFoldout-" + stateKey);
        var header = CreateInlineRow(
            box.transform,
            "CompactFoldoutHeader-" + stateKey);
        AuraToolsUi.AddText(
            header.transform,
            title,
            AuraToolsUi.ModuleTitleFontSize,
            TextAnchor.MiddleLeft,
            AuraToolsUi.Accent,
            AuraToolsUi.TextMinHeight,
            1f);
        var expanded = FoldoutStates.TryGetValue(
                           stateKey,
                           out var stored)
                       && stored;
        var body = CreateVerticalStack(
            box.transform,
            "CompactFoldoutBody-" + stateKey);
        Text? buttonLabel = null;
        var button = AuraToolsUi.AddButton(
            header.transform,
            expanded ? "收起" : "展开",
            () =>
            {
                expanded = !expanded;
                FoldoutStates[stateKey] = expanded;
                AuraToolsUi.SetFoldoutExpanded(
                    body,
                    expanded,
                    box.transform);
                if (buttonLabel != null)
                {
                    buttonLabel.text = expanded ? "收起" : "展开";
                }
            },
            72f);
        buttonLabel = button.GetComponentInChildren<Text>(true);
        AuraToolsUi.SetFoldoutExpanded(
            body,
            expanded,
            box.transform);
        return body.transform;
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

    private static void CreateAutoBattleModelManagementSection(
        Transform parent,
        AutoBattleSettings autoBattle)
    {
        var host = CreateVerticalStack(
            parent,
            "AutoBattleModelManagementSection");
        void Build()
        {
            AuraToolsUi.ClearChildren(host.transform);
            CreateAutoBattleSimulationRows(
                host.transform,
                autoBattle,
                includeModelManagement: true,
                includeEvaluation: false);
        }
        Build();
        host.AddComponent<AuraToolsLocalSectionRefreshView>().Configure(
            () =>
            {
                var snapshot = AuraToolsAutoBattleUiSnapshotRuntime.Snapshot(
                    autoBattle.Profile,
                    autoBattle.SelectedModelId);
                var external =
                    AuraToolsAutoBattleModelRuntime
                        .SnapshotExternalValidationModel();
                return snapshot.Revision
                       + "|"
                       + autoBattle.Profile
                       + "|"
                       + autoBattle.SelectedModelId
                       + "|"
                       + autoBattle.ExperimentalModelAcknowledgement
                       + "|"
                       + (external?.PackageSha256 ?? "none");
            },
            Build);
    }

    private static void CreateAutoBattleModelApplicationRows(Transform parent)
    {
        var statusText = AuraToolsUi.AddText(
            parent,
            "",
            AuraToolsUi.BodyFontSize,
            TextAnchor.MiddleLeft,
            AuraToolsUi.Text,
            AuraToolsUi.TextMinHeight,
            1f);
        var row = CreateInlineRow(
            parent,
            "AutoBattleModelApplicationModeRow");
        AuraToolsUi.AddText(
            row.transform,
            "运行方式",
            AuraToolsUi.HintFontSize,
            TextAnchor.MiddleLeft,
            AuraToolsUi.MutedText,
            AuraToolsUi.TextMinHeight,
            0f,
            92f);
        AuraToolsAutoBattleModelApplicationStatusView? view = null;
        Button AddModeButton(string mode, string label)
        {
            return AuraToolsUi.AddButton(
                row.transform,
                label,
                () =>
                {
                    if (!AuraToolsAutoBattleRuntime
                            .TrySetModelApplicationMode(
                                mode,
                                out _,
                                out var message))
                    {
                        AuraToolsLog.Warn(
                            "[AutoBattle][ModelActivation] " + message);
                    }
                    else
                    {
                        AuraToolsLog.Info(
                            "[AutoBattle][ModelActivation] " + message);
                    }
                    view?.RefreshNow();
                },
                104f);
        }
        var shadowButton = AddModeButton("shadow", "影子评估");
        var trialButton = AddModeButton("trial", "实机试用");
        var fullButton = AddModeButton("full", "完整应用");
        view = row.AddComponent<
            AuraToolsAutoBattleModelApplicationStatusView>();
        view.Configure(
            statusText,
            shadowButton,
            trialButton,
            fullButton);
    }

    private static void CreateAutoBattleEvaluationSection(
        Transform parent,
        AutoBattleSettings autoBattle)
    {
        var host = CreateVerticalStack(
            parent,
            "AutoBattleEvaluationSection");
        void Build()
        {
            AuraToolsUi.ClearChildren(host.transform);
            CreateAutoBattleSimulationRows(
                host.transform,
                autoBattle,
                includeModelManagement: false,
                includeEvaluation: true);
        }
        Build();
        host.AddComponent<AuraToolsLocalSectionRefreshView>().Configure(
            () =>
            {
                var evaluationModelId = string.IsNullOrWhiteSpace(
                    autoBattle.EvaluationModelId)
                    ? autoBattle.SelectedModelId
                    : autoBattle.EvaluationModelId;
                var snapshot = AuraToolsAutoBattleUiSnapshotRuntime.Snapshot(
                    autoBattle.Profile,
                    evaluationModelId);
                return snapshot.Revision
                       + "|"
                       + autoBattle.Profile
                       + "|"
                       + autoBattle.Simulation.ScenarioId
                       + "|"
                       + autoBattle.Simulation.DifficultyId
                       + "|"
                       + AutoBattleEvolutionView;
            },
            Build);
    }

    private static void CreateAutoBattleSimulationRows(
        Transform parent,
        AutoBattleSettings autoBattle,
        bool includeModelManagement,
        bool includeEvaluation)
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
        var fixedCampaignSelected = string.Equals(
            autoBattle.Simulation.ScenarioId,
            "witch.world-simulation.standard-v2",
            StringComparison.OrdinalIgnoreCase);
        if (fixedCampaignSelected)
        {
            AutoBattleEvolutionView = false;
        }
        var lockedControls = new List<Selectable>();
        if (includeModelManagement)
        {
        var library = uiSnapshot.Models
            .Where(item => string.Equals(
                item.ModelPurpose,
                "foundation",
                StringComparison.Ordinal))
            .ToList();
        var modelRow = CreateInlineRow(parent, "AutoBattleModelLibraryRow");
        AuraToolsUi.AddText(
            modelRow.transform,
            "模型",
            AuraToolsUi.HintFontSize,
            TextAnchor.MiddleLeft,
            AuraToolsUi.Text,
            AuraToolsUi.TextMinHeight,
            0f,
            64f);
        var modelLabels = new List<string> { "未选择底模" };
        modelLabels.AddRange(library.Select(item =>
            (string.Equals(
                item.DeploymentTier,
                CombatFoundationDeploymentTier.Experimental,
                StringComparison.OrdinalIgnoreCase)
                ? string.Equals(
                    item.CapabilityStatus,
                    CombatFoundationModelPackageProtocol.CapabilityStatusFail,
                    StringComparison.Ordinal)
                    ? "【实验·回退】"
                    : "【实验】"
                : "【正式】")
            + item.DisplayName));
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
                if (AuraToolsAutoBattleModelRuntime
                    .IsExperimentalFoundationModel(
                        autoBattle.SelectedModelId)
                    && !AuraToolsAutoBattleModelRuntime
                        .IsExperimentalFoundationAcknowledged(
                            autoBattle.SelectedModelId))
                {
                    autoBattle.TrainedModelMode = "shadow";
                }
                autoBattle.Normalize();
                AuraToolsConfigService.SaveMatchExperience();
                AuraToolsAutoBattleRuntime.ReloadModels();
                AuraToolsAutoBattleUiSnapshotRuntime.RequestRefresh(
                    autoBattle.Profile,
                    autoBattle.SelectedModelId,
                    force: true);
            },
            320f);
        lockedControls.Add(modelButton);
        var renameValue = selectedModelIndex >= 0
            ? library[selectedModelIndex].DisplayName
            : "";
        var renameRow = CreateInlineRow(
            parent,
            "AutoBattleModelRenameRow");
        AuraToolsUi.AddText(
            renameRow.transform,
            "名称",
            AuraToolsUi.HintFontSize,
            TextAnchor.MiddleLeft,
            AuraToolsUi.MutedText,
            AuraToolsUi.TextMinHeight,
            0f,
            64f);
        var renameInput = AuraToolsUi.AddInput(
            renameRow.transform,
            renameValue,
            value => renameValue = value,
            260f);
        renameInput.interactable = selectedModelIndex >= 0;
        var renameButton = AuraToolsUi.AddButton(
            renameRow.transform,
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
            },
            66f);
        renameButton.interactable = selectedModelIndex >= 0;
        var restoreNameButton = AuraToolsUi.AddButton(
            renameRow.transform,
            "自动命名",
            () =>
            {
                if (AuraToolsAutoBattleModelRuntime
                    .TryRestoreGeneratedLibraryModelName(
                        autoBattle.SelectedModelId,
                        out var restoreMessage))
                {
                    AuraToolsLog.Info(
                        "[AutoBattle][ModelLibrary] " + restoreMessage);
                }
                else
                {
                    AuraToolsLog.Warn(
                        "[AutoBattle][ModelLibrary] " + restoreMessage);
                }
                AuraToolsAutoBattleUiSnapshotRuntime.RequestRefresh(
                    autoBattle.Profile,
                    autoBattle.SelectedModelId,
                    force: true);
            },
            88f);
        restoreNameButton.interactable = selectedModelIndex >= 0
                                         && string.Equals(
                                             library[selectedModelIndex]
                                                 .ModelPurpose,
                                             "foundation",
                                             StringComparison.Ordinal);
        renameRow.SetActive(selectedModelIndex >= 0);
        var selectedEntry = selectedModelIndex >= 0
            ? library[selectedModelIndex]
            : null;
        var selectedExperimental = selectedEntry != null
                                   && string.Equals(
                                       selectedEntry.DeploymentTier,
                                       CombatFoundationDeploymentTier.Experimental,
                                       StringComparison.OrdinalIgnoreCase);
        var selectedCapabilityRegression = selectedExperimental
                                           && string.Equals(
                                               selectedEntry!.CapabilityStatus,
                                               CombatFoundationModelPackageProtocol
                                                   .CapabilityStatusFail,
                                               StringComparison.Ordinal);
        AuraToolsUi.AddText(
            parent,
            library.Count == 0
                ? "模型库为空"
                : selectedModelIndex < 0
                    ? "模型库：" + library.Count + " 个"
                    : "主体："
                      + library[selectedModelIndex].RoleId
                      + " / "
                      + library[selectedModelIndex].PartnerId
                      + " · 卡包 "
                      + library[selectedModelIndex]
                          .EnabledRewardCardPackIds.Count
                      + " · "
                      + (library[selectedModelIndex].CoverageLevel == "full"
                          ? "完全覆盖"
                          : "部分覆盖")
                      + " · "
                      + (selectedCapabilityRegression
                          ? "实验底模（能力回退）"
                          : selectedExperimental ? "实验底模" : "正式底模")
                      + " · 来源 "
                      + (string.IsNullOrWhiteSpace(
                            library[selectedModelIndex].DistributionOrigin)
                          ? "未知"
                          : library[selectedModelIndex].DistributionOrigin),
            AuraToolsUi.HintFontSize,
            TextAnchor.MiddleLeft,
            AuraToolsUi.MutedText,
            AuraToolsUi.TextMinHeight,
            1f);
        if (selectedExperimental)
        {
            var acknowledged = AuraToolsAutoBattleModelRuntime
                .IsExperimentalFoundationAcknowledged(
                    autoBattle.SelectedModelId);
            AuraToolsUi.AddText(
                parent,
                selectedCapabilityRegression
                    ? acknowledged
                        ? "⚠ 高风险实验底模：能力探针已检测到相对基线回退，已确认仅用于实机配置测试与问题收集。"
                        : "⚠ 高风险实验底模：能力探针已检测到相对基线回退；确认前不能主动接管战斗。"
                    : acknowledged
                        ? "⚠ 实验底模：已确认效果可能与正式底模存在差异；主动运行期间持续按实验模型标识。"
                        : "⚠ 实验底模：技术格式与运行安全已通过，但尚未取得正式质量认证；确认前不能主动接管战斗。",
                AuraToolsUi.HintFontSize,
                TextAnchor.MiddleLeft,
                AuraToolsUi.WarningText,
                AuraToolsUi.TextMinHeight,
                1f);
            var acknowledgementRow = CreateInlineRow(
                parent,
                "AutoBattleExperimentalFoundationAcknowledgement");
            AuraToolsUi.AddText(
                acknowledgementRow.transform,
                acknowledged ? "实验风险已确认" : "需要显式确认",
                AuraToolsUi.HintFontSize,
                TextAnchor.MiddleLeft,
                AuraToolsUi.WarningText,
                AuraToolsUi.TextMinHeight,
                1f);
            var acknowledgementButton = AuraToolsUi.AddButton(
                acknowledgementRow.transform,
                acknowledged ? "已确认" : "确认使用实验底模",
                () =>
                {
                    AuraToolsAutoBattleModelRuntime
                        .TryAcknowledgeExperimentalFoundation(
                            autoBattle.SelectedModelId,
                            out var acknowledgementMessage);
                    AuraToolsLog.Warn(
                        "[AutoBattle][ExperimentalFoundation] "
                        + acknowledgementMessage);
                    AuraToolsAutoBattleUiSnapshotRuntime.RequestRefresh(
                        autoBattle.Profile,
                        autoBattle.SelectedModelId,
                        force: true);
                },
                156f);
            acknowledgementButton.interactable = !acknowledged;
        }

        var bundledStatus =
            AuraToolsBundledFoundationModelRuntime.SnapshotStatus();
        var bundledStatusText = AuraToolsUi.AddText(
            parent,
            "Model 批量导入：" + bundledStatus.Message,
            AuraToolsUi.HintFontSize,
            TextAnchor.MiddleLeft,
            AuraToolsUi.MutedText,
            AuraToolsUi.TextMinHeight,
            1f);
        AuraToolsUi.AddText(
            parent,
            "按 Model/角色名 [RoleId]/使魔名 [PartnerId]/[可选的用户发布名]/ 放入固定模型文件；哈希、卡包和版本由程序识别，注册后不会自动选择或启用。",
            AuraToolsUi.HintFontSize,
            TextAnchor.MiddleLeft,
            AuraToolsUi.MutedText,
            AuraToolsUi.TextMinHeight,
            1f);
        var bundledRow = CreateInlineRow(
            parent,
            "AutoBattleBundledFoundationActions");
        var bundledImportButton = AuraToolsUi.AddButton(
            bundledRow.transform,
            "导入底模",
            () =>
            {
                if (AuraToolsBundledFoundationModelRuntime.TryQueueRescan(
                        out var scanMessage))
                {
                    bundledStatusText.text = "Model 批量导入：" + scanMessage;
                    AuraToolsLog.Info(
                        "[AutoBattle][BundledModels] " + scanMessage);
                }
                else
                {
                    bundledStatusText.text = "Model 批量导入：" + scanMessage;
                    AuraToolsLog.Warn(
                        "[AutoBattle][BundledModels] " + scanMessage);
                }
            },
            104f);
        bundledStatusText.gameObject
            .AddComponent<AuraToolsBundledFoundationImportStatusView>()
            .Configure(bundledStatusText, bundledImportButton);

        var externalEntry =
            AuraToolsAutoBattleModelRuntime.SnapshotExternalValidationModel();
        var externalStatusText = AuraToolsUi.AddText(
            parent,
            externalEntry == null
                ? "待验底模：未选择"
                : "待验底模：" + externalEntry.DisplayName,
            AuraToolsUi.HintFontSize,
            TextAnchor.MiddleLeft,
            AuraToolsUi.MutedText,
            AuraToolsUi.TextMinHeight,
            1f);
        var externalRow = CreateInlineRow(
            parent,
            "AutoBattleExternalFoundationValidationActions");
        var selectExternalButton = AuraToolsUi.AddButton(
            externalRow.transform,
            "导入外部待验包",
            () =>
            {
                OptionalFileDialog.PickFileAsync(
                    "选择外部待验底模包",
                    new[]
                    {
                        new OptionalFileDialogFilter(
                            "Aura 待验底模包",
                            "foundation-model-package-v5.json;foundation-model-package-v4.json;foundation-model-package-v3.json;*.aura-model.json"),
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
                        }
                        else
                        {
                            AuraToolsLog.Warn(
                                "[AutoBattle][ExternalValidation] "
                                + stageMessage);
                        }
                    });
            },
            142f);
        var promoteExternalButton = AuraToolsUi.AddButton(
            externalRow.transform,
            "加入模型库",
            () =>
            {
                var targetId = autoBattle.EvaluationModelId;
                if (!AuraToolsAutoBattleModelRuntime
                    .ExternalValidationMeetsGate(
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
        }

        if (!includeEvaluation)
        {
            AttachAutoBattleWorkLock(
                parent.gameObject,
                lockedControls.ToArray());
            return;
        }

        AuraToolsUi.AddText(
            parent,
            "标准评估（仅用于本地候选晋级）",
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
            },
            92f);
        Button? evolutionModeButton = null;
        if (!fixedCampaignSelected)
        {
            evolutionModeButton = AuraToolsUi.AddButton(
                modeRow.transform,
                "高级：策略进化",
                () =>
                {
                    AutoBattleEvolutionView = true;
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
            () => AuraToolsAutoBattleUiSnapshotRuntime.RequestRefresh(
                autoBattle.Profile,
                evaluationModelId,
                force: true),
            66f);
        AuraToolsUi.AddButton(
            scenarioRow.transform,
            "高级输入目录",
            AuraToolsAutoBattleSimulationRuntime.OpenInputDirectory,
            112f);
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
        if (fixedCampaignSelected)
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

        var statusText = AuraToolsUi.AddText(
            parent,
            "",
            AuraToolsUi.HintFontSize,
            TextAnchor.MiddleLeft,
            AuraToolsUi.MutedText,
            AuraToolsUi.TextMinHeight,
            1f);
        var actionRow = CreateInlineRow(
            parent,
            "AutoBattleSimulationActionRow");
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
        return settings.Epochs
               + "轮 · 学习率"
               + settings.LearningRate.ToString("0.####", CultureInfo.InvariantCulture)
               + " · 修正"
               + settings.MaximumCorrection.ToString("0.##", CultureInfo.InvariantCulture)
               + " · 偏好对"
               + settings.MinimumPreferencePairs;
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

internal sealed class AuraToolsBundledFoundationImportStatusView : MonoBehaviour
{
    private Text? statusText;
    private Button? importButton;
    private float nextRefreshAt;

    public void Configure(Text text, Button button)
    {
        statusText = text;
        importButton = button;
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
        var current = AuraToolsBundledFoundationModelRuntime.SnapshotStatus();
        if (statusText != null)
        {
            statusText.text = "Model 批量导入：" + current.Message;
            statusText.color = current.Stage == BundledFoundationImportStage.Failed
                ? AuraToolsUi.WarningText
                : current.Stage == BundledFoundationImportStage.Completed
                    ? AuraToolsUi.SuccessText
                    : AuraToolsUi.MutedText;
        }
        if (importButton != null)
        {
            importButton.interactable = !current.Busy;
        }
    }
}

internal sealed class AuraToolsLocalSectionRefreshView : MonoBehaviour
{
    private Func<string>? signatureProvider;
    private Action? rebuild;
    private string signature = "";
    private float nextRefreshAt;
    private bool rebuilding;

    public void Configure(
        Func<string> currentSignature,
        Action rebuildAction)
    {
        signatureProvider = currentSignature;
        rebuild = rebuildAction;
        signature = signatureProvider?.Invoke() ?? "";
    }

    private void Update()
    {
        if (rebuilding
            || Time.unscaledTime < nextRefreshAt
            || signatureProvider == null
            || rebuild == null)
        {
            return;
        }
        nextRefreshAt = Time.unscaledTime + 0.25f;
        var current = signatureProvider();
        if (string.Equals(current, signature, StringComparison.Ordinal))
        {
            return;
        }
        rebuilding = true;
        try
        {
            signature = current;
            rebuild();
            Canvas.ForceUpdateCanvases();
            if (transform is RectTransform sectionRect)
            {
                LayoutRebuilder.ForceRebuildLayoutImmediate(sectionRect);
            }
            if (transform.parent is RectTransform parentRect)
            {
                LayoutRebuilder.MarkLayoutForRebuild(parentRect);
            }
            signature = signatureProvider?.Invoke() ?? current;
        }
        finally
        {
            rebuilding = false;
        }
    }
}

internal sealed class AuraToolsAutoBattleModelApplicationStatusView :
    MonoBehaviour
{
    private Text? statusText;
    private Button? shadowButton;
    private Button? trialButton;
    private Button? fullButton;
    private float nextRefreshAt;

    public void Configure(
        Text text,
        Button shadow,
        Button trial,
        Button full)
    {
        statusText = text;
        shadowButton = shadow;
        trialButton = trial;
        fullButton = full;
        RefreshNow();
    }

    public void RefreshNow()
    {
        nextRefreshAt = 0f;
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
        var status =
            AuraToolsAutoBattleRuntime.SnapshotModelApplicationStatus();
        var busy = AuraToolsAutoBattleModelRuntime.AnyTrainingBusy()
                   || AuraToolsAutoBattleSimulationRuntime.GetStatus().Busy
                   || AuraToolsAutoBattleGameValidationRuntime.GetStatus().Busy;
        if (statusText != null)
        {
            var mismatch = !string.Equals(
                status.ConfiguredMode,
                status.EffectiveMode,
                StringComparison.Ordinal);
            var settings = AuraToolsConfigService.MatchExperience.AutoBattle;
            var snapshot = AuraToolsAutoBattleUiSnapshotRuntime.Snapshot(
                settings.Profile,
                status.SelectedModelId);
            var entry = snapshot.Models.FirstOrDefault(item => string.Equals(
                item.ModelId,
                status.SelectedModelId,
                StringComparison.Ordinal));
            var modelName = entry?.DisplayName;
            if (string.IsNullOrWhiteSpace(modelName))
            {
                modelName = status.ModelLoading || snapshot.Loading
                    ? "正在读取模型"
                    : string.IsNullOrWhiteSpace(status.SelectedModelId)
                        ? "尚未选择"
                        : "已选择模型";
            }
            var tier = entry == null
                ? ""
                : string.Equals(
                    entry.DeploymentTier,
                    CombatFoundationDeploymentTier.Experimental,
                    StringComparison.OrdinalIgnoreCase)
                    ? "（实验底模）"
                    : "（正式底模）";
            var details = status.ModelLoading
                ? "模型正在加载，接管前会等待加载完成"
                : status.EmergencyFallbackCount > 0
                    ? "技术兜底 "
                      + status.EmergencyFallbackCount
                      + " 次 · "
                      + CompactDiagnostic(status.LastFallbackReason)
                    : mismatch
                        ? CompactDiagnostic(status.Diagnostic)
                        : "模型正常时，决策完全由模型输出";
            statusText.text = "当前："
                              + Label(status.EffectiveMode)
                              + " · "
                              + DecisionOwnerLabel(status.DecisionOwner)
                              + "\n模型："
                              + modelName
                              + tier
                              + " · "
                              + details;
            statusText.color = mismatch || status.ModelIsolatedForBattle
                ? AuraToolsUi.WarningText
                : string.Equals(
                    status.EffectiveMode,
                    "off",
                    StringComparison.Ordinal)
                    ? AuraToolsUi.MutedText
                    : AuraToolsUi.SuccessText;
        }
        if (shadowButton != null)
        {
            shadowButton.interactable = !busy
                                        && !string.IsNullOrWhiteSpace(
                                            status.SelectedModelId)
                                        && !string.Equals(
                                            status.ConfiguredMode,
                                            "shadow",
                                            StringComparison.Ordinal);
        }
        if (trialButton != null)
        {
            trialButton.interactable = !busy
                                       && !string.IsNullOrWhiteSpace(
                                           status.SelectedModelId)
                                       && !string.Equals(
                                           status.ConfiguredMode,
                                           "trial",
                                           StringComparison.Ordinal);
        }
        if (fullButton != null)
        {
            fullButton.interactable = !busy
                                      && !string.IsNullOrWhiteSpace(
                                          status.SelectedModelId)
                                      && !string.Equals(
                                          status.ConfiguredMode,
                                          "full",
                                          StringComparison.Ordinal);
        }
    }

    private static string Label(string mode)
    {
        return mode switch
        {
            "shadow" => "影子评估",
            "trial" => "实机试用",
            "full" => "完整应用",
            _ => "关闭"
        };
    }

    private static string DecisionOwnerLabel(string owner)
    {
        return owner switch
        {
            "model" => "模型决策",
            "emergency-baseline" => "技术兜底",
            _ => "观察/基础策略"
        };
    }

    private static string CompactDiagnostic(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "等待重新加载";
        }
        return value.Length <= 30
            ? value
            : value.Substring(0, 27) + "...";
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
        var busy = AuraToolsAutoBattleSimulationRuntime.GetStatus().Busy
                   || AuraToolsAutoBattleGameValidationRuntime.GetStatus().Busy
                   || AuraToolsAutoBattleModelRuntime.AnyTrainingBusy();
        var selected = entry != null
                       && string.Equals(
                           settings.EvaluationModelId,
                           entry.ModelId,
                           StringComparison.Ordinal);
        var gateReason = "";
        var ready = selected
                    && AuraToolsAutoBattleModelRuntime
                        .ExternalValidationMeetsGate(
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
                ? "待验底模：未选择"
                : entry.DisplayName
                  + " · "
                  + DescribeTrainingSubject(entry.TrainingSubject)
                  + " · "
                  + (ready
                      ? "校验通过"
                      : CompactGateReason(gateReason));
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

    private static string DescribeTrainingSubject(
        CombatFoundationTrainingSubject? subject)
    {
        if (subject == null)
        {
            return "旧模型主体";
        }
        var packs = subject.EnabledRewardCardPackIds
            .Select(id => id.StartsWith(
                "cardpack_",
                StringComparison.OrdinalIgnoreCase)
                ? id.Substring("cardpack_".Length)
                : id)
            .Take(2)
            .ToList();
        var packText = string.Join(",", packs);
        if (subject.EnabledRewardCardPackIds.Count > packs.Count)
        {
            packText += "+" + (subject.EnabledRewardCardPackIds.Count - packs.Count);
        }
        return subject.RoleId
               + " / "
               + subject.PartnerId
               + " / "
               + (string.IsNullOrWhiteSpace(packText) ? "无卡包" : packText);
    }

    private static string CompactGateReason(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "等待校验";
        }
        return value.Length <= 24
            ? value
            : value.Substring(0, 21) + "...";
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
        var otherBusy = AuraToolsAutoBattleSimulationRuntime.GetStatus().Busy
                        || AuraToolsAutoBattleModelRuntime.GetTrainingStatus(
                            AuraToolsConfigService.MatchExperience.AutoBattle.Profile).Busy;
        var startReady =
            AuraToolsAutoBattleGameValidationRuntime
                .IsStartEnvironmentReady(out var startReason);
        if (statusText != null)
        {
            var progress = status.RequestedBattles <= 0
                ? ""
                : " · "
                  + status.CompletedBattles
                  + "/"
                  + status.RequestedBattles
                  + " 战";
            statusText.text = !status.Busy && !startReady
                ? CompactStatus(startReason) + " · 可选"
                : CompactStatus(status.Message) + progress;
            statusText.color = status.Stage == AutoBattleGameValidationStage.Failed
                               || status.Stage == AutoBattleGameValidationStage.Cancelled
                ? AuraToolsUi.WarningText
                : status.Stage == AutoBattleGameValidationStage.Passed
                    ? AuraToolsUi.SuccessText
                    : AuraToolsUi.MutedText;
        }
        if (runButton != null)
        {
            runButton.interactable =
                !status.Busy && !otherBusy && startReady;
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

    private static string CompactStatus(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "就绪";
        }
        return value.Length <= 32
            ? value
            : value.Substring(0, 29) + "...";
    }
}

internal sealed class AuraToolsAutoBattleTrainingStatusView : MonoBehaviour
{
    private string profile = "balanced";
    private Text? statusText;
    private Button? generateButton;
    private Button? importButton;
    private Button? rollbackButton;
    private Button? cancelButton;
    private float nextRefreshAt;

    public void Configure(
        string decisionProfile,
        Text text,
        Button generate,
        Button import,
        Button rollback,
        Button cancel)
    {
        profile = decisionProfile ?? "balanced";
        statusText = text;
        generateButton = generate;
        importButton = import;
        rollbackButton = rollback;
        cancelButton = cancel;
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
        profile = AuraToolsConfigService.MatchExperience.AutoBattle.Profile;
        var status = AuraToolsAutoBattleModelRuntime.GetTrainingStatus(profile);
        var simulationBusy =
            AuraToolsAutoBattleSimulationRuntime.GetStatus().Busy;
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
                                                + ModelModeSuffix(
                                                    AuraToolsConfigService
                                                        .MatchExperience
                                                        .AutoBattle
                                                        .TrainedModelMode)
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
            "trial" => "，正在实机试用",
            "full" => "，正在完整应用",
            "active" => "，正在实机试用",
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

internal sealed class AuraToolsPanelBuildMarker : MonoBehaviour
{
    internal bool Completed { get; set; }
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
