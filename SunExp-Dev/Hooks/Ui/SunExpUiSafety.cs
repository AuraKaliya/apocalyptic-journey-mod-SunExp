using SunExp.Dll.Infrastructure;
using UiRaycastSafetyShared;
using UnityEngine;
using Object = UnityEngine.Object;

namespace SunExp.Dll.Hooks.Ui;

public static class SunExpUiSafety
{
    public static bool CloseTransient(GameObject? root, string source, string logPrefix)
    {
        if (root == null)
        {
            return false;
        }

        var rootName = root.name;
        UiRaycastSafeDestroyRuntime.DisableAndHide(root, source, SunExpLog.Debug);
        DestroyChildren(root.transform, source + ":children", logPrefix);
        UiRaycastSafeDestroyRuntime.ScrubGraphicRegistryForFrames(4, source + ":close", SunExpLog.Debug);
        Object.Destroy(root);
        SunExpLog.Debug(logPrefix + " closed transient UI " + rootName + " from " + source + ".");
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
            UiRaycastSafeDestroyRuntime.DisableAndHide(go, source, SunExpLog.Debug);
            Object.Destroy(go);
        }

        UiRaycastSafeDestroyRuntime.ScrubGraphicRegistryForFrames(2, source + ":children", SunExpLog.Debug);
        SunExpLog.Debug(logPrefix + " cleared transient UI children from " + source + ".");
    }
}
