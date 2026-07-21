using Terrias.Dll.Infrastructure;
using Witch.Core;

namespace Terrias.Dll.Mechanics;

public static class SunCardThemeCatalog
{
    public static bool IsSunThemeCard(IDataConfig? config)
    {
        return CardVisualThemeCatalog.Resolve(config)?.Id == TerriasIds.SunCardVisualSkinId;
    }
}
