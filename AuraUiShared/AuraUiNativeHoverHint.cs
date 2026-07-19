using System;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using Object = UnityEngine.Object;

namespace AuraUi.Shared;

/// <summary>
/// Reuses the game's native SelectedMessage presentation for owner-supplied
/// hover text. The shared component owns only pointer and view lifecycle; the
/// consumer keeps the button's gameplay meaning.
/// </summary>
public sealed class AuraUiNativeHoverHint : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
{
    private const string NativeHintResource = "UI/SelectedMessage";

    private string message = "";
    private bool placeBelow = true;
    private GameObject? hintObject;

    public static AuraUiNativeHoverHint Attach(GameObject target, string message, bool placeBelow = true)
    {
        if (target == null)
        {
            throw new ArgumentNullException(nameof(target));
        }

        var hint = target.GetComponent<AuraUiNativeHoverHint>()
                   ?? target.AddComponent<AuraUiNativeHoverHint>();
        hint.Configure(message, placeBelow);
        return hint;
    }

    public void Configure(string value, bool below = true)
    {
        message = value ?? "";
        placeBelow = below;
        ApplyMessage();
        ApplyPosition();
    }

    public void OnPointerEnter(PointerEventData eventData)
    {
        EnsureHint();
        if (hintObject != null)
        {
            ApplyMessage();
            ApplyPosition();
            hintObject.SetActive(true);
        }
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        Hide();
    }

    private void EnsureHint()
    {
        if (hintObject != null)
        {
            return;
        }

        var prefab = ResourceLoader.Load<GameObject>(NativeHintResource, true);
        if (prefab == null)
        {
            return;
        }

        hintObject = Object.Instantiate(prefab, transform);
        ApplyMessage();
        ApplyPosition();
    }

    private void ApplyMessage()
    {
        var text = hintObject?.transform.Find("text")?.GetComponent<TMP_Text>();
        if (text != null)
        {
            text.text = message;
        }
    }

    private void ApplyPosition()
    {
        if (hintObject?.transform is RectTransform rect)
        {
            rect.anchoredPosition = new Vector2(0f, placeBelow ? -100f : 100f);
        }
    }

    private void Hide()
    {
        if (hintObject != null)
        {
            hintObject.SetActive(false);
        }
    }

    private void OnDisable()
    {
        Hide();
    }

    private void OnDestroy()
    {
        if (hintObject != null)
        {
            Object.Destroy(hintObject);
            hintObject = null;
        }
    }
}
