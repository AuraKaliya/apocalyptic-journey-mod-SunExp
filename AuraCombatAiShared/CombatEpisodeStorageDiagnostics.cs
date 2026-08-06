using System.Threading;

namespace AuraCombatAi.Shared;

public sealed class CombatEpisodeStorageSnapshot
{
    public long CompactStateVectors { get; set; }

    public long CompactCandidateVectors { get; set; }

    public long CompactStateValues { get; set; }

    public long CompactCandidateValues { get; set; }

    public long StateDictionaryMaterializations { get; set; }

    public long CandidateDictionaryMaterializations { get; set; }

    public long WorldModelObservationsBuilt { get; set; }

    public long WorldModelObservationsSkipped { get; set; }
}

public static class CombatEpisodeStorageDiagnostics
{
    private static long compactStateVectors;
    private static long compactCandidateVectors;
    private static long compactStateValues;
    private static long compactCandidateValues;
    private static long stateDictionaryMaterializations;
    private static long candidateDictionaryMaterializations;
    private static long worldModelObservationsBuilt;
    private static long worldModelObservationsSkipped;

    public static CombatEpisodeStorageSnapshot Capture()
    {
        return new CombatEpisodeStorageSnapshot
        {
            CompactStateVectors = Volatile.Read(ref compactStateVectors),
            CompactCandidateVectors = Volatile.Read(ref compactCandidateVectors),
            CompactStateValues = Volatile.Read(ref compactStateValues),
            CompactCandidateValues = Volatile.Read(ref compactCandidateValues),
            StateDictionaryMaterializations = Volatile.Read(
                ref stateDictionaryMaterializations),
            CandidateDictionaryMaterializations = Volatile.Read(
                ref candidateDictionaryMaterializations),
            WorldModelObservationsBuilt = Volatile.Read(
                ref worldModelObservationsBuilt),
            WorldModelObservationsSkipped = Volatile.Read(
                ref worldModelObservationsSkipped)
        };
    }

    internal static void CompactStateVector(int values)
    {
        Interlocked.Increment(ref compactStateVectors);
        Interlocked.Add(ref compactStateValues, values);
    }

    internal static void CompactCandidateVector(int values)
    {
        Interlocked.Increment(ref compactCandidateVectors);
        Interlocked.Add(ref compactCandidateValues, values);
    }

    internal static void StateDictionaryMaterialized()
    {
        Interlocked.Increment(ref stateDictionaryMaterializations);
    }

    internal static void CandidateDictionaryMaterialized()
    {
        Interlocked.Increment(ref candidateDictionaryMaterializations);
    }

    internal static void WorldModelObservation(bool built)
    {
        if (built)
        {
            Interlocked.Increment(ref worldModelObservationsBuilt);
        }
        else
        {
            Interlocked.Increment(ref worldModelObservationsSkipped);
        }
    }
}
