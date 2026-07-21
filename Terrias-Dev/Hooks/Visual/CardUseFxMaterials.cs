using UnityEngine;
using SunExp.Dll.Infrastructure;

namespace SunExp.Dll.Hooks.Visual;

internal static class CardUseFxMaterials
{
    public static Material? CreateFaceSweepMaterial(string visualEffectId)
    {
        var material = EffectMaterialFactory.CreateMaterial(
            visualEffectId,
            SunExpIds.CardFaceEffectShaderId,
            CardFaceEffectShaderIds.ShaderName,
            "[CardUseFx]");
        if (material == null)
        {
            return null;
        }

        material.name = "SunExp_CardUseFx_FaceSweep";
        if (material.HasProperty(CardFaceEffectShaderIds.OverlayMode))
        {
            material.SetFloat(CardFaceEffectShaderIds.OverlayMode, 1f);
        }
        if (material.HasProperty(CardFaceEffectShaderIds.FrameOnlyOverlay))
        {
            material.SetFloat(CardFaceEffectShaderIds.FrameOnlyOverlay, 0f);
        }

        return material;
    }
}
