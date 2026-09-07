using UnityEngine;
using AuraToolsExp.Dll.Features.MatchRecords.ReplayV17.Core;

namespace AuraToolsExp.Dll.Features.MatchRecords.ReplayV17.Playback;

internal static class ReplayPresentationPrimitivesV17
{
    internal static float FromQ16(int value) => value / 65_536f;

    internal static Vector3 Vector(ReplayVector3Q16V17? value) => new(
        FromQ16(value?.X ?? 0),
        FromQ16(value?.Y ?? 0),
        FromQ16(value?.Z ?? 0));

    internal static Color Color(ReplayColorQ8V17 value) =>
        new(value.R / 255f, value.G / 255f, value.B / 255f, value.A / 255f);
}

