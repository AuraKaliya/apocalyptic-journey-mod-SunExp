using System;
using Terrias.Dll.Infrastructure;
using UnityEngine.InputSystem;

namespace Terrias.Dll.GameApi;

public enum TerriasKeyboardKey
{
    Escape,
    Q,
    E,
    W,
    S,
    LeftArrow,
    RightArrow,
    UpArrow,
    DownArrow
}

public static class KeyboardInputApi
{
    private static bool disabled;

    public static bool WasPressedThisFrame(TerriasKeyboardKey key)
    {
        if (disabled)
        {
            return false;
        }

        try
        {
            var keyboard = Keyboard.current;
            if (keyboard == null)
            {
                return false;
            }

            return key switch
            {
                TerriasKeyboardKey.Escape => keyboard.escapeKey.wasPressedThisFrame,
                TerriasKeyboardKey.Q => keyboard.qKey.wasPressedThisFrame,
                TerriasKeyboardKey.E => keyboard.eKey.wasPressedThisFrame,
                TerriasKeyboardKey.W => keyboard.wKey.wasPressedThisFrame,
                TerriasKeyboardKey.S => keyboard.sKey.wasPressedThisFrame,
                TerriasKeyboardKey.LeftArrow => keyboard.leftArrowKey.wasPressedThisFrame,
                TerriasKeyboardKey.RightArrow => keyboard.rightArrowKey.wasPressedThisFrame,
                TerriasKeyboardKey.UpArrow => keyboard.upArrowKey.wasPressedThisFrame,
                TerriasKeyboardKey.DownArrow => keyboard.downArrowKey.wasPressedThisFrame,
                _ => false
            };
        }
        catch (Exception ex)
        {
            disabled = true;
            TerriasLog.WarnOnce(
                "KeyboardInputApi.Disabled",
                "Input System keyboard polling disabled after an unexpected failure: " + ex.Message);
            return false;
        }
    }
}
