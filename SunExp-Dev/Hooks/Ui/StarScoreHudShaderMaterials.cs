using System;
using System.Collections.Generic;
using SunExp.Dll.Infrastructure;
using UnityEngine;
using Object = UnityEngine.Object;

namespace SunExp.Dll.Hooks.Ui;

public static class StarScoreHudShaderIds
{
    public const string ShaderName = "SunExp/StarScoreHud";

    public static readonly int LitAmount = Shader.PropertyToID("_SunExpLitAmount");
    public static readonly int Pulse = Shader.PropertyToID("_SunExpPulse");
    public static readonly int FlowTime = Shader.PropertyToID("_SunExpFlowTime");
    public static readonly int FlowStrength = Shader.PropertyToID("_SunExpFlowStrength");
    public static readonly int SlotIndex = Shader.PropertyToID("_SunExpSlotIndex");
    public static readonly int Tint = Shader.PropertyToID("_SunExpTint");
}

public static class StarScoreHudShaderMaterials
{
    private static Shader? cachedShader;
    private static bool shaderLookupAttempted;

    public static Material? CreateLitMaterial(int slotIndex)
    {
        var shader = FindShader();
        if (shader == null)
        {
            return null;
        }

        var material = new Material(shader)
        {
            name = "SunExp_StarScoreHud_LitSlot" + Math.Max(0, slotIndex)
        };

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

    private static Shader? FindShader()
    {
        if (shaderLookupAttempted)
        {
            return cachedShader;
        }

        shaderLookupAttempted = true;
        try
        {
            cachedShader = Shader.Find(StarScoreHudShaderIds.ShaderName);
            if (cachedShader == null)
            {
                SunExpLog.Debug("[StarScoreHud] shader not found; using UI layered fallback: " + StarScoreHudShaderIds.ShaderName);
            }
        }
        catch (Exception ex)
        {
            SunExpLog.Warn("[StarScoreHud] shader lookup failed; using UI layered fallback: " + ex.Message);
        }

        return cachedShader;
    }
}
