using System.Collections.Generic;
using AuraShared.Core;

namespace AuraToolsExp.Dll.Infrastructure;

public static class ReflectionUtil
{
    public static object? GetMemberValue(object? target, string name)
    {
        return AuraSharedReflection.GetMemberValue(target, name);
    }

    public static object? GetStaticMemberValue(System.Type? type, string name)
    {
        return AuraSharedReflection.GetStaticMemberValue(type, name);
    }

    public static void SetMemberValue(object? target, string name, object? value)
    {
        AuraSharedReflection.SetMemberValue(target, name, value);
    }

    public static string ReadString(object? target, params string[] names)
    {
        return AuraSharedReflection.ReadString(target, names);
    }

    public static IEnumerable<object> Enumerate(object? value)
    {
        return AuraSharedReflection.Enumerate(value);
    }

    public static IEnumerable<string> EnumerateStrings(object? value)
    {
        return AuraSharedReflection.EnumerateStrings(value);
    }

    public static Dictionary<string, string>? AsStringDictionary(object? value)
    {
        return AuraSharedReflection.AsStringDictionary(value);
    }
}
