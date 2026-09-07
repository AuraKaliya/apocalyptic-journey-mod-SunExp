using AuraToolsExp.Dll.Features.MatchRecords.ReplayV17.Core;
using UnityEngine;

namespace AuraToolsExp.Dll.Features.MatchRecords.ReplayV17.Playback
{
    internal static class ReplayResourceResolverV17
    {
        internal static Sprite[] Frames = System.Array.Empty<Sprite>();
        internal static Sprite[] Sprites(string path, System.Collections.Generic.IReadOnlyList<string> names) => Frames;
        internal static Sprite RequiredSprite(string path, string usage) => null;
        internal static Texture RequiredTextureOrSprite(string path, string usage) => null;
    }
    // Only native artwork binding is substituted. The timeline, lifetime and
    // RectTransform projection under test are copied unchanged from production.
    internal sealed class ReplayUiTemplateCacheV17 { internal GameObject CardTemplate; }
    internal sealed class CardBindingObservation : MonoBehaviour
    {
        internal int Cost;
        internal bool BurnPrepared;
        internal float Fade;
        internal string RenderedName;
    }
    internal static class ReplayUiV17
    {
        internal static GameObject CreateCard(Transform parent, ReplayVisibleCardStateV17 state,
            ReplayCardDescriptorV17 descriptor, Vector2 size, GameObject template)
        {
            var result = new GameObject(state.CardInstanceId, typeof(RectTransform));
            result.transform.SetParent(parent, false);
            var rect = (RectTransform)result.transform;
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = size;
            result.AddComponent<CardBindingObservation>().Cost = state.DisplayedCost;
            result.GetComponent<CardBindingObservation>().RenderedName = state.RenderedName;
            return result;
        }
        internal static void UpdateCard(GameObject card, ReplayVisibleCardStateV17 state, ReplayCardDescriptorV17 descriptor)
        {
            var binding = card.GetComponent<CardBindingObservation>();
            binding.Cost = state.DisplayedCost;
            binding.RenderedName = state.RenderedName;
        }
    }
}
namespace AuraToolsExp.Dll.GameApi
{
    internal static class ReplayNativeCardPresentationApi
    {
        internal static void PrepareBurn(Transform root) =>
            root.GetComponent<AuraToolsExp.Dll.Features.MatchRecords.ReplayV17.Playback.CardBindingObservation>().BurnPrepared = true;
        internal static void SetBurnFade(Transform root, float fade) =>
            root.GetComponent<AuraToolsExp.Dll.Features.MatchRecords.ReplayV17.Playback.CardBindingObservation>().Fade = fade;
    }
}
