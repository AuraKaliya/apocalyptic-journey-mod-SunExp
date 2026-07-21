using System;
using System.Collections.Generic;
using Terrias.Dll.Hooks.Visual;
using Terrias.Dll.Infrastructure;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Terrias.Dll.Hooks.Ui;

public static class StarScoreHudShaderIds
{
    public const string ShaderName = "Terrias/StarScoreHud";
    public const string ShaderId = "terrias.star_score_hud";
    public const string LitSlotEffectId = "terrias.star_score_hud.lit_slot";

    public static readonly int LitAmount = Shader.PropertyToID("_TerriasLitAmount");
    public static readonly int Pulse = Shader.PropertyToID("_TerriasPulse");
    public static readonly int FlowTime = Shader.PropertyToID("_TerriasFlowTime");
    public static readonly int FlowStrength = Shader.PropertyToID("_TerriasFlowStrength");
    public static readonly int SlotIndex = Shader.PropertyToID("_TerriasSlotIndex");
    public static readonly int Tint = Shader.PropertyToID("_TerriasTint");
    public static readonly int GlowColor = Shader.PropertyToID("_TerriasGlowColor");
    public static readonly int FlowColor = Shader.PropertyToID("_TerriasFlowColor");
    public static readonly int FlowSpeed = Shader.PropertyToID("_TerriasFlowSpeed");
    public static readonly int FlowScale = Shader.PropertyToID("_TerriasFlowScale");
    public static readonly int EdgeGlow = Shader.PropertyToID("_TerriasEdgeGlow");
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
            TerriasLog.Debug("[StarScoreHud] shader not found; using UI layered fallback: " + StarScoreHudShaderIds.ShaderName);
            return null;
        }

        material.name = "Terrias_StarScoreHud_LitSlot" + Math.Max(0, slotIndex);
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
