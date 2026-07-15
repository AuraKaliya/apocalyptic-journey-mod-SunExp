using System;
using Michsky.MUIP;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Events;
using UnityEngine.UI;
using Witch.UI;

namespace AuraUi.Shared;

/// <summary>
/// Safely adopts an existing game-native ButtonManager after its visual tree
/// has been cloned by a consumer. The shared binding owns interaction events
/// while the native component continues to own its normal, highlighted, and
/// disabled presentation.
/// </summary>
public sealed class AuraUiNativeButtonBinding : MonoBehaviour
{
    private ButtonManager? manager;
    private bool ownsLabel;
    private bool disposed;

    public ButtonManager? Manager => manager;

    public bool OwnsLabel => ownsLabel;

    public static bool TryBind(
        ButtonManager? target,
        string? label,
        UnityAction? onClick,
        bool interactable,
        out AuraUiNativeButtonBinding? binding,
        out string failureReason,
        UnityAction? onRightClick = null,
        UnityAction? onHover = null,
        UnityAction? onLeave = null)
    {
        binding = null;
        if (target == null)
        {
            failureReason = "native ButtonManager is missing";
            return false;
        }

        binding = target.GetComponent<AuraUiNativeButtonBinding>()
                  ?? target.gameObject.AddComponent<AuraUiNativeButtonBinding>();
        binding.Configure(
            target,
            label,
            onClick,
            interactable,
            onRightClick,
            onHover,
            onLeave);
        failureReason = "";
        return true;
    }

    public static void NeutralizeTree(GameObject? root, bool disable = true)
    {
        if (root == null)
        {
            return;
        }

        foreach (var target in root.GetComponentsInChildren<ButtonManager>(true))
        {
            ResetEvents(target);
            if (disable)
            {
                target.Interactable(false);
            }

            target.UpdateUI();
        }
    }

    public void SetLabel(string value)
    {
        if (disposed || manager == null || !ownsLabel)
        {
            return;
        }

        manager.SetText(value ?? "");
    }

    public void SetTextColor(Color color)
    {
        if (disposed || manager == null)
        {
            return;
        }

        SetTextColor(manager.normalText, color);
        SetTextColor(manager.highlightedText, color);
        SetTextColor(manager.disabledText, color);
    }

    public void SetInteractable(bool value)
    {
        if (disposed || manager == null)
        {
            return;
        }

        manager.Interactable(value);
        manager.UpdateUI();
    }

    public bool HasValidHitArea()
    {
        if (disposed || manager == null || manager.transform is not RectTransform rect)
        {
            return false;
        }

        return rect.rect.width > 1f && rect.rect.height > 1f;
    }

    public void Unbind()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        if (manager != null)
        {
            ResetEvents(manager);
            manager.Interactable(false);
        }

        manager = null;
        ownsLabel = false;
    }

    private void Configure(
        ButtonManager target,
        string? label,
        UnityAction? onClick,
        bool interactable,
        UnityAction? onRightClick,
        UnityAction? onHover,
        UnityAction? onLeave)
    {
        disposed = false;
        manager = target;
        ownsLabel = label != null;
        ResetEvents(target);
        if (label != null)
        {
            target.enableText = true;
            target.SetText(label);
        }

        AddListener(target.onClick, onClick);
        AddListener(target.onRightClick, onRightClick);
        AddListener(target.onHover, onHover);
        AddListener(target.onLeave, onLeave);
        target.Interactable(interactable);
        target.UpdateUI();
    }

    private static void ResetEvents(ButtonManager target)
    {
        target.onClick = new UnityEvent();
        target.onDoubleClick = new UnityEvent();
        target.onRightClick = new UnityEvent();
        target.onHover = new UnityEvent();
        target.onLeave = new UnityEvent();
        var unityButton = target.GetComponent<Button>();
        if (unityButton != null)
        {
            unityButton.onClick = new Button.ButtonClickedEvent();
        }
    }

    private static void AddListener(UnityEvent target, UnityAction? listener)
    {
        if (listener != null)
        {
            target.AddListener(listener);
        }
    }

    private static void SetTextColor(TMP_Text? text, Color color)
    {
        if (text != null)
        {
            text.color = color;
        }
    }

    private void OnDestroy()
    {
        Unbind();
    }
}

/// <summary>
/// Adopts a cloned native item surface without assigning any game semantics.
/// The native ButtonManager remains responsible for its normal/highlighted/
/// disabled presentation, while AuraUiPointerSurface owns safe callbacks.
/// </summary>
public sealed class AuraUiNativeItemAnchor : MonoBehaviour
{
    [SerializeField]
    private KeywordDisplay? tooltip;

    [SerializeField]
    private ButtonManager? visualManager;

    [SerializeField]
    private string nativeTypeName = "";

    public GameObject EventTarget => gameObject;

    public KeywordDisplay? Tooltip => tooltip;

    public ButtonManager? VisualManager => visualManager;

    public string NativeTypeName => nativeTypeName;

    public static AuraUiNativeItemAnchor Capture(
        GameObject eventTarget,
        KeywordDisplay? tooltip,
        ButtonManager? visualManager,
        string? nativeTypeName)
    {
        if (eventTarget == null)
        {
            throw new ArgumentNullException(nameof(eventTarget));
        }

        var anchor = eventTarget.GetComponent<AuraUiNativeItemAnchor>()
                     ?? eventTarget.AddComponent<AuraUiNativeItemAnchor>();
        anchor.tooltip = tooltip;
        anchor.visualManager = visualManager;
        anchor.nativeTypeName = nativeTypeName ?? "";
        return anchor;
    }

    public static AuraUiNativeItemAnchor? Find(GameObject root, string nativeTypeName)
    {
        if (root == null)
        {
            return null;
        }

        foreach (var anchor in root.GetComponentsInChildren<AuraUiNativeItemAnchor>(true))
        {
            if (string.Equals(anchor.nativeTypeName, nativeTypeName, StringComparison.Ordinal))
            {
                return anchor;
            }
        }

        return null;
    }

    public void EnableTooltip()
    {
        if (tooltip != null)
        {
            tooltip.enabled = true;
        }
    }
}

public sealed class AuraUiNativeItemSurface : MonoBehaviour
{
    private AuraUiNativeButtonBinding? visualBinding;
    private AuraUiPointerSurface? pointerSurface;
    private Action? leftAction;
    private Action? rightAction;
    private int lastLeftFrame = -1;
    private int lastRightFrame = -1;

    public ButtonManager? VisualManager => visualBinding?.Manager;

    public static AuraUiNativeItemSurface Bind(
        AuraUiNativeItemAnchor anchor,
        Action? onLeftClick = null,
        Action? onRightClick = null,
        bool interactable = true)
    {
        if (anchor == null)
        {
            throw new ArgumentNullException(nameof(anchor));
        }

        var target = anchor.EventTarget;
        var surface = target.GetComponent<AuraUiNativeItemSurface>()
                      ?? target.AddComponent<AuraUiNativeItemSurface>();
        surface.leftAction = onLeftClick;
        surface.rightAction = onRightClick;
        surface.lastLeftFrame = -1;
        surface.lastRightFrame = -1;

        AuraUiNativeButtonBinding? binding = null;
        if (anchor.VisualManager != null)
        {
            AuraUiNativeButtonBinding.TryBind(
                anchor.VisualManager,
                label: null,
                onClick: surface.InvokeLeft,
                interactable,
                out binding,
                out _,
                onRightClick: surface.InvokeRight);
        }

        surface.pointerSurface = AuraUiPointerSurface.Bind(
            target,
            onLeftClick: _ => surface.InvokeLeft(),
            onRightClick: _ => surface.InvokeRight(),
            ensureRaycastTarget: true);
        surface.visualBinding = binding;
        anchor.EnableTooltip();
        return surface;
    }

    public static AuraUiNativeItemSurface Bind(
        GameObject target,
        ButtonManager? visualManager = null,
        Action<PointerEventData>? onEnter = null,
        Action<PointerEventData>? onExit = null,
        Action<PointerEventData>? onLeftClick = null,
        Action<PointerEventData>? onRightClick = null,
        bool interactable = true)
    {
        if (target == null)
        {
            throw new ArgumentNullException(nameof(target));
        }

        AuraUiNativeButtonBinding? binding = null;
        if (visualManager != null)
        {
            AuraUiNativeButtonBinding.TryBind(
                visualManager,
                label: null,
                onClick: null,
                interactable,
                out binding,
                out _);
        }

        var pointer = AuraUiPointerSurface.Bind(
            target,
            onEnter,
            onExit,
            onLeftClick,
            onRightClick,
            ensureRaycastTarget: true);
        var surface = target.GetComponent<AuraUiNativeItemSurface>()
                      ?? target.AddComponent<AuraUiNativeItemSurface>();
        surface.visualBinding = binding;
        surface.pointerSurface = pointer;
        surface.leftAction = null;
        surface.rightAction = null;
        return surface;
    }

    public void SetIcon(Sprite? sprite)
    {
        var manager = VisualManager;
        if (manager == null || sprite == null)
        {
            return;
        }

        manager.enableIcon = true;
        manager.SetIcon(sprite);
        manager.UpdateUI();
    }

    public void SetInteractable(bool value)
    {
        visualBinding?.SetInteractable(value);
        if (pointerSurface != null)
        {
            pointerSurface.enabled = value;
        }
    }

    public bool HasValidHitArea()
    {
        return pointerSurface != null
               && pointerSurface.HasValidHitArea()
               && (visualBinding == null || visualBinding.HasValidHitArea());
    }

    private void OnDestroy()
    {
        visualBinding = null;
        pointerSurface = null;
        leftAction = null;
        rightAction = null;
    }

    private void InvokeLeft()
    {
        var frame = Time.frameCount;
        if (lastLeftFrame == frame)
        {
            return;
        }

        lastLeftFrame = frame;
        leftAction?.Invoke();
    }

    private void InvokeRight()
    {
        var frame = Time.frameCount;
        if (lastRightFrame == frame)
        {
            return;
        }

        lastRightFrame = frame;
        rightAction?.Invoke();
    }
}

/// <summary>
/// Semantic-free pointer callbacks for cloned or generated UI surfaces. The
/// consumer owns the action meaning; this component only owns the raycast and
/// event lifecycle.
/// </summary>
public sealed class AuraUiPointerSurface : MonoBehaviour,
    IPointerEnterHandler,
    IPointerExitHandler,
    IPointerClickHandler
{
    private Action<PointerEventData>? entered;
    private Action<PointerEventData>? exited;
    private Action<PointerEventData>? leftClicked;
    private Action<PointerEventData>? rightClicked;

    public static AuraUiPointerSurface Bind(
        GameObject target,
        Action<PointerEventData>? onEnter = null,
        Action<PointerEventData>? onExit = null,
        Action<PointerEventData>? onLeftClick = null,
        Action<PointerEventData>? onRightClick = null,
        bool ensureRaycastTarget = true)
    {
        if (target == null)
        {
            throw new ArgumentNullException(nameof(target));
        }

        if (ensureRaycastTarget)
        {
            EnsureRaycastTarget(target);
        }

        var surface = target.GetComponent<AuraUiPointerSurface>()
                      ?? target.AddComponent<AuraUiPointerSurface>();
        surface.Configure(onEnter, onExit, onLeftClick, onRightClick);
        return surface;
    }

    public static Graphic EnsureRaycastTarget(GameObject target)
    {
        var graphic = target.GetComponent<Graphic>();
        if (graphic == null)
        {
            var image = target.AddComponent<Image>();
            image.color = new Color(0f, 0f, 0f, 0.002f);
            graphic = image;
        }

        graphic.raycastTarget = true;
        return graphic;
    }

    public void Configure(
        Action<PointerEventData>? onEnter,
        Action<PointerEventData>? onExit,
        Action<PointerEventData>? onLeftClick,
        Action<PointerEventData>? onRightClick)
    {
        entered = onEnter;
        exited = onExit;
        leftClicked = onLeftClick;
        rightClicked = onRightClick;
    }

    public bool HasValidHitArea()
    {
        if (transform is not RectTransform rect)
        {
            return false;
        }

        var graphic = GetComponent<Graphic>();
        return graphic != null
               && graphic.raycastTarget
               && rect.rect.width > 1f
               && rect.rect.height > 1f;
    }

    public void OnPointerEnter(PointerEventData eventData)
    {
        entered?.Invoke(eventData);
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        exited?.Invoke(eventData);
    }

    public void OnPointerClick(PointerEventData eventData)
    {
        if (eventData.button == PointerEventData.InputButton.Right)
        {
            rightClicked?.Invoke(eventData);
        }
        else if (eventData.button == PointerEventData.InputButton.Left)
        {
            leftClicked?.Invoke(eventData);
        }
    }

    private void OnDestroy()
    {
        entered = null;
        exited = null;
        leftClicked = null;
        rightClicked = null;
    }
}
