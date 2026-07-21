using System;
using System.Collections.Generic;
using Terrias.Dll.GameApi;
using Terrias.Dll.Infrastructure;
using Terrias.Dll.Mechanics;
using UnityEngine;
using UnityEngine.UI;

namespace Terrias.Dll.Hooks.Ui;

public static class ElementalCrystalPresenter
{
    private const string RegistryKey = "ElementalCrystal";
    private const string CrystalSpritePath = "Mods/Terrias/ModResource/Images/Buff/GenshinImpact/元素-岩";
    private static readonly Dictionary<string, ElementalCrystalIconView> Active = new(StringComparer.Ordinal);
    private static GameObject? root;
    private static RectTransform? iconLayer;
    private static bool initialized;

    public static void Initialize()
    {
        if (initialized)
        {
            return;
        }

        initialized = true;
        ElementalCrystalChallengeService.Spawned += Show;
        ElementalCrystalChallengeService.Resolved += HandleResolution;
        TerriasTransientUiRegistry.Register(RegistryKey, CloseAll);
    }

    public static void CloseAll(string source)
    {
        foreach (var view in Active.Values)
        {
            if (view != null)
            {
                view.DisableInteraction();
            }
        }

        Active.Clear();
        iconLayer = null;
        if (root != null)
        {
            var closing = root;
            root = null;
            TerriasUiSafety.CloseTransient(closing, source, "[ElementalCrystalUi]");
        }
    }

    private static void Show(ElementalCrystalEventSnapshot snapshot)
    {
        if (snapshot == null || string.IsNullOrWhiteSpace(snapshot.EventId) || Active.ContainsKey(snapshot.EventId))
        {
            return;
        }

        EnsureRoot();
        if (iconLayer == null)
        {
            return;
        }

        var rect = TerriasUiComponents.CreateRectTransform(
            "Crystal-" + snapshot.EventId,
            iconLayer,
            new Vector2(0.5f, 0.5f),
            new Vector2(0.5f, 0.5f),
            new Vector2(0.5f, 0.5f),
            new Vector2(104f, 104f));
        rect.anchoredPosition = PositionFor(snapshot.EventId);

        var image = rect.gameObject.AddComponent<Image>();
        image.sprite = TerriasResourceCache.Load<Sprite>(CrystalSpritePath, true, "elemental.crystal-ui");
        image.type = Image.Type.Simple;
        image.preserveAspect = true;
        image.color = image.sprite == null ? new Color(0.95f, 0.72f, 0.16f, 0.95f) : Color.white;
        image.raycastTarget = true;

        var button = rect.gameObject.AddComponent<Button>();
        button.targetGraphic = image;
        var colors = button.colors;
        colors.normalColor = Color.white;
        colors.highlightedColor = new Color(1f, 0.92f, 0.55f, 1f);
        colors.pressedColor = new Color(1f, 0.72f, 0.2f, 1f);
        button.colors = colors;

        var timerRect = TerriasUiComponents.CreateRectTransform(
            "Timer",
            rect,
            new Vector2(0.5f, 0f),
            new Vector2(0.5f, 0f),
            new Vector2(0.5f, 1f),
            new Vector2(112f, 28f));
        timerRect.anchoredPosition = new Vector2(0f, -5f);
        var timer = TerriasUiComponents.ConfigureText(
            timerRect.gameObject,
            "",
            19,
            TextAnchor.MiddleCenter,
            new Color(1f, 0.92f, 0.58f, 1f));
        timer.raycastTarget = false;

        var view = rect.gameObject.AddComponent<ElementalCrystalIconView>();
        view.Configure(snapshot, button, timer, () =>
        {
            var localStatusId = FightPlayer.Instance?.Status?.InstanceId ?? "";
            ElementalCrystalChallengeService.RequestLocalClaim(snapshot.EventId, localStatusId);
        });
        Active[snapshot.EventId] = view;
    }

    private static void HandleResolution(ElementalCrystalResolutionSnapshot resolution)
    {
        if (resolution == null)
        {
            return;
        }

        if (resolution.EventId == "*")
        {
            CloseAll("ElementalCrystalPresenter.BattleClear");
            return;
        }

        if (!Active.TryGetValue(resolution.EventId, out var view))
        {
            return;
        }

        Active.Remove(resolution.EventId);
        view.DisableInteraction();
        UnityEngine.Object.Destroy(view.gameObject);
        if (Active.Count == 0)
        {
            CloseAll("ElementalCrystalPresenter.Empty");
        }
    }

    private static void EnsureRoot()
    {
        if (root != null && iconLayer != null)
        {
            return;
        }

        root = new GameObject(
            TerriasIds.ElementalCrystalUiRoot,
            typeof(RectTransform),
            typeof(Canvas),
            typeof(CanvasScaler),
            typeof(GraphicRaycaster),
            typeof(CanvasGroup));
        var canvas = root.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 32740;

        var scaler = root.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;

        var group = root.GetComponent<CanvasGroup>();
        group.alpha = 1f;
        group.interactable = true;
        group.blocksRaycasts = true;

        iconLayer = TerriasUiComponents.CreateRectTransform(
            "CentralCrystalRegion",
            root.transform,
            new Vector2(0.5f, 0.5f),
            new Vector2(0.5f, 0.5f),
            new Vector2(0.5f, 0.5f),
            new Vector2(620f, 380f));
    }

    private static Vector2 PositionFor(string eventId)
    {
        unchecked
        {
            var hash = 17;
            foreach (var ch in eventId ?? "")
            {
                hash = hash * 31 + ch;
            }

            var positive = hash & 0x7fffffff;
            var x = positive % 481 - 240;
            var y = positive / 481 % 241 - 120;
            return new Vector2(x, y);
        }
    }
}

public sealed class ElementalCrystalIconView : MonoBehaviour
{
    private ElementalCrystalEventSnapshot? snapshot;
    private Button? button;
    private Text? timer;
    private Action? claim;
    private Vector3 baseScale;

    public void Configure(ElementalCrystalEventSnapshot value, Button ownerButton, Text timerText, Action claimAction)
    {
        snapshot = value;
        button = ownerButton;
        timer = timerText;
        claim = claimAction;
        baseScale = transform.localScale;
        button.onClick.AddListener(Claim);
        Refresh();
    }

    public void DisableInteraction()
    {
        if (button != null)
        {
            button.interactable = false;
            button.onClick.RemoveListener(Claim);
        }
    }

    private void Update()
    {
        Refresh();
        var pulse = 1f + Mathf.Sin(Time.unscaledTime * 7f) * 0.045f;
        transform.localScale = baseScale * pulse;
    }

    private void Claim()
    {
        DisableInteraction();
        claim?.Invoke();
    }

    private void Refresh()
    {
        if (snapshot == null)
        {
            return;
        }

        var remaining = Math.Max(0d, (snapshot.ExpiresAtUnixMilliseconds - ElementalCrystalChallengeService.NowUnixMilliseconds) / 1000d);
        if (timer != null)
        {
            timer.text = remaining.ToString("0.0") + "s";
        }

        if (remaining <= 0d)
        {
            DisableInteraction();
        }
    }

    private void OnDestroy()
    {
        DisableInteraction();
    }
}
