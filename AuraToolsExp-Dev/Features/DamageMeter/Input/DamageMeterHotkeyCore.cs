using System;
using System.Collections.Generic;

namespace AuraToolsExp.Dll.Features.DamageMeter.Input;

internal static class DamageMeterHotkeyNames
{
    private static readonly Dictionary<string, string> CanonicalNames = BuildCanonicalNames();

    private static readonly Dictionary<string, string> Aliases = new(StringComparer.OrdinalIgnoreCase)
    {
        ["BackQuote"] = "Backquote",
        ["Return"] = "Enter",
        ["Esc"] = "Escape",
        ["LeftControl"] = "LeftCtrl",
        ["RightControl"] = "RightCtrl",
        ["KeypadEnter"] = "NumpadEnter",
        ["KeypadDivide"] = "NumpadDivide",
        ["KeypadMultiply"] = "NumpadMultiply",
        ["KeypadPlus"] = "NumpadPlus",
        ["KeypadMinus"] = "NumpadMinus",
        ["KeypadPeriod"] = "NumpadPeriod",
        ["KeypadEquals"] = "NumpadEquals"
    };

    public static bool TryNormalize(string? value, out string canonical)
    {
        var candidate = value?.Trim() ?? "";
        if (Aliases.TryGetValue(candidate, out var alias))
        {
            candidate = alias;
        }
        else if (candidate.StartsWith("Alpha", StringComparison.OrdinalIgnoreCase)
                 && candidate.Length == 6
                 && char.IsDigit(candidate[5]))
        {
            candidate = "Digit" + candidate[5];
        }
        else if (candidate.StartsWith("Keypad", StringComparison.OrdinalIgnoreCase)
                 && candidate.Length == 7
                 && char.IsDigit(candidate[6]))
        {
            candidate = "Numpad" + candidate[6];
        }

        if (CanonicalNames.TryGetValue(candidate, out canonical!))
        {
            return true;
        }

        canonical = "F8";
        return false;
    }

    private static Dictionary<string, string> BuildCanonicalNames()
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        Add(result,
            "Space", "Enter", "Tab", "Backquote", "Quote", "Semicolon", "Comma", "Period",
            "Slash", "Backslash", "LeftBracket", "RightBracket", "Minus", "Equals",
            "LeftShift", "RightShift", "LeftAlt", "RightAlt", "AltGr", "LeftCtrl", "RightCtrl",
            "LeftMeta", "LeftApple", "LeftWindows", "LeftCommand", "RightCommand", "RightWindows",
            "RightApple", "RightMeta", "ContextMenu", "Escape", "LeftArrow", "RightArrow",
            "UpArrow", "DownArrow", "Backspace", "PageDown", "PageUp", "Home", "End", "Insert",
            "Delete", "CapsLock", "NumLock", "PrintScreen", "ScrollLock", "Pause",
            "NumpadEnter", "NumpadDivide", "NumpadMultiply", "NumpadPlus", "NumpadMinus",
            "NumpadPeriod", "NumpadEquals", "OEM1", "OEM2", "OEM3", "OEM4", "OEM5",
            "IMESelected");

        for (var key = 'A'; key <= 'Z'; key++)
        {
            Add(result, key.ToString());
        }

        for (var digit = 0; digit <= 9; digit++)
        {
            Add(result, "Digit" + digit, "Numpad" + digit);
        }

        for (var function = 1; function <= 24; function++)
        {
            Add(result, "F" + function);
        }

        return result;
    }

    private static void Add(Dictionary<string, string> target, params string[] values)
    {
        foreach (var value in values)
        {
            target[value] = value;
        }
    }
}

internal sealed class DamageMeterInputFaultGate
{
    public bool IsFaulted { get; private set; }

    public bool TryPoll(Func<bool> poll, Action<Exception> onFirstFailure)
    {
        if (IsFaulted)
        {
            return false;
        }

        try
        {
            return poll();
        }
        catch (Exception ex)
        {
            IsFaulted = true;
            onFirstFailure(ex);
            return false;
        }
    }

    public void Reset()
    {
        IsFaulted = false;
    }
}
