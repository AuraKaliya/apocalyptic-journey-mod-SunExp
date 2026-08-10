using System;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace AuraCombatAi.Shared;

public static class CombatFoundationParallelismProtocol
{
    public const string Version =
        "foundation-parallelism-v4-phase-aware-128m-reserve";

    public const long DefaultPerLaneBytes = 384L * 1024L * 1024L;

    public const long DefaultTransientPerLaneBytes = 128L * 1024L * 1024L;

    public const long MinimumReserveBytes = 128L * 1024L * 1024L;

    public const long DefaultTeacherReserveBytes =
        128L * 1024L * 1024L;

    public const long DefaultTeacherPeakBytes =
        3L * 1024L * 1024L * 1024L;

    public const int MaximumSupportedParallelism = 64;
}

public sealed class CombatFoundationResourceSnapshot
{
    public long TotalPhysicalMemoryBytes { get; set; }

    public long AvailablePhysicalMemoryBytes { get; set; }

    public long ProcessPrivateMemoryBytes { get; set; }

    public long ProcessWorkingSetBytes { get; set; }

    public long GcHeapSizeBytes { get; set; }

    public long GcFragmentedBytes { get; set; }

    public long GcTotalCommittedBytes { get; set; }

    public long GcLiveMemoryBytes { get; set; }

    public static CombatFoundationResourceSnapshot Capture()
    {
        var result = new CombatFoundationResourceSnapshot();
        try
        {
            using var process = Process.GetCurrentProcess();
            process.Refresh();
            result.ProcessPrivateMemoryBytes = Math.Max(
                0L,
                process.PrivateMemorySize64);
            result.ProcessWorkingSetBytes = Math.Max(0L, process.WorkingSet64);
        }
        catch
        {
            // Resource telemetry must never stop training.
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            var status = new MemoryStatusEx();
            if (GlobalMemoryStatusEx(status))
            {
                result.TotalPhysicalMemoryBytes = ClampToLong(status.TotalPhys);
                result.AvailablePhysicalMemoryBytes = ClampToLong(
                    status.AvailPhys);
            }
        }

#if NET8_0_OR_GREATER
        var memory = GC.GetGCMemoryInfo();
        result.GcHeapSizeBytes = Math.Max(0L, memory.HeapSizeBytes);
        result.GcFragmentedBytes = Math.Max(0L, memory.FragmentedBytes);
        result.GcTotalCommittedBytes = Math.Max(
            0L,
            memory.TotalCommittedBytes);
        result.GcLiveMemoryBytes = Math.Max(0L, GC.GetTotalMemory(false));
        if (result.TotalPhysicalMemoryBytes <= 0L)
        {
            result.TotalPhysicalMemoryBytes = Math.Max(
                0L,
                memory.TotalAvailableMemoryBytes);
        }
        if (result.AvailablePhysicalMemoryBytes <= 0L)
        {
            result.AvailablePhysicalMemoryBytes = Math.Max(
                0L,
                memory.TotalAvailableMemoryBytes - memory.MemoryLoadBytes);
        }
#else
        result.GcLiveMemoryBytes = Math.Max(0L, GC.GetTotalMemory(false));
        result.GcHeapSizeBytes = result.GcLiveMemoryBytes;
#endif
        return result;
    }

    private static long ClampToLong(ulong value)
    {
        return value > long.MaxValue ? long.MaxValue : (long)value;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalMemoryStatusEx(
        [In, Out] MemoryStatusEx buffer);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private sealed class MemoryStatusEx
    {
        public uint Length = (uint)Marshal.SizeOf(typeof(MemoryStatusEx));
        public uint MemoryLoad;
        public ulong TotalPhys;
        public ulong AvailPhys;
        public ulong TotalPageFile;
        public ulong AvailPageFile;
        public ulong TotalVirtual;
        public ulong AvailVirtual;
        public ulong AvailExtendedVirtual;
    }
}

public sealed class CombatFoundationParallelismDecision
{
    public string ProtocolVersion { get; set; } =
        CombatFoundationParallelismProtocol.Version;

    public int Iteration { get; set; }

    public int RequestedMaximumParallelism { get; set; }

    public int SelectedParallelism { get; set; }

    public int CapacityParallelism { get; set; }

    public long FixedProcessBytes { get; set; }

    public long ProcessWorkingSetBytes { get; set; }

    public long GcHeapSizeBytes { get; set; }

    public long GcFragmentedBytes { get; set; }

    public long GcTotalCommittedBytes { get; set; }

    public long GcLiveMemoryBytes { get; set; }

    public long AvailablePhysicalMemoryBytes { get; set; }

    public long MemoryReserveBytes { get; set; }

    public long MemoryHeadroomBytes { get; set; }

    public long PredictedPerLaneBytes { get; set; }

    public long PredictedPeakPrivateBytes { get; set; }

    public long ReleasedSearchMemoryBytes { get; set; }

    public int RetainedPlannerCount { get; set; }

    public string Reason { get; set; } = "";
}

public static class CombatFoundationMemoryExecutionPolicy
{
    private const long Gibibyte = 1024L * 1024L * 1024L;

    public static int SelectIterationsPerProcess(
        int configuredIterations,
        CombatFoundationResourceSnapshot resources)
    {
        resources ??= new CombatFoundationResourceSnapshot();
        var configured = Math.Max(1, Math.Min(6, configuredIterations));
        var total = Math.Max(0L, resources.TotalPhysicalMemoryBytes);
        var available = Math.Max(0L, resources.AvailablePhysicalMemoryBytes);
        if ((total > 0L && total <= 40L * Gibibyte)
            || (available > 0L && available <= 12L * Gibibyte))
        {
            return 1;
        }
        if ((total > 0L && total <= 64L * Gibibyte)
            || (available > 0L && available <= 24L * Gibibyte))
        {
            return Math.Min(2, configured);
        }
        return configured;
    }

    public static int SelectModelTrainingParallelism(
        int configuredParallelism,
        CombatFoundationResourceSnapshot resources)
    {
        resources ??= new CombatFoundationResourceSnapshot();
        var configured = Math.Max(
            1,
            Math.Min(
                CombatFoundationParallelismProtocol.MaximumSupportedParallelism,
                configuredParallelism));
        var total = Math.Max(0L, resources.TotalPhysicalMemoryBytes);
        var available = Math.Max(0L, resources.AvailablePhysicalMemoryBytes);
        if ((total > 0L && total <= 40L * Gibibyte)
            || (available > 0L && available <= 12L * Gibibyte))
        {
            return Math.Min(12, configured);
        }
        if ((total > 0L && total <= 64L * Gibibyte)
            || (available > 0L && available <= 24L * Gibibyte))
        {
            return Math.Min(20, configured);
        }
        return configured;
    }

    public static CombatFoundationMemoryExecutionDecision SelectAdaptive(
        int configuredIterations,
        int configuredModelParallelism,
        CombatFoundationResourceSnapshot resources,
        CombatFoundationSegmentResourceObservation? previous)
    {
        resources ??= new CombatFoundationResourceSnapshot();
        var iterations = SelectIterationsPerProcess(
            configuredIterations,
            resources);
        var modelParallelism = SelectModelTrainingParallelism(
            configuredModelParallelism,
            resources);
        if (previous == null)
        {
            return new CombatFoundationMemoryExecutionDecision
            {
                IterationsPerProcess = iterations,
                ModelTrainingParallelism = modelParallelism,
                Mode = "cold-start-conservative",
                Reason = "no prior isolated-segment memory observation"
            };
        }

        var workerPeak = Math.Max(
            previous.WorkerPeakWorkingSetBytes,
            previous.EndPrivateMemoryBytes);
        var observedTreePeak = SaturatingAdd(
            workerPeak,
            previous.TransformerPeakWorkingSetBytes);
        var total = Math.Max(0L, resources.TotalPhysicalMemoryBytes);
        var available = Math.Max(0L, resources.AvailablePhysicalMemoryBytes);
        var reserve = Math.Max(4L * Gibibyte, total / 8L);
        var fragmented = Math.Max(0L, previous.GcFragmentedBytes);
        var heap = Math.Max(1L, previous.GcHeapSizeBytes);
        var fragmentationPressure = fragmented > 512L * 1024L * 1024L
                                    && fragmented * 4L > heap;
        var predictedNextPeak = observedTreePeak <= 0L
            ? 0L
            : SaturatingAdd(observedTreePeak, observedTreePeak / 4L);
        var healthy = !previous.ResourceFailure
                      && !fragmentationPressure
                      && (available <= 0L
                          || predictedNextPeak <= Math.Max(
                              0L,
                              available - reserve));
        if (!healthy)
        {
            return new CombatFoundationMemoryExecutionDecision
            {
                IterationsPerProcess = 1,
                ModelTrainingParallelism = Math.Min(
                    12,
                    Math.Max(1, configuredModelParallelism)),
                Mode = "pressure-backoff",
                ObservedProcessTreePeakBytes = observedTreePeak,
                PredictedNextPeakBytes = predictedNextPeak,
                ReserveBytes = reserve,
                Reason = previous.ResourceFailure
                    ? "previous segment reported a resource failure"
                    : fragmentationPressure
                        ? "managed heap fragmentation exceeded the safety ratio"
                        : "observed process-tree peak does not fit current headroom"
            };
        }

        var configuredIterationLimit = Math.Max(
            1,
            Math.Min(6, configuredIterations));
        iterations = Math.Min(
            configuredIterationLimit,
            Math.Max(2, previous.IterationsPerProcess + 1));
        var configuredModelLimit = Math.Max(
            1,
            Math.Min(
                CombatFoundationParallelismProtocol.MaximumSupportedParallelism,
                configuredModelParallelism));
        modelParallelism = Math.Min(
            configuredModelLimit,
            Math.Max(20, previous.ModelTrainingParallelism + 8));
        return new CombatFoundationMemoryExecutionDecision
        {
            IterationsPerProcess = iterations,
            ModelTrainingParallelism = modelParallelism,
            Mode = "observed-healthy-ramp",
            ObservedProcessTreePeakBytes = observedTreePeak,
            PredictedNextPeakBytes = predictedNextPeak,
            ReserveBytes = reserve,
            Reason = "prior segment completed within measured memory headroom"
        };
    }

    private static long SaturatingAdd(long left, long right)
    {
        if (left <= 0L) return Math.Max(0L, right);
        if (right <= 0L) return left;
        return left > long.MaxValue - right ? long.MaxValue : left + right;
    }
}

public sealed class CombatFoundationSegmentResourceObservation
{
    public int IterationsPerProcess { get; set; } = 1;

    public int ModelTrainingParallelism { get; set; } = 1;

    public long WorkerPeakWorkingSetBytes { get; set; }

    public long EndPrivateMemoryBytes { get; set; }

    public long TransformerPeakWorkingSetBytes { get; set; }

    public long GcHeapSizeBytes { get; set; }

    public long GcFragmentedBytes { get; set; }

    public bool ResourceFailure { get; set; }
}

public sealed class CombatFoundationMemoryExecutionDecision
{
    public int IterationsPerProcess { get; set; } = 1;

    public int ModelTrainingParallelism { get; set; } = 1;

    public string Mode { get; set; } = "";

    public long ObservedProcessTreePeakBytes { get; set; }

    public long PredictedNextPeakBytes { get; set; }

    public long ReserveBytes { get; set; }

    public string Reason { get; set; } = "";
}

public static class CombatFoundationParallelismPlanner
{
    public static CombatFoundationParallelismDecision Select(
        int iteration,
        int requestedMaximumParallelism,
        CombatFoundationResourceSnapshot resources,
        CombatSearchMemoryTrimReport? trim = null,
        long configuredPerLaneBytes = 0L,
        long configuredReserveBytes = 0L)
    {
        resources ??= new CombatFoundationResourceSnapshot();
        trim ??= new CombatSearchMemoryTrimReport();
        var requested = Math.Max(
            1,
            Math.Min(
                CombatFoundationParallelismProtocol.MaximumSupportedParallelism,
                requestedMaximumParallelism));
        var reserve = configuredReserveBytes > 0L
            ? configuredReserveBytes
            : CombatFoundationParallelismProtocol.MinimumReserveBytes;
        var observedPerPlanner = trim.PlannerCount <= 0
            ? 0L
            : trim.ReleasedEstimatedBytes / trim.PlannerCount;
        var perLaneFloor = configuredPerLaneBytes > 0L
            ? configuredPerLaneBytes
            : CombatFoundationParallelismProtocol.DefaultPerLaneBytes;
        var perLane = Math.Max(
            perLaneFloor,
            SaturatingAdd(
                observedPerPlanner + observedPerPlanner / 4L,
                CombatFoundationParallelismProtocol
                    .DefaultTransientPerLaneBytes));
        var available = Math.Max(0L, resources.AvailablePhysicalMemoryBytes);
        var headroom = Math.Max(0L, available - reserve);
        var rawCapacity = perLane <= 0L
            ? requested
            : (int)Math.Min(
                CombatFoundationParallelismProtocol.MaximumSupportedParallelism,
                headroom / perLane);
        var selected = Math.Max(1, Math.Min(requested, rawCapacity));
        var predictedLanes = SaturatingMultiply(perLane, selected);
        return new CombatFoundationParallelismDecision
        {
            Iteration = Math.Max(1, iteration),
            RequestedMaximumParallelism = requested,
            SelectedParallelism = selected,
            CapacityParallelism = Math.Max(0, rawCapacity),
            FixedProcessBytes = Math.Max(
                0L,
                resources.ProcessPrivateMemoryBytes),
            ProcessWorkingSetBytes = Math.Max(
                0L,
                resources.ProcessWorkingSetBytes),
            GcHeapSizeBytes = Math.Max(0L, resources.GcHeapSizeBytes),
            GcFragmentedBytes = Math.Max(0L, resources.GcFragmentedBytes),
            GcTotalCommittedBytes = Math.Max(
                0L,
                resources.GcTotalCommittedBytes),
            GcLiveMemoryBytes = Math.Max(0L, resources.GcLiveMemoryBytes),
            AvailablePhysicalMemoryBytes = available,
            MemoryReserveBytes = reserve,
            MemoryHeadroomBytes = headroom,
            PredictedPerLaneBytes = perLane,
            PredictedPeakPrivateBytes = SaturatingAdd(
                Math.Max(0L, resources.ProcessPrivateMemoryBytes),
                predictedLanes),
            ReleasedSearchMemoryBytes = Math.Max(
                0L,
                trim.ReleasedEstimatedBytes),
            RetainedPlannerCount = Math.Max(0, trim.PlannerCount),
            Reason = "memory-capacity: available=" + available
                     + ", reserve=" + reserve
                     + ", perLane=" + perLane
                     + ", rawCapacity=" + rawCapacity
                     + ", selected=" + selected
        };
    }

    private static long SaturatingAdd(long left, long right)
    {
        left = Math.Max(0L, left);
        right = Math.Max(0L, right);
        return left > long.MaxValue - right ? long.MaxValue : left + right;
    }

    private static long SaturatingMultiply(long value, int multiplier)
    {
        value = Math.Max(0L, value);
        multiplier = Math.Max(0, multiplier);
        return multiplier == 0 || value == 0L
            ? 0L
            : value > long.MaxValue / multiplier
                ? long.MaxValue
                : value * multiplier;
    }
}
