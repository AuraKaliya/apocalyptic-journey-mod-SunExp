using System;
using UnityEngine;

namespace AuraShared.Core;

public enum AuraCardPresentationViewState
{
    Idle,
    Bound,
    NativeVisualSuppressed,
    Exiting,
    Resetting,
    Destroyed
}

/// <summary>
/// Shared lifecycle marker for a pooled card presentation root. It is the
/// authoritative apply gate and presentation generation for every consumer.
/// </summary>
public sealed class AuraCardPresentationViewMarker : MonoBehaviour
{
    public int Generation { get; private set; }

    public AuraCardPresentationViewState State { get; private set; } =
        AuraCardPresentationViewState.Idle;

    public bool AcceptsApply => State == AuraCardPresentationViewState.Bound;

    public void BeginGeneration(int generation)
    {
        if (State != AuraCardPresentationViewState.Idle)
        {
            throw new InvalidOperationException(
                "presentation generation can begin only from Idle; current=" + State);
        }
        if (!AuraPresentationMaterialCoordinator.IsViewClean(
                transform.GetInstanceID(),
                Generation,
                out var diagnostic))
        {
            throw new InvalidOperationException(
                "presentation generation cannot advance while materials remain: "
                + diagnostic);
        }

        Generation = generation <= 0 ? 1 : generation;
        State = AuraCardPresentationViewState.Bound;
    }

    public bool TryTransition(
        AuraCardPresentationViewState expected,
        AuraCardPresentationViewState next)
    {
        if (State != expected)
        {
            return false;
        }

        State = next;
        return true;
    }

    public void ForceState(AuraCardPresentationViewState state)
    {
        State = state;
    }

    private void OnDestroy()
    {
        State = AuraCardPresentationViewState.Destroyed;
        AuraPresentationMaterialCoordinator.AbandonView(
            transform.GetInstanceID(),
            Generation);
    }
}
