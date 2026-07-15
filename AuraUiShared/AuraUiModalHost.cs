using System;
using UiRaycastSafetyShared;
using UnityEngine;
using UnityEngine.UI;
using Witch.UI;

namespace AuraUi.Shared;

public static class AuraUiModalHost
{
    public static Transform? ModalParent()
    {
        return UIManager.Instance?.upperCanvasTf ?? UIManager.Instance?.canvasTf;
    }

    /// <summary>
    /// Resolves the same UI plane used by the game's ordinary UI screens,
    /// Tooltip, and Floating Window. Native screens that rely on those global
    /// overlays must use this parent instead of the upper modal canvas.
    /// </summary>
    public static Transform? NativeUiParent()
    {
        return UIManager.Instance?.canvasTf;
    }

    public static GameObject? CreateFullscreenRoot(string name, Color blockerColor, Action<string>? warn = null)
    {
        var parent = ModalParent();
        if (parent == null)
        {
            warn?.Invoke("[AuraUi] modal parent unavailable for " + name + ".");
            return null;
        }

        return CreateFullscreenRoot(name, parent, blockerColor);
    }

    public static GameObject? CreateNativeFullscreenRoot(
        string name,
        Color blockerColor,
        Action<string>? warn = null)
    {
        var parent = NativeUiParent();
        if (parent == null)
        {
            warn?.Invoke("[AuraUi] native UI parent unavailable for " + name + ".");
            return null;
        }

        return CreateFullscreenRoot(name, parent, blockerColor);
    }

    public static GameObject CreateFullscreenRoot(string name, Transform parent, Color blockerColor)
    {
        var root = AuraUiComponents.CreateRect(name, parent, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
        root.transform.SetAsLastSibling();
        var blocker = root.AddComponent<Image>();
        blocker.color = blockerColor;
        blocker.raycastTarget = true;
        return root;
    }

    public static bool Close(ref GameObject? root, string source, Action<string>? debug = null)
    {
        var current = root;
        root = null;
        if (current == null)
        {
            return false;
        }

        UiRaycastSafeDestroyRuntime.DisableAndHide(current, source, debug);
        UnityEngine.Object.Destroy(current);
        UiRaycastSafeDestroyRuntime.ScrubGraphicRegistryForFrames(2, source, debug);
        return true;
    }
}
