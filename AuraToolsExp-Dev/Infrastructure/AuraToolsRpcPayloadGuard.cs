using System;
using System.Text;
using AuraShared.Core;

namespace AuraToolsExp.Dll.Infrastructure;

public static class AuraToolsRpcPayloadGuard
{
    public const int MirrorStringLimitBytes = 65534;
    public const int DefaultSoftLimitBytes = 56000;

    public static bool TryMeasureUtf8Json(object? payload, out int bytes, out string error)
    {
        bytes = 0;
        error = "";
        if (payload == null)
        {
            return true;
        }

        try
        {
            var json = AuraSharedJson.Serialize(payload) ?? "";
            bytes = Encoding.UTF8.GetByteCount(json);
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }

    public static bool FitsSoftLimit(object? payload, int limit, out int bytes, out string error)
    {
        if (!TryMeasureUtf8Json(payload, out bytes, out error))
        {
            return true;
        }

        return bytes <= limit;
    }
}
