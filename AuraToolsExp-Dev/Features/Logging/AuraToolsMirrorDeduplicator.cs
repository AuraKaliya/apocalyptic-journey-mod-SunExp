using System;
using System.Collections.Generic;
using System.Linq;

namespace AuraToolsExp.Dll.Features.Logging;

internal sealed class AuraToolsMirrorDeduplicator
{
    private const double CrossSourceWindowMilliseconds = 300d;
    private readonly Dictionary<string, MirrorEntry> lastByMessage =
        new(StringComparer.OrdinalIgnoreCase);

    public bool Allow(
        string source,
        string level,
        string? tag,
        string message,
        DateTime utcNow)
    {
        if (!TryBuildKey(source, level, tag, message, out var key))
        {
            return true;
        }

        if (lastByMessage.TryGetValue(key, out var previous)
            && !string.Equals(previous.Source, source, StringComparison.OrdinalIgnoreCase)
            && (utcNow - previous.TimestampUtc).TotalMilliseconds
               < CrossSourceWindowMilliseconds)
        {
            return false;
        }

        lastByMessage[key] = new MirrorEntry(source, utcNow);
        if (lastByMessage.Count > 1024)
        {
            foreach (var stale in lastByMessage
                         .Where(pair => (utcNow - pair.Value.TimestampUtc).TotalSeconds > 2d)
                         .Select(pair => pair.Key)
                         .ToList())
            {
                lastByMessage.Remove(stale);
            }
        }

        return true;
    }

    public void Clear()
    {
        lastByMessage.Clear();
    }

    private static bool TryBuildKey(
        string source,
        string level,
        string? tag,
        string message,
        out string key)
    {
        var normalizedSource = (source ?? "").Trim();
        var normalizedLevel = (level ?? "").Trim();
        var normalizedMessage = (message ?? "").Trim();
        string origin;

        if (string.Equals(normalizedSource, "Command", StringComparison.OrdinalIgnoreCase))
        {
            origin = (tag ?? "").Trim();
            ConsumeLevelPrefix(ref normalizedMessage, ref normalizedLevel);
        }
        else if (string.Equals(normalizedSource, "Unity", StringComparison.OrdinalIgnoreCase)
                 && TryConsumeBracketPrefix(ref normalizedMessage, out origin))
        {
            ConsumeLevelPrefix(ref normalizedMessage, ref normalizedLevel);
        }
        else
        {
            key = "";
            return false;
        }

        if (string.IsNullOrWhiteSpace(origin) || string.IsNullOrWhiteSpace(normalizedMessage))
        {
            key = "";
            return false;
        }

        key = normalizedLevel + "|" + origin + "|" + StableMessageKey(normalizedMessage);
        return true;
    }

    private static void ConsumeLevelPrefix(ref string message, ref string level)
    {
        var candidate = message;
        if (!TryConsumeBracketPrefix(ref candidate, out var prefix))
        {
            return;
        }

        if (!string.Equals(prefix, "DEBUG", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(prefix, "INFO", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(prefix, "WARNING", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(prefix, "WARN", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(prefix, "ERROR", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        level = string.Equals(prefix, "WARN", StringComparison.OrdinalIgnoreCase)
            ? "Warning"
            : char.ToUpperInvariant(prefix[0]) + prefix.Substring(1).ToLowerInvariant();
        message = candidate;
    }

    private static bool TryConsumeBracketPrefix(ref string value, out string prefix)
    {
        prefix = "";
        if (string.IsNullOrWhiteSpace(value) || value[0] != '[')
        {
            return false;
        }

        var closing = value.IndexOf(']');
        if (closing <= 1)
        {
            return false;
        }

        prefix = value.Substring(1, closing - 1).Trim();
        value = value.Substring(closing + 1).TrimStart();
        return !string.IsNullOrWhiteSpace(prefix);
    }

    private static string StableMessageKey(string message)
    {
        return message.Length <= 256 ? message : message.Substring(0, 256);
    }

    private readonly struct MirrorEntry
    {
        public MirrorEntry(string source, DateTime timestampUtc)
        {
            Source = source;
            TimestampUtc = timestampUtc;
        }

        public string Source { get; }

        public DateTime TimestampUtc { get; }
    }
}
