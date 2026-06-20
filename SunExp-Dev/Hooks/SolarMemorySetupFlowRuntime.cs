using System;
using System.Collections.Generic;
using SunExp.Dll.GameApi;
using SunExp.Dll.Infrastructure;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;
using Witch;
using Witch.Core;
using Witch.UI;
using Witch.UI.Window;
using Object = UnityEngine.Object;

namespace SunExp.Dll.Hooks;

public static class SolarMemorySetupFlowRuntime
{
    private const int OriginSetupPointTotal = 50;
    private const int OriginLargeStep = 10;
    private const string OriginWindowName = "SunExp_SolarMemoryOriginSetup";
    private const string BlessingChromeName = "SunExp_SolarMemoryBlessingSetup";
    private const string ButtonSpritePath = "Mods/SunExp/ModResource/Images/UI/button-\u4e5d\u5bab\u683c.png";
    private const string PanelSpritePath = "Mods/SunExp/ModResource/Images/UI/background-\u4e5d\u5bab\u683c.png";
    private static readonly Color Gold = new(0.82f, 0.72f, 0.42f);
    private static readonly Color PaleGold = new(0.93f, 0.86f, 0.58f);
    private static readonly Color DeepBlue = new(0.02f, 0.02f, 0.16f, 0.98f);
    private static readonly Color HeaderTint = new(0.025f, 0.025f, 0.14f, 0.98f);
    private static readonly Color RowTint = new(0.07f, 0.07f, 0.21f, 0.98f);
    private static readonly Dictionary<string, int> pendingOriginAdds = new(StringComparer.Ordinal);
    private static readonly Dictionary<string, Text> originValueTexts = new(StringComparer.Ordinal);
    private static GameObject? activeOriginRoot;
    private static GameObject? activeBlessingChrome;
    private static Text? originSummaryText;
    private static Text? originHintText;
    private static Sprite? buttonSprite;
    private static Sprite? panelSprite;
    private static bool buttonSpriteLoadAttempted;
    private static bool panelSpriteLoadAttempted;
    private static bool blessingStepActive;

    public static bool IsBlessingStepActive => blessingStepActive;

    public static bool IsOriginWindowOpen => activeOriginRoot != null;

    public static void StartAfterStarterDeck()
    {
        SolarMemoryPreparationRuntime.StartOrResume();
    }

    public static void OpenOriginSetupWindow()
    {
        try
        {
            CloseOriginWindow();
            EnsureSetupVars();

            var parent = UIManager.Instance?.upperCanvasTf ?? UIManager.Instance?.canvasTf;
            if (parent == null || RoleTable.Instance == null)
            {
                return;
            }

            pendingOriginAdds.Clear();
            originValueTexts.Clear();
            foreach (var spec in OriginSpecs())
            {
                pendingOriginAdds[spec.Key] = 0;
            }

            activeOriginRoot = CreateRect(OriginWindowName, parent, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero).gameObject;
            activeOriginRoot.transform.SetAsLastSibling();
            var blocker = activeOriginRoot.AddComponent<Image>();
            blocker.color = new Color(0f, 0f, 0f, 0.74f);

            var window = CreateRect("Window", activeOriginRoot.transform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(0.5f, 0.5f), new Vector2(760f, 520f));
            ApplyPanelImage(window.gameObject, DeepBlue, true);

            var header = CreateRect("Header", window.transform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f),
                new Vector2(-44f, 86f));
            header.anchoredPosition = new Vector2(0f, -22f);
            ApplyPanelImage(header.gameObject, HeaderTint, true);
            AddText(header, "Title", "\u65e5\u8000\u56de\u5fc6\u00b7\u672c\u6e90\u52a0\u70b9", 28, FontStyle.Bold, TextAnchor.MiddleCenter, PaleGold,
                Vector2.zero, new Vector2(700f, 42f));
            originSummaryText = AddText(header, "Summary", "", 17, FontStyle.Normal, TextAnchor.MiddleCenter, Gold,
                new Vector2(0f, -30f), new Vector2(700f, 28f));

            var y = -132f;
            foreach (var spec in OriginSpecs())
            {
                CreateOriginRow(window, spec.Key, spec.Label, y);
                y -= 68f;
            }

            originHintText = AddText(window, "Hint", "", 15, FontStyle.Normal, TextAnchor.MiddleLeft, PaleGold,
                new Vector2(36f, -448f), new Vector2(330f, 40f));
            CreateButton(window, "Reset", "\u91cd\u7f6e", new Vector2(380f, -448f), new Vector2(140f, 44f), ResetOriginPending);
            CreateButton(window, "Confirm", "\u786e\u8ba4", new Vector2(550f, -448f), new Vector2(140f, 44f), ConfirmOriginSetup);
            RefreshOriginTexts();
        }
        catch (Exception ex)
        {
            SunExpLog.Error("Solar memory origin setup window failed", ex);
        }
    }

    private static void CreateOriginRow(RectTransform window, string key, string label, float y)
    {
        var row = CreateRect("Origin-" + key, window, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f),
            new Vector2(-70f, 58f));
        row.anchoredPosition = new Vector2(0f, y);
        ApplyPanelImage(row.gameObject, RowTint, true);
        AddText(row, "Name", label, 21, FontStyle.Bold, TextAnchor.MiddleLeft, Color.white, new Vector2(24f, 0f), new Vector2(180f, 52f));
        AddText(row, "Role", OriginRoleLabel(key), 16, FontStyle.Bold, TextAnchor.MiddleCenter, Gold, new Vector2(214f, 0f), new Vector2(74f, 52f));

        var controls = CreateRect("Controls-" + key, row, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(1f, 0.5f),
            new Vector2(340f, 44f));
        controls.anchoredPosition = new Vector2(-32f, 0f);
        var layout = controls.gameObject.AddComponent<HorizontalLayoutGroup>();
        layout.spacing = 8f;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = false;
        layout.childForceExpandHeight = true;

        CreateLayoutButton(controls, "-", new Vector2(58f, 42f), () => RemoveOriginPoint(key));
        var valueBox = CreateLayoutBox(controls, "ValueBox-" + key, new Vector2(96f, 42f));
        ApplyPanelImage(valueBox.gameObject, HeaderTint, true);
        originValueTexts[key] = AddText(valueBox, "Value", "", 20, FontStyle.Bold, TextAnchor.MiddleCenter, PaleGold,
            Vector2.zero, new Vector2(96f, 42f));
        CreateLayoutButton(controls, "+", new Vector2(58f, 42f), () => AddOriginPoint(key, 1));
        CreateLayoutButton(controls, "++", new Vector2(70f, 42f), () => AddOriginPoint(key, OriginLargeStep));
    }

    private static void AddOriginPoint(string key, int amount)
    {
        var add = AllowedOriginAdd(key, amount);
        if (add <= 0)
        {
            var cap = OriginCapFor(key);
            SetOriginHint(OriginRemaining() <= 0
                ? "\u672c\u6e90\u70b9\u6570\u5df2\u5206\u914d\u5b8c\u6210\u3002"
                : "\u8be5\u672c\u6e90\u5df2\u8fbe\u5230 " + cap + " \u70b9\u4e0a\u9650\u3002");
            return;
        }

        pendingOriginAdds[key] = pendingOriginAdds.TryGetValue(key, out var current) ? current + add : add;
        RefreshOriginTexts();
        if (add < amount)
        {
            SetOriginHint("\u5df2\u6dfb\u52a0 " + add + " \u70b9\uff0c\u53d7\u5269\u4f59\u70b9\u6570\u6216\u672c\u6e90\u52a8\u6001\u4e0a\u9650\u9650\u5236\u3002");
        }
    }

    private static void RemoveOriginPoint(string key)
    {
        if (!pendingOriginAdds.TryGetValue(key, out var current) || current <= 0)
        {
            return;
        }

        pendingOriginAdds[key] = current - 1;
        RefreshOriginTexts();
    }

    private static void ResetOriginPending()
    {
        foreach (var key in OriginSpecKeys())
        {
            pendingOriginAdds[key] = 0;
        }

        RefreshOriginTexts();
    }

    private static void ConfirmOriginSetup()
    {
        NormalizePendingOriginAdds();
        if (OriginRemaining() > 0)
        {
            SetOriginHint("\u8bf7\u5148\u5206\u914d\u5b8c\u6240\u6709\u672c\u6e90\u70b9\u6570\u3002");
            RefreshOriginTexts();
            return;
        }

        var role = RoleTable.Instance;
        if (role == null)
        {
            return;
        }

        foreach (var pair in pendingOriginAdds)
        {
            if (pair.Value > 0 && role.VarsMap.ContainsKey(pair.Key))
            {
                role.UseVarsChanges(pair.Key, pair.Value);
            }
        }

        SolarMemoryPlayerSetupState.SetInt(SunExpIds.SolarMemoryOriginPointsKey, 0);
        SolarMemoryPlayerSetupState.SetFlag(SunExpIds.SolarMemoryOriginConfiguredKey, true);
        CloseOriginWindow();
        SunExpLog.Info("[SolarMemorySetup] origin allocation confirmed.");
        SolarMemoryPreparationRuntime.CompleteOriginAllocation();
    }

    public static void OpenBlessingSetupWindow()
    {
        try
        {
            if (!SolarMemoryModeRuntime.IsSolarMemoryRun() || RoleTable.Instance == null)
            {
                return;
            }

            EnsureSetupVars();
            if (SolarMemoryPlayerSetupState.IsSet(SunExpIds.SolarMemoryBlessConfiguredKey))
            {
                SolarMemoryPreparationRuntime.CompleteBlessingSelection();
                return;
            }

            blessingStepActive = true;
            SunExpLog.Info("[SolarMemorySetup] opening blessing picker.");
            SolarMemoryBlessingPickerRuntime.Open(() =>
            {
                blessingStepActive = false;
                SolarMemoryPreparationRuntime.CompleteBlessingSelection();
            });
            if (!SolarMemoryBlessingPickerRuntime.IsOpen
                && !SolarMemoryPlayerSetupState.IsSet(SunExpIds.SolarMemoryBlessConfiguredKey))
            {
                blessingStepActive = false;
            }
        }
        catch (Exception ex)
        {
            blessingStepActive = false;
            CloseBlessingChrome();
            SolarMemoryBlessingPickerRuntime.Close();
            SunExpLog.Error("Solar memory blessing setup failed", ex);
        }
    }

    public static void ClosePreparationWindows()
    {
        CloseOriginWindow();
        CloseBlessingChrome();
        SolarMemoryBlessingPickerRuntime.Close();
        blessingStepActive = false;
    }

    private static void CreateBlessingChrome(Transform parent, int step)
    {
        CloseBlessingChrome();
        activeBlessingChrome = CreateRect(BlessingChromeName, parent, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero).gameObject;
        activeBlessingChrome.transform.SetAsLastSibling();

        var header = CreateRect("Header", activeBlessingChrome.transform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
            new Vector2(0.5f, 1f), new Vector2(720f, 82f));
        header.anchoredPosition = new Vector2(0f, -24f);
        ApplyPanelImage(header.gameObject, HeaderTint, false);
        AddText(header, "Title", "\u65e5\u8000\u56de\u5fc6\u00b7\u795d\u798f\u9009\u53d6", 28, FontStyle.Bold, TextAnchor.MiddleCenter,
            PaleGold, new Vector2(0f, -8f), new Vector2(680f, 40f));
        AddText(header, "Progress", "\u795d\u798f " + Math.Min(5, step) + "/5", 18, FontStyle.Normal, TextAnchor.MiddleCenter,
            Gold, new Vector2(0f, -46f), new Vector2(680f, 28f));
    }

    private static void EnsureSetupVars()
    {
        if (SolarMemoryPlayerSetupState.GetValue(SunExpIds.SolarMemoryOriginPointsKey, "") == "")
        {
            SolarMemoryPlayerSetupState.SetInt(SunExpIds.SolarMemoryOriginPointsKey, OriginAssignablePointTotal());
        }

        if (!SolarMemoryPlayerSetupState.IsSet(SunExpIds.SolarMemoryOriginConfiguredKey))
        {
            var assignable = OriginAssignablePointTotal();
            if (SolarMemoryPlayerSetupState.GetInt(SunExpIds.SolarMemoryOriginPointsKey, 0) != assignable)
            {
                SolarMemoryPlayerSetupState.SetInt(SunExpIds.SolarMemoryOriginPointsKey, assignable);
            }
        }
    }

    private static bool IsSetupFinished()
    {
        return SolarMemoryPlayerSetupState.IsSet(SunExpIds.SolarMemorySetupFinishedKey);
    }

    private static int BlessingPickCount()
    {
        return SolarMemoryPlayerSetupState.GetInt(SunExpIds.SolarMemoryBlessPickCountKey, 0);
    }

    private static int OriginRemaining()
    {
        var total = Math.Max(0, SolarMemoryPlayerSetupState.GetInt(SunExpIds.SolarMemoryOriginPointsKey, OriginSetupPointTotal));
        var used = 0;
        foreach (var value in pendingOriginAdds.Values)
        {
            used += Math.Max(0, value);
        }

        return Math.Max(0, total - used);
    }

    private static void RefreshOriginTexts()
    {
        var role = RoleTable.Instance;
        if (role == null)
        {
            return;
        }

        foreach (var spec in OriginSpecs())
        {
            if (!originValueTexts.TryGetValue(spec.Key, out var text))
            {
                continue;
            }

            var baseValue = OriginBaseValue(spec.Key);
            var pending = pendingOriginAdds.TryGetValue(spec.Key, out var add) ? add : 0;
            var cap = OriginCapFor(spec.Key);
            text.text = pending > 0 ? baseValue + " +" + pending + "/" + cap : baseValue + "/" + cap;
        }

        if (originSummaryText != null)
        {
            originSummaryText.text = "\u5269\u4f59\u672c\u6e90\u70b9\u6570\uff1a" + OriginRemaining() + "/" + OriginAssignablePointTotal();
        }

        SetOriginHint(OriginRemaining() == 0
            ? "\u53ef\u4ee5\u786e\u8ba4\uff0c\u4e0b\u4e00\u6b65\u5c06\u8fdb\u5165\u795d\u798f\u9009\u53d6\u3002"
            : "\u8bf7\u5206\u914d " + OriginAssignablePointTotal() + " \u70b9\u672c\u6e90\u70b9\u6570\u3002");
    }

    private static int AllowedOriginAdd(string key, int amount)
    {
        var remaining = OriginRemaining();
        if (remaining <= 0 || amount <= 0)
        {
            return 0;
        }

        var pending = pendingOriginAdds.TryGetValue(key, out var add) ? add : 0;
        var capacity = OriginCapFor(key) - OriginBaseValue(key) - pending;
        return Math.Max(0, Math.Min(amount, Math.Min(remaining, capacity)));
    }

    private static int OriginAssignablePointTotal()
    {
        var capacity = 0;
        foreach (var key in OriginSpecKeys())
        {
            capacity += Math.Max(0, OriginCapFor(key) - OriginBaseValue(key));
        }

        return Math.Min(OriginSetupPointTotal, capacity);
    }

    private static void NormalizePendingOriginAdds()
    {
        var keys = new List<string>();
        foreach (var key in OriginSpecKeys())
        {
            keys.Add(key);
            if (!pendingOriginAdds.TryGetValue(key, out var pending) || pending <= 0)
            {
                continue;
            }

            var cap = OriginCapFor(key);
            var allowed = Math.Max(0, cap - OriginBaseValue(key));
            if (pending > allowed)
            {
                pendingOriginAdds[key] = allowed;
            }
        }

        var overflow = 0;
        foreach (var value in pendingOriginAdds.Values)
        {
            overflow += Math.Max(0, value);
        }

        overflow -= OriginAssignablePointTotal();
        for (var i = keys.Count - 1; i >= 0 && overflow > 0; i--)
        {
            var key = keys[i];
            if (!pendingOriginAdds.TryGetValue(key, out var pending) || pending <= 0)
            {
                continue;
            }

            var remove = Math.Min(pending, overflow);
            pendingOriginAdds[key] = pending - remove;
            overflow -= remove;
        }
    }

    private static int OriginCapFor(string key)
    {
        var role = RoleTable.Instance;
        if (role == null)
        {
            return 20;
        }

        var chosen = role.ChooseVars;
        if (chosen != null && chosen.Count > 0 && string.Equals(chosen[0], key, StringComparison.Ordinal))
        {
            return role.MainVarUpperBound;
        }

        if (chosen != null && chosen.Contains(key))
        {
            return role.SecondaryVarUpperBound;
        }

        return role.OtherVarUpperBound;
    }

    private static string OriginRoleLabel(string key)
    {
        var role = RoleTable.Instance;
        var chosen = role?.ChooseVars;
        if (chosen != null && chosen.Count > 0 && string.Equals(chosen[0], key, StringComparison.Ordinal))
        {
            return "\u4e3b";
        }

        if (chosen != null && chosen.Contains(key))
        {
            return "\u6b21";
        }

        return "\u672a\u9009";
    }

    private static int OriginBaseValue(string key)
    {
        var role = RoleTable.Instance;
        return role != null && role.VarsMap.TryGetValue(key, out var value) ? value : 0;
    }

    private static void SetOriginHint(string value)
    {
        if (originHintText != null)
        {
            originHintText.text = value;
        }
    }

    private static IEnumerable<(string Key, string Label)> OriginSpecs()
    {
        yield return ("Strength", "\u9b54\u529b");
        yield return ("Lucky", "\u7cbe\u795e");
        yield return ("Perceive", "\u611f\u77e5");
        yield return ("Wisdom", "\u5e78\u8fd0");
    }

    private static IEnumerable<string> OriginSpecKeys()
    {
        foreach (var spec in OriginSpecs())
        {
            yield return spec.Key;
        }
    }

    private static RectTransform CreateButton(RectTransform parent, string name, string label, Vector2 anchoredPosition, Vector2 size, Action action)
    {
        var rect = CreateRect(name, parent, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f), size);
        rect.anchoredPosition = anchoredPosition;
        var image = rect.gameObject.AddComponent<Image>();
        image.sprite = GetButtonSprite();
        image.type = image.sprite != null ? Image.Type.Sliced : Image.Type.Simple;
        image.color = image.sprite != null ? Color.white : new Color(0.05f, 0.05f, 0.22f, 0.96f);
        var button = rect.gameObject.AddComponent<Button>();
        button.targetGraphic = image;
        button.onClick.AddListener(new UnityAction(action));
        AddText(rect, "Text", label, 16, FontStyle.Bold, TextAnchor.MiddleCenter, PaleGold, Vector2.zero, size);
        return rect;
    }

    private static RectTransform CreateLayoutBox(Transform parent, string name, Vector2 size)
    {
        var rect = CreateRect(name, parent, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), size);
        var element = rect.gameObject.AddComponent<LayoutElement>();
        element.minWidth = size.x;
        element.preferredWidth = size.x;
        element.minHeight = size.y;
        element.preferredHeight = size.y;
        return rect;
    }

    private static Button CreateLayoutButton(Transform parent, string label, Vector2 size, Action action)
    {
        var rect = CreateLayoutBox(parent, "Button-" + label, size);
        var image = rect.gameObject.AddComponent<Image>();
        image.sprite = GetButtonSprite();
        image.type = image.sprite != null ? Image.Type.Sliced : Image.Type.Simple;
        image.color = image.sprite != null ? Color.white : new Color(0.05f, 0.05f, 0.22f, 0.96f);
        var button = rect.gameObject.AddComponent<Button>();
        button.targetGraphic = image;
        button.onClick.AddListener(new UnityAction(action));
        AddText(rect, "Text", label, 18, FontStyle.Bold, TextAnchor.MiddleCenter, PaleGold, Vector2.zero, size);
        return button;
    }

    private static Text AddText(RectTransform parent, string name, string value, int fontSize, FontStyle style, TextAnchor alignment,
        Color color, Vector2 anchoredPosition, Vector2 size)
    {
        var rect = CreateRect(name, parent, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), size);
        rect.anchoredPosition = anchoredPosition;
        var text = rect.gameObject.AddComponent<Text>();
        text.text = value;
        text.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
        text.fontSize = fontSize;
        text.fontStyle = style;
        text.alignment = alignment;
        text.color = color;
        text.horizontalOverflow = HorizontalWrapMode.Wrap;
        text.verticalOverflow = VerticalWrapMode.Truncate;
        text.resizeTextForBestFit = true;
        text.resizeTextMinSize = Math.Max(10, fontSize - 6);
        text.resizeTextMaxSize = fontSize;
        text.raycastTarget = false;
        return text;
    }

    private static RectTransform CreateRect(string name, Transform parent, Vector2 anchorMin, Vector2 anchorMax, Vector2 pivot, Vector2 sizeDelta)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var rect = go.GetComponent<RectTransform>();
        rect.anchorMin = anchorMin;
        rect.anchorMax = anchorMax;
        rect.pivot = pivot;
        rect.sizeDelta = sizeDelta;
        rect.anchoredPosition = Vector2.zero;
        return rect;
    }

    private static void ApplyPanelImage(GameObject go, Color fallbackOrTint, bool raycastTarget)
    {
        var image = go.AddComponent<Image>();
        image.sprite = GetPanelSprite();
        image.type = image.sprite != null ? Image.Type.Sliced : Image.Type.Simple;
        image.fillCenter = true;
        image.color = image.sprite != null ? new Color(1f, 1f, 1f, fallbackOrTint.a) : fallbackOrTint;
        image.raycastTarget = raycastTarget;
        if (image.sprite != null)
        {
            AddPanelTint(go, fallbackOrTint, raycastTarget);
        }
    }

    private static void AddPanelTint(GameObject target, Color color, bool raycastTarget)
    {
        var tint = CreateRect("PanelTint", target.transform, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero);
        tint.offsetMin = new Vector2(3f, 3f);
        tint.offsetMax = new Vector2(-3f, -3f);
        var image = tint.gameObject.AddComponent<Image>();
        image.color = new Color(color.r, color.g, color.b, Mathf.Min(0.62f, color.a));
        image.raycastTarget = raycastTarget;
        tint.SetAsFirstSibling();
    }

    private static Sprite? GetButtonSprite()
    {
        if (buttonSprite != null)
        {
            return buttonSprite;
        }

        if (buttonSpriteLoadAttempted)
        {
            return null;
        }

        buttonSpriteLoadAttempted = true;
        buttonSprite = CreateNineSliceSprite(ButtonSpritePath, new Vector4(24f, 12f, 24f, 12f));
        return buttonSprite;
    }

    private static Sprite? GetPanelSprite()
    {
        if (panelSprite != null)
        {
            return panelSprite;
        }

        if (panelSpriteLoadAttempted)
        {
            return null;
        }

        panelSpriteLoadAttempted = true;
        panelSprite = CreateNineSliceSprite(PanelSpritePath, new Vector4(4f, 4f, 4f, 4f));
        return panelSprite;
    }

    private static Sprite? CreateNineSliceSprite(string path, Vector4 border)
    {
        try
        {
            var source = ResourceLoader.Load<Sprite>(path, true);
            if (source == null || source.texture == null)
            {
                return null;
            }

            var texture = source.texture;
            texture.filterMode = FilterMode.Point;
            texture.wrapMode = TextureWrapMode.Clamp;
            return Sprite.Create(texture, source.rect, new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect, border);
        }
        catch (Exception ex)
        {
            SunExpLog.Warn("[SolarMemorySetup] failed to load UI sprite " + path + ": " + ex.Message);
            return null;
        }
    }

    private static void CloseOriginWindow()
    {
        if (activeOriginRoot != null)
        {
            Object.Destroy(activeOriginRoot);
            activeOriginRoot = null;
        }

        originValueTexts.Clear();
        originSummaryText = null;
        originHintText = null;
        pendingOriginAdds.Clear();
    }

    private static void CloseBlessingChrome()
    {
        if (activeBlessingChrome != null)
        {
            Object.Destroy(activeBlessingChrome);
            activeBlessingChrome = null;
        }
    }
}
