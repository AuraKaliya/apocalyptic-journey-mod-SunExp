using UnityEngine;
using UnityEngine.UI;

namespace SunExp.Dll.Hooks.Visual;

internal sealed class CardVisualSkinMarker : MonoBehaviour
{
    private Transform? frameNode;
    private Transform? backgroundNode;
    private Image? frameImage;
    private Image? backgroundImage;
    private MeshRenderer? frameMesh;
    private MeshRenderer? backgroundMesh;
    private Material? frameMaterial;
    private Material? backgroundMaterial;
    private Material? originalFrameImageMaterial;
    private Material? originalFrameMeshMaterial;
    private Material? frameEffectOwnedMaterial;
    private bool originalFrameImageMaterialCaptured;
    private bool originalFrameMeshMaterialCaptured;

    public Transform? FrameNode => ResolveNode(ref frameNode, "Front/FrontBack");

    public Transform? BackgroundNode => ResolveNode(ref backgroundNode, "Front/background");

    public Image? FrameImage => ResolveImage(FrameNode, ref frameImage);

    public Image? BackgroundImage => ResolveImage(BackgroundNode, ref backgroundImage);

    public MeshRenderer? FrameMesh => ResolveMesh(FrameNode, ref frameMesh);

    public MeshRenderer? BackgroundMesh => ResolveMesh(BackgroundNode, ref backgroundMesh);

    public Material? FrameMaterial => ResolveMaterial(FrameMesh, ref frameMaterial);

    public Material? BackgroundMaterial => ResolveMaterial(BackgroundMesh, ref backgroundMaterial);

    public string LastSkinId { get; set; } = "";

    public string LastVisualSignature { get; set; } = "";

    public string LastFrameEffectId { get; set; } = "";

    public int LastFrameTextureId { get; set; }

    public int LastBackgroundTextureId { get; set; }

    public Texture? LastFrameTexture { get; set; }

    public Material? FrameEffectOwnedMaterial => frameEffectOwnedMaterial;

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

    private void OnDestroy()
    {
        ClearOwnedFrameEffectMaterial();
    }
}
