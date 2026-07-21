using System;
using System.Collections.Generic;
using SunExp.Dll.Hooks.Visual;
using SunExp.Dll.Infrastructure;
using UnityEngine;
using Object = UnityEngine.Object;

namespace SunExp.Dll.Hooks.Ui;

public static class StarScoreHudShaderIds
{
    public const string ShaderName = "SunExp/StarScoreHud";
    public const string ShaderId = "sunexp.star_score_hud";
    public const string LitSlotEffectId = "sunexp.star_score_hud.lit_slot";

    public static readonly int LitAmount = Shader.PropertyToID("_SunExpLitAmount");
    public static readonly int Pulse = Shader.PropertyToID("_SunExpPulse");
    public static readonly int FlowTime = Shader.PropertyToID("_SunExpFlowTime");
    public static readonly int FlowStrength = Shader.PropertyToID("_SunExpFlowStrength");
    public static readonly int SlotIndex = Shader.PropertyToID("_SunExpSlotIndex");
    public static readonly int Tint = Shader.PropertyToID("_SunExpTint");
    public static readonly int GlowColor = Shader.PropertyToID("_SunExpGlowColor");
    public static readonly int FlowColor = Shader.PropertyToID("_SunExpFlowColor");
    public static readonly int FlowSpeed = Shader.PropertyToID("_SunExpFlowSpeed");
    public static readonly int FlowScale = Shader.PropertyToID("_SunExpFlowScale");
    public static readonly int EdgeGlow = Shader.PropertyToID("_SunExpEdgeGlow");
}

public static class StarScoreHudShaderMaterials
{
    public static Material? CreateLitMaterial(int slotIndex)
    {
        var material = EffectMaterialFactory.CreateMaterial(
            StarScoreHudShaderIds.LitSlotEffectId,
            StarScoreHudShaderIds.ShaderId,
            StarScoreHudShaderIds.ShaderName,
            "[StarScoreHud]");
        if (material == null)
        {
            SunExpLog.Debug("[StarScoreHud] shader not found; using UI layered fallback: " + StarScoreHudShaderIds.ShaderName);
            return null;
        }

        material.name = "SunExp_StarScoreHud_LitSlot" + Math.Max(0, slotIndex);
        material.SetFloat(StarScoreHudShaderIds.SlotIndex, Math.Max(0, slotIndex));
        material.SetFloat(StarScoreHudShaderIds.LitAmount, 0f);
        material.SetFloat(StarScoreHudShaderIds.Pulse, 0f);
        material.SetFloat(StarScoreHudShaderIds.FlowTime, 0f);
        material.SetFloat(StarScoreHudShaderIds.FlowStrength, 0f);
        material.SetColor(StarScoreHudShaderIds.Tint, Color.white);
        return material;
    }

    public static void DestroyAll(IEnumerable<Material?> materials)
    {
        foreach (var material in materials)
        {
            if (material != null)
            {
                Object.Destroy(material);
            }
        }
    }

}
