using System;
using System.Collections.Generic;

namespace Terrias.Dll.Hooks.Ui;

internal static class SpiritWarehouseSelectionPolicy
{
    public static string ResolveInitial(
        string? rememberedUid,
        string? activeUid,
        IReadOnlyList<string>? visibleUids)
    {
        var visible = NormalizeVisible(visibleUids);
        if (visible.Count == 0) return "";

        var remembered = Normalize(rememberedUid);
        if (remembered.Length > 0 && visible.Contains(remembered)) return remembered;

        var active = Normalize(activeUid);
        if (active.Length > 0 && visible.Contains(active)) return active;

        return visible[0];
    }

    public static string ResolveVisible(string? selectedUid, IReadOnlyList<string>? visibleUids)
    {
        var visible = NormalizeVisible(visibleUids);
        if (visible.Count == 0) return "";

        var selected = Normalize(selectedUid);
        return selected.Length > 0 && visible.Contains(selected) ? selected : visible[0];
    }

    private static List<string> NormalizeVisible(IReadOnlyList<string>? visibleUids)
    {
        var result = new List<string>();
        if (visibleUids == null) return result;

        var seen = new HashSet<string>(StringComparer.Ordinal);
        for (var i = 0; i < visibleUids.Count; i++)
        {
            var uid = Normalize(visibleUids[i]);
            if (uid.Length > 0 && seen.Add(uid)) result.Add(uid);
        }

        return result;
    }

    private static string Normalize(string? uid) => (uid ?? "").Trim();
}
