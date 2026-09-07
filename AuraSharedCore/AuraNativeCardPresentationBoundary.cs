using UnityEngine;

namespace AuraShared.Core;

internal sealed class AuraNativeCardPresentationBoundary : MonoBehaviour
{
    internal readonly AuraNativeCardPresentationState State = new();

    private void OnDestroy() => AuraPresentationMaterialCoordinator.AbandonView(
        transform.GetInstanceID(), GetComponent<AuraCardPresentationViewMarker>()?.Generation ?? 0);
}
