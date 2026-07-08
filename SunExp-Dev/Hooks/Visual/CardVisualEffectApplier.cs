using SunExp.Dll.Mechanics;
using SunExp.Dll.Infrastructure;
using Witch.Core;

namespace SunExp.Dll.Hooks.Visual;

internal static class CardVisualEffectApplier
{
    public static bool Apply(CardVisualSkinMarker marker, IDataConfig? config)
    {
        var faceEffect = CardVisualEffectRegistry.Resolve(CardVisualEffectTarget.Face, config);
        var frameEffect = CardVisualEffectRegistry.Resolve(CardVisualEffectTarget.Frame, config);
        SunExpPerformanceCounters.Record(faceEffect == null
            ? "CardVisualEffect.FaceMiss"
            : "CardVisualEffect.FaceResolved");
        SunExpPerformanceCounters.Record(frameEffect == null
            ? "CardVisualEffect.FrameMiss"
            : "CardVisualEffect.FrameResolved");

        var changed = faceEffect == null
            ? CardFaceEffectApplier.Clear(marker)
            : CardFaceEffectApplier.Apply(marker, faceEffect);

        changed = (frameEffect == null
            ? CardFrameEffectApplier.Clear(marker)
            : CardFrameEffectApplier.Apply(marker, frameEffect, config)) || changed;

        return changed;
    }
}
