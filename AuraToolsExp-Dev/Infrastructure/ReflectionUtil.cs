using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;

namespace AuraToolsExp.Dll.Infrastructure;

public static class ReflectionUtil
{
    public static object? GetMemberValue(object? target, string name)
    {
        if (target == null || string.IsNullOrWhiteSpace(name))
        {
            return null;
        }

        try
        {
            var type = target.GetType();
            return type.GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(target)
                   ?? type.GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(target);
        }
        catch
        {
            return null;
        }
    }

    public static object? GetStaticMemberValue(Type? type, string name)
    {
        if (type == null || string.IsNullOrWhiteSpace(name))
        {
            return null;
        }

        try
        {
            return type.GetProperty(name, BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(null)
                   ?? type.GetField(name, BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(null);
        }
        catch
        {
            return null;
        }
    }

    public static void SetMemberValue(object? target, string name, object? value)
    {
        if (target == null || string.IsNullOrWhiteSpace(name))
        {
            return;
        }

        try
        {
            var type = target.GetType();
            var property = type.GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (property != null && property.CanWrite)
            {
                property.SetValue(target, value);
                return;
            }

            var field = type.GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            field?.SetValue(target, value);
        }
        catch
        {
            // Best-effort compatibility helper.
        }
    }

    public static string ReadString(object? target, params string[] names)
    {
        foreach (var name in names)
        {
            var value = GetMemberValue(target, name);
            if (value is string text && !string.IsNullOrWhiteSpace(text))
            {
                return text;
            }

            if (value != null)
            {
                var converted = value.ToString();
                if (!string.IsNullOrWhiteSpace(converted))
                {
                    return converted;
                }
            }
        }

        return "";
    }

    public static IEnumerable<object> Enumerate(object? value)
    {
        if (value is IEnumerable enumerable)
        {
            foreach (var item in enumerable)
            {
                if (item != null)
                {
                    yield return item;
                }
            }
        }
    }

    public static IEnumerable<string> EnumerateStrings(object? value)
    {
        foreach (var item in Enumerate(value))
        {
            if (item is string text && !string.IsNullOrWhiteSpace(text))
            {
                yield return text;
            }
        }
    }

    public static Dictionary<string, string>? AsStringDictionary(object? value)
    {
        if (value is Dictionary<string, string> typed)
        {
            return typed;
        }

        if (value is IDictionary dictionary)
        {
            var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (DictionaryEntry entry in dictionary)
            {
                if (entry.Key == null)
                {
                    continue;
                }

                result[entry.Key.ToString() ?? ""] = entry.Value?.ToString() ?? "";
            }

            return result;
        }

        return null;
    }
}
