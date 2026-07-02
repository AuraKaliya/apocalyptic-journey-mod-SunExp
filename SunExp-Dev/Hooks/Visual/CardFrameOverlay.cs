using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace SunExp.Dll.Hooks.Visual;

internal sealed class CardFrameOverlay : MonoBehaviour
{
    public const string OverlayName = "SunExp_CardFrameEffectOverlay";

    private Image? overlayImage;
    private RectTransform? overlayRect;
    private MeshFilter? overlayMeshFilter;
    private MeshRenderer? overlayMeshRenderer;
    private Mesh? overlayMesh;
    private Mesh? sourceMesh;

    public bool ApplyImage(Image source, Material material, Sprite frameSprite, Transform? frameNode, Transform? backgroundNode, bool fallbackShape)
    {
        var overlay = EnsureImageOverlay(source, frameNode, backgroundNode);
        if (overlay == null)
        {
            return false;
        }

        if (fallbackShape)
        {
            CopyFallbackImageShape(source, overlay);
        }
        else
        {
            CopyImageShape(source, overlay);
        }

        var changed = DestroyMeshOverlay()
            || !ReferenceEquals(overlay.material, material)
            || !ReferenceEquals(overlay.sprite, frameSprite)
            || !overlay.gameObject.activeSelf;
        overlay.material = material;
        overlay.sprite = frameSprite;
        overlay.gameObject.SetActive(true);
        return changed;
    }

    public bool ApplyMesh(MeshRenderer source, Material material, Texture frameTexture)
    {
        var overlay = EnsureMeshOverlay(source);
        if (overlay == null)
        {
            return false;
        }

        var changed = DestroyImageOverlay()
            || !ReferenceEquals(overlay.sharedMaterial, material)
            || !overlay.gameObject.activeSelf
            || overlay.enabled != source.enabled;
        overlay.sharedMaterial = material;
        overlay.enabled = source.enabled;
        overlay.gameObject.SetActive(source.gameObject.activeSelf);
        CopyMeshRenderState(source, overlay);
        if (overlay.sharedMaterial != null)
        {
            CardFrameEffectMaterials.ApplyRuntimeTexture(overlay.sharedMaterial, frameTexture);
        }

        return changed;
    }

    public bool Clear()
    {
        var changed = DestroyImageOverlay();
        changed = DestroyMeshOverlay() || changed;
        return changed;
    }

    public bool SetVisible(bool visible)
    {
        var changed = false;
        if (overlayImage != null && overlayImage.gameObject.activeSelf != visible)
        {
            overlayImage.gameObject.SetActive(visible);
            changed = true;
        }

        if (overlayMeshRenderer != null && overlayMeshRenderer.gameObject.activeSelf != visible)
        {
            overlayMeshRenderer.gameObject.SetActive(visible);
            changed = true;
        }

        return changed;
    }

    private Image? EnsureImageOverlay(Image source, Transform? frameNode, Transform? backgroundNode)
    {
        if (overlayImage != null)
        {
            PositionImageOverlay(source, frameNode, backgroundNode);
            return overlayImage;
        }

        var parent = ResolveOverlayParent(source.transform, frameNode, backgroundNode);
        if (parent == null)
        {
            return null;
        }

        var overlayObject = new GameObject(OverlayName, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        overlayObject.layer = source.gameObject.layer;
        overlayObject.transform.SetParent(parent, false);

        overlayRect = overlayObject.GetComponent<RectTransform>();
        overlayImage = overlayObject.GetComponent<Image>();
        overlayImage.raycastTarget = false;
        overlayImage.maskable = source.maskable;
        PositionImageOverlay(source, frameNode, backgroundNode);
        return overlayImage;
    }

    private MeshRenderer? EnsureMeshOverlay(MeshRenderer source)
    {
        if (overlayMeshRenderer != null)
        {
            PositionMeshOverlay(source);
            UpdateOverlayMesh(source);
            return overlayMeshRenderer;
        }

        var sourceFilter = source.GetComponent<MeshFilter>();
        if (sourceFilter == null || sourceFilter.sharedMesh == null)
        {
            return null;
        }

        var overlayObject = new GameObject(OverlayName, typeof(MeshFilter), typeof(MeshRenderer));
        overlayObject.layer = source.gameObject.layer;
        overlayObject.transform.SetParent(source.transform, false);

        overlayMeshFilter = overlayObject.GetComponent<MeshFilter>();
        overlayMeshRenderer = overlayObject.GetComponent<MeshRenderer>();
        PositionMeshOverlay(source);
        UpdateOverlayMesh(source);
        return overlayMeshRenderer;
    }

    private void PositionImageOverlay(Image source, Transform? frameNode, Transform? backgroundNode)
    {
        if (overlayImage == null || overlayRect == null)
        {
            return;
        }

        var parent = ResolveOverlayParent(source.transform, frameNode, backgroundNode);
        if (parent != null && overlayImage.transform.parent != parent)
        {
            overlayImage.transform.SetParent(parent, false);
        }

        var sourceRect = source.transform as RectTransform;
        if (sourceRect != null)
        {
            CopyRectShape(sourceRect, overlayRect);
        }

        if (frameNode != null && frameNode.parent == overlayImage.transform.parent)
        {
            var frameIndex = frameNode.GetSiblingIndex();
            var overlayIndex = overlayImage.transform.GetSiblingIndex();
            var targetIndex = overlayIndex < frameIndex ? frameIndex : frameIndex + 1;
            var textIndex = FindFirstTextContentSiblingIndex(overlayImage.transform.parent, overlayImage.transform);
            if (textIndex >= 0)
            {
                targetIndex = Mathf.Min(targetIndex, overlayIndex < textIndex ? textIndex - 1 : textIndex);
            }

            overlayImage.transform.SetSiblingIndex(targetIndex);
        }
        else
        {
            var textIndex = FindFirstTextContentSiblingIndex(overlayImage.transform.parent, overlayImage.transform);
            if (textIndex >= 0)
            {
                overlayImage.transform.SetSiblingIndex(textIndex);
            }
            else
            {
                overlayImage.transform.SetAsLastSibling();
            }
        }
    }

    private void PositionMeshOverlay(MeshRenderer source)
    {
        if (overlayMeshRenderer == null)
        {
            return;
        }

        var overlayTransform = overlayMeshRenderer.transform;
        if (overlayTransform.parent != source.transform)
        {
            overlayTransform.SetParent(source.transform, false);
        }

        overlayTransform.localPosition = Vector3.zero;
        overlayTransform.localRotation = Quaternion.identity;
        overlayTransform.localScale = Vector3.one;
    }

    private void UpdateOverlayMesh(MeshRenderer source)
    {
        if (overlayMeshFilter == null)
        {
            return;
        }

        var sourceFilter = source.GetComponent<MeshFilter>();
        var mesh = sourceFilter == null ? null : sourceFilter.sharedMesh;
        if (mesh == null)
        {
            return;
        }

        if (ReferenceEquals(sourceMesh, mesh) && overlayMesh != null)
        {
            overlayMeshFilter.sharedMesh = overlayMesh;
            return;
        }

        DestroyOverlayMeshAsset();
        sourceMesh = mesh;
        overlayMesh = BuildFullUvMesh(mesh);
        overlayMeshFilter.sharedMesh = overlayMesh;
    }

    private static Mesh BuildFullUvMesh(Mesh source)
    {
        var vertices = source.vertices;
        var bounds = source.bounds;
        var size = bounds.size;
        var min = bounds.min;
        var uv = new Vector2[vertices.Length];
        for (var i = 0; i < vertices.Length; i++)
        {
            var x = Mathf.Approximately(size.x, 0f) ? 0.5f : (vertices[i].x - min.x) / size.x;
            var y = Mathf.Approximately(size.y, 0f) ? 0.5f : (vertices[i].y - min.y) / size.y;
            uv[i] = new Vector2(x, y);
        }

        var mesh = new Mesh
        {
            name = source.name + "_SunExpFrameOverlayFullUv",
            vertices = vertices,
            triangles = source.triangles,
            uv = uv,
            colors = source.colors,
            normals = source.normals,
            tangents = source.tangents
        };
        mesh.RecalculateBounds();
        return mesh;
    }

    private static Transform? ResolveOverlayParent(Transform source, Transform? frameNode, Transform? backgroundNode)
    {
        if (frameNode != null && frameNode.parent != null)
        {
            return frameNode.parent;
        }

        if (backgroundNode != null && backgroundNode.parent != null)
        {
            return backgroundNode.parent;
        }

        return source.parent;
    }

    private static void CopyImageShape(Image source, Image target)
    {
        target.type = source.type;
        target.color = Color.white;
        target.preserveAspect = source.preserveAspect;
        target.fillCenter = source.fillCenter;
        target.fillMethod = source.fillMethod;
        target.fillOrigin = source.fillOrigin;
        target.fillAmount = source.fillAmount;
        target.fillClockwise = source.fillClockwise;
        target.raycastTarget = false;
        target.maskable = source.maskable;
    }

    private static void CopyFallbackImageShape(Image source, Image target)
    {
        target.type = Image.Type.Simple;
        target.color = Color.white;
        target.preserveAspect = source.preserveAspect;
        target.fillCenter = true;
        target.raycastTarget = false;
        target.maskable = source.maskable;
    }

    private static void CopyRectShape(RectTransform source, RectTransform target)
    {
        target.anchorMin = source.anchorMin;
        target.anchorMax = source.anchorMax;
        target.anchoredPosition = source.anchoredPosition;
        target.sizeDelta = source.sizeDelta;
        target.pivot = source.pivot;
        target.localScale = source.localScale;
        target.localRotation = source.localRotation;
    }

    private void CopyMeshRenderState(MeshRenderer source, MeshRenderer target)
    {
        target.sortingLayerID = source.sortingLayerID;
        var targetOrder = source.sortingOrder + 1;
        target.sortingOrder = targetOrder;
        RaiseTextRenderersAbove(transform, target.transform, target.sortingLayerID, targetOrder + 1);
        target.lightProbeUsage = source.lightProbeUsage;
        target.reflectionProbeUsage = source.reflectionProbeUsage;
    }

    private static int FindFirstTextContentSiblingIndex(Transform? parent, Transform overlay)
    {
        if (parent == null)
        {
            return -1;
        }

        var result = -1;
        for (var i = 0; i < parent.childCount; i++)
        {
            var child = parent.GetChild(i);
            if (child == overlay || child.name == OverlayName)
            {
                continue;
            }

            if (!ContainsTextContentGraphic(child))
            {
                continue;
            }

            result = i;
            break;
        }

        return result;
    }

    private static bool ContainsTextContentGraphic(Transform node)
    {
        var graphics = node.GetComponentsInChildren<Graphic>(true);
        for (var i = 0; i < graphics.Length; i++)
        {
            var graphic = graphics[i];
            if (graphic == null || graphic is Image)
            {
                continue;
            }

            return true;
        }

        var components = node.GetComponentsInChildren<UnityEngine.Component>(true);
        for (var i = 0; i < components.Length; i++)
        {
            var component = components[i];
            if (component == null)
            {
                continue;
            }

            var typeName = component.GetType().Name;
            if (typeName == "TextMeshProUGUI" || typeName == "TMP_Text" || typeName == "TextMeshPro")
            {
                return true;
            }
        }

        return false;
    }

    private static void RaiseTextRenderersAbove(Transform root, Transform overlay, int sortingLayerID, int minimumSortingOrder)
    {
        var renderers = root.GetComponentsInChildren<Renderer>(true);
        for (var i = 0; i < renderers.Length; i++)
        {
            var renderer = renderers[i];
            if (renderer == null || renderer.transform == overlay || renderer.transform.IsChildOf(overlay))
            {
                continue;
            }

            if (!IsTextRenderer(renderer))
            {
                continue;
            }

            if (renderer.sortingLayerID != sortingLayerID || renderer.sortingOrder >= minimumSortingOrder)
            {
                continue;
            }

            renderer.sortingOrder = minimumSortingOrder;
        }
    }

    private static bool IsTextRenderer(Renderer renderer)
    {
        var components = renderer.GetComponents<UnityEngine.Component>();
        for (var i = 0; i < components.Length; i++)
        {
            var component = components[i];
            if (component == null)
            {
                continue;
            }

            var typeName = component.GetType().Name;
            if (typeName == "TextMeshPro" || typeName == "TextMeshProUGUI" || typeName == "TMP_Text")
            {
                return true;
            }
        }

        return renderer.name.IndexOf("text", System.StringComparison.OrdinalIgnoreCase) >= 0
            || renderer.name.IndexOf("title", System.StringComparison.OrdinalIgnoreCase) >= 0
            || renderer.name.IndexOf("cost", System.StringComparison.OrdinalIgnoreCase) >= 0
            || renderer.name.IndexOf("desc", System.StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private bool DestroyImageOverlay()
    {
        if (overlayImage == null)
        {
            overlayRect = null;
            return false;
        }

        var overlayObject = overlayImage.gameObject;
        overlayImage = null;
        overlayRect = null;
        Destroy(overlayObject);
        return true;
    }

    private bool DestroyMeshOverlay()
    {
        if (overlayMeshRenderer == null)
        {
            overlayMeshFilter = null;
            DestroyOverlayMeshAsset();
            return false;
        }

        var overlayObject = overlayMeshRenderer.gameObject;
        overlayMeshFilter = null;
        overlayMeshRenderer = null;
        Destroy(overlayObject);
        DestroyOverlayMeshAsset();
        return true;
    }

    private void DestroyOverlayMeshAsset()
    {
        if (overlayMesh != null)
        {
            Object.Destroy(overlayMesh);
            overlayMesh = null;
        }

        sourceMesh = null;
    }

    private void OnDestroy()
    {
        Clear();
    }
}
