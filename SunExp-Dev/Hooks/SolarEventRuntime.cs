using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using SunExp.Dll.GameApi;
using SunExp.Dll.Infrastructure;
using Witch.Core;
using Witch.Mod;

namespace SunExp.Dll.Hooks;

public static class SolarEventRuntime
{
    public static void EnsureInCurrentLayer(ModHookContext context)
    {
        try
        {
            if (SolarMemoryModeRuntime.IsSolarMemoryRun())
            {
                return;
            }

            if (!EnsureInCurrentLayer(context.Target))
            {
                foreach (var arg in context.Arguments ?? Array.Empty<object>())
                {
                    if (EnsureInCurrentLayer(arg))
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

    public static void RepairMapSelection(ModHookContext context)
    {
        try
        {
            if (SolarMemoryModeRuntime.IsSolarMemoryRun())
            {
                return;
            }

            var args = context.Arguments ?? Array.Empty<object>();
            for (var i = 0; i < args.Length - 1; i++)
            {
                if (RepairMapArrays(args[i], args[i + 1]))
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

    public static string CurrentEventId()
    {
        var progress = DictionaryUtil.ParseInt(PlayerApi.GetGameVar(SunExpIds.WunaEventProgressKey, "0"));
        if (progress >= SunExpIds.WunaEventMaxProgress)
        {
            return SunExpIds.WunaEventFullRepeat;
        }

        var next = Math.Min(SunExpIds.WunaEventMaxProgress, Math.Max(1, progress + 1));
        return SunExpIds.WunaEventFullPrefix + next.ToString("00");
    }

    private static bool EnsureInCurrentLayer(object? treeOrManager)
    {
        var nodes = GetSelectNodes(treeOrManager);
        var count = Count(nodes);
        if (nodes == null || count <= 0)
        {
            return false;
        }

        var startIndex = Math.Max(0, GetCurrentMapLevelNumber() / 6 * GetLayerSegmentSize());
        if (startIndex >= count)
        {
            return false;
        }

        var endIndex = Math.Min(count - 1, startIndex + GetLayerSegmentSize() - 1);
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
                return ApplySolarEventNode(node);
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

        return ApplySolarEventNode(firstEvent ?? firstFallback);
    }

    private static bool ApplySolarEventNode(object? node)
    {
        if (node == null)
        {
            return false;
        }

        var template = SolarMapData();
        if (template != null)
        {
            template["Id"] = SunExpIds.SolarEventMapId;
            template["Type"] = "Event";
            template["NodeId"] = CurrentEventId();
            template["Level"] = "-1";
            if (ReplaceNodeData(node, template))
            {
                SetMember(node, "type", "Event");
                return true;
            }
        }

        var changed = false;
        changed = SetNodeData(node, "Id", SunExpIds.SolarEventMapId) || changed;
        changed = SetNodeData(node, "Type", "Event") || changed;
        changed = SetNodeData(node, "NodeId", CurrentEventId()) || changed;
        changed = SetNodeData(node, "Level", "-1") || changed;
        SetMember(node, "type", "Event");
        return changed;
    }

    private static Dictionary<string, string>? SolarMapData()
    {
        try
        {
            var data = Singleton<GameConfigManager>.Instance.GetOne(DataType.Map, SunExpIds.SolarEventMapId);
            return data == null ? null : new Dictionary<string, string>(data);
        }
        catch (Exception ex)
        {
            SunExpLog.Warn("Solar event map template unavailable: " + ex.Message);
            return null;
        }
    }

    private static bool ReplaceNodeData(object node, Dictionary<string, string> template)
    {
        var data = Member(node, "data");
        if (data is IDictionary<string, string> dictionary)
        {
            dictionary.Clear();
            foreach (var pair in template)
            {
                dictionary[pair.Key] = pair.Value;
            }

            return true;
        }

        return SetMemberRaw(node, "data", template);
    }

    private static bool RepairMapArrays(object? maps, object? mapData)
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

            changed = SetItem(maps, i, SunExpIds.SolarEventMapId) || changed;
            changed = SetItem(mapData, i, CurrentEventId()) || changed;
        }

        return changed;
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

    private static bool IsSolarEventNode(object node)
    {
        return IsSolarEventMapId(GetNodeData(node, "Id"));
    }

    private static bool IsSolarEventMapId(object? id)
    {
        var value = Convert.ToString(id);
        return value == SunExpIds.SolarEventMapId || value == SunExpIds.SolarEventShortMapId;
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

    private static int GetCurrentMapLevelNumber()
    {
        var manager = AppDomain.CurrentDomain.GetAssemblies()
            .Select(asm => asm.GetType("MapManager"))
            .FirstOrDefault(type => type != null)
            ?.GetProperty("Instance")?.GetValue(null);
        return DictionaryUtil.ParseInt(Convert.ToString(Member(manager, "Level") ?? Member(Member(manager, "ModeMapManager"), "Level")));
    }

    private static int GetLayerSegmentSize()
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

    private static bool SetMemberRaw(object? target, string name, object value)
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
                property.SetValue(target, value);
                return true;
            }

            var field = target.GetType().GetField(name);
            if (field != null)
            {
                field.SetValue(target, value);
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
