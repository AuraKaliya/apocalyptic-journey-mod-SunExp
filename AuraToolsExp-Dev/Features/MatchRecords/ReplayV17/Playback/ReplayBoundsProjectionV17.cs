using AuraToolsExp.Dll.Features.MatchRecords.ReplayV17.Core;
using UnityEngine;

namespace AuraToolsExp.Dll.Features.MatchRecords.ReplayV17.Playback;

internal static class ReplayBoundsProjectionV17
{
    internal static Bounds FromRecorded(ReplayBoundsQ16V17 value) => new(
        ReplayPresentationPrimitivesV17.Vector(value.Center), ReplayPresentationPrimitivesV17.Vector(value.Size));

    internal static Bounds Transform(Bounds local, Matrix4x4 matrix)
    {
        var centre = matrix.MultiplyPoint3x4(local.center);
        var x = matrix.MultiplyVector(new Vector3(local.extents.x, 0, 0));
        var y = matrix.MultiplyVector(new Vector3(0, local.extents.y, 0));
        var z = matrix.MultiplyVector(new Vector3(0, 0, local.extents.z));
        return new Bounds(centre, 2f * new Vector3(
            Mathf.Abs(x.x) + Mathf.Abs(y.x) + Mathf.Abs(z.x),
            Mathf.Abs(x.y) + Mathf.Abs(y.y) + Mathf.Abs(z.y),
            Mathf.Abs(x.z) + Mathf.Abs(y.z) + Mathf.Abs(z.z)));
    }
}
