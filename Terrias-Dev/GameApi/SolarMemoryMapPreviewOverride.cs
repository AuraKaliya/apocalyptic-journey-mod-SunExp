using System;
using System.Collections.Generic;

namespace Terrias.Dll.GameApi;

/// <summary>A temporary change to the exact enemy data read by native MapItem.</summary>
public sealed class SolarMemoryMapPreviewOverride
{
    private readonly IDictionary<string, string> enemyData;
    private readonly bool hadAnimation;
    private bool restored;

    public SolarMemoryMapPreviewOverride(
        IDictionary<string, string> enemyData, string enemyId, string fallbackAnimation)
    {
        this.enemyData = enemyData ?? throw new ArgumentNullException(nameof(enemyData));
        EnemyId = enemyId;
        hadAnimation = enemyData.TryGetValue("Animation", out var original);
        OriginalAnimation = original ?? "";
        FallbackAnimation = fallbackAnimation;
        enemyData["Animation"] = fallbackAnimation;
    }

    public string EnemyId { get; }
    public string OriginalAnimation { get; }
    public string FallbackAnimation { get; }

    public bool Restore()
    {
        if (restored)
        {
            return false;
        }

        restored = true;
        if (!enemyData.TryGetValue("Animation", out var current)
            || !string.Equals(current, FallbackAnimation, StringComparison.Ordinal))
        {
            return false;
        }

        if (hadAnimation)
        {
            enemyData["Animation"] = OriginalAnimation;
        }
        else
        {
            enemyData.Remove("Animation");
        }
        return true;
    }
}
