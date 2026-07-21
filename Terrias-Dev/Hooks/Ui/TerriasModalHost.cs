using AuraUi.Shared;
using SunExp.Dll.Infrastructure;
using UnityEngine;

namespace SunExp.Dll.Hooks.Ui;

public static class SunExpModalHost
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
        return AuraUiModalHost.CreateFullscreenRoot(name, blockerColor, SunExpLog.Warn);
    }

    public static GameObject? CreateNativeFullscreenRoot(string name, Color blockerColor)
    {
        return AuraUiModalHost.CreateNativeFullscreenRoot(name, blockerColor, SunExpLog.Warn);
    }

    public static GameObject CreateFullscreenRoot(string name, Transform parent, Color blockerColor)
    {
        return AuraUiModalHost.CreateFullscreenRoot(name, parent, blockerColor);
    }

    public static bool Close(ref GameObject? root, string source, string logPrefix)
    {
        var closed = SunExpUiSafety.CloseTransient(root, source, logPrefix);
        root = null;
        return closed;
    }
}
