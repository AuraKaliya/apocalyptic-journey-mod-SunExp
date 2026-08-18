using System;
using System.Collections.Generic;
using AuraToolsExp.Dll.Infrastructure;
using UnityEngine;

namespace AuraToolsExp.Dll.Features.Settings;

internal static class AuraToolsIconRegistry
{
    private const string Root = "Mods/AuraToolsExp/ModResource/Images/UI/ToolboxIcons/";

    private static readonly Dictionary<string, string> Files =
        new(StringComparer.Ordinal)
        {
            ["category.all"] = "all.png",
            ["category.gameplay"] = "gameplay.png",
            ["category.presentation"] = "presentation.png",
            ["category.records"] = "records.png",
            ["category.multiplayer"] = "multiplayer.png",
            ["category.intelligence"] = "intelligence.png",
            ["category.system"] = "system.png",
            ["category.extensions"] = "extensions.png",
            ["system.file-logging"] = "file-logging.png",
            ["presentation.skin"] = "skin.png",
            ["presentation.battle-bgm"] = "battle-bgm.png",
            ["presentation.card-use-audio"] = "card-use-audio.png",
            ["gameplay.starter-deck"] = "starter-deck.png",
            ["gameplay.card-refresh"] = "card-refresh.png",
            ["gameplay.feast"] = "feast.png",
            ["gameplay.safe-box"] = "safe-box.png",
            ["presentation.pixel-emoji"] = "pixel-emoji.png",
            ["multiplayer.mod-sync"] = "mod-sync.png",
            ["records.damage-statistics"] = "damage-statistics.png",
            ["records.battle-replay"] = "battle-replay.png",
            ["intelligence.auto-battle"] = "auto-battle.png",
            ["presentation.skill-cg"] = "skill-cg.png",
            ["presentation.card-use-cg"] = "card-use-cg.png",
            ["action.search"] = "search.png",
            ["action.clear"] = "clear.png",
            ["action.folder"] = "folder.png",
            ["action.settings"] = "settings.png",
            ["status.warning"] = "warning.png"
        };

    private static readonly Dictionary<string, Sprite?> Cache =
        new(StringComparer.Ordinal);

    internal static Sprite? Resolve(string iconKey, string fallbackKey = "")
    {
        var normalized = Normalize(iconKey);
        if (!Files.TryGetValue(normalized, out var file))
        {
            normalized = Normalize(fallbackKey);
            if (!Files.TryGetValue(normalized, out file))
            {
                return null;
            }
        }

        if (Cache.TryGetValue(normalized, out var cached))
        {
            return cached;
        }

        var sprite = AuraToolsResourceCache.Load<Sprite>(Root + file, true);
        Cache[normalized] = sprite;
        return sprite;
    }

    private static string Normalize(string value)
    {
        return string.IsNullOrWhiteSpace(value) ? "" : value.Trim();
    }
}
