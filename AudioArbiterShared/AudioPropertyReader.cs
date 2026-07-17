using System;
using System.Reflection;

namespace AudioArbiter.Shared;

internal static class AudioPropertyReader
{
    public static string ReadString(object? source, string propertyName, string fallback = "")
    {
        try
        {
            var value = Read(source, propertyName);
            return value?.ToString() ?? fallback;
        }
        catch
        {
            return fallback;
        }
    }

    public static int ReadInt(object? source, string propertyName, int fallback)
    {
        try
        {
            var value = Read(source, propertyName);
            if (value is int typed)
            {
                return typed;
            }

            return int.TryParse(value?.ToString(), out var parsed) ? parsed : fallback;
        }
        catch
        {
            return fallback;
        }
    }

    public static long ReadLong(object? source, string propertyName, long fallback)
    {
        try
        {
            var value = Read(source, propertyName);
            return value is long typed ? typed : Convert.ToInt64(value);
        }
        catch
        {
            return fallback;
        }
    }

    public static bool ReadBool(object? source, string propertyName, bool fallback)
    {
        try
        {
            var value = Read(source, propertyName);
            if (value is bool typed)
            {
                return typed;
            }

            return bool.TryParse(value?.ToString(), out var parsed) ? parsed : fallback;
        }
        catch
        {
            return fallback;
        }
    }

    public static float ReadFloat(object? source, string propertyName, float fallback)
    {
        try
        {
            var value = Read(source, propertyName);
            if (value is float typed)
            {
                return typed;
            }

            return float.TryParse(value?.ToString(), out var parsed) ? parsed : fallback;
        }
        catch
        {
            return fallback;
        }
    }

    private static object? Read(object? source, string propertyName)
    {
        if (source == null)
        {
            return null;
        }

        return source.GetType()
            .GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public)
            ?.GetValue(source);
    }
}
