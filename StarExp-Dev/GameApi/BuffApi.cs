namespace StarExp.Dll.GameApi;

public static class BuffApi
{
    public static int Level(IStatusManager? status, string buffId)
    {
        return status?.GetBuff(buffId)?.buffConfig?.Level ?? 0;
    }

    public static bool Has(IStatusManager? status, string buffId)
    {
        return status?.GetBuff(buffId) != null;
    }
}
