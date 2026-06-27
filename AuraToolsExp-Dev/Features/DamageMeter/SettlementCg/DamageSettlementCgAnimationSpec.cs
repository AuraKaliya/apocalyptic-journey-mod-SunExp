using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace AuraToolsExp.Dll.Features.DamageMeter.SettlementCg;

public sealed class DamageSettlementCgAnimationSpec
{
    public const float DefaultFrameSeconds = 0.125f;

    public float FrameSeconds { get; set; } = DefaultFrameSeconds;

    public bool Loop { get; set; } = true;

    public string Direction { get; set; } = "Right";

    public IReadOnlyList<string> OrderedFrameNames { get; set; } = Array.Empty<string>();

    public static DamageSettlementCgAnimationSpec FromJson(
        string json,
        IEnumerable<string> frameNames)
    {
        var spec = new DamageSettlementCgAnimationSpec
        {
            OrderedFrameNames = OrderFrameNames(frameNames).ToList()
        };

        if (string.IsNullOrWhiteSpace(json))
        {
            return spec;
        }

        try
        {
            var document = JObject.Parse(json);
            var frameSeconds = ReadFloat(document, "AnimationPerFrame", -1f);
            if (frameSeconds <= 0f)
            {
                var frameRate = ReadFloat(document, "FrameRate", -1f);
                frameSeconds = frameRate > 0f ? 1f / frameRate : -1f;
            }

            if (frameSeconds > 0f)
            {
                spec.FrameSeconds = Math.Max(0.02f, Math.Min(2f, frameSeconds));
            }

            var frameCount = Math.Max(0, ReadInt(document, "FrameCount", 0));
            if (frameCount > 0 && spec.OrderedFrameNames.Count > frameCount)
            {
                spec.OrderedFrameNames = spec.OrderedFrameNames.Take(frameCount).ToList();
            }

            spec.Loop = ReadBool(document, "isLoop", true);
            spec.Direction = ReadString(document, "Direction", "Right");
        }
        catch
        {
            // Invalid animation configs fall back to deterministic frame order.
        }

        return spec;
    }

    public static IReadOnlyList<string> OrderFrameNames(IEnumerable<string> frameNames)
    {
        return (frameNames ?? Array.Empty<string>())
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .OrderBy(NormalizedPrefix, StringComparer.OrdinalIgnoreCase)
            .ThenBy(LastNumber)
            .ThenBy(name => name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static string NormalizedPrefix(string name)
    {
        var text = (name ?? "").Trim();
        var lastDigit = -1;
        for (var i = 0; i < text.Length; i++)
        {
            if (char.IsDigit(text[i]))
            {
                lastDigit = i;
                break;
            }
        }

        return lastDigit < 0 ? text : text.Substring(0, lastDigit);
    }

    private static int LastNumber(string name)
    {
        var text = (name ?? "").Trim();
        var end = -1;
        for (var i = text.Length - 1; i >= 0; i--)
        {
            if (char.IsDigit(text[i]))
            {
                end = i;
                break;
            }
        }

        if (end < 0)
        {
            return 0;
        }

        var start = end;
        while (start > 0 && char.IsDigit(text[start - 1]))
        {
            start--;
        }

        return int.TryParse(text.Substring(start, end - start + 1), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)
            ? value
            : 0;
    }

    private static float ReadFloat(JObject document, string property, float fallback)
    {
        var token = document[property];
        if (token == null)
        {
            return fallback;
        }

        return float.TryParse(token.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
            ? value
            : fallback;
    }

    private static int ReadInt(JObject document, string property, int fallback)
    {
        var token = document[property];
        if (token == null)
        {
            return fallback;
        }

        return int.TryParse(token.ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)
            ? value
            : fallback;
    }

    private static bool ReadBool(JObject document, string property, bool fallback)
    {
        var token = document[property];
        if (token == null)
        {
            return fallback;
        }

        return bool.TryParse(token.ToString(), out var value) ? value : fallback;
    }

    private static string ReadString(JObject document, string property, string fallback)
    {
        var token = document[property];
        var value = token?.ToString()?.Trim() ?? "";
        return string.IsNullOrWhiteSpace(value) ? fallback : value;
    }
}
