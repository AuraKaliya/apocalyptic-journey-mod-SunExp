using System;
using System.Text;

namespace AuraOnline.Shared;

public static class AuraChatTextLimiter
{
    public const int PlayerTextLimit = 20;
    public const int SystemLineLimit = 500;
    public const int DisplayLineLimit = 30;

    public static string LimitPlayerText(string? rawText)
    {
        return LimitByDisplayUnits(rawText, PlayerTextLimit);
    }

    public static string LimitSystemLine(string? rawText)
    {
        var text = rawText ?? "";
        if (DisplayLength(text) <= SystemLineLimit)
        {
            return text;
        }

        return TruncatePlainText(text, SystemLineLimit) + "...";
    }

    public static string WrapPlainText(string? text, int displayLineLimit = DisplayLineLimit)
    {
        var value = text ?? "";
        if (displayLineLimit <= 0)
        {
            return value;
        }

        var builder = new StringBuilder(value.Length + value.Length / displayLineLimit);
        var line = 0;
        foreach (var ch in value)
        {
            if (ch == '\r')
            {
                continue;
            }

            if (ch == '\n')
            {
                builder.Append('\n');
                line = 0;
                continue;
            }

            builder.Append(ch);
            line += DisplayWidth(ch);
            if (line >= displayLineLimit)
            {
                builder.Append('\n');
                line = 0;
            }
        }

        return builder.ToString();
    }

    public static int DisplayLength(string? text)
    {
        var value = text ?? "";
        var length = 0;
        foreach (var ch in value)
        {
            if (ch == '\r' || ch == '\n')
            {
                continue;
            }

            length += DisplayWidth(ch);
        }

        return length;
    }

    private static string LimitByDisplayUnits(string? rawText, int maxUnits)
    {
        var text = rawText ?? "";
        if (AuraChatEmojiParser.DisplayLength(text) <= maxUnits)
        {
            return text;
        }

        var builder = new StringBuilder(text.Length);
        var used = 0;
        foreach (var segment in AuraChatEmojiParser.Parse(text))
        {
            if (segment.Kind == "Sticker")
            {
                if (used + 1 > maxUnits)
                {
                    break;
                }

                builder.Append(AuraChatEmojiParser.StickerFallback(segment.PackId, segment.StickerId));
                used++;
                continue;
            }

            foreach (var ch in segment.Text)
            {
                var width = DisplayWidth(ch);
                if (used + width > maxUnits)
                {
                    return builder.Append("...").ToString();
                }

                builder.Append(ch);
                used += width;
            }
        }

        return builder.Append("...").ToString();
    }

    private static string TruncatePlainText(string text, int maxUnits)
    {
        var builder = new StringBuilder(text.Length);
        var used = 0;
        foreach (var ch in text)
        {
            var width = DisplayWidth(ch);
            if (used + width > maxUnits)
            {
                break;
            }

            builder.Append(ch);
            used += width;
        }

        return builder.ToString();
    }

    private static int DisplayWidth(char ch)
    {
        return char.IsControl(ch) ? 0 : 1;
    }
}
