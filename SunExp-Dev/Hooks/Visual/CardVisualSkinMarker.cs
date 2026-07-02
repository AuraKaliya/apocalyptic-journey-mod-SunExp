using UnityEngine;
using UnityEngine.UI;

namespace SunExp.Dll.Hooks.Visual;

internal sealed class CardVisualSkinMarker : MonoBehaviour
{
    private const string FaceEffectOverlayName = "SunExp_CardFaceEffectOverlay";
    private const string FrameEffectOverlayName = "SunExp_CardFrameEffectOverlay";

    private Transform? frameNode;
    private Transform? backgroundNode;
    private Image? frameImage;
    private Image? backgroundImage;
    private Image? frameEffectOverlayImage;
    private RectTransform? frameEffectOverlayRect;
    private Image? faceEffectOverlayImage;
    private RectTransform? faceEffectOverlayRect;
    private MeshRenderer? frameMesh;
    private MeshRenderer? backgroundMesh;
    private Material? frameMaterial;
    private Material? backgroundMaterial;
    private Material? originalFrameImageMaterial;
    private Material? originalFrameMeshMaterial;
    private Material? originalFaceImageMaterial;
    private Material? originalFaceMeshMaterial;
    private Material? frameEffectOwnedMaterial;
    private Material? faceEffectOwnedMaterial;
    private bool originalFrameImageMaterialCaptured;
    private bool originalFrameMeshMaterialCaptured;
    private bool originalFaceImageMaterialCaptured;
    private bool originalFaceMeshMaterialCaptured;

    public Transform? FrameNode => ResolveNode(ref frameNode, "Front/FrontBack");

    public Transform? BackgroundNode => ResolveNode(ref backgroundNode, "Front/background");

    public Image? FrameImage => ResolveImage(FrameNode, ref frameImage);

    public Image? BackgroundImage => ResolveImage(BackgroundNode, ref backgroundImage);

    public Image? FaceImage => BackgroundImage;

    public MeshRenderer? FrameMesh => ResolveMesh(FrameNode, ref frameMesh);

    public MeshRenderer? BackgroundMesh => ResolveMesh(BackgroundNode, ref backgroundMesh);

    public MeshRenderer? FaceMesh => BackgroundMesh;

    public Material? FrameMaterial => ResolveMaterial(FrameMesh, ref frameMaterial);

    public Material? BackgroundMaterial => ResolveMaterial(BackgroundMesh, ref backgroundMaterial);

    public Material? FaceMaterial => BackgroundMaterial;

    public string LastSkinId { get; set; } = "";

    public string LastVisualSignature { get; set; } = "";

    public string LastFrameEffectId { get; set; } = "";

    public string LastFaceEffectId { get; set; } = "";

    public int LastFrameTextureId { get; set; }

    public int LastBackgroundTextureId { get; set; }

    public Texture? LastFrameTexture { get; set; }

    public Texture? LastFaceTexture { get; set; }

    public Material? FrameEffectOwnedMaterial => frameEffectOwnedMaterial;

    public Material? FaceEffectOwnedMaterial => faceEffectOwnedMaterial;

    public string FaceEffectTargetSummary { get; private set; } = "";

    public bool ApplyFaceImageEffectOverlay(Material material)
    {
        var source = FaceImage;
        if (source == null)
        {
            DestroyFaceEffectOverlay();
            return false;
        }

        var overlay = EnsureFaceEffectOverlay(source);
        if (overlay == null)
        {
            return false;
        }

        CopyImageShape(source, overlay);
        var changed = !ReferenceEquals(overlay.material, material)
            || !ReferenceEquals(overlay.sprite, source.sprite)
            || !overlay.gameObject.activeSelf;
        overlay.material = material;
        overlay.sprite = source.sprite;
        overlay.gameObject.SetActive(true);
        FaceEffectTargetSummary = "overlay=" + FaceEffectOverlayName;
        return changed;
    }

    public bool ApplyFaceImageEffectMaterial(Material material)
    {
        var image = FaceImage;
        if (image == null)
        {
            return false;
        }

        if (!originalFaceImageMaterialCaptured)
        {
            originalFaceImageMaterial = image.material;
            originalFaceImageMaterialCaptured = true;
        }

        var changed = !ReferenceEquals(image.material, material);
        image.material = material;
        return changed;
    }

    public bool ApplyFaceMeshEffectMaterial(Material material)
    {
        var mesh = FaceMesh;
        if (mesh == null)
        {
            return false;
        }

        if (!originalFaceMeshMaterialCaptured)
        {
            originalFaceMeshMaterial = FaceMaterial;
            originalFaceMeshMaterialCaptured = true;
        }

        var changed = !ReferenceEquals(FaceMaterial, material);
        mesh.material = material;
        backgroundMaterial = material;
        return changed;
    }

    public bool ClearFaceEffectMaterial()
    {
        var changed = false;
        changed = DestroyFaceEffectOverlay() || changed;
        if (originalFaceImageMaterialCaptured)
        {
            var image = FaceImage;
            if (image != null)
            {
                changed = changed || !ReferenceEquals(image.material, originalFaceImageMaterial);
                image.material = originalFaceImageMaterial;
            }

            originalFaceImageMaterial = null;
            originalFaceImageMaterialCaptured = false;
        }

        if (originalFaceMeshMaterialCaptured)
        {
            var mesh = FaceMesh;
            if (mesh != null)
            {
                changed = true;
                mesh.material = originalFaceMeshMaterial;
                backgroundMaterial = originalFaceMeshMaterial;
                if (LastFaceTexture != null && backgroundMaterial != null)
                {
                    backgroundMaterial.mainTexture = LastFaceTexture;
                }
            }

            originalFaceMeshMaterial = null;
            originalFaceMeshMaterialCaptured = false;
        }

        ClearOwnedFaceEffectMaterial();
        if (LastFaceEffectId.Length > 0)
        {
            changed = true;
        }

        LastFaceEffectId = "";
        FaceEffectTargetSummary = "";
        return changed;
    }

    public void ReplaceOwnedFaceEffectMaterial(Material? material)
    {
        if (!ReferenceEquals(faceEffectOwnedMaterial, material))
        {
            ClearOwnedFaceEffectMaterial();
            faceEffectOwnedMaterial = material;
        }
    }

    public void ClearOwnedFaceEffectMaterial()
    {
        if (faceEffectOwnedMaterial != null)
        {
            CardFaceEffectMaterials.DestroyOwned(faceEffectOwnedMaterial);
            faceEffectOwnedMaterial = null;
        }
    }

    public bool ApplyFrameImageEffectMaterial(Material material)
    {
        var image = FrameImage;
        if (image == null)
        {
            return false;
        }

        if (!originalFrameImageMaterialCaptured)
        {
            originalFrameImageMaterial = image.material;
            originalFrameImageMaterialCaptured = true;
        }

        var changed = !ReferenceEquals(image.material, material);
        image.material = material;
        return changed;
    }

    public bool ApplyFrameImageEffectOverlay(Material material)
    {
        var source = FrameImage ?? BackgroundImage;
        if (source == null)
        {
            DestroyFrameEffectOverlay();
            return false;
        }

        var overlay = EnsureFrameEffectOverlay(source);
        if (overlay == null)
        {
            return false;
        }

        CopyImageShape(source, overlay);
        var changed = !ReferenceEquals(overlay.material, material)
            || !ReferenceEquals(overlay.sprite, source.sprite)
            || !overlay.gameObject.activeSelf;
        overlay.material = material;
        overlay.sprite = source.sprite;
        overlay.gameObject.SetActive(true);
        return changed;
    }

    public bool ApplyFrameMeshEffectMaterial(Material material)
    {
        var mesh = FrameMesh;
        if (mesh == null)
        {
            return false;
        }

        if (!originalFrameMeshMaterialCaptured)
        {
            originalFrameMeshMaterial = FrameMaterial;
            originalFrameMeshMaterialCaptured = true;
        }

        var changed = !ReferenceEquals(FrameMaterial, material);
        mesh.material = material;
        frameMaterial = material;
        return changed;
    }

    public bool ClearFrameEffectMaterial()
    {
        var changed = false;
        changed = DestroyFrameEffectOverlay() || changed;
        if (originalFrameImageMaterialCaptured)
        {
            var image = FrameImage;
            if (image != null)
            {
                changed = changed || !ReferenceEquals(image.material, originalFrameImageMaterial);
                image.material = originalFrameImageMaterial;
            }

            originalFrameImageMaterial = null;
            originalFrameImageMaterialCaptured = false;
        }

        if (originalFrameMeshMaterialCaptured)
        {
            var mesh = FrameMesh;
            if (mesh != null)
            {
                changed = true;
                mesh.material = originalFrameMeshMaterial;
                frameMaterial = originalFrameMeshMaterial;
                if (LastFrameTexture != null && frameMaterial != null)
                {
                    frameMaterial.mainTexture = LastFrameTexture;
                }
            }

            originalFrameMeshMaterial = null;
            originalFrameMeshMaterialCaptured = false;
        }

        ClearOwnedFrameEffectMaterial();
        if (LastFrameEffectId.Length > 0)
        {
            changed = true;
        }

        LastFrameEffectId = "";
        return changed;
    }

    public void ReplaceOwnedFrameEffectMaterial(Material? material)
    {
        if (!ReferenceEquals(frameEffectOwnedMaterial, material))
        {
            ClearOwnedFrameEffectMaterial();
            frameEffectOwnedMaterial = material;
        }
    }

    public void ClearOwnedFrameEffectMaterial()
    {
        if (frameEffectOwnedMaterial != null)
        {
            CardFrameEffectMaterials.DestroyOwned(frameEffectOwnedMaterial);
            frameEffectOwnedMaterial = null;
        }
    }

    private Transform? ResolveNode(ref Transform? cached, string path)
    {
        if (cached != null)
        {
            return cached;
        }

        cached = transform.Find(path);
        return cached;
    }

    private static Image? ResolveImage(Transform? node, ref Image? cached)
    {
        if (cached != null)
        {
            return cached;
        }

        cached = node == null ? null : node.GetComponent<Image>();
        return cached;
    }

    private static MeshRenderer? ResolveMesh(Transform? node, ref MeshRenderer? cached)
    {
        if (cached != null)
        {
            return cached;
        }

        cached = node == null ? null : node.GetComponent<MeshRenderer>();
        return cached;
    }

    private static Material? ResolveMaterial(MeshRenderer? mesh, ref Material? cached)
    {
        if (cached != null)
        {
            return cached;
        }

        cached = mesh == null ? null : mesh.material;
        return cached;
    }

    private Image? EnsureFaceEffectOverlay(Image source)
    {
        if (faceEffectOverlayImage != null)
        {
            PositionFaceEffectOverlay(source);
            return faceEffectOverlayImage;
        }

        var parent = ResolveFaceEffectOverlayParent(source);
        if (parent == null)
        {
            return null;
        }

        var overlayObject = new GameObject(FaceEffectOverlayName, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        overlayObject.layer = source.gameObject.layer;
        overlayObject.transform.SetParent(parent, false);

        faceEffectOverlayRect = overlayObject.GetComponent<RectTransform>();
        faceEffectOverlayImage = overlayObject.GetComponent<Image>();
        faceEffectOverlayImage.raycastTarget = false;
        faceEffectOverlayImage.maskable = source.maskable;
        PositionFaceEffectOverlay(source);
        return faceEffectOverlayImage;
    }

    private Image? EnsureFrameEffectOverlay(Image source)
    {
        if (frameEffectOverlayImage != null)
        {
            PositionFrameEffectOverlay(source);
            return frameEffectOverlayImage;
        }

        var parent = ResolveFaceEffectOverlayParent(source);
        if (parent == null)
        {
            return null;
        }

        var overlayObject = new GameObject(FrameEffectOverlayName, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        overlayObject.layer = source.gameObject.layer;
        overlayObject.transform.SetParent(parent, false);

        frameEffectOverlayRect = overlayObject.GetComponent<RectTransform>();
        frameEffectOverlayImage = overlayObject.GetComponent<Image>();
        frameEffectOverlayImage.raycastTarget = false;
        frameEffectOverlayImage.maskable = source.maskable;
        PositionFrameEffectOverlay(source);
        return frameEffectOverlayImage;
    }

    private Transform? ResolveFaceEffectOverlayParent(Image source)
    {
        var frame = FrameNode;
        if (frame != null && frame.parent != null)
        {
            return frame.parent;
        }

        var background = BackgroundNode;
        if (background != null && background.parent != null)
        {
            return background.parent;
        }

        return source.transform.parent;
    }

    private void PositionFaceEffectOverlay(Image source)
    {
        if (faceEffectOverlayImage == null || faceEffectOverlayRect == null)
        {
            return;
        }

        var parent = ResolveFaceEffectOverlayParent(source);
        if (parent != null && faceEffectOverlayImage.transform.parent != parent)
        {
            faceEffectOverlayImage.transform.SetParent(parent, false);
        }

        var sourceRect = source.transform as RectTransform;
        if (sourceRect != null)
        {
            CopyRectShape(sourceRect, faceEffectOverlayRect);
        }

        var frame = FrameNode;
        if (frame != null && frame.parent == faceEffectOverlayImage.transform.parent)
        {
            var frameIndex = frame.GetSiblingIndex();
            var overlayIndex = faceEffectOverlayImage.transform.GetSiblingIndex();
            var targetIndex = overlayIndex < frameIndex ? frameIndex - 1 : frameIndex;
            faceEffectOverlayImage.transform.SetSiblingIndex(targetIndex);
        }
        else
        {
            faceEffectOverlayImage.transform.SetAsLastSibling();
        }
    }

    private void PositionFrameEffectOverlay(Image source)
    {
        if (frameEffectOverlayImage == null || frameEffectOverlayRect == null)
        {
            return;
        }

        var parent = ResolveFaceEffectOverlayParent(source);
        if (parent != null && frameEffectOverlayImage.transform.parent != parent)
        {
            frameEffectOverlayImage.transform.SetParent(parent, false);
        }

        var sourceRect = source.transform as RectTransform;
        if (sourceRect != null)
        {
            CopyRectShape(sourceRect, frameEffectOverlayRect);
        }

        var frame = FrameNode;
        if (frame != null && frame.parent == frameEffectOverlayImage.transform.parent)
        {
            var frameIndex = frame.GetSiblingIndex();
            var overlayIndex = frameEffectOverlayImage.transform.GetSiblingIndex();
            var targetIndex = overlayIndex < frameIndex ? frameIndex : frameIndex + 1;
            frameEffectOverlayImage.transform.SetSiblingIndex(targetIndex);
        }
        else
        {
            frameEffectOverlayImage.transform.SetAsLastSibling();
        }
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

    private bool DestroyFaceEffectOverlay()
    {
        if (faceEffectOverlayImage == null)
        {
            faceEffectOverlayRect = null;
            return false;
        }

        var overlayObject = faceEffectOverlayImage.gameObject;
        faceEffectOverlayImage = null;
        faceEffectOverlayRect = null;
        Destroy(overlayObject);
        return true;
    }

    private bool DestroyFrameEffectOverlay()
    {
        if (frameEffectOverlayImage == null)
        {
            frameEffectOverlayRect = null;
            return false;
        }

        var overlayObject = frameEffectOverlayImage.gameObject;
        frameEffectOverlayImage = null;
        frameEffectOverlayRect = null;
        Destroy(overlayObject);
        return true;
    }

    private void OnDestroy()
    {
        DestroyFrameEffectOverlay();
        DestroyFaceEffectOverlay();
        ClearOwnedFaceEffectMaterial();
        ClearOwnedFrameEffectMaterial();
    }
}
