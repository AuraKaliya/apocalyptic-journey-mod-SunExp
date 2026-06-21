using System;
using AuraShared.Core;

namespace AuraSkin.Shared.Infrastructure;

public static class SkinLog
{
    private const string Tag = "AuraSkin";

    public static void Info(string message) => AuraSharedLog.Info(Tag, message);

    public static void Warn(string message) => AuraSharedLog.Warn(Tag, message);

    public static void Error(string message, Exception? exception = null)
    {
        AuraSharedLog.Error(Tag, message, exception);
    }
}
