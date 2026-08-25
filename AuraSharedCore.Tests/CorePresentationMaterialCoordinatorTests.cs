using AuraShared.Core;

internal static partial class CoreTestSuite
{
    public static void TestPresentationMaterialCoordinatorContracts()
    {
        const int rootId = 8101;
        const int generation = 17;
        const int targetId = 8201;
        var native = new FakeMaterial(100);
        var effect = new FakeMaterial(200);
        var exit = new FakeMaterial(300);
        object? current = native;
        var alive = true;
        var released = new List<int>();

        var effectLease = Acquire(rootId, generation, targetId, "card-effect", effect);
        Assert(ReferenceEquals(current, effect) && effectLease.OwnsCurrent,
            "the first coordinated layer owns the live renderer material");
        var exitLease = Acquire(rootId, generation, targetId, "exit-burn", exit);
        Assert(ReferenceEquals(current, exit) && exitLease.OwnsCurrent && !effectLease.OwnsCurrent,
            "a newer coordinated layer becomes the only current material owner");

        var earlyEffectRelease = effectLease.Release();
        Assert(earlyEffectRelease.IsPending
               && ReferenceEquals(current, exit)
               && released.Count == 0,
            "an out-of-order lower-layer release waits without restoring or destroying materials");
        Assert(!AuraPresentationMaterialCoordinator.IsViewClean(rootId, generation, out var activeStack)
               && activeStack.Contains("card-effect", StringComparison.Ordinal)
               && activeStack.Contains("exit-burn", StringComparison.Ordinal),
            "a pooled view cannot return idle while coordinated material layers remain");

        var exitRelease = exitLease.Release();
        Assert(exitRelease.IsClean
               && ReferenceEquals(current, native)
               && released.SequenceEqual(new[] { 300, 200 }),
            "releasing the top layer drains pending predecessors in LIFO order to the native material");
        Assert(AuraPresentationMaterialCoordinator.IsViewClean(rootId, generation, out _),
            "a fully drained material stack permits pooled-view reuse");
        effectLease.Release();
        exitLease.Release();
        Assert(released.SequenceEqual(new[] { 300, 200 }),
            "coordinated material release is idempotent");

        var blockedEffect = new FakeMaterial(400);
        var foreign = new FakeMaterial(999);
        var blockedLease = Acquire(rootId, generation + 1, targetId, "blocked-effect", blockedEffect);
        current = foreign;
        var blockedRelease = blockedLease.Release();
        Assert(blockedRelease.IsBlocked
               && ReferenceEquals(current, foreign)
               && !released.Contains(400),
            "an external material mutation blocks restoration instead of reviving a stale predecessor");
        AuraPresentationMaterialCoordinator.AbandonView(rootId, generation + 1);
        Assert(ReferenceEquals(current, foreign)
               && released.Count(value => value == 400) == 1
               && AuraPresentationMaterialCoordinator.IsViewClean(rootId, generation + 1, out _),
            "destroying a blocked pooled view releases owned materials without touching the foreign renderer state");

        current = native;
        var oldGenerationLease = Acquire(rootId, generation + 2, targetId, "old-generation", effect);
        var conflictingRequest = Request(rootId, generation + 3, targetId, "new-generation", exit);
        Assert(!AuraPresentationMaterialCoordinator.TryAcquire(
                   conflictingRequest,
                   out var conflictingLease,
                   out var generationFailure)
               && conflictingLease == null
               && generationFailure.Contains("still owned", StringComparison.Ordinal),
            "one renderer cannot cross pooled-view generations while an older stack is active");
        oldGenerationLease.Release();

        current = native;
        alive = true;
        var deadTargetLease = Acquire(rootId, generation + 4, targetId, "dead-target", exit);
        alive = false;
        var deadTargetRelease = deadTargetLease.Release();
        Assert(deadTargetRelease.IsClean
               && released.Count(value => value == 300) == 2,
            "a destroyed target abandons and releases its applied material without attempting restoration");

        alive = true;
        current = native;
        var identityFailureRequest = Request(
            rootId,
            generation + 5,
            targetId,
            "identity-failure",
            effect);
        identityFailureRequest.MaterialInstanceId = material =>
            ReferenceEquals(material, effect)
                ? throw new InvalidOperationException("identity fixture")
                : (material as FakeMaterial)?.Id ?? 0;
        Assert(!AuraPresentationMaterialCoordinator.TryAcquire(
                   identityFailureRequest,
                   out _,
                   out var identityFailure)
               && identityFailure.Contains("identity fixture", StringComparison.Ordinal)
               && ReferenceEquals(current, native)
               && AuraPresentationMaterialCoordinator.IsViewClean(rootId, generation + 5, out _),
            "material callback failures are reported without mutating or stranding the view");

        current = native;
        var partialWriteRequest = Request(
            rootId,
            generation + 6,
            targetId,
            "partial-write",
            effect);
        partialWriteRequest.WriteCurrentMaterial = material =>
        {
            current = material;
            if (ReferenceEquals(material, effect))
            {
                throw new InvalidOperationException("partial write fixture");
            }
        };
        Assert(!AuraPresentationMaterialCoordinator.TryAcquire(
                   partialWriteRequest,
                   out _,
                   out var partialWriteFailure)
               && partialWriteFailure.Contains("partial write fixture", StringComparison.Ordinal)
               && ReferenceEquals(current, native)
               && AuraPresentationMaterialCoordinator.IsViewClean(rootId, generation + 6, out _),
            "a partially failed attachment rolls back to the captured native material");

        current = native;
        var quarantineRequest = Request(
            rootId,
            generation + 7,
            targetId,
            "rollback-failure",
            effect);
        quarantineRequest.WriteCurrentMaterial = material =>
        {
            if (ReferenceEquals(material, effect))
            {
                current = material;
                throw new InvalidOperationException("attach fixture");
            }

            throw new InvalidOperationException("rollback fixture");
        };
        Assert(!AuraPresentationMaterialCoordinator.TryAcquire(
                   quarantineRequest,
                   out _,
                   out var quarantineFailure)
               && quarantineFailure.Contains("rollback=", StringComparison.Ordinal)
               && !AuraPresentationMaterialCoordinator.IsViewClean(
                   rootId,
                   generation + 7,
                   out var quarantineDiagnostic)
               && quarantineDiagnostic.Contains("fault=", StringComparison.Ordinal),
            "an attachment that cannot roll back quarantines the view generation instead of returning it to the pool");
        AuraPresentationMaterialCoordinator.AbandonView(rootId, generation + 7);
        Assert(AuraPresentationMaterialCoordinator.IsViewClean(rootId, generation + 7, out _),
            "destroying a quarantined view clears the coordinator ownership record");

        AuraPresentationMaterialLease Acquire(
            int viewRootId,
            int viewGeneration,
            int rendererId,
            string owner,
            FakeMaterial applied)
        {
            var acquired = AuraPresentationMaterialCoordinator.TryAcquire(
                Request(viewRootId, viewGeneration, rendererId, owner, applied),
                out var lease,
                out var failure);
            Assert(acquired && lease != null,
                "coordinated material layer acquires: " + owner + "; " + failure);
            return lease!;
        }

        AuraPresentationMaterialAcquireRequest Request(
            int viewRootId,
            int viewGeneration,
            int rendererId,
            string owner,
            FakeMaterial applied)
        {
            return new AuraPresentationMaterialAcquireRequest
            {
                ViewRootInstanceId = viewRootId,
                ViewGeneration = viewGeneration,
                TargetInstanceId = rendererId,
                OwnerId = owner,
                AppliedMaterial = applied,
                IsTargetAlive = () => alive,
                ReadCurrentMaterial = () => current,
                WriteCurrentMaterial = material => current = material,
                MaterialInstanceId = material => (material as FakeMaterial)?.Id ?? 0,
                ReleaseAppliedMaterial = material => released.Add(((FakeMaterial)material).Id)
            };
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
