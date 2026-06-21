using System;
using AuraShared.Core;

namespace AuraToolsExp.Dll.Infrastructure;

public static class AuraToolsLog
{
    private const string Tag = "AuraTools";

    public static void Info(string message)
    {
        AuraSharedLog.Info(Tag, message);
    }

    public static void Warn(string message)
    {
        AuraSharedLog.Warn(Tag, message);
    }

    public static void Error(string message, Exception? ex = null)
    {
        AuraSharedLog.Error(Tag, message, ex);
    }
}
