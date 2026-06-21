using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using AuraSkin.Shared.Mechanics;
using AuraSkin.Shared.Models;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Witch.UI.Window;

namespace AuraSkin.Shared.Hooks;

public sealed class SkinPanelController : MonoBehaviour
{
    private static readonly Color PanelColor = new(0.025f, 0.035f, 0.10f, 0.97f);
    private static readonly Color BorderColor = new(0.83f, 0.67f, 0.28f, 1f);
    private static readonly Color ButtonColor = new(0.08f, 0.12f, 0.28f, 0.98f);
    private static readonly Color TextColor = new(0.95f, 0.91f, 0.76f, 1f);

    private GameEntryUI? entry;
    private TMP_FontAsset? font;
    private GameObject? drawer;
    private Button? triggerButton;
    private TextMeshProUGUI? triggerText;
    private TextMeshProUGUI? nameText;
    private TextMeshProUGUI? authorText;
    private TextMeshProUGUI? countText;
    private TextMeshProUGUI? hintText;
    private Image? previewImage;
    private Button? previousButton;
    private Button? nextButton;
    private IReadOnlyList<SkinDefinition> skins = Array.Empty<SkinDefinition>();
    private DataConfig? career;
    private bool refreshQueued;

    public void Initialize(GameEntryUI gameEntry)
    {
        entry = gameEntry;
        font = gameEntry.GetComponentInChildren<TMP_Text>(true)?.font;
        BuildUi();
        RefreshState();
    }

    public void RefreshState()
    {
        if (entry == null)
        {
            return;
        }

        career = ResolveCurrentCareer();
        var active = career != null;
        if (triggerButton != null)
        {
            triggerButton.gameObject.SetActive(active);
        }

        if (!active || career == null)
        {
            drawer?.SetActive(false);
            return;
        }

        var careerId = SkinRuntime.CareerId(career);
        skins = SkinRuntime.GetSkins(careerId);
        var selectedId = SkinRuntime.GetSelectedSkinId(careerId);
        var selectedIndex = 0;
        for (var i = 0; i < skins.Count; i++)
        {
            if (string.Equals(skins[i].SkinId, selectedId, StringComparison.OrdinalIgnoreCase))
            {
                selectedIndex = i + 1;
                break;
            }
        }

        var total = skins.Count + 1;
        var selected = selectedIndex == 0 ? null : skins[selectedIndex - 1];
        if (triggerText != null)
        {
            triggerText.text = "皮肤\n" + (selectedIndex + 1) + "/" + total;
        }

        if (nameText != null)
        {
            nameText.text = selected?.Name ?? "默认皮肤";
        }

        if (authorText != null)
        {
            authorText.text = selected == null
                ? "官方 / 角色 MOD 原始资源"
                : (string.IsNullOrWhiteSpace(selected.Author) ? "作者：未注明" : "作者：" + selected.Author);
        }

        if (countText != null)
        {
            countText.text = (selectedIndex + 1) + " / " + total;
        }

        if (hintText != null)
        {
            hintText.text = skins.Count == 0 ? "未发现该角色的额外皮肤包" : "切换会立即生效，并保存在本机";
        }

        if (previewImage != null)
        {
            previewImage.sprite = SkinRuntime.LoadPreview(career);
            previewImage.enabled = previewImage.sprite != null;
        }

        if (previousButton != null)
        {
            previousButton.interactable = total > 1;
        }

        if (nextButton != null)
        {
            nextButton.interactable = total > 1;
        }
    }

    public void QueueRefresh()
    {
        if (!refreshQueued && gameObject.activeInHierarchy)
        {
            StartCoroutine(RefreshAfterFrame());
        }
    }

    private void BuildUi()
    {
        triggerButton = AddButton(transform, "SkinTrigger", new Vector2(62f, 146f), new Vector2(-42f, 0f),
            new Vector2(1f, 0.5f), ToggleDrawer, out triggerText, "皮肤\n1/1", 19);

        drawer = AddImageObject(transform, "SkinDrawer", new Vector2(380f, 390f), new Vector2(-265f, 0f),
            new Vector2(1f, 0.5f), PanelColor);
        AddOutline(drawer, BorderColor, 2f);

        AddText(drawer.transform, "Title", "角色皮肤", 26, TextAnchor.MiddleLeft,
            new Vector2(270f, 42f), new Vector2(-38f, 166f), TextColor);
        AddButton(drawer.transform, "Close", new Vector2(42f, 42f), new Vector2(164f, 166f),
            new Vector2(0.5f, 0.5f), () => drawer.SetActive(false), out _, "×", 27);

        var preview = AddImageObject(drawer.transform, "Preview", new Vector2(184f, 232f), new Vector2(-83f, 18f),
            new Vector2(0.5f, 0.5f), new Color(0.02f, 0.025f, 0.07f, 0.95f));
        AddOutline(preview, new Color(0.35f, 0.42f, 0.65f, 0.9f), 1f);
        previewImage = preview.GetComponent<Image>();
        previewImage.preserveAspect = true;

        nameText = AddText(drawer.transform, "SkinName", "默认皮肤", 23, TextAnchor.MiddleCenter,
            new Vector2(165f, 50f), new Vector2(93f, 92f), TextColor);
        authorText = AddText(drawer.transform, "Author", "", 15, TextAnchor.UpperCenter,
            new Vector2(165f, 56f), new Vector2(93f, 48f), new Color(0.78f, 0.82f, 0.92f, 1f));

        previousButton = AddButton(drawer.transform, "Previous", new Vector2(52f, 52f), new Vector2(28f, -32f),
            new Vector2(0.5f, 0.5f), () => Move(-1), out _, "‹", 34);
        nextButton = AddButton(drawer.transform, "Next", new Vector2(52f, 52f), new Vector2(158f, -32f),
            new Vector2(0.5f, 0.5f), () => Move(1), out _, "›", 34);
        countText = AddText(drawer.transform, "Count", "1 / 1", 18, TextAnchor.MiddleCenter,
            new Vector2(80f, 45f), new Vector2(93f, -32f), TextColor);

        AddButton(drawer.transform, "Default", new Vector2(165f, 48f), new Vector2(93f, -98f),
            new Vector2(0.5f, 0.5f), SelectDefault, out _, "恢复默认", 18);
        hintText = AddText(drawer.transform, "Hint", "", 14, TextAnchor.MiddleCenter,
            new Vector2(340f, 42f), new Vector2(0f, -164f), new Color(0.70f, 0.76f, 0.88f, 1f));
        drawer.SetActive(false);
    }

    private IEnumerator RefreshAfterFrame()
    {
        refreshQueued = true;
        yield return new WaitForEndOfFrame();
        RefreshState();
        transform.SetAsLastSibling();
        refreshQueued = false;
    }

    private DataConfig? ResolveCurrentCareer()
    {
        if (GameEntryUI.career != null)
        {
            return GameEntryUI.career;
        }

        if (RoleTable.Instance?.Career != null)
        {
            return RoleTable.Instance.Career;
        }

        if (entry?.showCareers != null)
        {
            foreach (var showCareer in entry.showCareers)
            {
                if (showCareer == null)
                {
                    continue;
                }

                var back = showCareer.transform.Find("Back");
                if (back != null && back.gameObject.activeSelf && showCareer.dataConfig != null)
                {
                    return showCareer.dataConfig;
                }
            }

            var first = entry.showCareers.FirstOrDefault(showCareer => showCareer?.dataConfig != null);
            if (first != null)
            {
                return first.dataConfig;
            }
        }

        return null;
    }

    private void ToggleDrawer()
    {
        if (drawer == null)
        {
            return;
        }

        drawer.SetActive(!drawer.activeSelf);
        drawer.transform.SetAsLastSibling();
        if (drawer.activeSelf)
        {
            RefreshState();
        }
    }

    private void Move(int delta)
    {
        if (career == null)
        {
            return;
        }

        var selectedId = SkinRuntime.GetSelectedSkinId(SkinRuntime.CareerId(career));
        var index = 0;
        for (var i = 0; i < skins.Count; i++)
        {
            if (string.Equals(skins[i].SkinId, selectedId, StringComparison.OrdinalIgnoreCase))
            {
                index = i + 1;
                break;
            }
        }

        var total = skins.Count + 1;
        var next = (index + delta + total) % total;
        ApplySelection(next == 0 ? "" : skins[next - 1].SkinId);
    }

    private void SelectDefault()
    {
        ApplySelection("");
    }

    private void ApplySelection(string skinId)
    {
        if (entry == null || career == null)
        {
            return;
        }

        SkinRuntime.Select(career, skinId);
        SkinUiRuntime.RefreshEntryVisuals(entry, career);
        RefreshState();
    }

    private Button AddButton(Transform parent, string name, Vector2 size, Vector2 position, Vector2 anchor,
        Action action, out TextMeshProUGUI label, string text, int fontSize)
    {
        var gameObject = AddImageObject(parent, name, size, position, anchor, ButtonColor);
        AddOutline(gameObject, BorderColor, 1f);
        var button = gameObject.AddComponent<Button>();
        button.targetGraphic = gameObject.GetComponent<Image>();
        button.onClick.AddListener(() => action());
        label = AddText(gameObject.transform, "Text", text, fontSize, TextAnchor.MiddleCenter,
            size - new Vector2(8f, 8f), Vector2.zero, TextColor);
        return button;
    }

    private TextMeshProUGUI AddText(Transform parent, string name, string value, int fontSize, TextAnchor anchor,
        Vector2 size, Vector2 position, Color color)
    {
        var gameObject = new GameObject(name, typeof(RectTransform));
        gameObject.transform.SetParent(parent, false);
        SetRect((RectTransform)gameObject.transform, size, position, new Vector2(0.5f, 0.5f));
        var text = gameObject.AddComponent<TextMeshProUGUI>();
        if (font != null)
        {
            text.font = font;
        }

        text.text = value;
        text.fontSize = fontSize;
        text.color = color;
        text.alignment = ToTmpAlignment(anchor);
        text.raycastTarget = false;
        return text;
    }

    private static GameObject AddImageObject(Transform parent, string name, Vector2 size, Vector2 position,
        Vector2 anchor, Color color)
    {
        var gameObject = new GameObject(name, typeof(RectTransform));
        gameObject.transform.SetParent(parent, false);
        SetRect((RectTransform)gameObject.transform, size, position, anchor);
        var image = gameObject.AddComponent<Image>();
        image.color = color;
        return gameObject;
    }

    private static void AddOutline(GameObject gameObject, Color color, float distance)
    {
        var outline = gameObject.AddComponent<Outline>();
        outline.effectColor = color;
        outline.effectDistance = new Vector2(distance, -distance);
    }

    private static void SetRect(RectTransform rect, Vector2 size, Vector2 position, Vector2 anchor)
    {
        rect.anchorMin = anchor;
        rect.anchorMax = anchor;
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.sizeDelta = size;
        rect.anchoredPosition = position;
    }

    private static TextAlignmentOptions ToTmpAlignment(TextAnchor anchor)
    {
        return anchor switch
        {
            TextAnchor.MiddleLeft => TextAlignmentOptions.MidlineLeft,
            TextAnchor.UpperCenter => TextAlignmentOptions.Top,
            _ => TextAlignmentOptions.Center
        };
    }
}
