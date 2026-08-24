using AuraShared.Core;
using AuraToolsExp.Dll.Features.CardVisual;

internal static partial class AuraToolsTestSuite
{
    public static void TestCardVisualRenderTargetPolicy()
    {
        Assert(CardVisualRenderTargetPolicy.Resolve(
                   backgroundHasMesh: true,
                   frameHasImage: true,
                   frameHasMesh: true)
               == CardVisualRenderTargetKind.Mesh,
            "a combat card follows the native background MeshRenderer gate even when a legacy Image coexists");
        Assert(CardVisualRenderTargetPolicy.Resolve(
                   backgroundHasMesh: false,
                   frameHasImage: true,
                   frameHasMesh: false)
               == CardVisualRenderTargetKind.Image,
            "an Image-only card frame uses the UI material contract");
        Assert(CardVisualRenderTargetPolicy.Resolve(
                   backgroundHasMesh: true,
                   frameHasImage: false,
                   frameHasMesh: true)
               == CardVisualRenderTargetKind.Mesh,
            "a native Mesh card keeps the URP material contract");
        Assert(CardVisualRenderTargetPolicy.Resolve(
                   backgroundHasMesh: false,
                   frameHasImage: false,
                   frameHasMesh: true)
               == CardVisualRenderTargetKind.None,
            "a Mesh on only the frame node cannot override the native Image-mode selector");
        Assert(CardVisualRenderTargetPolicy.Resolve(
                   backgroundHasMesh: true,
                   frameHasImage: true,
                   frameHasMesh: false)
               == CardVisualRenderTargetKind.None,
            "a malformed native Mesh card fails closed instead of mutating its legacy Image");
        Assert(CardVisualRenderTargetPolicy.Resolve(
                   backgroundHasMesh: false,
                   frameHasImage: false,
                   frameHasMesh: false)
               == CardVisualRenderTargetKind.None,
            "a surface without a native frame renderer fails closed");

        var lease = new AuraPresentationMaterialLeaseState();
        lease.Bind(targetInstanceId: 10, originalMaterialInstanceId: 20, appliedMaterialInstanceId: 30);
        Assert(lease.IsActive && lease.Owns(10, 30),
            "a dynamic material lease owns one exact renderer and material instance");
        Assert(!lease.Owns(10, 31) && !lease.Owns(11, 30),
            "a material lease cannot claim a foreign material or pooled renderer");
        var firstDetach = lease.PlanDetach(10, 30);
        Assert(firstDetach.RestoreOriginal && firstDetach.ReleaseApplied,
            "pool release restores the original material before destroying the dynamic material");
        lease.Clear();
        Assert(!lease.IsActive && !lease.PlanDetach(10, 30).ReleaseApplied,
            "a cleared pooled view cannot destroy the previous card's material twice");

        lease.Bind(targetInstanceId: 10, originalMaterialInstanceId: 0, appliedMaterialInstanceId: 35);
        Assert(lease.PlanDetach(10, 35).RestoreOriginal,
            "an Image using Unity's implicit default material restores its intentional null material");
        lease.Clear();

        lease.Bind(targetInstanceId: 10, originalMaterialInstanceId: 20, appliedMaterialInstanceId: 40);
        Assert(lease.Owns(10, 40),
            "the same pooled renderer can acquire a new dynamic material after a theme-only binding");
        var foreignOwner = lease.PlanDetach(10, 50);
        Assert(foreignOwner.BlockedByForeignMaterial
               && !foreignOwner.RestoreOriginal
               && !foreignOwner.ReleaseApplied,
            "a dynamic visual cannot overwrite or destroy the material held by a newer exit-animation owner");
        var destroyedTarget = lease.PlanDetach(0, 0);
        Assert(!destroyedTarget.RestoreOriginal && destroyedTarget.ReleaseApplied,
            "destroying a card view releases its dynamic material without touching another renderer");
    }
}
