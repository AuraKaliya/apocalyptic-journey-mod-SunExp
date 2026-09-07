using System;

namespace AuraShared.Core;

/// <summary>Nested native refreshes have one presentation commit; exit is terminal.</summary>
internal sealed class AuraNativeCardPresentationState
{
    private int depth;
    private bool released;
    private bool exiting;

    internal bool AcceptsApply => depth == 0 && !exiting;

    internal void Begin(bool changesMaterials, Action release)
    {
        depth++;
        if (exiting || !changesMaterials || released) return;
        released = true;
        release();
    }

    internal bool End()
    {
        if (depth == 0) return false;
        depth--;
        if (depth != 0) return false;
        released = false;
        return !exiting;
    }

    internal void Exit(Action release)
    {
        if (exiting) return;
        exiting = true;
        release();
    }
}
