namespace Terrias.Dll.Mechanics;

public enum TerriasPresentationInvalidationDecision
{
    PreserveNative,
    SuppressNoChange,
    ConvertToDelta
}

public static class TerriasPresentationInvalidationPolicy
{
    public static TerriasPresentationInvalidationDecision Decide(
        bool wasPending,
        bool isPending,
        bool allActiveCardsManaged,
        int mutationCount,
        bool allMutationsKnown,
        bool allImpactsDeltaSafe)
    {
        if (wasPending
            || !isPending
            || !allActiveCardsManaged
            || !allMutationsKnown
            || !allImpactsDeltaSafe)
        {
            return TerriasPresentationInvalidationDecision.PreserveNative;
        }

        return mutationCount <= 0
            ? TerriasPresentationInvalidationDecision.SuppressNoChange
            : TerriasPresentationInvalidationDecision.ConvertToDelta;
    }
}
