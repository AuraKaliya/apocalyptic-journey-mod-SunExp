using System;
using System.Collections.Generic;

namespace AuraOnline.Shared;

public static class AuraChatEmojiParser
{
    private const int MaxIdentifierLength = 64;

    public static List<AuraChatRenderSegment> Parse(string? rawText)
    {
        var result = new List<AuraChatRenderSegment>();
        var text = rawText ?? "";
        var cursor = 0;
        while (cursor < text.Length)
        {
            var tokenStart = text.IndexOf("#[", cursor, StringComparison.Ordinal);
            if (tokenStart < 0)
            {
                AddText(result, text.Substring(cursor));
                break;
            }

            if (tokenStart > cursor)
            {
                AddText(result, text.Substring(cursor, tokenStart - cursor));
            }

            var tokenEnd = text.IndexOf(']', tokenStart + 2);
            if (tokenEnd < 0)
            {
                AddText(result, text.Substring(tokenStart));
                break;
            }

            var token = text.Substring(tokenStart + 2, tokenEnd - tokenStart - 2);
            if (TryParseToken(token, out var packId, out var stickerId))
            {
                result.Add(new AuraChatRenderSegment
                {
                    Kind = "Sticker",
                    PackId = packId,
                    StickerId = stickerId
                });
            }
            else
            {
                AddText(result, text.Substring(tokenStart, tokenEnd - tokenStart + 1));
            }

            cursor = tokenEnd + 1;
        }

        return result;
    }

    public static int DisplayLength(string? rawText)
    {
        var length = 0;
        foreach (var segment in Parse(rawText))
        {
            length += segment.Kind == "Sticker"
                ? 1
                : AuraChatTextLimiter.DisplayLength(segment.Text);
        }

        return length;
    }

    public static string StickerFallback(string packId, string stickerId)
    {
        return "#[" + packId + ":" + stickerId + "]";
    }

    private static void AddText(ICollection<AuraChatRenderSegment> result, string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return;
        }

        result.Add(new AuraChatRenderSegment
        {
            Kind = "Text",
            Text = text
        });
    }

    private static bool TryParseToken(string token, out string packId, out string stickerId)
    {
        packId = "";
        stickerId = "";
        var colon = token.IndexOf(':');
        if (colon <= 0 || colon == token.Length - 1)
        {
            return false;
        }

        packId = token.Substring(0, colon);
        stickerId = token.Substring(colon + 1);
        return IsIdentifier(packId) && IsIdentifier(stickerId);
    }

    private static bool IsIdentifier(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > MaxIdentifierLength)
        {
            return false;
        }

        foreach (var ch in value)
        {
            if (!char.IsLetterOrDigit(ch) && ch != '_' && ch != '-' && ch != '.')
            {
                return false;
            }
        }

        return true;
    }
}
