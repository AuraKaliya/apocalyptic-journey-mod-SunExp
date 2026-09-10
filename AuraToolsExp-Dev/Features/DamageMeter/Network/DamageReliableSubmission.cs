using System;
using System.Collections.Generic;
using System.Linq;
using AuraToolsExp.Dll.Features.DamageMeter.Model;

namespace AuraToolsExp.Dll.Features.DamageMeter.Network;

internal static class DamageSubmissionProtocol
{
    internal const int MaximumBatchEvents = 64;
    internal const int MaximumBatchBytes = 44000;
    internal const int MaximumBufferedEvents = 4096;
}

internal sealed class DamageSubmissionOutbox
{
    private readonly SortedDictionary<long, DamageEvent> pending = new();
    internal int Count => pending.Count;
    internal long LastEnqueued { get; private set; }
    internal void Clear() { pending.Clear(); LastEnqueued = 0; }
    internal bool Add(DamageEvent item)
    {
        if (item.ReporterSequence <= LastEnqueued || pending.Count >= DamageSubmissionProtocol.MaximumBufferedEvents) return false;
        LastEnqueued = item.ReporterSequence;
        pending.Add(item.ReporterSequence, item.Copy());
        return true;
    }
    internal List<DamageEvent> Batch(int preferredCount, Func<List<DamageEvent>, bool> fits)
    {
        var result = new List<DamageEvent>();
        foreach (var item in pending.Values)
        {
            if (result.Count >= Math.Min(DamageSubmissionProtocol.MaximumBatchEvents, Math.Max(1, preferredCount))) break;
            result.Add(item.Copy());
            if (!fits(result)) { result.RemoveAt(result.Count - 1); break; }
        }
        return result;
    }
    internal bool Acknowledge(long through)
    {
        if (through < 0 || through > LastEnqueued) return false;
        foreach (var key in pending.Keys.TakeWhile(key => key <= through).ToArray()) pending.Remove(key);
        return true;
    }
}

internal sealed class DamageReceiveStream
{
    private readonly SortedDictionary<long, DamageEvent> pending = new();
    internal long Through { get; private set; }
    internal long? FinalSequence { get; private set; }
    internal bool IsComplete => FinalSequence.HasValue && Through >= FinalSequence.Value;
    internal bool Add(DamageEvent item)
    {
        if (item.ReporterSequence <= 0 || item.ReporterSequence > Through + DamageSubmissionProtocol.MaximumBufferedEvents) return false;
        if (item.ReporterSequence <= Through || pending.ContainsKey(item.ReporterSequence)) return true;
        if (FinalSequence.HasValue && item.ReporterSequence > FinalSequence.Value) return false;
        pending[item.ReporterSequence] = item.Copy();
        return true;
    }
    internal DamageEvent? Next => pending.TryGetValue(Through + 1, out var item) ? item : null;
    internal void ConfirmNext() { pending.Remove(++Through); }
    internal bool Complete(long lastSequence)
    {
        if (lastSequence < Through || lastSequence > Through + DamageSubmissionProtocol.MaximumBufferedEvents
            || pending.Keys.Any(sequence => sequence > lastSequence)
            || FinalSequence.HasValue && FinalSequence.Value != lastSequence) return false;
        FinalSequence = lastSequence;
        return true;
    }
}
