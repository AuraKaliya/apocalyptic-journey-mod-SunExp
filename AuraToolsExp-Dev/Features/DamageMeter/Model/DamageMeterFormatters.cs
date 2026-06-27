using System;
using System.Globalization;

namespace AuraToolsExp.Dll.Features.DamageMeter.Model;

public static class DamageMeterFormatters
{
    public static string FormatScientific(long value)
    {
        return FormatScientific((double)Math.Max(0L, value));
    }

    public static string FormatScientific(double value)
    {
        if (double.IsNaN(value) || double.IsInfinity(value) || value <= 0d)
        {
            return "0.000 E+00";
        }

        var exponent = (int)Math.Floor(Math.Log10(value));
        var mantissa = value / Math.Pow(10d, exponent);
        var truncated = Math.Floor(mantissa * 1000d) / 1000d;
        if (truncated >= 10d)
        {
            truncated /= 10d;
            exponent++;
        }

        var sign = exponent >= 0 ? "+" : "-";
        return truncated.ToString("0.000", CultureInfo.InvariantCulture)
               + " E"
               + sign
               + Math.Abs(exponent).ToString("00", CultureInfo.InvariantCulture);
    }

    public static string TrimDisplayName(string value, int maxLength = DamageMeterProtocol.MaxHistoryNameLength)
    {
        value = string.IsNullOrWhiteSpace(value) ? "未知成员" : value.Trim();
        maxLength = Math.Max(1, maxLength);
        var indexes = StringInfo.ParseCombiningCharacters(value);
        if (indexes.Length <= maxLength)
        {
            return value;
        }

        return value.Substring(0, indexes[maxLength]);
    }
}
