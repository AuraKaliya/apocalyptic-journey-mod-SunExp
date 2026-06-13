using System;

namespace SunExp.Dll.Infrastructure;

public static class SunExpLog
{
    public static void Info(string message)
    {
        Commands.Log(SunExpIds.ModLogTag, message);
    }

    public static void Warn(string message)
    {
        Commands.Log(SunExpIds.ModLogTag, "[WARN] " + message);
    }

    public static void Error(string message, Exception? exception = null)
    {
        var text = exception == null ? message : message + " :: " + exception;
        Commands.Log(SunExpIds.ModLogTag, "[ERROR] " + text);
    }

    public static void Debug(string message)
    {
        Commands.Log(SunExpIds.ModLogTag, "[DEBUG] " + message);
    }
}
