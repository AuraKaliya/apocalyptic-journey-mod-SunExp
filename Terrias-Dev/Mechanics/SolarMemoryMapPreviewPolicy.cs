using System;
using System.Collections.Generic;
using Terrias.Dll.Infrastructure;

namespace Terrias.Dll.Mechanics;

public static class SolarMemoryMapPreviewPolicy
{
    public const string SecondSunFallbackAnimation = "AnimationLib/\u707e\u5384\u5148\u5146";
    public const string SaintWunaFallbackAnimation = "AnimationLib/\u5931\u5fc3\u9b54\u5973";
    public const string GenericFightFallbackAnimation = "AnimationLib/\u707e\u5384\u5148\u5146";

    public static string ResolveFallback(
        string? levelId,
        string? originalAnimation,
        Func<string, bool> hasNativePreviewFrames)
    {
        if (hasNativePreviewFrames == null)
        {
            throw new ArgumentNullException(nameof(hasNativePreviewFrames));
        }

        var original = (originalAnimation ?? "").Trim();
        if (original.Length > 0 && hasNativePreviewFrames(original))
        {
            return "";
        }

        foreach (var candidate in CandidateFallbacks(levelId))
        {
            if (!string.IsNullOrWhiteSpace(candidate)
                && !string.Equals(candidate, original, StringComparison.Ordinal)
                && hasNativePreviewFrames(candidate))
            {
                return candidate;
            }
        }

        return "";
    }

    private static IEnumerable<string> CandidateFallbacks(string? levelId)
    {
        if (IsLevel(levelId, TerriasIds.SolarBossSecondSunLevelId, "level_second_sun_last_day"))
        {
            yield return SecondSunFallbackAnimation;
        }
        else if (IsLevel(levelId, TerriasIds.SolarBossSaintWunaLevelId, "level_saint_wuna"))
        {
            yield return SaintWunaFallbackAnimation;
        }

        yield return GenericFightFallbackAnimation;
    }

    private static bool IsLevel(string? actual, string fullId, string shortId)
    {
        return string.Equals(actual, fullId, StringComparison.Ordinal)
               || string.Equals(actual, shortId, StringComparison.Ordinal);
    }
}
