using UnityEngine;
using Terrias.Dll.Infrastructure;

namespace Terrias.Dll.Hooks.Visual;

internal static class CardUseFxMaterials
{
    private const string ShaderId = "terrias.card_use_fx.stardust.shader";
    private const string ShaderName = "Terrias/CardUseStardust";
    private static readonly int OverlayMode = Shader.PropertyToID("_TerriasOverlayMode");
    private static readonly int FrameOnlyOverlay = Shader.PropertyToID("_TerriasFrameOnlyOverlay");

    public static Material? CreateFaceSweepMaterial(string visualEffectId)
    {
        var material = EffectMaterialFactory.CreateMaterial(
            visualEffectId,
            ShaderId,
            ShaderName,
            "[CardUseFx]");
        if (material == null)
        {
            return null;
        }

        material.name = "Terrias_CardUseFx_FaceSweep";
        if (material.HasProperty(OverlayMode))
        {
            material.SetFloat(OverlayMode, 1f);
        }
        if (material.HasProperty(FrameOnlyOverlay))
        {
            material.SetFloat(FrameOnlyOverlay, 0f);
        }

        return material;
    }
}
