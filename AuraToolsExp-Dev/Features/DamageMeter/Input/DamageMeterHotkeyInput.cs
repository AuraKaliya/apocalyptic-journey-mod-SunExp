using System;
using TMPro;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace AuraToolsExp.Dll.Features.DamageMeter.Input;

internal static class DamageMeterHotkeyInput
{
    private static readonly DamageMeterInputFaultGate FaultGate = new();
    private static Key configuredKey = Key.F8;

    public static bool Configure(string? value, out string canonical)
    {
        var valid = DamageMeterHotkeyNames.TryNormalize(value, out canonical)
                    && Enum.TryParse(canonical, true, out configuredKey)
                    && configuredKey != Key.None;
        if (!valid)
        {
            canonical = "F8";
            configuredKey = Key.F8;
        }

        FaultGate.Reset();
        return valid;
    }

    public static bool WasPressedThisFrame(Action<Exception> onFirstFailure)
    {
        return FaultGate.TryPoll(() =>
        {
            if (IsTextInputFocused())
            {
                return false;
            }

            var keyboard = Keyboard.current;
            return keyboard != null && keyboard[configuredKey].wasPressedThisFrame;
        }, onFirstFailure);
    }

    private static bool IsTextInputFocused()
    {
        var selected = EventSystem.current?.currentSelectedGameObject;
        if (selected == null)
        {
            return false;
        }

        var legacyInput = selected.GetComponent<InputField>();
        if (legacyInput != null && legacyInput.isFocused)
        {
            return true;
        }

        var tmpInput = selected.GetComponent<TMP_InputField>();
        return tmpInput != null && tmpInput.isFocused;
    }
}
