using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using AuraShared.Core;
using AuraToolsExp.Dll.Config;
using AuraToolsExp.Dll.Features.Audio;
using AuraToolsExp.Dll.Features.DamageMeter;
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
    private static GameObject? activePanel;
    private static Transform? activePanelHost;
    private static Transform? activeTabParent;
    private static readonly Dictionary<string, bool> FoldoutStates = new(StringComparer.Ordinal);
    private static bool loggedHookRegistration;
    private static bool loggedInjectionSuccess;
    private static bool loggedNoTabParent;

    public static void Initialize(ModConfig modConfig)
    {
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
            EnsureTabButton(parent);
            BindNativeTabsToHide(parent);
            EnsurePanel(setting, parent, panelHost);
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
    }

    private static void RegisterAfter(ModConfig config, string target, Action<ModHookContext> action)
    {
        AuraSharedHooks.RegisterAfter(config, target, action, warn: AuraToolsLog.Warn);
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

    private static void EnsureTabButton(Transform? tabParent)
    {
        if (tabParent == null)
        {
            return;
        }

        var existing = tabParent.Find(AuraTabButtonName);
        if (existing == null)
        {
            var buttonObject = CreatePlainTabButton(tabParent);
            buttonObject.transform.SetAsLastSibling();
            AdjustTabSize(buttonObject);
            ConfigureTabButton(buttonObject);
        }
        else
        {
            existing.SetAsLastSibling();
            AdjustTabSize(existing.gameObject);
            ConfigureTabButton(existing.gameObject);
        }
    }

    private static void ConfigureTabButton(GameObject buttonObject)
    {
        var manager = buttonObject.GetComponent<ButtonManager>();
        if (manager != null)
        {
            manager.onClick.RemoveAllListeners();
            manager.onDoubleClick.RemoveAllListeners();
            manager.onRightClick.RemoveAllListeners();
            manager.enableText = true;
            manager.buttonText = AuraToolsIds.SettingsTabName;
            manager.Interactable(true);
            manager.onClick.AddListener(ShowAuraPanel);
            manager.UpdateUI();
            SetTabVisualText(buttonObject, AuraToolsIds.SettingsTabName);

            var nativeButton = buttonObject.GetComponent<Button>();
            if (nativeButton != null)
            {
                nativeButton.onClick.RemoveAllListeners();
            }

            return;
        }

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

        RemoveTextChildren(buttonObject.transform);
        AuraToolsUi.AddFillText(buttonObject.transform, AuraToolsIds.SettingsTabName, AuraToolsUi.TabFontSize, TextAnchor.MiddleCenter, AuraToolsUi.Accent);
    }

    private static void SetTabVisualText(GameObject buttonObject, string label)
    {
        foreach (var text in buttonObject.GetComponentsInChildren<TMP_Text>(true))
        {
            text.text = label;
        }

        foreach (var text in buttonObject.GetComponentsInChildren<Text>(true))
        {
            text.text = label;
        }
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
        RebuildPanel(activePanel.transform);
    }

    private static void RebuildPanel(Transform panel)
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
        CreateDataDirectorySection(content);
        CreateSkinSection(content);
        CreateAudioSection(content);
        CreateMatchExperienceSection(content);
        CreateLoggingSection(content);
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
            AuraToolsUi.AddText(content, "仅替换出牌音效；高级模式可为每个角色指定独立音效。", AuraToolsUi.HintFontSize, TextAnchor.MiddleLeft, AuraToolsUi.MutedText, AuraToolsUi.TextMinHeight, 1f);
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
            AuraToolsUi.AddText(statusRow.transform, "共享皮肤目录：" + AuraToolsConfigService.SkinsDirectory, AuraToolsUi.BodyFontSize, TextAnchor.MiddleLeft, AuraToolsUi.Text, AuraToolsUi.TextMinHeight, 1f);
            AuraToolsUi.AddButton(statusRow.transform, "打开目录", () => FileResourceUtil.OpenDirectory(AuraToolsConfigService.SkinsDirectory), 92f);
            AuraToolsUi.AddButton(statusRow.transform, "重新扫描", () =>
            {
                AuraToolsSkinRuntime.RegisterBundledPackage();
                AuraToolsSkinRuntime.Reload();
                RebuildPanel(activePanel!.transform);
            }, 92f);

            var autoInstallRow = CreateInlineRow(content, "SkinAutoInstallInfoRow");
            AuraToolsConfigService.Skin.AutoInstallBundledSkins = true;
            AuraToolsUi.AddText(autoInstallRow.transform, "说明：AuraToolsExp 内置角色皮肤会自动安装并补齐到共享皮肤目录。", AuraToolsUi.HintFontSize, TextAnchor.MiddleLeft, AuraToolsUi.MutedText, AuraToolsUi.TextMinHeight, 1f);

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

            foreach (var line in AuraToolsSkinRuntime.StatusLines())
            {
                AuraToolsUi.AddText(content, line, AuraToolsUi.HintFontSize, TextAnchor.MiddleLeft, AuraToolsUi.MutedText, AuraToolsUi.TextMinHeight, 1f);
            }
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

        CreateSubmodule(parent, "随身保险箱", AuraToolsConfigService.MatchExperience.SafeBox.Enabled, value =>
        {
            AuraToolsConfigService.MatchExperience.SafeBox.Enabled = value;
            AuraToolsConfigService.SaveMatchExperience();
        }, content =>
        {
            AuraToolsUi.AddText(content, "开启后在冒险 TopBar 增加随身保险箱入口；功能只提供总开关。", AuraToolsUi.HintFontSize, TextAnchor.MiddleLeft, AuraToolsUi.MutedText, AuraToolsUi.TextMinHeight, 1f);
        });

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

            AuraToolsUi.AddText(content, "声明：该模块初始版本代码由【哈基米】提供，后续由【Aura】进行维护和功能开发。", AuraToolsUi.HintFontSize, TextAnchor.MiddleLeft, AuraToolsUi.MutedText, AuraToolsUi.TextMinHeight, 1f);
        }, damageMeter.Enabled ? AuraToolsUi.SuccessText : AuraToolsUi.MutedText);

        CreateSubmodule(parent, "技能CG", AuraToolsConfigService.SkillCg.Enabled, value =>
        {
            AuraToolsConfigService.SkillCg.Enabled = value;
            AuraToolsConfigService.SaveSkillCg();
        }, content =>
        {
            var row = CreateInlineRow(content, "SkillCgConfigRow");
            var ruleCount = AuraToolsConfigService.SkillCg.Roles.Values.Sum(role => role.Rules.Count);
            AuraToolsUi.AddText(row.transform, "角色：" + AuraToolsConfigService.SkillCg.Roles.Count + "，规则：" + ruleCount + "，联机同步：" + (AuraToolsConfigService.SkillCg.SyncRemote ? "开" : "关"), AuraToolsUi.BodyFontSize, TextAnchor.MiddleLeft, AuraToolsUi.Text, AuraToolsUi.TextMinHeight, 1f);
            AuraToolsUi.AddButton(row.transform, "配置", () => AuraToolsSkillCgEditor.Show(activePanel!.transform), 88f);
            AuraToolsUi.AddButton(row.transform, AuraToolsConfigService.SkillCg.SyncRemote ? "关闭同步" : "开启同步", () =>
            {
                AuraToolsConfigService.SkillCg.SyncRemote = !AuraToolsConfigService.SkillCg.SyncRemote;
                AuraToolsConfigService.SaveSkillCg();
                RebuildPanel(activePanel!.transform);
            }, 96f);
        });
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
            var row = CreateInlineRow(content, "LoggingRow");
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
        var baseName = battleBgm ? "battle_bgm" : "card_use";
        var imported = FileResourceUtil.ImportAudioPath(trimmed, FileResourceUtil.CommonAudioDirectory(), baseName, out var message);
        settings.Common.RelativePath = string.IsNullOrWhiteSpace(imported) ? trimmed : imported;
        if (string.IsNullOrWhiteSpace(imported) && !string.IsNullOrWhiteSpace(trimmed))
        {
            AuraToolsLog.Warn("[Settings] common audio path kept as typed: " + message);
        }

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

        return File.Exists(AuraToolsConfigService.ResolveConfiguredPath(relativeOrAbsolute))
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
        content.SetActive(state.Expanded);
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
            content.SetActive(state.Expanded);
            UpdateFoldoutLabel();
        }

        var headerButton = header.AddComponent<Button>();
        headerButton.targetGraphic = headerImage;
        headerButton.onClick.AddListener(ToggleFoldout);
        var foldoutButton = AuraToolsUi.AddButton(header.transform, state.Expanded ? "收起" : "展开", ToggleFoldout, AuraToolsUi.ButtonMinWidth, AuraToolsUi.ButtonHeight);
        foldoutLabel = foldoutButton.GetComponentInChildren<Text>();
        UpdateFoldoutLabel();

        buildContent(content.transform);
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

    private static void CreateDamageMeterToggleRow(Transform parent, string label, bool value, Action<bool> changed)
    {
        var row = CreateInlineRow(parent, "DamageMeterToggle-" + label);
        AuraToolsUi.AddToggle(row.transform, value, changed);
        AuraToolsUi.AddText(row.transform, label, AuraToolsUi.BodyFontSize, TextAnchor.MiddleLeft, AuraToolsUi.Text, AuraToolsUi.TextMinHeight, 1f);
    }
}

internal sealed class AuraToolsFoldoutState : MonoBehaviour
{
    public bool Expanded = true;
}

internal sealed class AuraToolsNativeTabRelay : MonoBehaviour, IPointerDownHandler
{
    public void OnPointerDown(PointerEventData eventData)
    {
        AuraToolsSettingsRuntime.HideActivePanel();
    }
}
