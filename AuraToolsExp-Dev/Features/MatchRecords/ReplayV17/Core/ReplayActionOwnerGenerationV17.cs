namespace AuraToolsExp.Dll.Features.MatchRecords.ReplayV17.Core;

internal sealed class ReplayActionOwnerGenerationV17
{
    private int? generation;

    internal bool Observe(int? current, bool active)
    {
        if (generation.HasValue)
            return !current.HasValue || current.Value == generation.Value;
        if (active && current.HasValue && current.Value > 0) generation = current;
        return true;
    }
}
