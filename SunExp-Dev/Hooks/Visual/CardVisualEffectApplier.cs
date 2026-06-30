using SunExp.Dll.Mechanics;
using Witch.Core;

namespace SunExp.Dll.Hooks.Visual;

internal static class CardVisualEffectApplier
{
    public static bool Apply(CardVisualSkinMarker marker, IDataConfig? config)
    {
        var frameEffect = CardVisualEffectRegistry.Resolve(CardVisualEffectTarget.Frame, config);
        return frameEffect == null
            ? CardFrameEffectApplier.Clear(marker)
            : CardFrameEffectApplier.Apply(marker, frameEffect);
    }
}
