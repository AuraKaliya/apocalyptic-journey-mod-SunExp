using AuraShared.Core;
using Terrias.Dll.Infrastructure;
using Witch.Mod;

namespace Terrias.Dll.Hooks;

public static class TerriasCardPresentationLifecycleBridge
{
    private static bool initialized;

    public static void Initialize(ModConfig modConfig)
    {
        if (initialized) return;
        initialized = true;
        AuraCardPresentationRuntime.Register(
            modConfig,
            "Terrias",
            "ContentPresentation",
            new AuraCardPresentationSubscription
            {
                Apply = context => TerriasCardPresentationRouter.RequestApply(new TerriasCardPresentationContext
                {
                    Root = context.Root,
                    Config = context.Config,
                    Card = context.Card,
                    Source = context.Source,
                    Surface = MapSurface(context.Surface)
                })
            });
        TerriasLog.InfoAlways("Terrias content presentation subscribed to the shared card lifecycle.");
    }

    private static TerriasCardPresentationSurface MapSurface(AuraCardPresentationSurface surface)
    {
        return surface switch
        {
            AuraCardPresentationSurface.CombatCard => TerriasCardPresentationSurface.CombatCard,
            AuraCardPresentationSurface.CardStyle => TerriasCardPresentationSurface.CardStyle,
            AuraCardPresentationSurface.RewardChoice => TerriasCardPresentationSurface.RewardChoice,
            AuraCardPresentationSurface.Display => TerriasCardPresentationSurface.Display,
            AuraCardPresentationSurface.Shop => TerriasCardPresentationSurface.Shop,
            AuraCardPresentationSurface.Warehouse => TerriasCardPresentationSurface.Warehouse,
            AuraCardPresentationSurface.SafeBox => TerriasCardPresentationSurface.SafeBox,
            AuraCardPresentationSurface.Dictionary => TerriasCardPresentationSurface.Dictionary,
            AuraCardPresentationSurface.CardPack => TerriasCardPresentationSurface.CardPack,
            _ => TerriasCardPresentationSurface.Unknown
        };
    }
}
