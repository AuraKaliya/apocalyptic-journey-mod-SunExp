using UnityEngine;

namespace AuraToolsExp.Dll.Features.DamageMeter;

internal sealed class AuraToolsDamageMeterDriver : MonoBehaviour
{
    private void Update()
    {
        AuraToolsDamageMeterRuntime.Tick();
    }
}
