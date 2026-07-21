using System;

namespace SunExp.Dll.Mechanics;

public enum PooledCardViewState
{
    Idle,
    Bound,
    NativeVisualSuppressed,
    Exiting,
    Resetting
}

public enum PooledCardExitKind
{
    Burn,
    MoveToDiscard,
    MoveToDrawPile,
    Unsupported
}

public static class PooledCardViewExit
{
    public const string DiscardTargetPath = "Canvas/FightUI/ClockBoard/弃牌堆";
    public const string DrawPileTargetPath = "Canvas/FightUI/Left/Card";

    public static PooledCardExitKind ClassifyThrowTarget(string? targetPath)
    {
        if (string.Equals(targetPath, DiscardTargetPath, StringComparison.Ordinal))
        {
            return PooledCardExitKind.MoveToDiscard;
        }

        if (string.Equals(targetPath, DrawPileTargetPath, StringComparison.Ordinal))
        {
            return PooledCardExitKind.MoveToDrawPile;
        }

        return PooledCardExitKind.Unsupported;
    }
}
