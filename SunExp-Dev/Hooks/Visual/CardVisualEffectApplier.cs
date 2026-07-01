using SunExp.Dll.Mechanics;
using Witch.Core;

namespace SunExp.Dll.Hooks.Visual;

internal static class CardVisualEffectApplier
{
    public static bool Apply(CardVisualSkinMarker marker, IDataConfig? config)
    {
        var faceEffect = CardVisualEffectRegistry.Resolve(CardVisualEffectTarget.Face, config);
        return faceEffect == null
            ? CardFaceEffectApplier.Clear(marker)
            : CardFaceEffectApplier.Apply(marker, faceEffect);
    }
}
