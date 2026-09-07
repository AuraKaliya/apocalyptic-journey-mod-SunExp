using UnityEngine;

// The fixture has the host's exact semantic type identity, independent of
// GameObject names. It never reads or writes game tutorial progress.
namespace Witch.UI.Window
{
    public sealed class TutorialSpotlightUI : MonoBehaviour
    {
        public static int LifecycleCalls;

        private void Awake() { LifecycleCalls++; }
        private void OnEnable() { LifecycleCalls++; }
    }
}
