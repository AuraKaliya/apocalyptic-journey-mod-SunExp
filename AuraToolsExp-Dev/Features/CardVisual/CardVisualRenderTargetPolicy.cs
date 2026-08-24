namespace AuraToolsExp.Dll.Features.CardVisual;

internal enum CardVisualRenderTargetKind
{
    None,
    Image,
    Mesh
}

internal static class CardVisualRenderTargetPolicy
{
    internal static CardVisualRenderTargetKind Resolve(
        bool backgroundHasMesh,
        bool frameHasImage,
        bool frameHasMesh)
    {
        // This intentionally mirrors Witch.ICard.SetCardStyle. The native game
        // selects the whole card presentation mode from Front/background and
        // then writes the matching component on Front/FrontBack. A legacy
        // Image may coexist with the live MeshRenderer on combat cards; its
        // mere presence must never switch the card to the UI material path.
        if (backgroundHasMesh)
        {
            return frameHasMesh
                ? CardVisualRenderTargetKind.Mesh
                : CardVisualRenderTargetKind.None;
        }

        return frameHasImage
            ? CardVisualRenderTargetKind.Image
            : CardVisualRenderTargetKind.None;
    }
}
