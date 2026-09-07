using System;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace AuraToolsExp.Dll.Features.MatchRecords.ReplayV17.Playback;

/// <summary>
/// Instantiates the complete native prefab below an inactive quarantine root,
/// removes runtime-only UI subtrees and gameplay/input behaviours before activating
/// the remaining presentation in the replay
/// scene. Unlike the retired component copier, this preserves native sorting
/// groups, renderer materials, masks, canvas channels, layout components, and
/// prefab-authored hierarchy.
/// </summary>
internal static class ReplayNativePrefabInstanceV17
{
    internal static GameObject Clone(GameObject template, Transform parent, string name)
    {
        if (template == null) throw new ArgumentNullException(nameof(template));
        if (parent == null) throw new ArgumentNullException(nameof(parent));

        var quarantine = new GameObject("ReplayNativePrefabQuarantine");
        quarantine.SetActive(false);
        quarantine.transform.SetParent(parent, false);
        GameObject? instance = null;
        try
        {
            instance = Object.Instantiate(template, quarantine.transform, false);
            instance.name = name;
            Sanitize(instance);
            instance.transform.SetParent(parent, false);
            instance.SetActive(template.activeSelf);
            return instance;
        }
        catch
        {
            if (instance != null) Object.Destroy(instance);
            throw;
        }
        finally
        {
            Object.Destroy(quarantine);
        }
    }

    private static void Sanitize(GameObject root)
    {
        RemoveRuntimeOnlyUiSubtrees(root);

        foreach (var transform in root.GetComponentsInChildren<Transform>(true))
            transform.gameObject.layer = 30;

        foreach (var graphic in root.GetComponentsInChildren<Graphic>(true))
            graphic.raycastTarget = false;
        foreach (var group in root.GetComponentsInChildren<CanvasGroup>(true))
        {
            group.interactable = false;
            group.blocksRaycasts = false;
        }
        foreach (var collider in root.GetComponentsInChildren<Collider>(true))
            collider.enabled = false;
        foreach (var collider in root.GetComponentsInChildren<Collider2D>(true))
            collider.enabled = false;
        foreach (var raycaster in root.GetComponentsInChildren<GraphicRaycaster>(true))
            raycaster.enabled = false;
        foreach (var trigger in root.GetComponentsInChildren<EventTrigger>(true))
            trigger.enabled = false;
        foreach (var selectable in root.GetComponentsInChildren<Selectable>(true))
            selectable.interactable = false;

        foreach (var behaviour in root.GetComponentsInChildren<MonoBehaviour>(true))
        {
            if (IsPassiveUiBehaviour(behaviour)) continue;
            Object.DestroyImmediate(behaviour);
        }
    }

    private static void RemoveRuntimeOnlyUiSubtrees(GameObject root)
    {
        foreach (var behaviour in root.GetComponentsInChildren<MonoBehaviour>(true))
        {
            if (behaviour == null
                || !string.Equals(
                    behaviour.GetType().FullName,
                    "Witch.UI.Window.TutorialSpotlightUI",
                    StringComparison.Ordinal))
                continue;

            // The native owner hides/destroys its authored preview outside tutorials.
            // Stripping only that script leaves its active mask, dialogue and avatar.
            // Match ownership before stripping components; names and sibling indexes
            // are not stable identities, and the source prefab must remain untouched.
            if (behaviour.gameObject == root)
                throw new InvalidOperationException("A tutorial UI cannot be a replay presentation root.");

            Object.DestroyImmediate(behaviour.gameObject);
        }
    }

    private static bool IsPassiveUiBehaviour(MonoBehaviour value)
    {
        return value is Graphic
               || value is Mask
               || value is RectMask2D
               || value is LayoutGroup
               || value is ContentSizeFitter
               || value is AspectRatioFitter
               || value is CanvasScaler
               || value is BaseMeshEffect
               || value is TMP_Text;
    }
}
