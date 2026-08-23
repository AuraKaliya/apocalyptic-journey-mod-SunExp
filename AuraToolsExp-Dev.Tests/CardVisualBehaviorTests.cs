using AuraToolsExp.Dll.Features.CardVisual;

internal static partial class AuraToolsTestSuite
{
    public static void TestCardVisualRenderTargetPolicy()
    {
        Assert(CardVisualRenderTargetPolicy.Resolve(hasImage: true, hasMesh: true)
               == CardVisualRenderTargetKind.Image,
            "a native card frame Image always wins over a legacy MeshRenderer on the same node");
        Assert(CardVisualRenderTargetPolicy.Resolve(hasImage: true, hasMesh: false)
               == CardVisualRenderTargetKind.Image,
            "an Image-only card frame uses the UI material contract");
        Assert(CardVisualRenderTargetPolicy.Resolve(hasImage: false, hasMesh: true)
               == CardVisualRenderTargetKind.Mesh,
            "a genuine Mesh-only presentation surface keeps the URP material contract");
        Assert(CardVisualRenderTargetPolicy.Resolve(hasImage: false, hasMesh: false)
               == CardVisualRenderTargetKind.None,
            "a surface without a native frame renderer fails closed");
    }
}
