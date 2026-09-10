using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Terrias.Dll.Mechanics;

public sealed class EndlessAbyssChoiceState
{
    public int Version { get; set; } = 1;
    public string Key { get; set; } = "";
    public List<string> Offers { get; set; } = new();
    public List<bool> Refreshed { get; set; } = new();
    public List<string> Selected { get; set; } = new();

    public static EndlessAbyssChoiceState Create(string key, IReadOnlyList<string> pool, Func<int, int> pick)
    {
        var remaining = pool.Distinct(StringComparer.Ordinal).ToList();
        if (remaining.Count < 3) throw new InvalidOperationException("Abyss choices require three distinct candidates.");
        var state = new EndlessAbyssChoiceState { Key = key };
        while (state.Offers.Count < 3)
        {
            var index = pick(remaining.Count);
            state.Offers.Add(remaining[index]);
            state.Refreshed.Add(false);
            remaining.RemoveAt(index);
        }
        return state;
    }

    public void Validate(IReadOnlyList<string> pool)
    {
        if (Version != 1 || string.IsNullOrWhiteSpace(Key) || Offers == null || Refreshed == null || Selected == null
            || Offers.Count != 3 || Refreshed.Count != 3 || Offers.Distinct(StringComparer.Ordinal).Count() != 3
            || Offers.Any(id => !pool.Contains(id)) || Selected.Any(id => !Offers.Contains(id))
            || Selected.Distinct(StringComparer.Ordinal).Count() != Selected.Count)
            throw new InvalidDataException("Saved abyss choices are damaged or unsupported; refresh allowances were preserved.");
    }

    public bool Toggle(string id, int required)
    {
        if (!Offers.Contains(id)) return false;
        if (Selected.Remove(id)) return true;
        if (required == 1) Selected.Clear();
        if (Selected.Count >= required) return false;
        Selected.Add(id);
        return true;
    }

    public bool CanRefresh(int slot, IReadOnlyList<string> pool, Func<string, bool> available) =>
        slot >= 0 && slot < Offers.Count && !Refreshed[slot]
        && pool.Any(id => !Offers.Contains(id) && available(id));

    public bool Refresh(int slot, IReadOnlyList<string> pool, Func<string, bool> available, Func<int, int> pick)
    {
        if (!CanRefresh(slot, pool, available)) return false;
        var remaining = pool.Where(id => !Offers.Contains(id) && available(id)).Distinct().ToList();
        var replacement = remaining[pick(remaining.Count)];
        Selected.Remove(Offers[slot]);
        Offers[slot] = replacement;
        Refreshed[slot] = true;
        return true;
    }

    public bool IsReady(int required, Func<string, bool> available) =>
        Selected.Count == required && Selected.All(id => Offers.Contains(id) && available(id));
}
