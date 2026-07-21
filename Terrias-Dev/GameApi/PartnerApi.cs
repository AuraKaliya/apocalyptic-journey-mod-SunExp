using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using Terrias.Dll.Infrastructure;

namespace Terrias.Dll.GameApi;

public static class PartnerApi
{
    public static bool IsCurrentPartner(string localId, string fullId)
    {
        var id = CurrentPartnerId();
        return TerriasContentIdCompatibility.Equivalent(id, localId)
            || TerriasContentIdCompatibility.Equivalent(id, fullId)
            || (!string.IsNullOrWhiteSpace(id) && id.EndsWith("_" + localId, StringComparison.OrdinalIgnoreCase));
    }

    public static void RemovePlaceholderBlessing(string localId, string fullId)
    {
        var role = RoleTable.Instance;
        if (role == null)
        {
            return;
        }

        RemoveMatchingDataConfigs(Member(role, "blessingConfigs"), localId, fullId);
        RemoveMatchingStrings(Member(role, "ExtraordinaryBlessings"), localId, fullId);
    }

    public static string CurrentPartnerId()
    {
        var partner = StaticMember(FindType("GameEntryUI"), "partner");
        return DataConfigId(partner);
    }

    private static void RemoveMatchingDataConfigs(object? list, string localId, string fullId)
    {
        if (list == null)
        {
            return;
        }

        var removeAt = list.GetType().GetMethod("RemoveAt", new[] { typeof(int) });
        if (removeAt == null)
        {
            return;
        }

        for (var i = Count(list) - 1; i >= 0; i--)
        {
            if (MatchesId(DataConfigId(Item(list, i)), localId, fullId))
            {
                removeAt.Invoke(list, new object[] { i });
            }
        }
    }

    private static void RemoveMatchingStrings(object? list, string localId, string fullId)
    {
        if (list == null)
        {
            return;
        }

        var removeAt = list.GetType().GetMethod("RemoveAt", new[] { typeof(int) });
        if (removeAt == null)
        {
            return;
        }

        for (var i = Count(list) - 1; i >= 0; i--)
        {
            if (MatchesId(Convert.ToString(Item(list, i)) ?? "", localId, fullId))
            {
                removeAt.Invoke(list, new object[] { i });
            }
        }
    }

    private static bool MatchesId(string value, string localId, string fullId)
    {
        return TerriasContentIdCompatibility.Equivalent(value, localId)
            || TerriasContentIdCompatibility.Equivalent(value, fullId)
            || (!string.IsNullOrWhiteSpace(value) && value.EndsWith("_" + localId, StringComparison.OrdinalIgnoreCase));
    }

    private static string DataConfigId(object? config)
    {
        var data = Member(config, "data") as IDictionary<string, string>;
        return data != null && data.TryGetValue("Id", out var id) ? id : "";
    }

    private static int Count(object? collection)
    {
        return collection is ICollection concrete ? concrete.Count : 0;
    }

    private static object? Item(object? collection, int index)
    {
        try
        {
            return collection?.GetType().GetMethod("get_Item")?.Invoke(collection, new object[] { index });
        }
        catch
        {
            return null;
        }
    }

    private static object? Member(object? target, string name)
    {
        if (target == null)
        {
            return null;
        }

        const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        return target.GetType().GetProperty(name, flags)?.GetValue(target)
            ?? target.GetType().GetField(name, flags)?.GetValue(target);
    }

    private static object? StaticMember(Type? type, string name)
    {
        if (type == null)
        {
            return null;
        }

        const BindingFlags flags = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
        return type.GetProperty(name, flags)?.GetValue(null)
            ?? type.GetField(name, flags)?.GetValue(null);
    }

    private static Type? FindType(string name)
    {
        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            try
            {
                foreach (var type in assembly.GetTypes())
                {
                    if (type.Name == name || type.FullName == name)
                    {
                        return type;
                    }
                }
            }
            catch
            {
                // Some runtime assemblies can reject GetTypes.
            }
        }

        return null;
    }
}
