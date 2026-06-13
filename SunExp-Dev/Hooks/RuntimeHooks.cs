using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using SunExp.Dll.GameApi;
using SunExp.Dll.Infrastructure;
using Witch.Core;
using Witch.Mod;

namespace SunExp.Dll.Hooks;

public static class RuntimeHooks
{
    private const string SolarMapId = "SunExp_sunexp_solar_event";
    private const string SolarShortMapId = "solar_event";

    public static void Initialize(ModConfig modConfig)
    {
        RegisterBefore(modConfig, "MapSelectUI.ReadyToSelect", EnsureSolarEventInCurrentLayerFromHook);
        RegisterAfter(modConfig, "NormalMapManager.RandomGenerate", EnsureSolarEventInCurrentLayerFromHook);
        RegisterAfter(modConfig, "NormalMapManager.GeneratrMap", EnsureSolarEventInCurrentLayerFromHook);
        RegisterBefore(modConfig, "MapManager.UserCode_CmdSelectMap__String[]__String[]__NetworkConnectionToClient", RepairSolarEventMapSelection);
        RegisterBefore(modConfig, "MapManager.UserCode_CmdSelectMapIncludeSender__String[]__String[]__NetworkConnectionToClient", RepairSolarEventMapSelection);
        RegisterBefore(modConfig, "MapManager.CmdSelectMap", RepairSolarEventMapSelection);
        RegisterBefore(modConfig, "MapManager.CmdSelectMapIncludeSender", RepairSolarEventMapSelection);
        RegisterBefore(modConfig, "MapManager.TargetUpdateMap", RepairSolarEventMapSelection);
        RegisterBefore(modConfig, "MapManager.RpcUpdateMap", RepairSolarEventMapSelection);
        RegisterBefore(modConfig, "ScriptExecutor.AddBuff", OnScriptExecutorAddBuffBefore);
        AnimatedBlessingIconRuntime.Initialize(modConfig);
        SunExpLog.Info("Runtime hooks registered");
    }

    private static void RegisterBefore(ModConfig config, string target, Action<ModHookContext> action)
    {
        try
        {
            config.AddMethodHookBefore(target, action);
            SunExpLog.Debug("Hook before registered: " + target);
        }
        catch (Exception ex)
        {
            SunExpLog.Warn("Hook before failed: " + target + " -> " + ex.Message);
        }
    }

    private static void RegisterAfter(ModConfig config, string target, Action<ModHookContext> action)
    {
        try
        {
            config.AddMethodHookAfter(target, action);
            SunExpLog.Debug("Hook after registered: " + target);
        }
        catch (Exception ex)
        {
            SunExpLog.Warn("Hook after failed: " + target + " -> " + ex.Message);
        }
    }

    private static void OnScriptExecutorAddBuffBefore(ModHookContext context)
    {
        try
        {
            var executor = context.Target as ScriptExecutor;
            var args = context.Arguments;
            var buffId = Convert.ToString(args != null && args.Length > 0 ? args[0] : null);
            if (executor == null || buffId != SunExpIds.Burn)
            {
                return;
            }

            var amount = DictionaryUtil.ParseInt(Convert.ToString(args != null && args.Length > 1 ? args[1] : null));
            if (amount <= 0)
            {
                return;
            }

            var handled = new HashSet<string>(StringComparer.Ordinal);
            foreach (var target in HookTargets(executor))
            {
                var key = target.InstanceId ?? target.GetHashCode().ToString();
                if (handled.Add(key))
                {
                    ExecutorApi.HandleBurnOverflow(executor, target, buffId, amount);
                }
            }
        }
        catch (Exception ex)
        {
            SunExpLog.Error("AddBuff before hook failed", ex);
        }
    }

    private static IEnumerable<IStatusManager> HookTargets(ScriptExecutor executor)
    {
        foreach (var target in executor.Object ?? new List<IStatusManager>())
        {
            if (target != null)
            {
                yield return target;
            }
        }

        if (executor.Target != null)
        {
            yield return executor.Target;
        }

        if (executor.Self != null)
        {
            yield return executor.Self;
        }
    }

    private static void EnsureSolarEventInCurrentLayerFromHook(ModHookContext context)
    {
        try
        {
            if (!EnsureSolarEventInCurrentLayer(context.Target))
            {
                foreach (var arg in context.Arguments ?? Array.Empty<object>())
                {
                    if (EnsureSolarEventInCurrentLayer(arg))
                    {
                        return;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            SunExpLog.Error("Solar event layer hook failed", ex);
        }
    }

    private static bool EnsureSolarEventInCurrentLayer(object? treeOrManager)
    {
        var nodes = GetSelectNodes(treeOrManager);
        var count = Count(nodes);
        if (nodes == null || count <= 0)
        {
            return false;
        }

        var startIndex = Math.Max(0, GetCurrentMapLevelNumber() / 6 * GetSolarLayerSegmentSize());
        if (startIndex >= count)
        {
            return false;
        }

        var endIndex = Math.Min(count - 1, startIndex + GetSolarLayerSegmentSize() - 1);
        object? firstEvent = null;
        object? firstFallback = null;
        for (var i = startIndex; i <= endIndex; i++)
        {
            var node = Item(nodes, i);
            if (node == null)
            {
                continue;
            }

            if (IsSolarEventNode(node))
            {
                return TrySetSolarEventNode(node);
            }

            if (IsBreakNode(node) || IsProtectedFixedEventNode(node))
            {
                continue;
            }

            firstFallback ??= node;
            if (firstEvent == null && IsEventNode(node))
            {
                firstEvent = node;
            }
        }

        return TrySetSolarEventNode(firstEvent ?? firstFallback);
    }

    private static object? GetSelectNodes(object? treeOrManager)
    {
        var tree = Member(treeOrManager, "MapTree") ?? Member(treeOrManager, "mapTree") ?? treeOrManager;
        return Member(tree, "SelectNode") ?? Member(CurrentMapTree(), "SelectNode");
    }

    private static object? CurrentMapTree()
    {
        var manager = Type.GetType("MapManager")?.GetProperty("Instance")?.GetValue(null)
            ?? AppDomain.CurrentDomain.GetAssemblies()
                .Select(asm => asm.GetType("MapManager"))
                .FirstOrDefault(type => type != null)
                ?.GetProperty("Instance")?.GetValue(null);
        return Member(manager, "MapTree") ?? Member(Member(manager, "ModeMapManager"), "MapTree");
    }

    private static bool TrySetSolarEventNode(object? node)
    {
        if (node == null)
        {
            return false;
        }

        var changed = false;
        changed = SetNodeData(node, "Id", SolarMapId) || changed;
        changed = SetNodeData(node, "Type", "Event") || changed;
        changed = SetNodeData(node, "NodeId", CurrentSolarEventId()) || changed;
        changed = SetNodeData(node, "Level", "-1") || changed;
        SetMember(node, "type", "Event");
        return changed;
    }

    private static bool IsSolarEventNode(object node)
    {
        return IsSolarEventMapId(GetNodeData(node, "Id"));
    }

    private static bool IsSolarEventMapId(object? id)
    {
        var value = Convert.ToString(id);
        return value == SolarMapId || value == SolarShortMapId;
    }

    private static bool IsEventNode(object node)
    {
        var typeName = Convert.ToString(GetNodeData(node, "Type"));
        if (typeName == "Event")
        {
            return true;
        }

        return Convert.ToString(Member(node, "type")) == "Event";
    }

    private static bool IsBreakNode(object node)
    {
        var nodeId = Convert.ToString(GetNodeData(node, "NodeId"));
        var id = Convert.ToString(GetNodeData(node, "Id"));
        return (nodeId?.Contains("Breaks") ?? false) || (id?.Contains("Breaks") ?? false);
    }

    private static bool IsProtectedFixedEventNode(object node)
    {
        var nodeId = Convert.ToString(GetNodeData(node, "NodeId"));
        return nodeId is "event_2001" or "event_2002" or "event_2003" or "event_2004" or "event_2005" or "event_2006" or "event_2015" or "event_999";
    }

    private static string CurrentSolarEventId()
    {
        var progress = DictionaryUtil.ParseInt(PlayerApi.GetGameVar("SunExp_WunaEventProgressV2", "0"));
        if (progress >= 6)
        {
            return "SunExp_sunexp_Sub_wuna_event_repeat";
        }

        var next = Math.Min(6, Math.Max(1, progress + 1));
        return "SunExp_sunexp_Sub_wuna_event_" + next.ToString("00");
    }

    private static int GetCurrentMapLevelNumber()
    {
        var manager = AppDomain.CurrentDomain.GetAssemblies()
            .Select(asm => asm.GetType("MapManager"))
            .FirstOrDefault(type => type != null)
            ?.GetProperty("Instance")?.GetValue(null);
        return DictionaryUtil.ParseInt(Convert.ToString(Member(manager, "Level") ?? Member(Member(manager, "ModeMapManager"), "Level")));
    }

    private static int GetSolarLayerSegmentSize()
    {
        var exDelete = 0;
        try
        {
            var saveManager = AppDomain.CurrentDomain.GetAssemblies()
                .Select(asm => asm.GetType("GameSaveManager"))
                .FirstOrDefault(type => type != null);
            var gameVar = AppDomain.CurrentDomain.GetAssemblies()
                .Select(asm => asm.GetType("GameVar"))
                .FirstOrDefault(type => type != null);
            var exDeleteKey = gameVar?.GetField("ExDeleteDes")?.GetValue(null) ?? "ExDeleteDes";
            var value = saveManager?.GetMethod("GetValue")?.Invoke(null, new[] { exDeleteKey });
            exDelete = DictionaryUtil.ParseInt(Convert.ToString(value));
        }
        catch
        {
            exDelete = 0;
        }

        return Math.Max(1, 8 - exDelete);
    }

    private static void RepairSolarEventMapSelection(ModHookContext context)
    {
        try
        {
            var args = context.Arguments ?? Array.Empty<object>();
            for (var i = 0; i < args.Length - 1; i++)
            {
                if (RepairSolarEventMapArrays(args[i], args[i + 1]))
                {
                    SunExpLog.Debug("Solar event map selection repaired");
                }
            }
        }
        catch (Exception ex)
        {
            SunExpLog.Error("Solar event map selection hook failed", ex);
        }
    }

    private static bool RepairSolarEventMapArrays(object? maps, object? mapData)
    {
        var count = Count(maps);
        if (maps == null || mapData == null || count <= 0)
        {
            return false;
        }

        var changed = false;
        for (var i = 0; i < count; i++)
        {
            if (!IsSolarEventMapId(Item(maps, i)))
            {
                continue;
            }

            changed = SetItem(maps, i, SolarMapId) || changed;
            changed = SetItem(mapData, i, CurrentSolarEventId()) || changed;
        }

        return changed;
    }

    private static object? GetNodeData(object node, string key)
    {
        var data = Member(node, "data");
        return DictionaryGet(data, key);
    }

    private static bool SetNodeData(object node, string key, string value)
    {
        var data = Member(node, "data");
        return DictionarySet(data, key, value);
    }

    private static object? DictionaryGet(object? dictionary, string key)
    {
        if (dictionary == null)
        {
            return null;
        }

        try
        {
            var contains = dictionary.GetType().GetMethod("ContainsKey")?.Invoke(dictionary, new object[] { key });
            if (contains is bool hasKey && !hasKey)
            {
                return null;
            }

            return dictionary.GetType().GetMethod("get_Item")?.Invoke(dictionary, new object[] { key });
        }
        catch
        {
            return null;
        }
    }

    private static bool DictionarySet(object? dictionary, string key, string value)
    {
        if (dictionary == null)
        {
            return false;
        }

        try
        {
            dictionary.GetType().GetMethod("set_Item")?.Invoke(dictionary, new object[] { key, value });
            return true;
        }
        catch
        {
            try
            {
                dictionary.GetType().GetMethod("Set")?.Invoke(dictionary, new object[] { key, value });
                return true;
            }
            catch
            {
                return false;
            }
        }
    }

    private static int Count(object? collection)
    {
        if (collection == null)
        {
            return 0;
        }

        if (collection is ICollection concrete)
        {
            return concrete.Count;
        }

        return DictionaryUtil.ParseInt(Convert.ToString(Member(collection, "Count")));
    }

    private static object? Item(object? collection, int index)
    {
        if (collection == null)
        {
            return null;
        }

        try
        {
            return collection.GetType().GetMethod("get_Item")?.Invoke(collection, new object[] { index });
        }
        catch
        {
            return null;
        }
    }

    private static bool SetItem(object? collection, int index, object value)
    {
        if (collection == null)
        {
            return false;
        }

        try
        {
            collection.GetType().GetMethod("set_Item")?.Invoke(collection, new[] { (object)index, value });
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static object? Member(object? target, string name)
    {
        if (target == null)
        {
            return null;
        }

        try
        {
            return target.GetType().GetProperty(name)?.GetValue(target)
                ?? target.GetType().GetField(name)?.GetValue(target);
        }
        catch
        {
            return null;
        }
    }

    private static bool SetMember(object? target, string name, object value)
    {
        if (target == null)
        {
            return false;
        }

        try
        {
            var property = target.GetType().GetProperty(name);
            if (property != null)
            {
                property.SetValue(target, Convert.ChangeType(value, property.PropertyType));
                return true;
            }

            var field = target.GetType().GetField(name);
            if (field != null)
            {
                field.SetValue(target, Convert.ChangeType(value, field.FieldType));
                return true;
            }
        }
        catch
        {
            return false;
        }

        return false;
    }
}
