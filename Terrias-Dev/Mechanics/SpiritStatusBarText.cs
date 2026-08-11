using System;
using System.Linq;

namespace Terrias.Dll.Mechanics;

public static class SpiritStatusBarText
{
    public static string FormatVerticalDigits(int value)
    {
        return string.Join("\n", Math.Max(0, value).ToString().Select(digit => digit.ToString()));
    }
}
