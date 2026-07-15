using System;
using System.Linq;
using System.Reflection;
using AuraShared.Core;
using AuraUi.Shared;
using SunExp.Dll.GameApi;
using SunExp.Dll.Hooks.Ui;
using SunExp.Dll.Infrastructure;
using SunExp.Dll.Mechanics;
using UnityEngine;
using UnityEngine.UI;
using Witch.Core;
using Witch.Mod;
using Object = UnityEngine.Object;

namespace SunExp.Dll.Hooks;

public static class FamiliarGrowthRuntime
{
    private const string LogPrefix = "[FamiliarGrowth]";
    private const string ButtonName = "SunExp_FamiliarArchiveLibraryButton";
    private const string ButtonText = "\u4f7f\u9b54\u6863\u6848";
    private const string ButtonBrushName = "SunExp_FamiliarArchiveBrush";
    private const string ButtonTextName = "SunExp_FamiliarArchiveText";
    private const float FallbackButtonWidth = 156f;
    private const float FallbackButtonHeight = 50f;
    private const float LibraryButtonGap = 12f;
    private static float lastLibraryButtonOpenTime = -1f;
    private static bool loggedNativeButtonFallback;

    public static void Initialize(ModConfig modConfig)
    {
        FamiliarGrowthApi.Initialize(modConfig);
        RegisterAfter(modConfig, "HouseManager.Awake", EnsureLibraryButton);
        RegisterAfter(modConfig, "HouseManager.OnEnable", EnsureLibraryButton);
        RegisterAfter(modConfig, "HouseManager.ChangeUIShow", EnsureLibraryButton);
        RegisterAfter(modConfig, "HouseManager.OpenWindowByIndex", EnsureLibraryButton);
        RegisterAfter(modConfig, "HouseManager.OpenLibrary", EnsureLibraryButton);
        RegisterAfter(modConfig, "GameEntryUI.NormalGame", MarkActiveForRun);
        RegisterBefore(modConfig, SunExpHookTargets.FightWinResetStates, GrantBattleWinExperience);
        SunExpHookRegistry.Before(modConfig, SunExpHookTargets.StatusManagerHit, OnStatusHitBefore, "FamiliarGrowth");
        SunExpHookRegistry.After(modConfig, SunExpHookTargets.StatusManagerHit, OnStatusHitAfter, "FamiliarGrowth");
        SunExpCombatActionRouter.RegisterActionEventHandler(
            "FamiliarGrowth",
            null,
            FamiliarBlessingEffectRuntime.AfterPlayerAction);
        BattleRewardAdjustmentService.Register(new BattleRewardAdjustmentRule(
            "FamiliarGrowth.ExtraChoices",
            context => BattleRewardApi.IsCurrentBattleReward()
                       && FamiliarBlessingEffectRuntime.EffectAmount("BattleRewardExtraChoice") > 0,
            context => FamiliarBlessingEffectRuntime.ApplyBattleRewardExtraChoices(context.RewardUi)));
        SunExpBattleLifecycleRouter.Register("FamiliarGrowth", new SunExpBattleLifecycleSubscription
        {
            AdventureStarting = MarkActiveForRun,
            FightInitialized = ApplySelectedCombatStartEffects,
            PlayerRoundStarted = context => FamiliarBlessingEffectRuntime.BeginPlayerRound(),
            FightEnding = context => FamiliarBlessingEffectRuntime.EndEpoch()
        });
        SunExpLog.Info(LogPrefix + " runtime initialized.");
    }

    public static void OpenPanel()
    {
        FamiliarGrowthApi.RefreshCurrentPartner();
        FamiliarGrowthPanel.Open();
    }

    private static void RegisterBefore(ModConfig config, string target, Action<ModHookContext> action)
    {
        SunExpHookRegistry.Before(config, target, action, "FamiliarGrowth");
    }

    private static void RegisterAfter(ModConfig config, string target, Action<ModHookContext> action)
    {
        SunExpHookRegistry.After(config, target, action, "FamiliarGrowth");
    }

    private static void EnsureLibraryButton(ModHookContext context)
    {
        try
        {
            var manager = context.Target;
            var libraryWindow = ResolveLibraryWindow(manager);
            if (libraryWindow == null)
            {
                return;
            }

            var cardButton = FindHouseItemTransform(libraryWindow, "cardShop")
                             ?? FindButtonLikeTransformByText(libraryWindow, "\u501f\u9605\u56fe\u4e66", "\u501f\u95b1\u5716\u66f8");
            var rollButton = FindHouseItemTransform(libraryWindow, "rollShop")
                             ?? FindButtonLikeTransformByText(libraryWindow, "\u67e5\u627e\u5178\u7c4d", "\u67e5\u627e\u5178\u7c4d");
            var template = rollButton ?? cardButton ?? FindLibraryButtonTemplate(libraryWindow);
            var parent = template?.parent ?? ResolveLibraryButtonParent(libraryWindow);
            if (parent == null)
            {
                return;
            }

            var templateManager = template == null ? null : FindButtonManagerComponent(template);
            var existing = FindDeepChild(libraryWindow, ButtonName);
            if (existing != null
                && templateManager != null
                && AuraUiNativeButtonCloneAdapter.IsOwnedClone(templateManager, existing))
            {
                if (TryConfigureNativeLibraryButton(existing, parent, templateManager, template, cardButton, rollButton, out var failureReason))
                {
                    return;
                }

                RejectUnsafeLibraryButton(existing, failureReason);
                existing = null;
            }

            if (existing != null && HasComponentNamed(existing.transform, "ButtonManager"))
            {
                RejectUnsafeLibraryButton(existing, "existing native-style button has no matching ownership marker");
                existing = null;
            }

            if (existing != null)
            {
                ConfigureFallbackLibraryButton(existing, parent, template, cardButton, rollButton);
                return;
            }

            if (templateManager != null)
            {
                var cloneResult = AuraUiNativeButtonCloneAdapter.TryClone(new AuraUiNativeButtonCloneRequest
                {
                    Template = templateManager,
                    Parent = parent,
                    CloneName = ButtonName,
                    Label = ButtonText,
                    OnClick = OpenPanelFromLibraryButton,
                    StripOwnerBehaviours = StripNativeHouseItems
                });
                var failureReason = cloneResult.FailureReason;
                if (cloneResult.Success
                    && cloneResult.Root != null
                    && TryConfigureNativeLibraryButton(
                        cloneResult.Root,
                        parent,
                        templateManager,
                        template,
                        cardButton,
                        rollButton,
                        out failureReason))
                {
                    return;
                }

                if (cloneResult.Root != null)
                {
                    RejectUnsafeLibraryButton(cloneResult.Root, failureReason);
                }
                else
                {
                    LogNativeButtonFallback(cloneResult.FailureReason);
                }
            }
            else
            {
                LogNativeButtonFallback("the 查找典籍 template has no ButtonManager");
            }

            var fallback = CreateLibraryButton(parent, template);
            ConfigureFallbackLibraryButton(fallback, parent, template, cardButton, rollButton);
        }
        catch (Exception ex)
        {
            SunExpLog.Warn(LogPrefix + " failed to create library button: " + ex.Message);
        }
    }

    private static void MarkActiveForRun(ModHookContext context)
    {
        try
        {
            FamiliarBlessingEffectRuntime.BeginRun();
            var active = FamiliarGrowthApi.BeginRunFromCurrentPartner();
            PlayerApi.SetGameVar(SunExpIds.FamiliarRunActivePartnerKey, active?.FullSpeciesId ?? "");
            SunExpLog.Info(LogPrefix + " active run familiar: " + (active?.FullSpeciesId ?? "none"));
        }
        catch (Exception ex)
        {
            SunExpLog.Warn(LogPrefix + " failed to snapshot active familiar: " + ex.Message);
        }
    }

    private static void GrantBattleWinExperience(ModHookContext context)
    {
        try
        {
            var active = FamiliarGrowthApi.Active();
            if (active == null
                || !AuraLifecycleOperationLedger.TryClaimBattleOperation(
                    SunExpIds.ModId,
                    "FamiliarGrowth",
                    "VictoryProgress",
                    active.FullSpeciesId,
                    "progress",
                    "experience-and-victory-effects"))
            {
                return;
            }

            ApplySelectedBattleWinEffects();
            var result = FamiliarGrowthApi.GrantActiveExperience(FamiliarRosterService.BattleWinExperience);
            if (result == null)
            {
                return;
            }

            if (result.Value.LeveledUp)
            {
                PlayerApi.ShowCaption("\u4f7f\u9b54\u6210\u957f\uff1a" + result.Value.Instance.Name + " Lv." + result.Value.Instance.Level);
            }

            SunExpLog.Debug(LogPrefix + " battle win exp +" + result.Value.GainedExperience + " -> " + result.Value.Instance.InstanceId);
        }
        catch (Exception ex)
        {
            SunExpLog.Warn(LogPrefix + " failed to grant battle experience: " + ex.Message);
        }
    }

    private static void ApplySelectedCombatStartEffects(ModHookContext context)
    {
        try
        {
            var status = FightPlayer.Instance?.Status;
            if (status == null)
            {
                return;
            }

            var applied = FamiliarBlessingEffectRuntime.BeginEpoch(status);

            if (applied > 0)
            {
                SunExpLog.Debug(LogPrefix + " applied combat start effects: " + applied);
            }

            var unsupported = FamiliarBlessingEffectRuntime.UnsupportedSelectedEffectKinds();
            if (unsupported.Count > 0)
            {
                SunExpLog.Warn(LogPrefix + " selected effects have no runtime handler: " + string.Join(",", unsupported));
            }
        }
        catch (Exception ex)
        {
            SunExpLog.Warn(LogPrefix + " failed to apply combat start effects: " + ex.Message);
        }
    }

    private static void OnStatusHitBefore(ModHookContext context)
    {
        FamiliarBlessingEffectRuntime.BeforePotentialLethal(
            context.Target as IStatusManager,
            HitAmount(context.Arguments));
    }

    private static void OnStatusHitAfter(ModHookContext context)
    {
        FamiliarBlessingEffectRuntime.AfterDamage(
            context.Target as IStatusManager,
            HitAmount(context.Arguments),
            context.Arguments != null && context.Arguments.Length > 3
                ? Convert.ToString(context.Arguments[3]) ?? ""
                : "");
    }

    private static int HitAmount(object[]? args)
    {
        if (args == null || args.Length == 0)
        {
            return 0;
        }

        try
        {
            return Math.Max(0, Convert.ToInt32(args[0]));
        }
        catch
        {
            return 0;
        }
    }

    private static void ApplySelectedBattleWinEffects()
    {
        var active = FamiliarGrowthApi.Active();
        if (active == null)
        {
            return;
        }

        var gold = FamiliarGrowthService.BlessingsFor(active)
            .SelectMany(blessing => blessing.Effects)
            .Where(effect => string.Equals(effect.Kind, "BattleWinGold", StringComparison.OrdinalIgnoreCase))
            .Sum(effect => Math.Max(0, effect.Amount));
        if (gold <= 0)
        {
            return;
        }

        if (PlayerApi.AddMoney(gold))
        {
            PlayerApi.ShowCaption("\u4f7f\u9b54\u795d\u798f\uff1a\u91d1\u5e01+" + gold);
        }
    }

    private static Transform? ResolveLibraryWindow(object? houseManager)
    {
        var windowItemParent = Member(houseManager, "WindowItemParent") as Transform;
        if (windowItemParent == null)
        {
            return null;
        }

        var windowButtonParent = Member(houseManager, "WindowButtonParent") as Transform;
        if (windowButtonParent != null && windowButtonParent.childCount > 0)
        {
            var byFirstWindowButton = windowItemParent.Find(windowButtonParent.GetChild(0).name);
            if (byFirstWindowButton != null)
            {
                return byFirstWindowButton;
            }
        }

        foreach (var name in new[] { "\u56fe\u4e66\u9986", "\u5716\u66f8\u9928", "Library" })
        {
            var byName = windowItemParent.Find(name);
            if (byName != null)
            {
                return byName;
            }
        }

        return windowItemParent.childCount > 0 ? windowItemParent.GetChild(0) : null;
    }

    private static Transform? FindHouseItemTransform(Transform root, string typeName)
    {
        foreach (var component in root.GetComponentsInChildren<MonoBehaviour>(true))
        {
            if (component == null || component.GetType().Name != "HouseItem")
            {
                continue;
            }

            if (string.Equals(Convert.ToString(Member(component, "houseItemType")), typeName, StringComparison.Ordinal))
            {
                return component.transform;
            }
        }

        return null;
    }

    private static Transform? FindLibraryButtonTemplate(Transform libraryWindow)
    {
        foreach (var component in libraryWindow.GetComponentsInChildren<MonoBehaviour>(true))
        {
            if (component != null && component.GetType().Name == "ButtonManager")
            {
                return component.transform;
            }
        }

        var button = libraryWindow.GetComponentInChildren<Button>(true);
        return button == null ? null : button.transform;
    }

    private static UnityEngine.Component? FindButtonManagerComponent(Transform root)
    {
        foreach (var component in root.GetComponents<MonoBehaviour>())
        {
            if (component != null && component.GetType().Name == "ButtonManager")
            {
                return component;
            }
        }

        foreach (var component in root.GetComponentsInChildren<MonoBehaviour>(true))
        {
            if (component != null && component.GetType().Name == "ButtonManager")
            {
                return component;
            }
        }

        return null;
    }

    private static Transform? FindButtonLikeTransformByText(Transform root, params string[] texts)
    {
        foreach (var component in root.GetComponentsInChildren<MonoBehaviour>(true))
        {
            if (component == null)
            {
                continue;
            }

            var typeName = component.GetType().Name;
            if (typeName == "ButtonManager")
            {
                if (MatchesAnyText(Convert.ToString(Member(component, "buttonText")), texts)
                    || MatchesAnyChildText(component.transform, texts))
                {
                    return component.transform;
                }
            }

            if (typeName == "HouseItem"
                && MatchesAnyText(Convert.ToString(Member(component, "oriStr")), texts))
            {
                return component.transform;
            }
        }

        foreach (var text in root.GetComponentsInChildren<Text>(true))
        {
            if (MatchesAnyText(text.text, texts))
            {
                return FindButtonLikeRoot(root, text.transform);
            }
        }

        foreach (var component in root.GetComponentsInChildren<UnityEngine.Component>(true))
        {
            if (component == null || !IsTmpText(component))
            {
                continue;
            }

            if (MatchesAnyText(Convert.ToString(Member(component, "text")), texts))
            {
                return FindButtonLikeRoot(root, component.transform);
            }
        }

        return null;
    }

    private static Transform? ResolveLibraryButtonParent(Transform libraryWindow)
    {
        foreach (var path in new[] { "Content/Right/Buttons", "Content/Right", "Content/Buttons", "Content" })
        {
            var candidate = libraryWindow.Find(path);
            if (candidate != null)
            {
                return candidate;
            }
        }

        return libraryWindow;
    }

    private static GameObject CreateLibraryButton(Transform parent, Transform? template)
    {
        var go = new GameObject(ButtonName, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var image = go.AddComponent<Image>();
        image.raycastTarget = true;
        var button = go.AddComponent<Button>();
        button.targetGraphic = image;
        return go;
    }

    private static bool TryConfigureNativeLibraryButton(
        GameObject go,
        Transform parent,
        UnityEngine.Component templateManager,
        Transform? template,
        Transform? cardButton,
        Transform? rollButton,
        out string failureReason)
    {
        go.name = ButtonName;
        if (go.transform.parent != parent)
        {
            go.transform.SetParent(parent, false);
        }

        go.SetActive(false);
        StripNativeHouseItems(go);
        SetChildrenActiveByName(go.transform, "New", false);
        ConfigureLibraryButtonRect(go, parent, template, cardButton, rollButton);
        var configured = AuraUiNativeButtonCloneAdapter.TryConfigureClone(
            templateManager,
            go,
            ButtonText,
            OpenPanelFromLibraryButton);
        if (!configured.Success)
        {
            failureReason = configured.FailureReason;
            return false;
        }

        go.SetActive(true);
        failureReason = "";
        return true;
    }

    private static void ConfigureFallbackLibraryButton(
        GameObject go,
        Transform parent,
        Transform? template,
        Transform? cardButton,
        Transform? rollButton)
    {
        go.name = ButtonName;
        if (go.transform.parent != parent)
        {
            go.transform.SetParent(parent, false);
        }

        go.SetActive(true);
        SetChildrenActiveByName(go.transform, "New", false);
        ConfigureLibraryButtonRect(go, parent, template, cardButton, rollButton);
        ApplyLibraryButtonSprites(go);
        ConfigureLibraryButtonText(go);
        ConfigureUnityButtons(go);
    }

    private static void ConfigureLibraryButtonRect(
        GameObject go,
        Transform parent,
        Transform? template,
        Transform? cardButton,
        Transform? rollButton)
    {
        var rect = go.GetComponent<RectTransform>() ?? go.AddComponent<RectTransform>();
        var cardRect = cardButton?.GetComponent<RectTransform>();
        var rollRect = rollButton?.GetComponent<RectTransform>();
        var templateRect = (template ?? cardButton ?? rollButton)?.GetComponent<RectTransform>();

        if (templateRect != null)
        {
            CopyRectSettings(rect, templateRect);
        }
        else
        {
            rect.anchorMin = new Vector2(1f, 0f);
            rect.anchorMax = new Vector2(1f, 0f);
            rect.pivot = new Vector2(1f, 0.5f);
            rect.sizeDelta = new Vector2(FallbackButtonWidth, FallbackButtonHeight);
        }

        if (parent.GetComponent<LayoutGroup>() == null)
        {
            if (cardRect != null
                && rollRect != null
                && cardRect.parent == rollRect.parent
                && rect.parent == cardRect.parent)
            {
                rect.anchoredPosition = new Vector2(rollRect.anchoredPosition.x, cardRect.anchoredPosition.y);
            }
            else if (cardRect != null && rect.parent == cardRect.parent)
            {
                var width = Math.Max(FallbackButtonWidth, cardRect.rect.width);
                rect.anchoredPosition = cardRect.anchoredPosition + new Vector2(width + LibraryButtonGap, 0f);
            }
            else if (rollRect != null && rect.parent == rollRect.parent)
            {
                var height = Math.Max(FallbackButtonHeight, rollRect.rect.height);
                rect.anchoredPosition = rollRect.anchoredPosition + new Vector2(0f, -height - LibraryButtonGap);
            }
            else if (templateRect != null)
            {
                var height = Math.Max(FallbackButtonHeight, templateRect.rect.height);
                rect.anchoredPosition = templateRect.anchoredPosition + new Vector2(0f, -height - 10f);
            }
            else
            {
                rect.anchoredPosition = new Vector2(-18f, 18f);
            }
        }

        var element = go.GetComponent<LayoutElement>() ?? go.AddComponent<LayoutElement>();
        element.minWidth = Math.Max(FallbackButtonWidth, rect.sizeDelta.x);
        element.preferredWidth = element.minWidth;
        element.minHeight = Math.Max(FallbackButtonHeight, rect.sizeDelta.y);
        element.preferredHeight = element.minHeight;
    }

    private static void CopyRectSettings(RectTransform target, RectTransform source)
    {
        target.anchorMin = source.anchorMin;
        target.anchorMax = source.anchorMax;
        target.pivot = source.pivot;
        target.sizeDelta = source.sizeDelta;
        target.localScale = source.localScale;
        target.localRotation = source.localRotation;
    }

    private static void StripNativeHouseItems(GameObject go)
    {
        foreach (var component in go.GetComponentsInChildren<MonoBehaviour>(true))
        {
            if (component == null || component.GetType().Name != "HouseItem")
            {
                continue;
            }

            try
            {
                Singleton<EventCenter>.Instance.RemoveEventListener(
                    LanguageEvent.LanguageChange.ToString(),
                    component);
            }
            catch (Exception ex)
            {
                SunExpLog.Warn(LogPrefix + " failed to detach cloned HouseItem language listener: " + ex.Message);
            }

            component.enabled = false;
            Object.Destroy(component);
        }
    }

    private static void RejectUnsafeLibraryButton(GameObject go, string reason)
    {
        LogNativeButtonFallback(reason);
        go.SetActive(false);
        go.name = ButtonName + "-Rejected";
        Object.Destroy(go);
    }

    private static void LogNativeButtonFallback(string reason)
    {
        if (loggedNativeButtonFallback)
        {
            return;
        }

        loggedNativeButtonFallback = true;
        SunExpLog.Warn(LogPrefix + " native 查找典籍 style clone rejected; using Aura fallback. reason=" + reason);
    }

    private static void ApplyLibraryButtonSprites(GameObject go)
    {
        var normalSprite = SunExpUiSprites.LibrarySubMenuButton(LogPrefix);
        normalSprite ??= SunExpUiSprites.Button(LogPrefix);

        foreach (Transform child in go.transform)
        {
            child.gameObject.SetActive(false);
        }

        var rootImage = go.GetComponent<Image>() ?? go.AddComponent<Image>();
        rootImage.sprite = null;
        rootImage.type = Image.Type.Simple;
        rootImage.color = new Color(1f, 1f, 1f, 0f);
        rootImage.raycastTarget = true;

        if (normalSprite == null)
        {
            return;
        }

        var brush = FindDirectChild(go.transform, ButtonBrushName);
        if (brush == null)
        {
            brush = new GameObject(ButtonBrushName, typeof(RectTransform), typeof(Image)).transform;
            brush.SetParent(go.transform, false);
        }

        brush.gameObject.SetActive(true);
        brush.SetAsFirstSibling();
        var brushRect = brush.GetComponent<RectTransform>() ?? brush.gameObject.AddComponent<RectTransform>();
        brushRect.anchorMin = Vector2.zero;
        brushRect.anchorMax = Vector2.one;
        brushRect.pivot = new Vector2(0.5f, 0.5f);
        brushRect.offsetMin = Vector2.zero;
        brushRect.offsetMax = Vector2.zero;

        var image = brush.GetComponent<Image>() ?? brush.gameObject.AddComponent<Image>();
        image.sprite = normalSprite;
        image.type = Image.Type.Simple;
        image.fillCenter = true;
        image.color = Color.white;
        image.raycastTarget = false;
        image.preserveAspect = false;
    }

    private static void ConfigureLibraryButtonText(GameObject go)
    {
        var textTransform = FindDirectChild(go.transform, ButtonTextName);
        if (textTransform == null)
        {
            textTransform = new GameObject(ButtonTextName, typeof(RectTransform)).transform;
            textTransform.SetParent(go.transform, false);
        }

        textTransform.gameObject.SetActive(true);
        textTransform.SetAsLastSibling();
        var textRect = textTransform.GetComponent<RectTransform>() ?? textTransform.gameObject.AddComponent<RectTransform>();
        textRect.anchorMin = Vector2.zero;
        textRect.anchorMax = Vector2.one;
        textRect.pivot = new Vector2(0.5f, 0.5f);
        textRect.offsetMin = new Vector2(4f, 0f);
        textRect.offsetMax = new Vector2(-4f, 0f);

        var text = textTransform.GetComponent<Text>() ?? textTransform.gameObject.AddComponent<Text>();
        text.text = ButtonText;
        text.font = AuraUiNativeBridge.ResolveLegacyFont();
        text.fontSize = 18;
        text.alignment = TextAnchor.MiddleCenter;
        text.color = new Color(0.98f, 0.92f, 0.78f);
        text.resizeTextForBestFit = true;
        text.resizeTextMinSize = 12;
        text.resizeTextMaxSize = 18;
        text.raycastTarget = false;
    }

    private static void ConfigureUnityButtons(GameObject go)
    {
        var image = go.GetComponent<Image>() ?? go.AddComponent<Image>();
        image.raycastTarget = true;
        var button = go.GetComponent<Button>() ?? go.AddComponent<Button>();

        button.enabled = true;
        button.interactable = true;
        var visual = FindDirectChild(go.transform, ButtonBrushName)?.GetComponent<Image>() ?? image;
        AuraUiButtonFeedback.Apply(button, visual, SunExpUiComponents.Theme.Accent);
        button.onClick.RemoveAllListeners();
        button.onClick.AddListener(OpenPanelFromLibraryButton);
    }

    private static void OpenPanelFromLibraryButton()
    {
        var now = Time.unscaledTime;
        if (lastLibraryButtonOpenTime >= 0f && now - lastLibraryButtonOpenTime < 0.08f)
        {
            return;
        }

        lastLibraryButtonOpenTime = now;
        OpenPanel();
    }

    private static bool MatchesAnyChildText(Transform root, params string[] texts)
    {
        foreach (var text in root.GetComponentsInChildren<Text>(true))
        {
            if (MatchesAnyText(text.text, texts))
            {
                return true;
            }
        }

        foreach (var component in root.GetComponentsInChildren<UnityEngine.Component>(true))
        {
            if (component != null
                && IsTmpText(component)
                && MatchesAnyText(Convert.ToString(Member(component, "text")), texts))
            {
                return true;
            }
        }

        return false;
    }

    private static bool MatchesAnyText(string? value, params string[] texts)
    {
        var haystack = value ?? "";
        if (string.IsNullOrWhiteSpace(haystack))
        {
            return false;
        }

        foreach (var text in texts)
        {
            if (!string.IsNullOrWhiteSpace(text)
                && haystack.IndexOf(text, StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return true;
            }
        }

        return false;
    }

    private static Transform FindButtonLikeRoot(Transform boundary, Transform source)
    {
        var current = source;
        while (current != null)
        {
            if (current.GetComponent<Button>() != null || HasComponentNamed(current, "ButtonManager") || HasComponentNamed(current, "HouseItem"))
            {
                return current;
            }

            if (current == boundary)
            {
                return current;
            }

            current = current.parent;
        }

        return source;
    }

    private static bool HasComponentNamed(Transform transform, string typeName)
    {
        foreach (var component in transform.GetComponents<MonoBehaviour>())
        {
            if (component != null && component.GetType().Name == typeName)
            {
                return true;
            }
        }

        return false;
    }

    private static Transform? FindDirectChild(Transform parent, string name)
    {
        for (var i = 0; i < parent.childCount; i++)
        {
            var child = parent.GetChild(i);
            if (child.name == name)
            {
                return child;
            }
        }

        return null;
    }

    private static GameObject? FindDeepChild(Transform parent, string name)
    {
        for (var i = 0; i < parent.childCount; i++)
        {
            var child = parent.GetChild(i);
            if (child.name == name)
            {
                return child.gameObject;
            }

            var nested = FindDeepChild(child, name);
            if (nested != null)
            {
                return nested;
            }
        }

        return null;
    }

    private static void SetChildrenActiveByName(Transform parent, string childName, bool active)
    {
        for (var i = 0; i < parent.childCount; i++)
        {
            var child = parent.GetChild(i);
            if (child.name == childName)
            {
                child.gameObject.SetActive(active);
            }

            SetChildrenActiveByName(child, childName, active);
        }
    }

    private static bool IsUnderName(Transform target, string name)
    {
        for (var current = target; current != null; current = current.parent)
        {
            if (string.Equals(current.name, name, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsTmpText(UnityEngine.Component component)
    {
        var name = component.GetType().Name;
        return name == "TMP_Text" || name == "TextMeshProUGUI" || name == "TextMeshPro";
    }

    private static void SetProperty(object target, string name, object value)
    {
        try
        {
            var property = target.GetType().GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            property?.SetValue(target, value);
        }
        catch
        {
            // Optional visual polish only.
        }
    }

    private static object? Member(object? target, string name)
    {
        if (target == null)
        {
            return null;
        }

        const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        var type = target.GetType();
        return type.GetProperty(name, flags)?.GetValue(target)
               ?? type.GetField(name, flags)?.GetValue(target);
    }
}
