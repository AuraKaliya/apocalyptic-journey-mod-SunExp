using UnityEngine;

namespace SunExp.Dll.Hooks.Visual;

public static class SunCardFrameSpriteCache
{
    public static Sprite? Load(string path, string logPrefix)
    {
        return CardVisualSkinSpriteCache.Load(path, logPrefix);
    }
}
