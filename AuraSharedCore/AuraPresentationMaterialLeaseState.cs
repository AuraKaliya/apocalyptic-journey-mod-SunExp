using System;

namespace AuraShared.Core;

/// <summary>
/// Describes how a presentation material owner may unwind its lease without
/// overwriting a newer owner on the same renderer.
/// </summary>
public readonly struct AuraPresentationMaterialDetachPlan
{
    public AuraPresentationMaterialDetachPlan(
        bool restoreOriginal,
        bool releaseApplied,
        bool blockedByForeignMaterial)
    {
        RestoreOriginal = restoreOriginal;
        ReleaseApplied = releaseApplied;
        BlockedByForeignMaterial = blockedByForeignMaterial;
    }

    public bool RestoreOriginal { get; }

    public bool ReleaseApplied { get; }

    public bool BlockedByForeignMaterial { get; }
}

/// <summary>
/// Unity-independent ownership state for layered presentation materials.
/// Owners unwind in LIFO order and may restore their original material only
/// while their exact applied material still owns the target.
/// </summary>
public sealed class AuraPresentationMaterialLeaseState
{
    public int TargetInstanceId { get; private set; }

    public int OriginalMaterialInstanceId { get; private set; }

    public int AppliedMaterialInstanceId { get; private set; }

    public bool IsActive => TargetInstanceId != 0 && AppliedMaterialInstanceId != 0;

    public void Bind(
        int targetInstanceId,
        int originalMaterialInstanceId,
        int appliedMaterialInstanceId)
    {
        if (targetInstanceId == 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(targetInstanceId),
                "a material lease requires a live target identity");
        }
        if (appliedMaterialInstanceId == 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(appliedMaterialInstanceId),
                "a material lease requires an applied material identity");
        }
        if (IsActive)
        {
            throw new InvalidOperationException(
                "an active presentation material lease must be detached before rebinding");
        }

        TargetInstanceId = targetInstanceId;
        OriginalMaterialInstanceId = originalMaterialInstanceId;
        AppliedMaterialInstanceId = appliedMaterialInstanceId;
    }

    public bool Owns(int targetInstanceId, int currentMaterialInstanceId)
    {
        return IsActive
               && targetInstanceId == TargetInstanceId
               && currentMaterialInstanceId == AppliedMaterialInstanceId;
    }

    public AuraPresentationMaterialDetachPlan PlanDetach(
        int currentTargetInstanceId,
        int currentMaterialInstanceId)
    {
        if (!IsActive)
        {
            return new AuraPresentationMaterialDetachPlan(false, false, false);
        }

        if (currentTargetInstanceId == 0)
        {
            return new AuraPresentationMaterialDetachPlan(
                restoreOriginal: false,
                releaseApplied: true,
                blockedByForeignMaterial: false);
        }

        if (Owns(currentTargetInstanceId, currentMaterialInstanceId))
        {
            return new AuraPresentationMaterialDetachPlan(
                restoreOriginal: true,
                releaseApplied: true,
                blockedByForeignMaterial: false);
        }

        return new AuraPresentationMaterialDetachPlan(
            restoreOriginal: false,
            releaseApplied: false,
            blockedByForeignMaterial: true);
    }

    public void Clear()
    {
        TargetInstanceId = 0;
        OriginalMaterialInstanceId = 0;
        AppliedMaterialInstanceId = 0;
    }
}
