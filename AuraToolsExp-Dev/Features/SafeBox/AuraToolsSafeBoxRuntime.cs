using System;
using System.Collections;
using System.Reflection;
using AuraShared.Core;
using AuraToolsExp.Dll.Config;
using AuraToolsExp.Dll.Infrastructure;
using Michsky.MUIP;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using Witch;
using Witch.Core;
using Witch.Mod;
using Witch.UI;
using Witch.UI.Window;
using Object = UnityEngine.Object;
using GameUIManager = Witch.UI.UIManager;

namespace AuraToolsExp.Dll.Features.SafeBox;

public static class AuraToolsSafeBoxRuntime
{
    private const string ButtonName = "AuraToolsSafeBoxButton";
    private const int UnlimitedMoneyActionCount = 999999;
    private const int RelaxedCardBottomCount = 0;
    private const int RelaxedCardTopCount = 999999;
    private const int RelaxedMaxReserveCardCount = 999999;
    private const int MinimumSafeBoxLevel = 2;
    private static LimitSnapshot? activeSnapshot;

    public static void Initialize(ModConfig modConfig)
    {
        RegisterAfter(modConfig, "TopBarUI.Awake", RefreshTopBarButton);
        RegisterAfter(modConfig, "TopBarUI.Start", InjectTopBarButton);
        RegisterAfter(modConfig, "TopBarUI.ShowLeftUp", ShowTopBarButton);
        RegisterAfter(modConfig, "TopBarUI.HideLeftUp", HideTopBarButton);
        RegisterAfter(modConfig, "MapSelectUI.Start", RefreshTopBarButtonForAdventureUi);
        RegisterAfter(modConfig, "MapSelectUI.ReadyToSelect", RefreshTopBarButtonForAdventureUi);
        RegisterAfter(modConfig, "MapSelectUI.ShowMap", RefreshTopBarButtonForAdventureUi);
        RegisterAfter(modConfig, "MapSelectUI.MapAnimation", RefreshTopBarButtonForAdventureUi);

        RegisterBefore(modConfig, "SafeBoxUI.PutIntoStore", PrepareUnlimitedSafeBox);
        RegisterAfter(modConfig, "SafeBoxUI.PutIntoStore", FinishUnlimitedSafeBox);
        RegisterBefore(modConfig, "SafeBoxUI.PutItBack", PrepareUnlimitedSafeBox);
        RegisterAfter(modConfig, "SafeBoxUI.PutItBack", FinishUnlimitedSafeBox);
        RegisterBefore(modConfig, "SafeBoxUI.RetainMoney", PrepareUnlimitedSafeBox);
        RegisterAfter(modConfig, "SafeBoxUI.RetainMoney", FinishUnlimitedSafeBox);
        RegisterBefore(modConfig, "SafeBoxUI.ChangeMoney", PrepareUnlimitedSafeBox);
        RegisterAfter(modConfig, "SafeBoxUI.ChangeMoney", FinishUnlimitedSafeBox);
        RegisterAfter(modConfig, "SafeBoxUI.ChangeCountShow", ReplaceCountShowWithUnlimited);
        RegisterAfter(modConfig, "SafeBoxUI.SafeboxSave", SaveRuntimeData);

        RegisterAfter(modConfig, "FightInit.Init", CloseSafeBoxForBlockingUi);
        RegisterAfter(modConfig, "FightUI.FadeIn", CloseSafeBoxForBlockingUi);
        RegisterAfter(modConfig, "EventUI.FadeIn", CloseSafeBoxForBlockingUi);
        RegisterAfter(modConfig, "EventUI.Init", CloseSafeBoxForBlockingUi);

        AuraToolsConfigService.Changed += RefreshTopBarButton;
    }

    internal static void HandleButtonClicked()
    {
        OpenSafeBox();
    }

    private static bool Enabled => AuraToolsConfigService.Root.MatchExperience.Enabled
                                   && AuraToolsConfigService.MatchExperience.SafeBox.Enabled;

    private static void RegisterBefore(ModConfig config, string target, Action<ModHookContext> action)
    {
        AuraSharedHooks.RegisterBefore(config, target, action, warn: AuraToolsLog.Warn);
    }

    private static void RegisterAfter(ModConfig config, string target, Action<ModHookContext> action)
    {
        AuraSharedHooks.RegisterAfter(config, target, action, warn: AuraToolsLog.Warn);
    }

    private static void InjectTopBarButton(ModHookContext context)
    {
        if (context.Target is TopBarUI topBar)
        {
            RefreshTopBarButton(topBar);
            ScheduleTopBarButtonRefresh();
        }
    }

    private static void RefreshTopBarButton(ModHookContext context)
    {
        if (context.Target is TopBarUI topBar)
        {
            RefreshTopBarButton(topBar);
            ScheduleTopBarButtonRefresh();
        }
    }

    private static void RefreshTopBarButtonForAdventureUi(ModHookContext context)
    {
        RefreshTopBarButton();
        ScheduleTopBarButtonRefresh(3);
    }

    private static void RefreshTopBarButton()
    {
        try
        {
            var topBar = GameUIManager.Instance?.GetUI<TopBarUI>("TopBarUI");
            if (topBar != null)
            {
                RefreshTopBarButton(topBar);
            }
        }
        catch (Exception ex)
        {
            AuraToolsLog.Warn("[SafeBox] refresh button failed: " + ex.Message);
        }
    }

    private static void RefreshTopBarButton(TopBarUI topBar)
    {
        if (EnsureTopBarButton(topBar))
        {
            SetTopBarButtonActive(topBar, Enabled && ShouldShowSafeBoxButton());
        }
    }

    private static bool EnsureTopBarButton(TopBarUI topBar)
    {
        var buttons = topBar.transform.Find("Content/Buttons");
        var template = buttons?.Find("CardBack");
        if (buttons == null || template == null)
        {
            return false;
        }

        var existing = buttons.Find(ButtonName);
        if (existing != null)
        {
            BindButton(existing.gameObject);
            ConfigureButton(existing.gameObject);
            return true;
        }

        var buttonObject = Object.Instantiate(template.gameObject, buttons);
        buttonObject.name = ButtonName;
        buttonObject.transform.SetAsLastSibling();
        buttonObject.SetActive(false);
        BindButton(buttonObject);
        ConfigureButton(buttonObject);
        return true;
    }

    private static void BindButton(GameObject buttonObject)
    {
        var manager = buttonObject.GetComponent<ButtonManager>();
        if (manager != null)
        {
            manager.onClick.RemoveAllListeners();
            manager.onDoubleClick.RemoveAllListeners();
            manager.onRightClick.RemoveAllListeners();
            manager.Interactable(true);
        }

        var button = buttonObject.GetComponent<Button>();
        if (button != null)
        {
            button.onClick.RemoveAllListeners();
        }

        foreach (var component in buttonObject.GetComponents<UnityEngine.Component>())
        {
            if (component != null && component.GetType().Name == "KeyItem")
            {
                Object.Destroy(component);
            }
        }

        var relay = buttonObject.GetComponent<AuraToolsSafeBoxButtonRelay>();
        if (relay == null)
        {
            buttonObject.AddComponent<AuraToolsSafeBoxButtonRelay>();
        }
    }

    private static void ConfigureButton(GameObject buttonObject)
    {
        const string label = "保险箱";
        var manager = buttonObject.GetComponent<ButtonManager>();
        if (manager != null)
        {
            manager.enableText = true;
            manager.buttonText = label;
            manager.SetText(label);
            manager.UpdateUI();
        }

        var text = buttonObject.GetComponentInChildren<Text>(true);
        if (text != null)
        {
            text.text = label;
        }
    }

    private static void ShowTopBarButton(ModHookContext context)
    {
        if (context.Target is TopBarUI topBar)
        {
            RefreshTopBarButton(topBar);
        }
    }

    private static void HideTopBarButton(ModHookContext context)
    {
        if (context.Target is TopBarUI topBar)
        {
            SetTopBarButtonActive(topBar, false);
        }
    }

    private static void SetTopBarButtonActive(TopBarUI topBar, bool active)
    {
        var button = topBar.transform.Find("Content/Buttons/" + ButtonName)?.gameObject;
        if (button != null)
        {
            button.SetActive(active);
        }
    }

    private static void ScheduleTopBarButtonRefresh(int delayFrames = 1)
    {
        try
        {
            GameUIManager.Instance?.StartCoroutine(RefreshTopBarButtonAfterFrames(delayFrames));
        }
        catch
        {
        }
    }

    private static IEnumerator RefreshTopBarButtonAfterFrames(int delayFrames)
    {
        for (var i = 0; i < delayFrames; i++)
        {
            yield return null;
        }

        RefreshTopBarButton();
    }

    private static bool ShouldShowSafeBoxButton()
    {
        return GameUIManager.Instance != null
               && RoleTable.Instance != null
               && (FightManager.Instance == null || FightManager.Instance.fightType == FightType.None)
               && !IsActiveUI<FightUI>("FightUI");
    }

    private static void OpenSafeBox()
    {
        try
        {
            if (!Enabled)
            {
                return;
            }

            var blockReason = GetOpenBlockReason();
            if (blockReason != "ok")
            {
                GameUIManager.Instance?.ShowTip("当前状态不能打开随身保险箱");
                AuraToolsLog.Warn("[SafeBox] open blocked: " + blockReason);
                return;
            }

            var safeBox = GameUIManager.Instance.ShowUI<SafeBoxUI>("SafeBoxUI", true);
            safeBox.transform.SetAsLastSibling();
            safeBox.ShowBackItem();
            ReplaceCountShowWithUnlimited(safeBox);
            PrimeUnlimitedFlags();
        }
        catch (Exception ex)
        {
            AuraToolsLog.Error("[SafeBox] failed to open SafeBoxUI", ex);
        }
    }

    private static string GetOpenBlockReason()
    {
        if (GameUIManager.Instance == null)
        {
            return "UIManager missing";
        }

        if (RoleTable.Instance == null)
        {
            return "RoleTable missing";
        }

        if (MapManager.Instance?.ModeMapManager == null)
        {
            return "MapManager missing";
        }

        if (FightManager.Instance != null && FightManager.Instance.fightType != FightType.None)
        {
            return "fight active";
        }

        if (IsActiveUI<FightUI>("FightUI") || IsActiveUI<EventUI>("EventUI") || IsActiveUI<DialogueUI>("DialogueUI"))
        {
            return "blocking UI active";
        }

        if (GameUIManager.Instance.WindowObj != null || GameUIManager.Instance.InputObj != null)
        {
            return "modal active";
        }

        return "ok";
    }

    private static bool IsActiveUI<T>(string uiName) where T : UIBase
    {
        try
        {
            var ui = GameUIManager.Instance?.GetUI<T>(uiName);
            return ui != null && ui.gameObject.activeInHierarchy;
        }
        catch
        {
            return false;
        }
    }

    private static void CloseSafeBoxForBlockingUi(ModHookContext context)
    {
        CloseSafeBox();
        RefreshTopBarButton();
    }

    private static void CloseSafeBox()
    {
        try
        {
            var uiManager = GameUIManager.Instance;
            var safeBox = uiManager?.GetUI<SafeBoxUI>("SafeBoxUI");
            if (safeBox != null)
            {
                uiManager!.CloseUI("SafeBoxUI");
            }
        }
        catch
        {
        }
    }

    private static void PrepareUnlimitedSafeBox(ModHookContext context)
    {
        if (!Enabled)
        {
            return;
        }

        try
        {
            activeSnapshot ??= LimitSnapshot.Capture();
            ApplyUnlimitedEnvironment();
        }
        catch (Exception ex)
        {
            AuraToolsLog.Error("[SafeBox] prepare failed", ex);
        }
    }

    private static void FinishUnlimitedSafeBox(ModHookContext context)
    {
        if (!Enabled)
        {
            return;
        }

        SafeBoxUI? safeBox = null;
        try
        {
            safeBox = context.Target as SafeBoxUI ?? GameUIManager.Instance?.GetUI<SafeBoxUI>("SafeBoxUI");
            activeSnapshot?.Restore();
            activeSnapshot = null;
            PrimeUnlimitedFlags();

            if (safeBox != null)
            {
                safeBox.ChangeMoneyShow();
                safeBox.ChangeCountShow();
                safeBox.UpdateCardShow();
                ReplaceCountShowWithUnlimited(safeBox);
            }

            SafeBoxUI.SafeboxSave();
            SaveRuntimeData();
            RefreshTopBar();
            RefreshTopBarButton();
        }
        catch (Exception ex)
        {
            activeSnapshot?.Restore();
            activeSnapshot = null;
            AuraToolsLog.Error("[SafeBox] finish failed", ex);
        }
    }

    private static void ApplyUnlimitedEnvironment()
    {
        var role = RoleTable.Instance;
        if (role == null)
        {
            return;
        }

        role.SafeBoxCardCount = 0;
        role.SafeBoxRelicCount = 0;
        role.SafeBoxSaveMoneyCount = UnlimitedMoneyActionCount;
        role.SafeBoxGetMoneyCount = UnlimitedMoneyActionCount;
        role.GetCardInBack = false;
        role.GetRelic = false;
        role.CardBottomCount = RelaxedCardBottomCount;
        role.CardTopCount = RelaxedCardTopCount;
        role.MaxAlCardCount = RelaxedMaxReserveCardCount;

        var mode = MapManager.Instance?.ModeMapManager;
        if (mode != null && mode.Level < MinimumSafeBoxLevel)
        {
            mode.Level = MinimumSafeBoxLevel;
        }
    }

    private static void PrimeUnlimitedFlags()
    {
        var role = RoleTable.Instance;
        if (role == null)
        {
            return;
        }

        role.SafeBoxCardCount = 0;
        role.SafeBoxRelicCount = 0;
        role.SafeBoxSaveMoneyCount = UnlimitedMoneyActionCount;
        role.SafeBoxGetMoneyCount = UnlimitedMoneyActionCount;
        role.GetCardInBack = false;
        role.GetRelic = false;
    }

    private static void ReplaceCountShowWithUnlimited(ModHookContext context)
    {
        if (Enabled && context.Target is SafeBoxUI safeBox)
        {
            ReplaceCountShowWithUnlimited(safeBox);
        }
    }

    private static void ReplaceCountShowWithUnlimited(SafeBoxUI safeBox)
    {
        SetText(safeBox.transform, "Content/Backpack/Windows/卡牌/Right/SafeCount/Title", "可存放卡牌: 不限");
        SetText(safeBox.transform, "Content/Backpack/Windows/遗物/Right/SafeCount/Title", "可存放遗物: 不限");
        SetText(safeBox.transform, "Content/Backpack/Windows/卡牌/Left/CanOut/text", "可带出: 不限");
        SetText(safeBox.transform, "Content/Backpack/Windows/遗物/Left/CanOut/text", "可带出: 不限");
    }

    private static void SetText(Transform root, string path, string value)
    {
        try
        {
            var target = root.Find(path);
            if (target == null)
            {
                return;
            }

            var text = target.GetComponent<Text>();
            if (text != null)
            {
                text.text = value;
                return;
            }

            var component = target.GetComponent("TMPro.TMP_Text");
            var property = component?.GetType().GetProperty("text", BindingFlags.Instance | BindingFlags.Public);
            property?.SetValue(component, value);
        }
        catch
        {
        }
    }

    private static void SaveRuntimeData(ModHookContext context)
    {
        if (Enabled)
        {
            SaveRuntimeData();
        }
    }

    private static void SaveRuntimeData()
    {
        try
        {
            Singleton<GameRuntimeData>.Instance.Save();
        }
        catch (Exception ex)
        {
            AuraToolsLog.Warn("[SafeBox] failed to save runtime data: " + ex.Message);
        }
    }

    private static void RefreshTopBar()
    {
        try
        {
            var topBar = GameUIManager.Instance?.GetUI<TopBarUI>("TopBarUI");
            if (topBar == null)
            {
                return;
            }

            topBar.UpdateRelics();
            topBar.ChangeMoney();
        }
        catch
        {
        }
    }

    private sealed class LimitSnapshot
    {
        private readonly RoleTable? role;
        private readonly IModeManager? mode;
        private readonly int cardBottomCount;
        private readonly int cardTopCount;
        private readonly int maxAlCardCount;
        private readonly int level;

        private LimitSnapshot(RoleTable? role, IModeManager? mode)
        {
            this.role = role;
            this.mode = mode;
            if (role != null)
            {
                cardBottomCount = role.CardBottomCount;
                cardTopCount = role.CardTopCount;
                maxAlCardCount = role.MaxAlCardCount;
            }

            if (mode != null)
            {
                level = mode.Level;
            }
        }

        public static LimitSnapshot Capture()
        {
            return new LimitSnapshot(RoleTable.Instance, MapManager.Instance?.ModeMapManager);
        }

        public void Restore()
        {
            if (role != null)
            {
                role.CardBottomCount = cardBottomCount;
                role.CardTopCount = cardTopCount;
                role.MaxAlCardCount = maxAlCardCount;
            }

            if (mode != null)
            {
                mode.Level = level;
            }
        }
    }
}

internal sealed class AuraToolsSafeBoxButtonRelay : MonoBehaviour, IPointerClickHandler, ISubmitHandler
{
    public void OnPointerClick(PointerEventData eventData)
    {
        if (eventData.button == PointerEventData.InputButton.Left)
        {
            AuraToolsSafeBoxRuntime.HandleButtonClicked();
        }
    }

    public void OnSubmit(BaseEventData eventData)
    {
        AuraToolsSafeBoxRuntime.HandleButtonClicked();
    }
}
