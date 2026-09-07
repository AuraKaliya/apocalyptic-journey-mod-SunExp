using System;
using System.Collections.Generic;

namespace AuraToolsExp.Dll.Features.MatchRecords.ReplayV17.Core;

internal static class ReplayTransformTrackV17
{
    // Keep both ends of a stationary interval. Dropping every repeated pose
    // stretches the next movement over the entire pause during interpolation.
    internal static bool Append(List<ReplayTransformSampleV17> values, ReplayTransformSampleV17 sample, int limit)
    {
        if (values.Count > 0)
        {
            var last = values[values.Count - 1];
            if (sample.OffsetTicks < last.OffsetTicks) throw new InvalidOperationException("Card trajectory time moved backwards.");
            if (sample.OffsetTicks == last.OffsetTicks) { values[values.Count - 1] = sample; return true; }
            if (values.Count > 1 && SamePose(last, sample) && SamePose(values[values.Count - 2], last))
            {
                values[values.Count - 1] = sample;
                return true;
            }
        }
        if (values.Count >= limit) return false;
        values.Add(sample);
        return true;
    }

    private static bool SamePose(ReplayTransformSampleV17 a, ReplayTransformSampleV17 b) =>
        a.CanvasPosition.X == b.CanvasPosition.X && a.CanvasPosition.Y == b.CanvasPosition.Y
        && a.CanvasSize.X == b.CanvasSize.X && a.CanvasSize.Y == b.CanvasSize.Y
        && a.LocalScale.X == b.LocalScale.X && a.LocalScale.Y == b.LocalScale.Y && a.LocalScale.Z == b.LocalScale.Z
        && a.RotationZQ16 == b.RotationZQ16 && a.AlphaQ16 == b.AlphaQ16
        && a.HasMaterialFade == b.HasMaterialFade && a.MaterialFadeQ16 == b.MaterialFadeQ16;

    internal static bool Append(List<ReplayWorldTransformSampleV17> values, ReplayWorldTransformSampleV17 sample, int limit)
    {
        if (values.Count > 0)
        {
            var last = values[values.Count - 1];
            if (sample.OffsetTicks < last.OffsetTicks) throw new InvalidOperationException("Actor trajectory time moved backwards.");
            if (sample.OffsetTicks == last.OffsetTicks) { values[values.Count - 1] = sample; return true; }
            if (values.Count > 1 && SamePose(last, sample) && SamePose(values[values.Count - 2], last))
            {
                values[values.Count - 1] = sample;
                return true;
            }
        }
        if (values.Count >= limit) return false;
        values.Add(sample);
        return true;
    }

    private static bool SamePose(ReplayWorldTransformSampleV17 a, ReplayWorldTransformSampleV17 b) =>
        Same(a.WorldPosition, b.WorldPosition) && Same(a.RootScale, b.RootScale)
        && Same(a.BodyLocalPosition, b.BodyLocalPosition) && Same(a.BodyLocalScale, b.BodyLocalScale)
        && a.SortingLayerName == b.SortingLayerName && a.SortingOrder == b.SortingOrder
        && Same(a.AttachmentBounds, b.AttachmentBounds);

    private static bool Same(ReplayVector3Q16V17 a, ReplayVector3Q16V17 b) => a.X == b.X && a.Y == b.Y && a.Z == b.Z;
    private static bool Same(ReplayBoundsQ16V17? a, ReplayBoundsQ16V17? b) =>
        a == null || b == null ? a == b : Same(a.Center, b.Center) && Same(a.Size, b.Size);
}
