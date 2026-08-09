using Terrias.Dll.Hooks.Ui.Archive;
using Terrias.Dll.Infrastructure;
using Terrias.Dll.Mechanics;
using Witch.Mod;

namespace Terrias.Dll.Hooks;

public static class WitchArchiveRuntime
{
    public static void Initialize(ModConfig modConfig)
    {
        WitchArchiveCatalog.Load(modConfig);
        TerriasLibrarySubMenuRuntime.Register(new TerriasLibrarySubMenuEntry(
            "witch-archive",
            "Terrias_WitchArchiveLibraryButton",
            () => WitchArchiveStrings.EntryLabel,
            TerriasLibrarySubMenuSlot.TopLeft,
            OpenPanel));
        TerriasLog.Info("[WitchArchive] runtime initialized.");
    }

    public static void OpenPanel()
    {
        WitchArchivePanel.Open();
    }
}
