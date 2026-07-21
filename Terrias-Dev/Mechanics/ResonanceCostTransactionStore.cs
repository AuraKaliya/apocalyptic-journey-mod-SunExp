using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Terrias.Dll.GameApi;
using Terrias.Dll.Infrastructure;

namespace Terrias.Dll.Mechanics;

public sealed class ResonanceCostTransactionStore
{
    private readonly Dictionary<IDataConfig, Entry> entries =
        new(ReferenceComparer<IDataConfig>.Instance);

    public ResonanceCostTransactionResult Begin(IStatusManager? owner, IDataConfig? config, int requestedPayment)
    {
        if (owner == null || config == null || requestedPayment <= 0 || entries.ContainsKey(config))
        {
            return ResonanceCostTransactionResult.None;
        }

        var paid = Math.Min(Math.Max(0, requestedPayment), CardConfigApi.CurrentCost(config));
        if (paid <= 0)
        {
            return ResonanceCostTransactionResult.None;
        }

        var entry = new Entry(owner, paid, -paid);
        entries[config] = entry;
        try
        {
            ApplyOnceCostDelta(config, entry.AppliedOnceCostDelta);
            return entry.ToResult(config);
        }
        catch
        {
            entries.Remove(config);
            throw;
        }
    }

    public bool Contains(IDataConfig? config)
    {
        return config != null && entries.ContainsKey(config);
    }

    public void MarkPaymentApplied(IDataConfig? config)
    {
        if (config != null && entries.TryGetValue(config, out var entry))
        {
            entry.PaymentApplied = true;
        }
    }

    public void MarkActionObserved(IDataConfig? config)
    {
        if (config != null && entries.TryGetValue(config, out var entry))
        {
            entry.ActionObserved = true;
        }
    }

    public bool ActionObserved(IDataConfig? config)
    {
        return config != null
            && entries.TryGetValue(config, out var entry)
            && entry.ActionObserved;
    }

    public ResonanceCostTransactionResult Cancel(IDataConfig? config)
    {
        if (config == null || !entries.TryGetValue(config, out var entry))
        {
            return ResonanceCostTransactionResult.None;
        }

        ApplyOnceCostDelta(config, -entry.AppliedOnceCostDelta);
        entries.Remove(config);
        return entry.ToResult(config);
    }

    public ResonanceCostTransactionResult Commit(IDataConfig? config)
    {
        if (config == null || !entries.TryGetValue(config, out var entry))
        {
            return ResonanceCostTransactionResult.None;
        }

        // A confirmed play consumes every one-use cost modifier on the card.
        DictionaryUtil.Set(config.Vars, "OnceExCost", "0");
        entries.Remove(config);
        return entry.ToResult(config);
    }

    public IReadOnlyList<ResonanceCostTransactionResult> CancelAll()
    {
        var cancelled = new List<ResonanceCostTransactionResult>(entries.Count);
        foreach (var pair in entries)
        {
            ApplyOnceCostDelta(pair.Key, -pair.Value.AppliedOnceCostDelta);
            cancelled.Add(pair.Value.ToResult(pair.Key));
        }

        entries.Clear();
        return cancelled;
    }

    private static void ApplyOnceCostDelta(IDataConfig config, int delta)
    {
        var current = DictionaryUtil.GetInt(config.Vars, "OnceExCost");
        DictionaryUtil.Set(config.Vars, "OnceExCost", (current + delta).ToString());
    }

    private sealed class Entry
    {
        public Entry(IStatusManager owner, int resonancePaid, int appliedOnceCostDelta)
        {
            Owner = owner;
            ResonancePaid = resonancePaid;
            AppliedOnceCostDelta = appliedOnceCostDelta;
        }

        public IStatusManager Owner { get; }

        public int ResonancePaid { get; }

        public int AppliedOnceCostDelta { get; }

        public bool PaymentApplied { get; set; }

        public bool ActionObserved { get; set; }

        public ResonanceCostTransactionResult ToResult(IDataConfig config)
        {
            return new ResonanceCostTransactionResult(
                config,
                Owner,
                ResonancePaid,
                PaymentApplied,
                ActionObserved);
        }
    }

    private sealed class ReferenceComparer<T> : IEqualityComparer<T>
        where T : class
    {
        public static readonly ReferenceComparer<T> Instance = new();

        public bool Equals(T? left, T? right)
        {
            return ReferenceEquals(left, right);
        }

        public int GetHashCode(T value)
        {
            return RuntimeHelpers.GetHashCode(value);
        }
    }
}

public readonly struct ResonanceCostTransactionResult
{
    public static readonly ResonanceCostTransactionResult None = new(null, null, 0, false, false);

    public ResonanceCostTransactionResult(
        IDataConfig? config,
        IStatusManager? owner,
        int resonancePaid,
        bool paymentApplied,
        bool actionObserved)
    {
        Config = config;
        Owner = owner;
        ResonancePaid = resonancePaid;
        PaymentApplied = paymentApplied;
        ActionObserved = actionObserved;
    }

    public IDataConfig? Config { get; }

    public IStatusManager? Owner { get; }

    public int ResonancePaid { get; }

    public bool PaymentApplied { get; }

    public bool ActionObserved { get; }

    public bool Found => Config != null;
}
