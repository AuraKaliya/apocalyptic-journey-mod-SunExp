using System;
using System.Collections.Generic;

namespace Terrias.Dll.Mechanics;

public enum ProjectionTurnQueueKind
{
    Other,
    NativePartner,
    TerriasAnchor,
    TerriasProjection,
    TerriasSpirit
}

public readonly struct ProjectionTurnQueueSnapshot
{
    public ProjectionTurnQueueSnapshot(
        int nativePartnerCount,
        int anchorCount,
        int directProjectionCount,
        int directSpiritCount)
    {
        NativePartnerCount = nativePartnerCount;
        AnchorCount = anchorCount;
        DirectProjectionCount = directProjectionCount;
        DirectSpiritCount = directSpiritCount;
    }

    public int NativePartnerCount { get; }

    public int AnchorCount { get; }

    public int DirectProjectionCount { get; }

    public int DirectSpiritCount { get; }

    public bool IsIsolated => AnchorCount == 0;
}

public static class ProjectionTurnQueuePolicy
{
    public static bool ShouldRemoveLegacyAnchor(ProjectionTurnQueueKind kind)
    {
        return kind == ProjectionTurnQueueKind.TerriasAnchor;
    }

    public static ProjectionTurnQueueSnapshot Analyze(IEnumerable<ProjectionTurnQueueKind>? kinds)
    {
        var nativePartners = 0;
        var anchors = 0;
        var projections = 0;
        var spirits = 0;
        foreach (var kind in kinds ?? Array.Empty<ProjectionTurnQueueKind>())
        {
            switch (kind)
            {
                case ProjectionTurnQueueKind.NativePartner:
                    nativePartners++;
                    break;
                case ProjectionTurnQueueKind.TerriasAnchor:
                    anchors++;
                    break;
                case ProjectionTurnQueueKind.TerriasProjection:
                    projections++;
                    break;
                case ProjectionTurnQueueKind.TerriasSpirit:
                    spirits++;
                    break;
            }
        }

        return new ProjectionTurnQueueSnapshot(nativePartners, anchors, projections, spirits);
    }
}
