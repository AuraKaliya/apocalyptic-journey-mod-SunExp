using AuraShared.Core;
using Terrias.Dll.Mechanics;
using UnityEngine;
using Witch.UI;

namespace Terrias.Dll.Hooks.Ui;

public sealed class PooledCombatCardViewMarker : MonoBehaviour
{
    public int Generation { get; set; }

    public string Bucket { get; set; } = "";

    public string ConfigInstanceId { get; set; } = "";

    public string PresentationSignature { get; set; } = "";

    public bool HasInitializedPresentation { get; set; }

    private AuraCardPresentationViewMarker SharedMarker =>
        GetComponent<AuraCardPresentationViewMarker>()
        ?? gameObject.AddComponent<AuraCardPresentationViewMarker>();

    public int PresentationGeneration => SharedMarker.Generation;

    public AuraCardPresentationViewState State => SharedMarker.State;

    public bool ReleasePending { get; set; }

    public int ReleaseAttempts { get; set; }

    public CardContainer? SuppressedCardContainer { get; set; }

    public PooledCardExitKind PendingExitKind { get; set; } = PooledCardExitKind.Unsupported;

    public string PendingExitTargetPath { get; set; } = "";

    public void BeginPresentationGeneration(int generation)
    {
        SharedMarker.BeginGeneration(generation);
    }

    public bool TryTransition(
        AuraCardPresentationViewState expected,
        AuraCardPresentationViewState next)
    {
        return SharedMarker.TryTransition(expected, next);
    }

    public void ForceState(AuraCardPresentationViewState state)
    {
        SharedMarker.ForceState(state);
    }
}
