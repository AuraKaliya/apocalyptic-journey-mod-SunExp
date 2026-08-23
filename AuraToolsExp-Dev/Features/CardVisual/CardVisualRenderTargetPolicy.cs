namespace AuraToolsExp.Dll.Features.CardVisual;

internal enum CardVisualRenderTargetKind
{
    None,
    Image,
    Mesh
}

internal static class CardVisualRenderTargetPolicy
{
    internal static CardVisualRenderTargetKind Resolve(bool hasImage, bool hasMesh)
    {
        if (hasImage) return CardVisualRenderTargetKind.Image;
        return hasMesh ? CardVisualRenderTargetKind.Mesh : CardVisualRenderTargetKind.None;
    }
}
