using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;

namespace AuraGameData.Shared;

public sealed class AuraGameDataDiagnosticsSnapshot
{
    public long PointLookups { get; set; }

    public long PointHits { get; set; }

    public long CandidateResolves { get; set; }

    public long TableViews { get; set; }

    public long UniqueTypeResolves { get; set; }

    public long Materializations { get; set; }

    public long CopiedRows { get; set; }

    public long NativeCaptures { get; set; }

    public long CatalogBuilds { get; set; }

    public long CatalogBuildTicks { get; set; }

    public long PublishedEpoch { get; set; }

    public IReadOnlyDictionary<string, AuraGameDataOperationMetric> Operations { get; set; }
        = new Dictionary<string, AuraGameDataOperationMetric>(StringComparer.Ordinal);
}

public sealed class AuraGameDataOperationMetric
{
    public long Count { get; set; }

    public long ElapsedTicks { get; set; }

    public long MaximumTicks { get; set; }
}

public static class AuraGameDataDiagnostics
{
    private static long pointLookups;
    private static long pointHits;
    private static long candidateResolves;
    private static long tableViews;
    private static long uniqueTypeResolves;
    private static long materializations;
    private static long copiedRows;
    private static long nativeCaptures;
    private static long catalogBuilds;
    private static long catalogBuildTicks;
    private static long publishedEpoch;
    private static readonly object OperationGate = new();
    private static readonly Dictionary<string, AuraGameDataOperationMetric> Operations = new(StringComparer.Ordinal);

    public static AuraGameDataDiagnosticsSnapshot Snapshot()
    {
        return new AuraGameDataDiagnosticsSnapshot
        {
            PointLookups = Interlocked.Read(ref pointLookups),
            PointHits = Interlocked.Read(ref pointHits),
            CandidateResolves = Interlocked.Read(ref candidateResolves),
            TableViews = Interlocked.Read(ref tableViews),
            UniqueTypeResolves = Interlocked.Read(ref uniqueTypeResolves),
            Materializations = Interlocked.Read(ref materializations),
            CopiedRows = Interlocked.Read(ref copiedRows),
            NativeCaptures = Interlocked.Read(ref nativeCaptures),
            CatalogBuilds = Interlocked.Read(ref catalogBuilds),
            CatalogBuildTicks = Interlocked.Read(ref catalogBuildTicks),
            PublishedEpoch = Interlocked.Read(ref publishedEpoch),
            Operations = OperationSnapshot()
        };
    }

    public static void Reset()
    {
        Interlocked.Exchange(ref pointLookups, 0);
        Interlocked.Exchange(ref pointHits, 0);
        Interlocked.Exchange(ref candidateResolves, 0);
        Interlocked.Exchange(ref tableViews, 0);
        Interlocked.Exchange(ref uniqueTypeResolves, 0);
        Interlocked.Exchange(ref materializations, 0);
        Interlocked.Exchange(ref copiedRows, 0);
        Interlocked.Exchange(ref nativeCaptures, 0);
        Interlocked.Exchange(ref catalogBuilds, 0);
        Interlocked.Exchange(ref catalogBuildTicks, 0);
        Interlocked.Exchange(ref publishedEpoch, 0);
        lock (OperationGate)
        {
            Operations.Clear();
        }
    }

    public static long Timestamp()
    {
        return Stopwatch.GetTimestamp();
    }

    public static void RecordOperation(string name, long startedTimestamp)
    {
        name = (name ?? "").Trim();
        if (name.Length == 0 || startedTimestamp <= 0)
        {
            return;
        }

        var elapsed = Math.Max(0, Stopwatch.GetTimestamp() - startedTimestamp);
        lock (OperationGate)
        {
            if (!Operations.TryGetValue(name, out var metric))
            {
                metric = new AuraGameDataOperationMetric();
                Operations[name] = metric;
            }

            metric.Count++;
            metric.ElapsedTicks += elapsed;
            metric.MaximumTicks = Math.Max(metric.MaximumTicks, elapsed);
        }
    }

    internal static void RecordPointLookup(bool hit)
    {
        Interlocked.Increment(ref pointLookups);
        if (hit)
        {
            Interlocked.Increment(ref pointHits);
        }
    }

    internal static void RecordCandidateResolve()
    {
        Interlocked.Increment(ref candidateResolves);
    }

    internal static void RecordTableView()
    {
        Interlocked.Increment(ref tableViews);
    }

    internal static void RecordUniqueTypeResolve()
    {
        Interlocked.Increment(ref uniqueTypeResolves);
    }

    internal static void RecordMaterialization()
    {
        Interlocked.Increment(ref materializations);
    }

    internal static void RecordCopiedRows(int count)
    {
        Interlocked.Add(ref copiedRows, Math.Max(0, count));
    }

    internal static void RecordNativeCapture()
    {
        Interlocked.Increment(ref nativeCaptures);
    }

    internal static void RecordCatalogBuild(long elapsedTicks)
    {
        Interlocked.Increment(ref catalogBuilds);
        Interlocked.Add(ref catalogBuildTicks, Math.Max(0, elapsedTicks));
    }

    internal static void RecordPublishedEpoch(long epoch)
    {
        Interlocked.Exchange(ref publishedEpoch, Math.Max(0, epoch));
    }

    private static IReadOnlyDictionary<string, AuraGameDataOperationMetric> OperationSnapshot()
    {
        lock (OperationGate)
        {
            var result = new Dictionary<string, AuraGameDataOperationMetric>(StringComparer.Ordinal);
            foreach (var pair in Operations)
            {
                result[pair.Key] = new AuraGameDataOperationMetric
                {
                    Count = pair.Value.Count,
                    ElapsedTicks = pair.Value.ElapsedTicks,
                    MaximumTicks = pair.Value.MaximumTicks
                };
            }

            return result;
        }
    }
}
