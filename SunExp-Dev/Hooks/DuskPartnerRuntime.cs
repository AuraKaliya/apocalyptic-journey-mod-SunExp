using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using SunExp.Dll.Infrastructure;
using Witch.Core;
using Witch.Mod;

namespace SunExp.Dll.Hooks;

public static class DuskPartnerRuntime
{
    private const string DuskPartnerLocalId = "dusk";
    private const string DuskPartnerFullId = "SunExp_sunexp_dusk";
    private const string DuskBlessingLocalId = "dusk_afterheat_recovery";
    private const string DuskBlessingFullId = "SunExp_sunexp_dusk_afterheat_recovery";

    public static void Initialize(ModConfig modConfig)
    {
        RegisterAfter(modConfig, "GameEntryUI.CheckCareer", RemoveDuskPlaceholderBlessing);
        RegisterAfter(modConfig, "Fight_Start.Init", GrantDuskTraitOnFightStart);
    }

    private static void RegisterAfter(ModConfig config, string target, Action<ModHookContext> action)
    {
        try
        {
            config.AddMethodHookAfter(target, action);
            SunExpLog.Debug("Dusk partner hook registered: " + target);
        }
        catch (Exception ex)
        {
            SunExpLog.Warn("Dusk partner hook failed: " + target + " -> " + ex.Message);
        }
    }

    private static void RemoveDuskPlaceholderBlessing(ModHookContext context)
    {
        try
        {
            RemoveDuskPlaceholderBlessing();
        }
        catch (Exception ex)
        {
            SunExpLog.Error("Dusk placeholder blessing cleanup failed", ex);
        }
    }

    private static void GrantDuskTraitOnFightStart(ModHookContext context)
    {
        try
        {
            RemoveDuskPlaceholderBlessing();
            if (!IsCurrentPartnerDusk())
            {
                return;
            }

            var status = FightPlayer.Instance?.Status;
            if (status == null)
            {
                return;
            }

            status.AddBuff(SunExpIds.DuskAfterheatRecoveryTrait, 1);
            SunExpLog.Debug("Granted Dusk afterheat recovery trait at fight start.");
        }
        catch (Exception ex)
        {
            SunExpLog.Error("Dusk fight start trait grant failed", ex);
        }
    }

    private static bool IsCurrentPartnerDusk()
    {
        var partner = StaticMember(FindType("GameEntryUI"), "partner");
        var id = DataConfigId(partner);
        return IsDuskPartnerId(id);
    }

    private static bool IsDuskPartnerId(string? id)
    {
        var value = id ?? "";
        return string.Equals(id, DuskPartnerLocalId, StringComparison.OrdinalIgnoreCase)
            || string.Equals(id, DuskPartnerFullId, StringComparison.OrdinalIgnoreCase)
            || (!string.IsNullOrWhiteSpace(value) && value.EndsWith("_" + DuskPartnerLocalId, StringComparison.OrdinalIgnoreCase));
    }

    private static void RemoveDuskPlaceholderBlessing()
    {
        var role = RoleTable.Instance;
        if (role == null)
        {
            return;
        }

        RemoveMatchingDataConfigs(Member(role, "blessingConfigs"));
        RemoveMatchingStrings(Member(role, "ExtraordinaryBlessings"));
    }

    private static void RemoveMatchingDataConfigs(object? list)
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
            if (IsDuskBlessingId(DataConfigId(Item(list, i))))
            {
                removeAt.Invoke(list, new object[] { i });
            }
        }
    }

    private static void RemoveMatchingStrings(object? list)
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
            if (IsDuskBlessingId(Convert.ToString(Item(list, i))))
            {
                removeAt.Invoke(list, new object[] { i });
            }
        }
    }

    private static bool IsDuskBlessingId(string? id)
    {
        var value = id ?? "";
        return string.Equals(id, DuskBlessingLocalId, StringComparison.OrdinalIgnoreCase)
            || string.Equals(id, DuskBlessingFullId, StringComparison.OrdinalIgnoreCase)
            || (!string.IsNullOrWhiteSpace(value) && value.EndsWith("_" + DuskBlessingLocalId, StringComparison.OrdinalIgnoreCase));
    }

    private static string DataConfigId(object? config)
    {
        var data = Member(config, "data") as IDictionary<string, string>;
        if (data == null)
        {
            return "";
        }

        return data.TryGetValue("Id", out var id) ? id : "";
    }

    private static int Count(object? collection)
    {
        if (collection is ICollection concrete)
        {
            return concrete.Count;
        }

        return 0;
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
                // Some runtime assemblies can reject GetTypes; skip them.
            }
        }

        return null;
    }
}
