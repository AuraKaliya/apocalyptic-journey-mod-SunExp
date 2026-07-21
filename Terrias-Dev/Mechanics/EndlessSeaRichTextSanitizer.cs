using System;
using System.Text;

namespace Terrias.Dll.Mechanics;

public static class EndlessSeaRichTextSanitizer
{
    private const int MaxTagLength = 48;
    private static readonly string[] AllowedSimpleTags = { "b", "i", "u", "br" };
    private static readonly string[] AllowedScopedTags = { "color", "size", "align" };

    public static string Sanitize(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return "";
        }

        var builder = new StringBuilder(value.Length);
        for (var i = 0; i < value.Length; i++)
        {
            var current = value[i];
            if (current != '<')
            {
                builder.Append(current);
                continue;
            }

            var close = value.IndexOf('>', i + 1);
            if (close <= i || close - i > MaxTagLength)
            {
                builder.Append('＜');
                continue;
            }

            var tag = value.Substring(i + 1, close - i - 1);
            if (IsAllowedTag(tag))
            {
                builder.Append('<').Append(tag).Append('>');
            }
            else
            {
                builder.Append('＜').Append(tag).Append('＞');
            }

            i = close;
        }

        return builder.ToString();
    }

    public static bool IsAllowedTag(string tag)
    {
        if (string.IsNullOrWhiteSpace(tag))
        {
            return false;
        }

        var normalized = tag.Trim().ToLowerInvariant();
        if (normalized.StartsWith("/", StringComparison.Ordinal))
        {
            var closing = normalized.Substring(1);
            return Contains(AllowedSimpleTags, closing) || Contains(AllowedScopedTags, closing);
        }

        if (Contains(AllowedSimpleTags, normalized))
        {
            return true;
        }

        return IsAllowedColorTag(normalized)
            || IsAllowedSizeTag(normalized)
            || IsAllowedAlignTag(normalized);
    }

    private static bool IsAllowedColorTag(string tag)
    {
        if (!tag.StartsWith("color=", StringComparison.Ordinal))
        {
            return false;
        }

        var value = TrimQuotes(tag.Substring("color=".Length));
        if (value.Length != 7 && value.Length != 9)
        {
            return false;
        }

        if (value[0] != '#')
        {
            return false;
        }

        for (var i = 1; i < value.Length; i++)
        {
            var ch = value[i];
            if (!((ch >= '0' && ch <= '9') || (ch >= 'a' && ch <= 'f')))
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsAllowedSizeTag(string tag)
    {
        if (!tag.StartsWith("size=", StringComparison.Ordinal))
        {
            return false;
        }

        return int.TryParse(TrimQuotes(tag.Substring("size=".Length)), out var size)
            && size >= 10
            && size <= 32;
    }

    private static bool IsAllowedAlignTag(string tag)
    {
        if (!tag.StartsWith("align=", StringComparison.Ordinal))
        {
            return false;
        }

        var value = TrimQuotes(tag.Substring("align=".Length));
        return value == "left" || value == "center" || value == "right";
    }

    private static string TrimQuotes(string value)
    {
        return value.Trim().Trim('"', '\'');
    }

    private static bool Contains(string[] values, string target)
    {
        foreach (var value in values)
        {
            if (string.Equals(value, target, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }
}
