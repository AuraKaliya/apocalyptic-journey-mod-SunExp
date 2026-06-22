using UnityEngine;

namespace ChatExp.Dll.Infrastructure;

public static class ChatExpLog
{
    public static void Info(string message)
    {
        Debug.Log("[ChatExp] " + message);
    }

    public static void Warn(string message)
    {
        Debug.LogWarning("[ChatExp] " + message);
    }

    public static void Error(string message)
    {
        Debug.LogError("[ChatExp] " + message);
    }
}
