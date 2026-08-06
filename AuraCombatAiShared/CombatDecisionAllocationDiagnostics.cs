using System;
using System.Threading;

namespace AuraCombatAi.Shared;

public sealed class CombatDecisionAllocationSnapshot
{
    public long Decisions { get; set; }

    public long PreparationAllocatedBytes { get; set; }

    public long SearchAllocatedBytes { get; set; }

    public long SearchSetupAllocatedBytes { get; set; }

    public long SearchSimulationAllocatedBytes { get; set; }

    public long SearchResultAllocatedBytes { get; set; }

    public long ForwardApplyAllocatedBytes { get; set; }

    public long LeafEvaluationAllocatedBytes { get; set; }

    public long ScoreEvaluationAllocatedBytes { get; set; }

    public long SearchExpansionAllocatedBytes { get; set; }

    public long SearchTranspositionAllocatedBytes { get; set; }

    public long SearchBackpropagationAllocatedBytes { get; set; }

    public long SearchSelectionAllocatedBytes { get; set; }

    public long RootDeterminizationAllocatedBytes { get; set; }

    public long CycleAnalysisAllocatedBytes { get; set; }

    public long SimulationTrackedAllocatedBytes { get; set; }

    public CombatDecisionAllocationSnapshot DeltaFrom(
        CombatDecisionAllocationSnapshot baseline)
    {
        baseline ??= new CombatDecisionAllocationSnapshot();
        return new CombatDecisionAllocationSnapshot
        {
            Decisions = Math.Max(0L, Decisions - baseline.Decisions),
            PreparationAllocatedBytes = Math.Max(
                0L,
                PreparationAllocatedBytes - baseline.PreparationAllocatedBytes),
            SearchAllocatedBytes = Math.Max(
                0L,
                SearchAllocatedBytes - baseline.SearchAllocatedBytes),
            SearchSetupAllocatedBytes = Math.Max(
                0L,
                SearchSetupAllocatedBytes - baseline.SearchSetupAllocatedBytes),
            SearchSimulationAllocatedBytes = Math.Max(
                0L,
                SearchSimulationAllocatedBytes
                - baseline.SearchSimulationAllocatedBytes),
            SearchResultAllocatedBytes = Math.Max(
                0L,
                SearchResultAllocatedBytes - baseline.SearchResultAllocatedBytes),
            ForwardApplyAllocatedBytes = Math.Max(
                0L,
                ForwardApplyAllocatedBytes - baseline.ForwardApplyAllocatedBytes),
            LeafEvaluationAllocatedBytes = Math.Max(
                0L,
                LeafEvaluationAllocatedBytes
                - baseline.LeafEvaluationAllocatedBytes),
            ScoreEvaluationAllocatedBytes = Math.Max(
                0L,
                ScoreEvaluationAllocatedBytes
                - baseline.ScoreEvaluationAllocatedBytes),
            SearchExpansionAllocatedBytes = Math.Max(
                0L,
                SearchExpansionAllocatedBytes
                - baseline.SearchExpansionAllocatedBytes),
            SearchTranspositionAllocatedBytes = Math.Max(
                0L,
                SearchTranspositionAllocatedBytes
                - baseline.SearchTranspositionAllocatedBytes),
            SearchBackpropagationAllocatedBytes = Math.Max(
                0L,
                SearchBackpropagationAllocatedBytes
                - baseline.SearchBackpropagationAllocatedBytes),
            SearchSelectionAllocatedBytes = Math.Max(
                0L,
                SearchSelectionAllocatedBytes
                - baseline.SearchSelectionAllocatedBytes),
            RootDeterminizationAllocatedBytes = Math.Max(
                0L,
                RootDeterminizationAllocatedBytes
                - baseline.RootDeterminizationAllocatedBytes),
            CycleAnalysisAllocatedBytes = Math.Max(
                0L,
                CycleAnalysisAllocatedBytes
                - baseline.CycleAnalysisAllocatedBytes),
            SimulationTrackedAllocatedBytes = Math.Max(
                0L,
                SimulationTrackedAllocatedBytes
                - baseline.SimulationTrackedAllocatedBytes)
        };
    }
}

public static class CombatDecisionAllocationDiagnostics
{
    private static int detailedEnabled;
    private static long decisions;
    private static long preparationAllocatedBytes;
    private static long searchAllocatedBytes;
    private static long searchSetupAllocatedBytes;
    private static long searchSimulationAllocatedBytes;
    private static long searchResultAllocatedBytes;
    private static long forwardApplyAllocatedBytes;
    private static long leafEvaluationAllocatedBytes;
    private static long scoreEvaluationAllocatedBytes;
    private static long searchExpansionAllocatedBytes;
    private static long searchTranspositionAllocatedBytes;
    private static long searchBackpropagationAllocatedBytes;
    private static long searchSelectionAllocatedBytes;
    private static long rootDeterminizationAllocatedBytes;
    private static long cycleAnalysisAllocatedBytes;
    private static long simulationTrackedAllocatedBytes;

    public static bool DetailedEnabled
    {
        get => Volatile.Read(ref detailedEnabled) != 0;
        set => Volatile.Write(ref detailedEnabled, value ? 1 : 0);
    }

    public static CombatDecisionAllocationSnapshot Capture()
    {
        return new CombatDecisionAllocationSnapshot
        {
            Decisions = Volatile.Read(ref decisions),
            PreparationAllocatedBytes = Volatile.Read(
                ref preparationAllocatedBytes),
            SearchAllocatedBytes = Volatile.Read(ref searchAllocatedBytes),
            SearchSetupAllocatedBytes = Volatile.Read(
                ref searchSetupAllocatedBytes),
            SearchSimulationAllocatedBytes = Volatile.Read(
                ref searchSimulationAllocatedBytes),
            SearchResultAllocatedBytes = Volatile.Read(
                ref searchResultAllocatedBytes),
            ForwardApplyAllocatedBytes = Volatile.Read(
                ref forwardApplyAllocatedBytes),
            LeafEvaluationAllocatedBytes = Volatile.Read(
                ref leafEvaluationAllocatedBytes),
            ScoreEvaluationAllocatedBytes = Volatile.Read(
                ref scoreEvaluationAllocatedBytes),
            SearchExpansionAllocatedBytes = Volatile.Read(
                ref searchExpansionAllocatedBytes),
            SearchTranspositionAllocatedBytes = Volatile.Read(
                ref searchTranspositionAllocatedBytes),
            SearchBackpropagationAllocatedBytes = Volatile.Read(
                ref searchBackpropagationAllocatedBytes),
            SearchSelectionAllocatedBytes = Volatile.Read(
                ref searchSelectionAllocatedBytes),
            RootDeterminizationAllocatedBytes = Volatile.Read(
                ref rootDeterminizationAllocatedBytes),
            CycleAnalysisAllocatedBytes = Volatile.Read(
                ref cycleAnalysisAllocatedBytes),
            SimulationTrackedAllocatedBytes = Volatile.Read(
                ref simulationTrackedAllocatedBytes)
        };
    }

    internal static void Record(long preparationBytes, long searchBytes)
    {
        Interlocked.Increment(ref decisions);
        Interlocked.Add(
            ref preparationAllocatedBytes,
            Math.Max(0L, preparationBytes));
        Interlocked.Add(ref searchAllocatedBytes, Math.Max(0L, searchBytes));
    }

    internal static void RecordSearchBreakdown(
        long setupBytes,
        long simulationBytes,
        long resultBytes)
    {
        Interlocked.Add(
            ref searchSetupAllocatedBytes,
            Math.Max(0L, setupBytes));
        Interlocked.Add(
            ref searchSimulationAllocatedBytes,
            Math.Max(0L, simulationBytes));
        Interlocked.Add(
            ref searchResultAllocatedBytes,
            Math.Max(0L, resultBytes));
    }

    internal static void RecordForwardApply(long bytes)
    {
        if (!DetailedEnabled)
        {
            return;
        }
        Interlocked.Add(ref forwardApplyAllocatedBytes, Math.Max(0L, bytes));
    }

    internal static void RecordLeafEvaluation(long bytes)
    {
        if (!DetailedEnabled)
        {
            return;
        }
        Interlocked.Add(ref leafEvaluationAllocatedBytes, Math.Max(0L, bytes));
    }

    internal static void RecordScoreEvaluation(long bytes)
    {
        if (!DetailedEnabled)
        {
            return;
        }
        Interlocked.Add(ref scoreEvaluationAllocatedBytes, Math.Max(0L, bytes));
    }

    internal static void RecordSearchExpansion(long bytes)
    {
        if (!DetailedEnabled) return;
        Interlocked.Add(ref searchExpansionAllocatedBytes, Math.Max(0L, bytes));
    }

    internal static void RecordSearchTransposition(long bytes)
    {
        if (!DetailedEnabled) return;
        Interlocked.Add(
            ref searchTranspositionAllocatedBytes,
            Math.Max(0L, bytes));
    }

    internal static void RecordSearchBackpropagation(long bytes)
    {
        if (!DetailedEnabled) return;
        Interlocked.Add(
            ref searchBackpropagationAllocatedBytes,
            Math.Max(0L, bytes));
    }

    internal static void RecordSearchSelection(long bytes)
    {
        if (!DetailedEnabled) return;
        Interlocked.Add(ref searchSelectionAllocatedBytes, Math.Max(0L, bytes));
    }

    internal static void RecordRootDeterminization(long bytes)
    {
        if (!DetailedEnabled) return;
        Interlocked.Add(
            ref rootDeterminizationAllocatedBytes,
            Math.Max(0L, bytes));
    }

    internal static void RecordCycleAnalysis(long bytes)
    {
        if (!DetailedEnabled) return;
        Interlocked.Add(ref cycleAnalysisAllocatedBytes, Math.Max(0L, bytes));
    }

    internal static void RecordSimulation(long bytes)
    {
        if (!DetailedEnabled)
        {
            return;
        }
        Interlocked.Add(
            ref simulationTrackedAllocatedBytes,
            Math.Max(0L, bytes));
    }
}
