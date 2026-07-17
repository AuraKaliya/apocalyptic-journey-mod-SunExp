using System;

namespace AuraCg.Shared;

internal static class AuraCgPresentationPolicy
{
    public static bool UsesMaskedFlash(SkillCgRequest request)
    {
        if (string.Equals(request.FlashMode, SkillCgFlashModes.ScreenBwPulse, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return string.Equals(request.FlashMode, SkillCgFlashModes.MaskedInvert, StringComparison.OrdinalIgnoreCase)
            || string.Equals(request.FlashMode, SkillCgFlashModes.HybridBwPulse, StringComparison.OrdinalIgnoreCase)
            || request.FlashStartFrame > 0
            || request.FlashEndFrame > 0;
    }

    public static bool UsesScreenBwFlash(SkillCgRequest request)
    {
        return string.Equals(request.FlashMode, SkillCgFlashModes.ScreenBwPulse, StringComparison.OrdinalIgnoreCase)
            || string.Equals(request.FlashMode, SkillCgFlashModes.HybridBwPulse, StringComparison.OrdinalIgnoreCase);
    }
}
