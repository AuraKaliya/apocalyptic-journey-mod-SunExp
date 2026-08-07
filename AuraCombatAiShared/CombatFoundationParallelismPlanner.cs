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
