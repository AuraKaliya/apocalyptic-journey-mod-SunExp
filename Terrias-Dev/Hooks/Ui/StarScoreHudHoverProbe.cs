using UnityEngine;
using UnityEngine.EventSystems;

namespace Terrias.Dll.Hooks.Ui;

public sealed class StarScoreHudHoverProbe : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
{
    private StarScoreHudView? owner;

    public void Configure(StarScoreHudView view)
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
