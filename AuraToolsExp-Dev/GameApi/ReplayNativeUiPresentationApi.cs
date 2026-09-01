using TMPro;
using Witch.UI.Component;

namespace AuraToolsExp.Dll.GameApi;

internal static class ReplayNativeUiPresentationApi
{
    internal static void SetDigitText(TMP_Text? target, string value)
    {
        if (target != null) target.SetDigitText(value ?? "");
    }
}
