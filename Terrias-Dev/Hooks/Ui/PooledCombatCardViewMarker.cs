using SunExp.Dll.Mechanics;
using UnityEngine;
using Witch.UI;

namespace SunExp.Dll.Hooks.Ui;

public sealed class PooledCombatCardViewMarker : MonoBehaviour
{
    public int Generation { get; set; }

    public string Bucket { get; set; } = "";

    public string ConfigInstanceId { get; set; } = "";

    public string PresentationSignature { get; set; } = "";

    public bool HasInitializedPresentation { get; set; }

    public PooledCardViewState State { get; private set; } = PooledCardViewState.Idle;

    public bool ReleasePending { get; set; }

    public int ReleaseAttempts { get; set; }

    public CardContainer? SuppressedCardContainer { get; set; }

    public PooledCardExitKind PendingExitKind { get; set; } = PooledCardExitKind.Unsupported;

    public string PendingExitTargetPath { get; set; } = "";

    public bool TryTransition(PooledCardViewState expected, PooledCardViewState next)
    {
        if (State != expected)
        {
            return false;
        }

        State = next;
        return true;
    }

    public void ForceState(PooledCardViewState state)
    {
        State = state;
    }
}
