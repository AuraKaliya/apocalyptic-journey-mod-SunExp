using SunExp.Dll.Infrastructure;
using UnityEngine;
using UnityEngine.UI;
using Witch.UI;

namespace SunExp.Dll.Hooks.Ui;

public static class SunExpModalHost
{
    public static Transform? ModalParent()
    {
        return UIManager.Instance?.upperCanvasTf ?? UIManager.Instance?.canvasTf;
    }

    public static GameObject? CreateFullscreenRoot(string name, Color blockerColor)
    {
        var parent = ModalParent();
        if (parent == null)
        {
            SunExpLog.Warn("[SunExpUi] modal parent unavailable for " + name + ".");
            return null;
        }

        return CreateFullscreenRoot(name, parent, blockerColor);
    }

    public static GameObject CreateFullscreenRoot(string name, Transform parent, Color blockerColor)
    {
        var root = SunExpUiBuilder.CreateRect(name, parent, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero).gameObject;
        root.transform.SetAsLastSibling();
        var blocker = root.AddComponent<Image>();
        blocker.color = blockerColor;
        blocker.raycastTarget = true;
        return root;
    }

    public static bool Close(ref GameObject? root, string source, string logPrefix)
    {
        var closed = SunExpUiSafety.CloseTransient(root, source, logPrefix);
        root = null;
        return closed;
    }
}
