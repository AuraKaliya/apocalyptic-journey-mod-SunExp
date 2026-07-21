using System;
using System.Collections.Generic;
using System.Linq;
using Terrias.Dll.GameApi;
using Terrias.Dll.Infrastructure;
using Witch.Core;

namespace Terrias.Dll.Mechanics;

public sealed class CardVisualSkinRule
{
    private readonly HashSet<string> cardIds;
    private readonly HashSet<string> packIds;
    private readonly string[] iconPrefixes;

    public CardVisualSkinRule(
        CardVisualSkinSpec skin,
        IEnumerable<string>? cardIds = null,
        IEnumerable<string>? packIds = null,
        IEnumerable<string>? iconPrefixes = null,
        int priority = 0)
    {
        Skin = skin ?? throw new ArgumentNullException(nameof(skin));
        this.cardIds = NormalizeSet(cardIds);
        this.packIds = NormalizeSet(packIds);
        this.iconPrefixes = Normalize(iconPrefixes).ToArray();
        Priority = priority;
    }

    public CardVisualSkinSpec Skin { get; }

    public int Priority { get; }

    public bool Matches(IDataConfig? config)
    {
        if (config == null)
        {
            return false;
        }

        var id = CardConfigApi.Id(config);
        if (MatchesId(id))
        {
            return true;
        }

        var packBelong = DictionaryUtil.Get(config.data, "PackBelong");
        if (!string.IsNullOrWhiteSpace(packBelong) && packIds.Contains(packBelong.Trim()))
        {
            return true;
        }

        var icon = DictionaryUtil.Get(config.data, "Icon");
        foreach (var prefix in iconPrefixes)
        {
            if (!string.IsNullOrWhiteSpace(icon) && icon.StartsWith(prefix, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private bool MatchesId(string id)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            return false;
        }

        var value = id.Trim();
        if (cardIds.Contains(value))
        {
            return true;
        }

        foreach (var pattern in cardIds)
        {
            if (WildcardMatches(pattern, value))
            {
                return true;
            }
        }

        return false;
    }

    private static bool WildcardMatches(string pattern, string value)
    {
        if (string.IsNullOrWhiteSpace(pattern) || pattern.IndexOf('*') < 0)
        {
            return false;
        }

        var parts = pattern.Split(new[] { '*' }, StringSplitOptions.None);
        var index = 0;
        for (var i = 0; i < parts.Length; i++)
        {
            var part = parts[i];
            if (part.Length == 0)
            {
                continue;
            }

            var next = value.IndexOf(part, index, StringComparison.Ordinal);
            if (next < 0 || i == 0 && next != 0 && !pattern.StartsWith("*", StringComparison.Ordinal))
            {
                return false;
            }

            index = next + part.Length;
        }

        var lastPart = parts.Length == 0 ? "" : parts[parts.Length - 1];
        return pattern.EndsWith("*", StringComparison.Ordinal)
            || lastPart.Length == 0
            || value.EndsWith(lastPart, StringComparison.Ordinal);
    }

    private static HashSet<string> NormalizeSet(IEnumerable<string>? values)
    {
        return new HashSet<string>(Normalize(values), StringComparer.Ordinal);
    }

    private static IEnumerable<string> Normalize(IEnumerable<string>? values)
    {
        if (values == null)
        {
            yield break;
        }

        foreach (var value in values)
        {
            var normalized = value?.Trim() ?? "";
            if (normalized.Length > 0)
            {
                yield return normalized;
            }
        }
    }
}
