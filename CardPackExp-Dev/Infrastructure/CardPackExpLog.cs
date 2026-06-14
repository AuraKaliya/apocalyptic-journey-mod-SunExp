using System;
using UnityEngine;

namespace CardPackExp.Dll.Infrastructure;

public static class CardPackExpLog
{
    public static void Info(string message)
    {
        Debug.Log(Format(message));
    }

    public static void Warn(string message)
    {
        Debug.LogWarning(Format(message));
    }

    public static void Error(string message, Exception? exception = null)
    {
        Debug.LogError(Format(exception == null ? message : message + "\n" + exception));
    }

    private static string Format(string message)
    {
        return "[" + CardPackExpIds.ModLogTag + "] " + message;
    }
}
