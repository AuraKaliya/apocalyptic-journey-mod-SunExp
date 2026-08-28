namespace Terrias.Dll.Hooks.Ui;

internal enum SpiritElementDisplaySurface
{
    WarehouseCard,
    DetailHeader,
    PartySlot
}

internal static class SpiritElementDisplayPolicy
{
    internal static float IconSize(SpiritElementDisplaySurface surface) => surface switch
    {
        SpiritElementDisplaySurface.DetailHeader => 20f,
        SpiritElementDisplaySurface.PartySlot => 14f,
        _ => 16f
    };

    internal static float NameTrailingReserve(SpiritElementDisplaySurface surface)
        => IconSize(surface) + 9f;

    internal static bool ShowPersistentText => false;

    internal static bool ShowAttributeSummaryRow => false;
}
