using UnityEngine;
using UnityEngine.EventSystems;

namespace Terrias.Dll.Hooks.Ui;

public sealed class FieldBuffHudHoverProbe : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
{
    private FieldBuffHudView? owner;

    public void Configure(FieldBuffHudView view)
    {
        owner = view;
    }

    public void OnPointerEnter(PointerEventData eventData)
    {
        owner?.HandlePointerEntered();
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        owner?.HandlePointerExited();
    }

    private void OnDisable()
    {
        owner?.HandlePointerExited();
    }
}
