using UnityEngine;
using UnityEngine.UI;

namespace Terrias.Dll.Infrastructure
{
    public static class TerriasLog
    {
        public static void Warn(string message) => Debug.LogWarning(message);
    }
}
namespace Terrias.Dll.GameApi
{
    public static class TerriasResourceCache
    {
        public static T? Load<T>(string path, bool fromMod, string category) where T : Object => null;
    }
}
namespace AuraUi.Shared
{
    public static class AuraUiNativeBridge
    {
        public static Font ResolveLegacyFont() => Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
    }
}
namespace Terrias.Dll.Hooks.Ui
{
    public static class TerriasLocalizationScope
    {
        public static void BindLegacyIfAvailable(Text text, string value) { }
    }
}
