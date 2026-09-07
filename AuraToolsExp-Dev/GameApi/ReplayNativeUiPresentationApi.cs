using System;
using TMPro;
using UnityEngine.UI;
using Witch.UI;
using Witch.UI.Component;

namespace AuraToolsExp.Dll.GameApi;

internal static class ReplayNativeUiPresentationApi
{
    internal static void ConfigureCanvas(CanvasScaler target)
    {
        var source = UIManager.Instance?.canvasTf?.GetComponent<CanvasScaler>()
                     ?? throw new InvalidOperationException("The native UI canvas scale contract is unavailable.");
        target.uiScaleMode = source.uiScaleMode;
        target.referenceResolution = source.referenceResolution;
        target.screenMatchMode = source.screenMatchMode;
        target.matchWidthOrHeight = source.matchWidthOrHeight;
        target.referencePixelsPerUnit = source.referencePixelsPerUnit;
        target.scaleFactor = source.scaleFactor;
        target.physicalUnit = source.physicalUnit;
        target.fallbackScreenDPI = source.fallbackScreenDPI;
        target.defaultSpriteDPI = source.defaultSpriteDPI;
        target.dynamicPixelsPerUnit = source.dynamicPixelsPerUnit;
        var sourceCanvas = source.GetComponent<UnityEngine.Canvas>();
        var targetCanvas = target.GetComponent<UnityEngine.Canvas>();
        if (sourceCanvas != null && targetCanvas != null)
            targetCanvas.additionalShaderChannels = sourceCanvas.additionalShaderChannels;
        // Apply the copied scale before the first recorded card pose, rather
        // than waiting for the scaler's next Update after replay preflight.
        target.enabled = false;
        target.enabled = true;
    }

    internal static void SetDigitText(TMP_Text? target, string value)
    {
        if (target != null) target.SetDigitText(value ?? "");
    }
}
