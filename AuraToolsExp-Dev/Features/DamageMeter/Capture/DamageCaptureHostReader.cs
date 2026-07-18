using Witch;

namespace AuraToolsExp.Dll.Features.DamageMeter;

internal static class DamageCaptureHostReader
{
    internal static int SafeHp(IStatusManager status)
    {
        try { return status.CurHp; }
        catch { return 0; }
    }

    internal static int SafeDefend(IStatusManager status)
    {
        try { return status.Defend; }
        catch { return 0; }
    }

    internal static int SafeBuffLevel(IStatusManager status, string buffId)
    {
        try { return status.GetBuff(buffId)?.buffConfig?.Level ?? 0; }
        catch { return 0; }
    }

    internal static string SafeStatusId(IStatusManager? status)
    {
        try { return status?.InstanceId?.Trim() ?? ""; }
        catch { return ""; }
    }

    internal static string SafeDataId(IDataConfig? dataConfig)
    {
        try
        {
            if (dataConfig?.data != null && dataConfig.data.TryGetValue("Id", out var id))
            {
                return id?.Trim() ?? "";
            }
        }
        catch { }

        try { return dataConfig?.InstanceID?.Trim() ?? ""; }
        catch { return ""; }
    }
}
