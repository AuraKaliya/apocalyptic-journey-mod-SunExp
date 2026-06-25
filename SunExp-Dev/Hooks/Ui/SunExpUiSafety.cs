using SunExp.Dll.Infrastructure;
using UiRaycastSafetyShared;
using UnityEngine;

namespace SunExp.Dll.Hooks.Ui;

public static class SunExpUiSafety
{
    public static bool DisableRaycastsAndDestroyByName(string objectName, string source, string logPrefix)
    {
        var root = GameObject.Find(objectName);
        if (root == null)
        {
            return false;
        }

        UiRaycastSafeDestroyRuntime.DisableRaycasts(root, source, SunExpLog.Debug);
        Object.Destroy(root);
        SunExpLog.Debug(logPrefix + " closed transient UI " + objectName + " from " + source + ".");
        return true;
    }
}
