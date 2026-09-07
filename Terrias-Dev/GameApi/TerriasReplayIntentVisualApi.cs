using System;
using UnityEngine;

namespace Terrias.Dll.GameApi;

/// <summary>Record the sprite path actually selected by OtherObj.UpdataActionShow.</summary>
internal static class TerriasReplayIntentVisualApi
{
    internal const string Contract = "native-intent-resolved.v1";

    internal static string Icon(string requested) => Resolve(requested, "Icon/ActionIcon/蓄力");
    internal static string Background(string requested) => Resolve(requested, "Icon/ActionIcon/攻击底");

    private static string Resolve(string requested, string fallback)
    {
        if (Exists(requested)) return requested;
        if (Exists(fallback)) return fallback;
        throw new InvalidOperationException("Native companion intent sprite is unavailable: "
                                            + requested + "; fallback=" + fallback);
    }

    private static bool Exists(string path) => !string.IsNullOrWhiteSpace(path)
        && (TerriasResourceCache.Load<Sprite>(path, true) != null
            || TerriasResourceCache.Load<Sprite>(path, false) != null);
}
