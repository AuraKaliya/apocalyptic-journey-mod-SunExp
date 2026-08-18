using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace AuraToolsExp.Dll.Features.Settings;

internal sealed class ToolboxTooltipTrigger : MonoBehaviour,
    IPointerEnterHandler,
    IPointerExitHandler
{
    private string message = "";
    private ToolboxTooltipHost? host;

    internal static ToolboxTooltipTrigger Attach(GameObject target, string message)
    {
        var trigger = target.GetComponent<ToolboxTooltipTrigger>()
                      ?? target.AddComponent<ToolboxTooltipTrigger>();
        trigger.message = message ?? "";
        return trigger;
    }

    public void OnPointerEnter(PointerEventData eventData)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return;
        }

        var canvas = GetComponentInParent<Canvas>();
        if (canvas == null)
        {
            return;
        }

        host = canvas.GetComponent<ToolboxTooltipHost>()
               ?? canvas.gameObject.AddComponent<ToolboxTooltipHost>();
        host.Show(this, transform as RectTransform, message);
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        host?.Hide(this);
    }

    private void OnDisable()
    {
        host?.Hide(this);
    }

    private void OnDestroy()
    {
        host?.Hide(this);
    }
}

internal sealed class ToolboxTooltipHost : MonoBehaviour
{
    private const float TooltipHeight = 36f;
    private const float MinimumTooltipWidth = 52f;
    private const float MaximumTooltipWidth = 220f;
    private const float HorizontalPadding = 20f;
    private RectTransform? layerRect;
    private RectTransform? bubbleRect;
    private TextMeshProUGUI? label;
    private CanvasGroup? layerGroup;
    private ToolboxTooltipTrigger? owner;

    internal void Show(ToolboxTooltipTrigger trigger, RectTransform? anchor, string message)
    {
        if (anchor == null || string.IsNullOrWhiteSpace(message))
        {
            Hide(trigger);
            return;
        }

        EnsureView();
        if (layerRect == null || bubbleRect == null || label == null || layerGroup == null)
        {
            return;
        }

        owner = trigger;
        label.text = message.Trim();
        Canvas.ForceUpdateCanvases();
        var preferred = label.GetPreferredValues(label.text, MaximumTooltipWidth - HorizontalPadding, TooltipHeight);
        var width = Mathf.Clamp(preferred.x + HorizontalPadding, MinimumTooltipWidth, MaximumTooltipWidth);
        bubbleRect.sizeDelta = new Vector2(width, TooltipHeight);

        var camera = ResolveEventCamera();
        var corners = new Vector3[4];
        anchor.GetWorldCorners(corners);
        var minimum = ScreenToLayer(RectTransformUtility.WorldToScreenPoint(camera, corners[0]), camera);
        var maximum = ScreenToLayer(RectTransformUtility.WorldToScreenPoint(camera, corners[2]), camera);
        var container = layerRect.rect;
        var placement = ToolboxTooltipPlacementPolicy.Resolve(
            new ToolboxTooltipBounds(container.xMin, container.yMin, container.xMax, container.yMax),
            new ToolboxTooltipBounds(minimum.x, minimum.y, maximum.x, maximum.y),
            width,
            TooltipHeight);
        bubbleRect.anchoredPosition = new Vector2(placement.CenterX, placement.CenterY);
        bubbleRect.gameObject.SetActive(true);
        layerGroup.alpha = 1f;
        layerRect.SetAsLastSibling();
    }

    internal void Hide(ToolboxTooltipTrigger trigger)
    {
        if (owner != trigger)
        {
            return;
        }

        owner = null;
        if (bubbleRect != null)
        {
            bubbleRect.gameObject.SetActive(false);
        }
        if (layerGroup != null)
        {
            layerGroup.alpha = 0f;
        }
    }

    private void OnDisable()
    {
        owner = null;
        if (bubbleRect != null)
        {
            bubbleRect.gameObject.SetActive(false);
        }
    }

    private void EnsureView()
    {
        if (layerRect != null && bubbleRect != null && label != null && layerGroup != null)
        {
            RefreshSorting();
            return;
        }

        var layer = AuraToolsUi.CreateRect(
            "ToolboxTooltipLayer",
            transform,
            Vector2.zero,
            Vector2.one,
            new Vector2(0.5f, 0.5f),
            Vector2.zero);
        layerRect = layer.GetComponent<RectTransform>();
        layerRect.offsetMin = Vector2.zero;
        layerRect.offsetMax = Vector2.zero;
        AuraToolsUi.EnsureLayoutElement(layer).ignoreLayout = true;
        layerGroup = layer.AddComponent<CanvasGroup>();
        layerGroup.alpha = 0f;
        layerGroup.interactable = false;
        layerGroup.blocksRaycasts = false;
        var tooltipCanvas = layer.AddComponent<Canvas>();
        tooltipCanvas.overrideSorting = true;

        var bubble = AuraToolsUi.CreateRect(
            "Tooltip",
            layer.transform,
            new Vector2(0.5f, 0.5f),
            new Vector2(0.5f, 0.5f),
            new Vector2(0.5f, 0.5f),
            new Vector2(MinimumTooltipWidth, TooltipHeight));
        bubbleRect = bubble.GetComponent<RectTransform>();
        var background = ToolboxSurfaceV2.ApplyControl(bubble);
        background.raycastTarget = false;

        var textRoot = AuraToolsUi.CreateRect(
            "Label",
            bubble.transform,
            Vector2.zero,
            Vector2.one,
            Vector2.zero,
            Vector2.zero);
        var textRect = textRoot.GetComponent<RectTransform>();
        textRect.offsetMin = new Vector2(10f, 3f);
        textRect.offsetMax = new Vector2(-10f, -3f);
        label = AuraToolsUi.AddTmpFillText(
            textRoot.transform,
            "",
            ToolboxVisualSpec.DescriptionSize,
            TextAnchor.MiddleCenter,
            ToolboxVisualSpec.Text,
            true);
        label.raycastTarget = false;
        label.textWrappingMode = TextWrappingModes.NoWrap;
        bubble.SetActive(false);
        RefreshSorting();
    }

    private void RefreshSorting()
    {
        if (layerRect == null)
        {
            return;
        }

        var parentCanvas = GetComponent<Canvas>();
        var tooltipCanvas = layerRect.GetComponent<Canvas>();
        if (parentCanvas == null || tooltipCanvas == null)
        {
            return;
        }

        tooltipCanvas.sortingLayerID = parentCanvas.sortingLayerID;
        tooltipCanvas.sortingOrder = Mathf.Clamp(parentCanvas.sortingOrder + 100, short.MinValue, short.MaxValue);
    }

    private Camera? ResolveEventCamera()
    {
        var canvas = GetComponent<Canvas>();
        return canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay
            ? canvas.worldCamera
            : null;
    }

    private Vector2 ScreenToLayer(Vector2 screenPoint, Camera? camera)
    {
        if (layerRect != null
            && RectTransformUtility.ScreenPointToLocalPointInRectangle(layerRect, screenPoint, camera, out var local))
        {
            return local;
        }

        return Vector2.zero;
    }
}
