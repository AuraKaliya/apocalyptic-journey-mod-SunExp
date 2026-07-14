using Michsky.MUIP;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using Witch.UI;

namespace AuraUi.Shared;

public enum AuraUiButtonSoundStyle
{
    None,
    Pure,
    Metal
}

/// <summary>
/// Applies semantic-free selectable feedback to Aura-owned buttons. Native
/// ButtonManager controls keep their own state animation, ripple, and audio.
/// </summary>
public static class AuraUiButtonFeedback
{
    public const float DefaultFadeDuration = 0.1f;

    public static void Apply(
        Button button,
        Graphic target,
        AuraUiTheme theme,
        AuraUiButtonSoundStyle soundStyle = AuraUiButtonSoundStyle.Pure)
    {
        if (theme == null)
        {
            return;
        }

        Apply(
            button,
            target,
            theme.Control,
            theme.ControlHighlighted,
            Color.Lerp(theme.ControlHighlighted, Color.black, 0.16f),
            new Color(theme.Control.r * 0.55f, theme.Control.g * 0.55f, theme.Control.b * 0.55f, 0.58f),
            soundStyle);
    }

    public static void Apply(
        Button button,
        Graphic target,
        Color accent,
        AuraUiButtonSoundStyle soundStyle = AuraUiButtonSoundStyle.Pure)
    {
        if (button == null || target == null)
        {
            return;
        }

        var normal = target.color;
        var highlighted = Color.Lerp(normal, accent, normal == Color.white ? 0.24f : 0.20f);
        var pressed = normal == Color.white
            ? Color.Lerp(normal, accent, 0.46f)
            : Color.Lerp(normal, Color.black, 0.18f);
        var disabled = new Color(normal.r * 0.58f, normal.g * 0.58f, normal.b * 0.58f, 0.55f);
        Apply(button, target, normal, highlighted, pressed, disabled, soundStyle);
    }

    public static void Apply(
        Button button,
        Graphic target,
        Color normal,
        Color highlighted,
        Color pressed,
        Color disabled,
        AuraUiButtonSoundStyle soundStyle = AuraUiButtonSoundStyle.Pure)
    {
        if (button == null || target == null)
        {
            return;
        }

        // ButtonManager owns the complete native state machine. Adding Aura
        // feedback here would play the same sound twice and fight its fades.
        if (button.GetComponent<ButtonManager>() != null)
        {
            return;
        }

        target.color = Color.white;
        button.targetGraphic = target;
        button.transition = Selectable.Transition.ColorTint;
        var colors = button.colors;
        colors.normalColor = normal;
        colors.highlightedColor = highlighted;
        colors.pressedColor = pressed;
        colors.selectedColor = highlighted;
        colors.disabledColor = disabled;
        colors.colorMultiplier = 1f;
        colors.fadeDuration = DefaultFadeDuration;
        button.colors = colors;

        // Apply() normally runs after the Button component has already been
        // enabled. Assigning a new ColorBlock does not make Selectable replay
        // its current state, so the white tint base could be rendered for a
        // frame until the next pointer or activation event. Synchronize the
        // initial renderer tint immediately; normal hover/press transitions
        // continue to be owned by Button afterwards.
        var initialColor = button.IsInteractable()
            ? colors.normalColor
            : colors.disabledColor;
        target.CrossFadeColor(
            initialColor * colors.colorMultiplier,
            0f,
            true,
            true);

        var relay = button.GetComponent<AuraUiButtonSoundRelay>()
                    ?? button.gameObject.AddComponent<AuraUiButtonSoundRelay>();
        relay.Configure(button, soundStyle);
    }
}

/// <summary>
/// Reuses the game's ButtonSound implementation while preventing disabled
/// Selectables from emitting audio. The wrapped component stays disabled so
/// EventSystem cannot invoke it a second time.
/// </summary>
public sealed class AuraUiButtonSoundRelay : MonoBehaviour, IPointerEnterHandler, IPointerDownHandler
{
    private Button? targetButton;
    private ButtonSound? nativeSound;
    private AuraUiButtonSoundStyle soundStyle;

    public void Configure(Button button, AuraUiButtonSoundStyle style)
    {
        targetButton = button;
        soundStyle = style;
        if (style == AuraUiButtonSoundStyle.None)
        {
            if (nativeSound != null)
            {
                nativeSound.enabled = false;
            }

            return;
        }

        nativeSound = GetComponent<ButtonSound>() ?? gameObject.AddComponent<ButtonSound>();
        nativeSound.metal = style == AuraUiButtonSoundStyle.Metal;
        nativeSound.isPure = style == AuraUiButtonSoundStyle.Pure;
        nativeSound.enterSound = true;
        nativeSound.useDownSound = true;
        nativeSound.enabled = false;
    }

    public void OnPointerEnter(PointerEventData eventData)
    {
        if (!CanPlay())
        {
            return;
        }

        try
        {
            nativeSound!.OnPointerEnter(eventData);
        }
        catch
        {
            // Audio availability must not break button input.
        }
    }

    public void OnPointerDown(PointerEventData eventData)
    {
        if (!CanPlay() || eventData.button != PointerEventData.InputButton.Left)
        {
            return;
        }

        try
        {
            nativeSound!.OnPointerDown(eventData);
        }
        catch
        {
            // Audio availability must not break button input.
        }
    }

    private bool CanPlay()
    {
        return soundStyle != AuraUiButtonSoundStyle.None
               && targetButton != null
               && targetButton.IsActive()
               && targetButton.IsInteractable()
               && nativeSound != null;
    }
}
