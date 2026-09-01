using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

namespace AuraToolsExp.Dll.GameApi;

/// <summary>
/// Declares the intermediate camera-color surface required by retained replay
/// full-screen passes. The pass itself is intentionally empty: Renderer2D uses
/// its Color input while building the camera resources, and no gameplay or
/// screen-global effect is introduced.
/// </summary>
internal sealed class ReplayIntermediateColorRendererFeatureV17 : ScriptableRendererFeature
{
    private ReplayIntermediateColorPassV17? pass;

    public override void Create()
    {
        pass = new ReplayIntermediateColorPassV17();
    }

    public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
    {
        pass ??= new ReplayIntermediateColorPassV17();
        renderer.EnqueuePass(pass);
    }

    protected override void Dispose(bool disposing)
    {
        pass = null;
    }

    private sealed class ReplayIntermediateColorPassV17 : ScriptableRenderPass
    {
        internal ReplayIntermediateColorPassV17()
        {
            renderPassEvent = RenderPassEvent.BeforeRendering;
            ConfigureInput(ScriptableRenderPassInput.Color);
        }

        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            // Resource declaration only. Renderer2D observes the Color input
            // before recording passes and allocates cameraColor for the owned
            // full-screen feature clone.
        }

#pragma warning disable CS0672, CS0618
        public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
        {
            // Compatibility-mode equivalent of the no-op RenderGraph pass.
        }
#pragma warning restore CS0672, CS0618
    }
}
