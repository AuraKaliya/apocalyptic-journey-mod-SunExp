using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;

namespace AuraShared.Core;

public static class AuraSharedReflection
{
    private const BindingFlags InstanceFlags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private const BindingFlags StaticFlags = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

    public static object? GetMemberValue(object? target, string name)
    {
        if (target == null || string.IsNullOrWhiteSpace(name))
        {
            return null;
        }

        try
        {
            var type = target.GetType();
            return type.GetProperty(name, InstanceFlags)?.GetValue(target)
                   ?? type.GetField(name, InstanceFlags)?.GetValue(target);
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
            return type.GetProperty(name, StaticFlags)?.GetValue(null)
                   ?? type.GetField(name, StaticFlags)?.GetValue(null);
        }
        catch
        {
            return null;
        }
    }

    public static bool SetMemberValue(object? target, string name, object? value)
    {
        if (target == null || string.IsNullOrWhiteSpace(name))
        {
            return false;
        }

        try
        {
            var type = target.GetType();
            var property = type.GetProperty(name, InstanceFlags);
            if (property != null && property.CanWrite)
            {
                property.SetValue(target, value);
                return true;
            }

            var field = type.GetField(name, InstanceFlags);
            if (field == null)
            {
                return false;
            }

            field.SetValue(target, value);
            return true;
        }
        catch
        {
            return false;
        }
    }

    public static bool SetStaticMemberValue(Type? type, string name, object? value)
    {
        if (type == null || string.IsNullOrWhiteSpace(name))
        {
            return false;
        }

        try
        {
            var property = type.GetProperty(name, StaticFlags);
            if (property != null && property.CanWrite)
            {
                property.SetValue(null, value);
                return true;
            }

            var field = type.GetField(name, StaticFlags);
            if (field == null)
            {
                return false;
            }

            field.SetValue(null, value);
            return true;
        }
        catch
        {
            return false;
        }
    }

    public static object? InvokeMethod(object? target, string methodName, params object?[] args)
    {
        if (target == null || string.IsNullOrWhiteSpace(methodName))
        {
            return null;
        }

        try
        {
            return target.GetType()
                .GetMethod(methodName, InstanceFlags)
                ?.Invoke(target, args);
        }
        catch
        {
            return null;
        }
    }

    public static object? InvokeStaticMethod(Type? type, string methodName, params object?[] args)
    {
        if (type == null || string.IsNullOrWhiteSpace(methodName))
        {
            return null;
        }

        try
        {
            return type.GetMethod(methodName, StaticFlags)?.Invoke(null, args);
        }
        catch
        {
            return null;
        }
    }

    public static string ReadString(object? target, params string[] names)
    {
        return ReadStringOrDefault("", target, names);
    }

    public static string ReadStringOrDefault(string fallback, object? target, params string[] names)
    {
        foreach (var name in names)
        {
            var value = GetMemberValue(target, name);
            var text = Convert.ToString(value, CultureInfo.InvariantCulture);
            if (!string.IsNullOrWhiteSpace(text))
            {
                return text!;
            }
        }

        return fallback;
    }

    public static int ReadInt(object? target, string name, int fallback = 0)
    {
        return ToInt(GetMemberValue(target, name), fallback);
    }

    public static bool ReadBool(object? target, string name, bool fallback = false)
    {
        return ToBool(GetMemberValue(target, name), fallback);
    }

    public static float ReadFloat(object? target, string name, float fallback = 0f)
    {
        return ToFloat(GetMemberValue(target, name), fallback);
    }

    public static int ToInt(object? value, int fallback = 0)
    {
        if (value is int typed)
        {
            return typed;
        }

        return int.TryParse(Convert.ToString(value, CultureInfo.InvariantCulture), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : fallback;
    }

    public static bool ToBool(object? value, bool fallback = false)
    {
        if (value is bool typed)
        {
            return typed;
        }

        return bool.TryParse(Convert.ToString(value, CultureInfo.InvariantCulture), out var parsed)
            ? parsed
            : fallback;
    }

    public static float ToFloat(object? value, float fallback = 0f)
    {
        if (value is float typed)
        {
            return typed;
        }

        return float.TryParse(Convert.ToString(value, CultureInfo.InvariantCulture), NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : fallback;
    }

    public static IEnumerable<object> Enumerate(object? value)
    {
        if (value is string)
        {
            yield break;
        }

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
            var text = Convert.ToString(item, CultureInfo.InvariantCulture);
            if (!string.IsNullOrWhiteSpace(text))
            {
                yield return text!;
            }
        }
    }

    public static Dictionary<string, string>? AsStringDictionary(object? value)
    {
        if (value is Dictionary<string, string> typed)
        {
            return new Dictionary<string, string>(typed, StringComparer.OrdinalIgnoreCase);
        }

        if (value is IDictionary dictionary)
        {
            var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (DictionaryEntry entry in dictionary)
            {
                var key = Convert.ToString(entry.Key, CultureInfo.InvariantCulture);
                if (string.IsNullOrWhiteSpace(key))
                {
                    continue;
                }

                result[key!] = Convert.ToString(entry.Value, CultureInfo.InvariantCulture) ?? "";
            }

            return result;
        }

        return null;
    }

    public static Type? FindType(string fullNameOrName)
    {
        if (string.IsNullOrWhiteSpace(fullNameOrName))
        {
            return null;
        }

        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            Type? direct = null;
            try
            {
                direct = assembly.GetType(fullNameOrName, false);
            }
            catch
            {
            }

            if (direct != null)
            {
                return direct;
            }

            foreach (var type in SafeTypes(assembly))
            {
                if (type == null)
                {
                    continue;
                }

                if (string.Equals(type.FullName, fullNameOrName, StringComparison.Ordinal)
                    || string.Equals(type.Name, fullNameOrName, StringComparison.Ordinal))
                {
                    return type;
                }
            }
        }

        return null;
    }

    public static IEnumerable<Type?> SafeTypes(Assembly assembly)
    {
        try
        {
            return assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException ex)
        {
            return ex.Types;
        }
        catch
        {
            return Array.Empty<Type>();
        }
    }

    public static string UnwrapMessage(Exception ex)
    {
        return ex is TargetInvocationException { InnerException: not null }
            ? ex.InnerException.Message
            : ex.Message;
    }
}
