using System;
using System.Text;

namespace AuraShared.Core;

public static class AuraSharedPayloadBudget
{
    public static bool TryMeasureNativeRpc(object payload, out int bytes)
    {
        try
        {
            bytes = Encoding.UTF8.GetByteCount(Newtonsoft.Json.JsonConvert.SerializeObject(payload,
                new Newtonsoft.Json.JsonSerializerSettings { TypeNameHandling = Newtonsoft.Json.TypeNameHandling.All }));
            return true;
        }
        catch { bytes = int.MaxValue; return false; }
    }
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
            bytes = Encoding.UTF8.GetByteCount(AuraSharedJson.Serialize(payload) ?? "");
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
            return false;
        }

        return bytes <= Math.Min(MirrorStringLimitBytes - 1, Math.Max(1, limit));
    }
}
