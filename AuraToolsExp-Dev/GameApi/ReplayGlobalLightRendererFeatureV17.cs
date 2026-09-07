using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace AuraToolsExp.Dll.GameApi;

/// <summary>
/// Renderer2D culls even shapeless global lights by GameObject layer. Replay's
/// object mask therefore removes the native ambient light from Sprite-Lit HUDs.
/// Restore only that ambient input in the owned renderer's cull result, after
/// native culling and before layer batches/light textures are constructed.
/// No light is cloned, registered, moved or edited, and no native object layer
/// is added to the camera mask.
/// </summary>
internal sealed class ReplayGlobalLightRendererFeatureV17 : ScriptableRendererFeature
{
    private const BindingFlags InstanceMembers = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private ScriptableRenderer? boundRenderer;
    private List<Light2D>? visibleLights;

    public override void Create()
    {
        boundRenderer = null;
        visibleLights = null;
    }

    public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
    {
        var camera = renderingData.cameraData.camera;
        if (camera.cullingMask != 1 << 30)
            throw new InvalidOperationException("Replay ambient lighting requires the isolated replay camera mask.");
        if (!ReferenceEquals(boundRenderer, renderer))
        {
            var type = renderer.GetType();
            var data = type.GetField("m_Renderer2DData", InstanceMembers)?.GetValue(renderer)
                       ?? throw new MissingFieldException(type.FullName, "m_Renderer2DData");
            var cull = data.GetType().GetProperty("lightCullResult", InstanceMembers)?.GetValue(data)
                       ?? throw new MissingMemberException(data.GetType().FullName, "lightCullResult");
            visibleLights = cull.GetType().GetProperty("visibleLights", InstanceMembers)?.GetValue(cull) as List<Light2D>
                            ?? throw new MissingMemberException(cull.GetType().FullName, "visibleLights");
            boundRenderer = renderer;
        }
        foreach (var light in UnityEngine.Object.FindObjectsByType<Light2D>(FindObjectsSortMode.None))
        {
            if (!light.isActiveAndEnabled || light.lightType != Light2D.LightType.Global
                || visibleLights!.Contains(light)) continue;
            visibleLights.Add(light);
        }
        visibleLights!.Sort((left, right) => left.lightOrder.CompareTo(right.lightOrder));
    }

    protected override void Dispose(bool disposing) => Create();
}
