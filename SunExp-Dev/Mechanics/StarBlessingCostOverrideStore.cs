using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using SunExp.Dll.GameApi;
using SunExp.Dll.Infrastructure;

namespace SunExp.Dll.Mechanics;

public sealed class StarBlessingCostOverrideStore
{
    private readonly Dictionary<IDataConfig, Entry> entries =
        new(ReferenceComparer<IDataConfig>.Instance);

    public bool BeginPreview(IDataConfig? config)
    {
        if (config == null || entries.ContainsKey(config))
        {
            return false;
        }

        var currentCost = CardConfigApi.CurrentCost(config);
        var originalOnceCost = DictionaryUtil.GetInt(config.Vars, "OnceExCost");
        entries[config] = new Entry(originalOnceCost);
        DictionaryUtil.Set(config.Vars, "OnceExCost", (originalOnceCost - currentCost).ToString());
        return true;
    }

    public bool Contains(IDataConfig? config)
    {
        return config != null && entries.ContainsKey(config);
    }

    public void MarkBlessingConsumed(IDataConfig? config)
    {
        if (config != null && entries.TryGetValue(config, out var entry))
        {
            entry.BlessingConsumed = true;
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

    public StarBlessingCostOverrideResult Cancel(IDataConfig? config)
    {
        if (config == null || !entries.TryGetValue(config, out var entry))
        {
            return StarBlessingCostOverrideResult.None;
        }

        DictionaryUtil.Set(config.Vars, "OnceExCost", entry.OriginalOnceCost.ToString());
        entries.Remove(config);
        return new StarBlessingCostOverrideResult(config, entry.BlessingConsumed);
    }

    public StarBlessingCostOverrideResult Commit(IDataConfig? config)
    {
        if (config == null || !entries.TryGetValue(config, out var entry))
        {
            return StarBlessingCostOverrideResult.None;
        }

        // OnceExCost is a one-use modifier. A successful play consumes both the
        // blessing override and any other one-use cost modifier already on the card.
        DictionaryUtil.Set(config.Vars, "OnceExCost", "0");
        entries.Remove(config);
        return new StarBlessingCostOverrideResult(config, entry.BlessingConsumed);
    }

    public void CancelAll()
    {
        foreach (var pair in entries)
        {
            DictionaryUtil.Set(pair.Key.Vars, "OnceExCost", pair.Value.OriginalOnceCost.ToString());
        }

        entries.Clear();
    }

    private sealed class Entry
    {
        public Entry(int originalOnceCost)
        {
            OriginalOnceCost = originalOnceCost;
        }

        public int OriginalOnceCost { get; }

        public bool BlessingConsumed { get; set; }

        public bool ActionObserved { get; set; }
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

public readonly struct StarBlessingCostOverrideResult
{
    public static readonly StarBlessingCostOverrideResult None = new(null, false);

    public StarBlessingCostOverrideResult(IDataConfig? config, bool blessingConsumed)
    {
        Config = config;
        BlessingConsumed = blessingConsumed;
    }

    public IDataConfig? Config { get; }

    public bool BlessingConsumed { get; }
}
