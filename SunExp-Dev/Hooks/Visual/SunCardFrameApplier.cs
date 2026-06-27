using UnityEngine;
using Witch.Core;

namespace SunExp.Dll.Hooks.Visual;

public static class SunCardFrameApplier
{
    public static bool Apply(Transform? cardRoot, IDataConfig? config)
    {
        return CardVisualSkinApplier.Apply(cardRoot, config);
    }
}
