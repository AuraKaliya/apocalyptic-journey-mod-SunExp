using SunExp.Dll.Infrastructure;
using Witch.Core;

namespace SunExp.Dll.Mechanics;

public static class SunCardThemeCatalog
{
    public static bool IsSunThemeCard(IDataConfig? config)
    {
        return CardVisualThemeCatalog.Resolve(config)?.Id == SunExpIds.SunCardVisualSkinId;
    }
}
