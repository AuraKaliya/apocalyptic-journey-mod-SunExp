using Terrias.Dll.GameApi;
using Terrias.Dll.Infrastructure;
using Terrias.Dll.Mechanics;

namespace Terrias.Dll.Application;

public static class SolarMemoryMapPreviewApplication
{
    public static bool TryApplyAnimationOverride(
        MapTree.Node node, out SolarMemoryMapPreviewOverride? applied, out string reason)
    {
        applied = null;
        if (!SolarMemoryMapPreviewApi.TryGetNativePreviewEnemy(
                node, out var levelId, out var enemyId, out var enemyData, out reason)
            || enemyData == null)
        {
            return false;
        }

        var original = DictionaryUtil.Get(enemyData, "Animation");
        var fallback = SolarMemoryMapPreviewPolicy.ResolveFallback(
            levelId, original, SolarMemoryMapPreviewApi.HasNativePreviewFrames);
        if (fallback.Length == 0)
        {
            reason = SolarMemoryMapPreviewApi.HasNativePreviewFrames(original)
                ? "native-preview-already-valid"
                : "no-validated-native-fallback";
            return false;
        }

        applied = new SolarMemoryMapPreviewOverride(enemyData, enemyId, fallback);
        reason = "applied";
        return true;
    }
}
