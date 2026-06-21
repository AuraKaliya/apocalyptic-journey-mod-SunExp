using System;
using UnityEngine;

namespace SkinExp.Dll.Infrastructure;

public static class SkinLog
{
    private const string Prefix = "[SkinExp] ";

    public static void Info(string message) => Debug.Log(Prefix + message);

    public static void Warn(string message) => Debug.LogWarning(Prefix + message);

    public static void Error(string message, Exception? exception = null)
    {
        Debug.LogError(Prefix + message + (exception == null ? "" : " -> " + exception));
    }
}
