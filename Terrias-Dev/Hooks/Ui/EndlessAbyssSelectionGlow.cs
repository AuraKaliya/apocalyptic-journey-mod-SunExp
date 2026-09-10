using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Terrias.Dll.Hooks.Ui;

internal sealed class EndlessAbyssSelectionGlow : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
{
    private const float Padding = 14f;
    private Image? glow;
    private bool selected;
    private bool hovered;

    public void Bind(Image image) => Bind(UiSilhouetteSource.FromImage(image, transform));
    public void BindNativeCard(Transform card) => Bind(UiSilhouetteSource.FromNativeCard(card, transform));

    private void Bind(UiSilhouetteSource source)
    {
        if (glow == null)
        {
            var rect = TerriasUiBuilder.CreateRect("SelectionSilhouette", transform, Vector2.one * 0.5f,
                Vector2.one * 0.5f, Vector2.one * 0.5f, Vector2.zero);
            rect.SetAsFirstSibling();
            glow = rect.gameObject.AddComponent<Image>();
            glow.raycastTarget = false;
            glow.type = Image.Type.Simple;
        }
        glow.sprite = TerriasUiSprites.SilhouetteGlow(source, Padding);
        glow.rectTransform.anchoredPosition = source.Bounds.center;
        glow.rectTransform.sizeDelta = source.Bounds.size + Vector2.one * (Padding * 2f);
        hovered = false;
        Refresh();
    }

    public void SetSelected(bool value)
    {
        selected = value;
        Refresh();
    }

    public void Clear()
    {
        selected = hovered = false;
        if (glow != null) { glow.enabled = false; glow.sprite = null; }
    }

    public void OnPointerEnter(PointerEventData eventData)
    {
        hovered = GetComponent<Button>()?.IsInteractable() != false;
        Refresh();
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        hovered = false;
        Refresh();
    }

    private void Refresh()
    {
        if (glow == null) return;
        glow.enabled = glow.sprite != null && (selected || hovered);
        glow.color = new Color(1f, 0.82f, 0.32f, selected ? 1f : 0.28f);
    }

    private void OnDisable() => Clear();
}
