using System;

namespace AuraToolsExp.Dll.Features.Cg;

public static class AuraToolsCgSettingsLayoutPolicy
{
    public const float MaximumRolePageWidth = 980f;
    public const float MaximumEventPageWidth = 940f;
    public const float CandidateRowHeight = 84f;
    public const float CandidateFixedWidth = 470f;
    public const float MinimumCandidateTextWidth = 220f;
    public const float EventPreviewWidth = 520f;
    public const float EventPreviewHeight = 292f;
    public const float EventPreviewBodyHeight = 370f;
    public const float RoleFixedVerticalHeight = 290f;
    public const float EventFixedVerticalHeight = 220f;

    public static AuraToolsCgLayoutBudget Evaluate(float screenWidth, float screenHeight)
    {
        var roleContentWidth = ContentWidth(screenWidth, MaximumRolePageWidth);
        var eventContentWidth = ContentWidth(screenWidth, MaximumEventPageWidth);
        var contentHeight = Math.Max(0f, screenHeight - 56f);
        var roleBodyHeight = Math.Max(0f, contentHeight - RoleFixedVerticalHeight);
        var eventBodyHeight = Math.Max(0f, contentHeight - EventFixedVerticalHeight);
        return new AuraToolsCgLayoutBudget
        {
            RoleContentWidth = roleContentWidth,
            EventContentWidth = eventContentWidth,
            RoleCandidateTextWidth = Math.Max(0f, roleContentWidth - CandidateFixedWidth),
            VisibleRoleCandidateRows = (int)Math.Floor((roleBodyHeight + 8f) / (CandidateRowHeight + 8f)),
            EventPreviewFitsWidth = eventContentWidth >= EventPreviewWidth,
            EventPreviewFitsHeight = eventBodyHeight >= EventPreviewBodyHeight
        };
    }

    private static float ContentWidth(float screenWidth, float maximumWidth)
    {
        var overlayWidth = Math.Max(0f, screenWidth - 16f);
        var windowWidth = overlayWidth > maximumWidth + 36f
            ? maximumWidth
            : Math.Max(0f, overlayWidth - 20f);
        return Math.Max(0f, windowWidth - 32f);
    }
}

public sealed class AuraToolsCgLayoutBudget
{
    public float RoleContentWidth { get; set; }
    public float EventContentWidth { get; set; }
    public float RoleCandidateTextWidth { get; set; }
    public int VisibleRoleCandidateRows { get; set; }
    public bool EventPreviewFitsWidth { get; set; }
    public bool EventPreviewFitsHeight { get; set; }

    public bool Fits => RoleCandidateTextWidth >= AuraToolsCgSettingsLayoutPolicy.MinimumCandidateTextWidth
                        && VisibleRoleCandidateRows >= 4
                        && EventPreviewFitsWidth
                        && EventPreviewFitsHeight;
}
