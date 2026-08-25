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

        const int rootId = 9101;
        const int generation = 23;
        const int targetId = 9201;
        var native = new FakeMaterial(20);
        var effect = new FakeMaterial(30);
        var burn = new FakeMaterial(40);
        object? current = native;
        var released = new List<int>();

        var effectLease = Acquire("AuraTools.CardEffect", effect);
        var burnLease = Acquire("Terrias.ExitBurn", burn);
        Assert(ReferenceEquals(current, burn)
               && burnLease.OwnsCurrent
               && !effectLease.OwnsCurrent,
            "AuraTools and Terrias temporary materials share one authoritative renderer stack");
        Assert(effectLease.Release().IsPending && ReferenceEquals(current, burn),
            "clearing a lower AuraTools effect cannot overwrite a newer pooled exit layer");
        Assert(burnLease.Release().IsClean
               && ReferenceEquals(current, native)
               && released.SequenceEqual(new[] { 40, 30 }),
            "pooled exit cleanup drains the pending AuraTools effect back to the native card face");
        Assert(AuraPresentationMaterialCoordinator.IsViewClean(rootId, generation, out _),
            "a card view becomes reusable only after every visual owner has left the stack");
        effectLease.Release();
        burnLease.Release();
        Assert(released.SequenceEqual(new[] { 40, 30 }),
            "consumer cleanup cannot destroy coordinated dynamic materials twice");

        AuraPresentationMaterialLease Acquire(string owner, FakeMaterial material)
        {
            var acquired = AuraPresentationMaterialCoordinator.TryAcquire(
                new AuraPresentationMaterialAcquireRequest
                {
                    ViewRootInstanceId = rootId,
                    ViewGeneration = generation,
                    TargetInstanceId = targetId,
                    OwnerId = owner,
                    AppliedMaterial = material,
                    IsTargetAlive = () => true,
                    ReadCurrentMaterial = () => current,
                    WriteCurrentMaterial = value => current = value,
                    MaterialInstanceId = value => (value as FakeMaterial)?.Id ?? 0,
                    ReleaseAppliedMaterial = value => released.Add(((FakeMaterial)value).Id)
                },
                out var lease,
                out var failure);
            Assert(acquired && lease != null,
                "card visual material acquires through the shared coordinator: " + failure);
            return lease!;
        }
    }

    private sealed class FakeMaterial
    {
        public FakeMaterial(int id)
        {
            Id = id;
        }

        public int Id { get; }
    }
}
