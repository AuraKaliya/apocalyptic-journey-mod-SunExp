using System;

namespace Terrias.Dll.Mechanics;

/// <summary>
/// Declares the stable autonomous execution route owned by the projection-card
/// runtime. Per-instance card and attachment capability remains authoritative
/// in <c>ProjectionCardBattleState.Preflight</c>.
/// </summary>
public static class ProjectionActionAutomationPolicy
{
    public const string SourcePrefix = "projection-card:";

    public static bool DeclaresHeadlessExecutionRoute(string? sourceId)
    {
        if (sourceId == null)
        {
            return false;
        }

        var value = sourceId.Trim();
        if (!value.StartsWith(SourcePrefix, StringComparison.Ordinal))
        {
            return false;
        }

        return value.Substring(SourcePrefix.Length).Trim().Length > 0;
    }
}
