using Terrias.Dll.Infrastructure;
using UiRaycastSafetyShared;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Terrias.Dll.Hooks.Ui;

public static class TerriasUiSafety
{
    public static bool CloseTransient(GameObject? root, string source, string logPrefix)
    {
        if (root == null)
        {
            return false;
        }

        var rootName = root.name;
        UiRaycastSafeDestroyRuntime.DisableAndHide(root, source, TerriasLog.Debug);
        DestroyChildren(root.transform, source + ":children", logPrefix);
        UiRaycastSafeDestroyRuntime.ScrubGraphicRegistryForFrames(4, source + ":close", TerriasLog.Debug);
        Object.Destroy(root);
        TerriasLog.Debug(logPrefix + " closed transient UI " + rootName + " from " + source + ".");
        return true;
    }

    public static bool DisableRaycastsAndDestroyByName(string objectName, string source, string logPrefix)
    {
        var root = GameObject.Find(objectName);
        return CloseTransient(root, source, logPrefix);
    }

    public static void DestroyChildren(Transform? parent, string source, string logPrefix)
    {
        if (parent == null)
        {
            return;
        }

        for (var i = parent.childCount - 1; i >= 0; i--)
        {
            var child = parent.GetChild(i);
            if (child == null)
            {
                continue;
            }

            var go = child.gameObject;
            UiRaycastSafeDestroyRuntime.DisableAndHide(go, source, TerriasLog.Debug);
            Object.Destroy(go);
        }

        UiRaycastSafeDestroyRuntime.ScrubGraphicRegistryForFrames(2, source + ":children", TerriasLog.Debug);
        TerriasLog.Debug(logPrefix + " cleared transient UI children from " + source + ".");
    }
}
