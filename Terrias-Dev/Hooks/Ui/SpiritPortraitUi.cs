using System.Linq;
using Terrias.Dll.GameApi;
using Terrias.Dll.Mechanics;
using UnityEngine;

namespace Terrias.Dll.Hooks.Ui;

internal static class SpiritPortraitUi
{
    public static Sprite? Resolve(CapturedEnemySnapshot? snapshot, string purpose)
    {
        if (snapshot == null) return null;
        try
        {
            return TerriasResourceCache.LoadAll<Sprite>(snapshot.DictPath, purpose)?.FirstOrDefault()
                   ?? TerriasResourceCache.LoadAll<Sprite>(snapshot.IdlePath, purpose)?.FirstOrDefault();
        }
        catch
        {
            return null;
        }
    }
}
