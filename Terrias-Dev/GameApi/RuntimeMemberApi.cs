using System;
using System.Reflection;
using Terrias.Dll.Infrastructure;

namespace Terrias.Dll.GameApi;

public static class RuntimeMemberApi
{
    public static int ReadStaticNonNegativeInt(object? typeObject, string name, string context = "")
    {
        var value = ReadStaticMember(typeObject, name, context);
        return Math.Max(0, DictionaryUtil.ParseInt(Convert.ToString(value)));
    }

    public static object? ReadStaticMember(object? typeObject, string name, string context = "")
    {
        var type = typeObject as Type;
        if (type == null || string.IsNullOrWhiteSpace(name))
        {
            return null;
        }

        try
        {
            var property = type.GetProperty(name, BindingFlags.Public | BindingFlags.Static);
            if (property != null)
            {
                return property.GetValue(null);
            }

            return type.GetField(name, BindingFlags.Public | BindingFlags.Static)?.GetValue(null);
        }
        catch (TargetInvocationException ex)
        {
            var reason = ex.InnerException?.Message ?? ex.Message;
            TerriasLog.Debug("[RuntimeMemberApi] static getter unavailable: "
                             + Describe(type, name, context)
                             + ", error="
                             + reason);
            return null;
        }
        catch (Exception ex)
        {
            TerriasLog.Debug("[RuntimeMemberApi] static member read failed: "
                             + Describe(type, name, context)
                             + ", error="
                             + ex.Message);
            return null;
        }
    }

    private static string Describe(Type type, string name, string context)
    {
        var member = (type.FullName ?? type.Name) + "." + name;
        return string.IsNullOrWhiteSpace(context) ? member : member + ", context=" + context.Trim();
    }
}
