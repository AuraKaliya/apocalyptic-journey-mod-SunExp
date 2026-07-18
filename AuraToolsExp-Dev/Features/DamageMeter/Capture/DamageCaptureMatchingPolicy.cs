using System;

namespace AuraToolsExp.Dll.Features.DamageMeter.Capture;

internal static class DamageCaptureMatchingPolicy
{
    internal static bool IsHitMatch(string frameTargetId, string frameSourceId, string textTargetId, string textSourceId)
    {
        if (!string.Equals(frameTargetId, textTargetId, StringComparison.Ordinal))
        {
            return false;
        }

        return string.IsNullOrWhiteSpace(textSourceId)
               || string.IsNullOrWhiteSpace(frameSourceId)
               || string.Equals(frameSourceId, textSourceId, StringComparison.Ordinal);
    }

    internal static int Loss(int before, int after) => Math.Max(0, before - after);
}
