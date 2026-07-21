using System;
using UnityEngine;
using UnityEngine.EventSystems;

namespace Terrias.Dll.Hooks.Ui;

public sealed class EndlessSeaThemePackHoverProbe : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
{
    private Action<RectTransform>? onEnter;
    private Action? onExit;

    public void Configure(Action<RectTransform> enter, Action exit)
    {
        onEnter = enter;
        onExit = exit;
    }

    public void OnPointerEnter(PointerEventData eventData)
    {
        if (transform is RectTransform rect)
        {
            onEnter?.Invoke(rect);
        }
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        onExit?.Invoke();
    }

    private void OnDisable()
    {
        onExit?.Invoke();
    }
}
