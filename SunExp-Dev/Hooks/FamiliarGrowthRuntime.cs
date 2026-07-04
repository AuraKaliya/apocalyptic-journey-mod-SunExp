using System;
using System.Linq;
using System.Reflection;
using AuraShared.Core;
using SunExp.Dll.GameApi;
using SunExp.Dll.Hooks.Ui;
using SunExp.Dll.Infrastructure;
using SunExp.Dll.Mechanics;
using UnityEngine;
using UnityEngine.UI;
using Witch.Core;
using Witch.Mod;

namespace SunExp.Dll.Hooks;

public static class FamiliarGrowthRuntime
{
    private const string LogPrefix = "[FamiliarGrowth]";
    private const string ButtonName = "SunExp_FamiliarGrowthButton";
    private const string ButtonHostName = "SunExp_FamiliarGrowthButtonHost";
    private const float ButtonHeight = 50f;

    public static void Initialize(ModConfig modConfig)
    {
        FamiliarGrowthApi.Initialize(modConfig);
        RegisterAfter(modConfig, "HouseManager.Awake", EnsureHouseButton);
        RegisterAfter(modConfig, "HouseManager.OnEnable", EnsureHouseButton);
        RegisterAfter(modConfig, "HouseManager.ChangeUIShow", EnsureHouseButton);
        RegisterAfter(modConfig, "HouseUI.Awake", EnsureHouseButton);
        RegisterAfter(modConfig, "GameEntryUI.NormalGame", MarkSelectedForRun);
        RegisterAfter(modConfig, "Fight_Start.Init", ApplySelectedCombatStartEffects);
        RegisterAfter(modConfig, "Fight_Win.ResetStates", GrantBattleWinExperience);
        SunExpLog.Info(LogPrefix + " runtime initialized.");
    }

    public static void OpenPanel()
    {
        FamiliarGrowthPanel.Open();
    }

    private static void RegisterAfter(ModConfig config, string target, Action<ModHookContext> action)
    {
        AuraSharedHooks.RegisterAfter(config, target, action, SunExpLog.Debug, message => SunExpLog.Warn(LogPrefix + " " + message));
    }

    private static void EnsureHouseButton(ModHookContext context)
    {
        try
        {
            var parent = SafeHouseButtonParent(context.Target)
                         ?? SunExpModalHost.ModalParent();
            if (parent == null)
            {
                return;
            }

            var buttonObject = FindChild(parent, ButtonName) ?? CreateButtonObject(parent);
            ConfigureButton(buttonObject, parent == SunExpModalHost.ModalParent());
        }
        catch (Exception ex)
        {
            SunExpLog.Warn(LogPrefix + " failed to create house button: " + ex.Message);
        }
    }

    private static void MarkSelectedForRun(ModHookContext context)
    {
        try
        {
            var selected = FamiliarGrowthApi.Selected();
            PlayerApi.SetGameVar(SunExpIds.FamiliarRunSelectedInstanceKey, selected?.InstanceId ?? "");
            SunExpLog.Info(LogPrefix + " selected run familiar: " + (selected?.InstanceId ?? "none"));
        }
        catch (Exception ex)
        {
            SunExpLog.Warn(LogPrefix + " failed to mark selected familiar: " + ex.Message);
        }
    }

    private static void GrantBattleWinExperience(ModHookContext context)
    {
        try
        {
            ApplySelectedBattleWinEffects();
            var result = FamiliarGrowthApi.GrantSelectedExperience(FamiliarRosterService.BattleWinExperience);
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

            var selected = FamiliarGrowthApi.Selected();
            if (selected == null)
            {
                return;
            }

            var applied = 0;
            foreach (var effect in FamiliarGrowthService.BlessingsFor(selected).SelectMany(blessing => blessing.Effects))
            {
                applied += ApplyCombatStartEffect(status, effect) ? 1 : 0;
            }

            if (applied > 0)
            {
                SunExpLog.Debug(LogPrefix + " applied combat start effects: " + applied);
            }
        }
        catch (Exception ex)
        {
            SunExpLog.Warn(LogPrefix + " failed to apply combat start effects: " + ex.Message);
        }
    }

    private static bool ApplyCombatStartEffect(IStatusManager status, FamiliarBlessingEffect effect)
    {
        var kind = (effect.Kind ?? "").Trim();
        var amount = Math.Max(0, effect.Amount);
        if (kind.Length == 0 || amount <= 0)
        {
            return false;
        }

        if (kind.Equals("CombatStartBuff", StringComparison.OrdinalIgnoreCase))
        {
            var buffId = NormalizeRuntimeBuffId(effect.Value);
            if (buffId.Length == 0)
            {
                return false;
            }

            status.AddBuff(buffId, amount);
            return true;
        }

        if (kind.Equals("CombatStartResource", StringComparison.OrdinalIgnoreCase))
        {
            var buffId = NormalizeRuntimeBuffId(effect.Value);
            if (buffId.Length == 0)
            {
                return false;
            }

            status.AddBuff(buffId, amount);
            return true;
        }

        if (kind.Equals("CombatStartHeal", StringComparison.OrdinalIgnoreCase))
        {
            return Heal(status, amount);
        }

        if (kind.Equals("CombatStartShield", StringComparison.OrdinalIgnoreCase))
        {
            return AddShield(status, amount);
        }

        return false;
    }

    private static void ApplySelectedBattleWinEffects()
    {
        var selected = FamiliarGrowthApi.Selected();
        if (selected == null)
        {
            return;
        }

        var gold = FamiliarGrowthService.BlessingsFor(selected)
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

    private static bool Heal(IStatusManager status, int amount)
    {
        try
        {
            var maxHp = Math.Max(1, status.MaxHp);
            var next = Math.Min(maxHp, Math.Max(0, status.CurHp) + amount);
            if (next == status.CurHp)
            {
                return false;
            }

            status.CurHp = next;
            if (string.Equals(status.fatherObject?.GetType().Name, "FightPlayer", StringComparison.Ordinal) && RoleTable.Instance != null)
            {
                RoleTable.Instance.san = Math.Max(1, next);
            }

            status.UpdateStatus(true);
            return true;
        }
        catch (Exception ex)
        {
            SunExpLog.Debug(LogPrefix + " heal effect ignored: " + ex.Message);
            return false;
        }
    }

    private static bool AddShield(IStatusManager status, int amount)
    {
        try
        {
            var next = Math.Max(0, status.Defend) + amount;
            status.Defend = next;
            status.UpdateStatus(true);
            return true;
        }
        catch (Exception ex)
        {
            SunExpLog.Debug(LogPrefix + " shield effect ignored: " + ex.Message);
            return false;
        }
    }

    private static string NormalizeRuntimeBuffId(string value)
    {
        var id = (value ?? "").Trim();
        if (id.Equals("starlight", StringComparison.OrdinalIgnoreCase))
        {
            return SunExpIds.Starlight;
        }

        return id;
    }

    private static GameObject CreateButtonObject(Transform parent)
    {
        var go = new GameObject(ButtonName, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var rect = go.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(1f, 0f);
        rect.anchorMax = new Vector2(1f, 0f);
        rect.pivot = new Vector2(1f, 0f);
        rect.sizeDelta = new Vector2(156f, ButtonHeight);
        rect.anchoredPosition = new Vector2(-18f, 18f);
        return go;
    }

    private static Transform? SafeHouseButtonParent(object? houseManager)
    {
        var windowButtonParent = Member(houseManager, "WindowButtonParent") as Transform;
        var anchor = windowButtonParent?.parent;
        if (anchor == null)
        {
            return null;
        }

        var host = FindChild(anchor, ButtonHostName) ?? new GameObject(ButtonHostName, typeof(RectTransform));
        host.transform.SetParent(anchor, false);
        var rect = host.GetComponent<RectTransform>() ?? host.AddComponent<RectTransform>();
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
        host.transform.SetAsLastSibling();
        return host.transform;
    }

    private static void ConfigureButton(GameObject go, bool fallbackPlacement)
    {
        go.name = ButtonName;
        var rect = go.GetComponent<RectTransform>() ?? go.AddComponent<RectTransform>();
        if (fallbackPlacement)
        {
            rect.anchorMin = new Vector2(1f, 0f);
            rect.anchorMax = new Vector2(1f, 0f);
            rect.pivot = new Vector2(1f, 0f);
            rect.sizeDelta = new Vector2(156f, ButtonHeight);
            rect.anchoredPosition = new Vector2(-18f, 18f);
        }

        var element = go.GetComponent<LayoutElement>() ?? go.AddComponent<LayoutElement>();
        element.minWidth = 132f;
        element.preferredWidth = 156f;
        element.minHeight = ButtonHeight;
        element.preferredHeight = ButtonHeight;

        var image = go.GetComponent<Image>() ?? go.AddComponent<Image>();
        image.sprite = SunExpUiSprites.Button(LogPrefix);
        image.type = image.sprite != null ? Image.Type.Sliced : Image.Type.Simple;
        image.fillCenter = true;
        image.color = image.sprite != null ? Color.white : new Color(0.08f, 0.07f, 0.16f, 0.96f);
        image.raycastTarget = true;

        var button = go.GetComponent<Button>() ?? go.AddComponent<Button>();
        button.targetGraphic = image;
        button.onClick.RemoveAllListeners();
        button.onClick.AddListener(OpenPanel);

        var text = go.transform.Find("Text")?.GetComponent<Text>();
        if (text == null)
        {
            var textGo = new GameObject("Text", typeof(RectTransform));
            textGo.transform.SetParent(go.transform, false);
            var textRect = textGo.GetComponent<RectTransform>();
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.pivot = new Vector2(0.5f, 0.5f);
            textRect.offsetMin = Vector2.zero;
            textRect.offsetMax = Vector2.zero;
            text = textGo.AddComponent<Text>();
        }

        text.text = "\u4f7f\u9b54\u6863\u6848";
        text.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
        text.fontSize = 14;
        text.alignment = TextAnchor.MiddleCenter;
        text.color = new Color(0.92f, 0.88f, 0.72f);
        text.resizeTextForBestFit = true;
        text.resizeTextMinSize = 10;
        text.resizeTextMaxSize = 14;
        text.raycastTarget = false;
    }

    private static GameObject? FindChild(Transform parent, string name)
    {
        for (var i = 0; i < parent.childCount; i++)
        {
            var child = parent.GetChild(i);
            if (child != null && child.name == name)
            {
                return child.gameObject;
            }
        }

        return null;
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
