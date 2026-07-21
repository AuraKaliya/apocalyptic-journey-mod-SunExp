using AuraUi.Shared;
using Terrias.Dll.Infrastructure;
using UnityEngine;

namespace Terrias.Dll.Hooks.Ui;

public static class TerriasModalHost
{
    public static Transform? ModalParent()
    {
        return AuraUiModalHost.ModalParent();
    }

    public static Transform? NativeUiParent()
    {
        return AuraUiModalHost.NativeUiParent();
    }

    public static GameObject? CreateFullscreenRoot(string name, Color blockerColor)
    {
        return AuraUiModalHost.CreateFullscreenRoot(name, blockerColor, TerriasLog.Warn);
    }

    public static GameObject? CreateNativeFullscreenRoot(string name, Color blockerColor)
    {
        return AuraUiModalHost.CreateNativeFullscreenRoot(name, blockerColor, TerriasLog.Warn);
    }

    public static GameObject CreateFullscreenRoot(string name, Transform parent, Color blockerColor)
    {
        return AuraUiModalHost.CreateFullscreenRoot(name, parent, blockerColor);
    }

    public static bool Close(ref GameObject? root, string source, string logPrefix)
    {
        var closed = TerriasUiSafety.CloseTransient(root, source, logPrefix);
        root = null;
        return closed;
    }
}
