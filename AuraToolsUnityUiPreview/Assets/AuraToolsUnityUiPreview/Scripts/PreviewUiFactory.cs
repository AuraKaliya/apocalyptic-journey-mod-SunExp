using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace AuraTools.UnityUiPreview
{
    internal sealed class PreviewScrollArea
    {
        internal GameObject Root;
        internal RectTransform Viewport;
        internal RectTransform Content;
        internal ScrollRect Scroll;
    }

    internal static class PreviewUi
    {
        internal static GameObject Rect(
            string name,
            Transform parent,
            Vector2 anchorMin,
            Vector2 anchorMax,
            Vector2 pivot,
            Vector2 sizeDelta,
            Vector2 anchoredPosition)
        {
            var root = new GameObject(name, typeof(RectTransform));
            root.transform.SetParent(parent, false);
            var rect = root.GetComponent<RectTransform>();
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.pivot = pivot;
            rect.sizeDelta = sizeDelta;
            rect.anchoredPosition = anchoredPosition;
            return root;
        }

        internal static GameObject Stretch(string name, Transform parent, Vector4 inset)
        {
            var root = Rect(name, parent, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
            var rect = root.GetComponent<RectTransform>();
            rect.offsetMin = new Vector2(inset.x, inset.w);
            rect.offsetMax = new Vector2(-inset.z, -inset.y);
            return root;
        }

        internal static Image Image(GameObject root, Color color, Sprite sprite = null)
        {
            var image = root.AddComponent<Image>();
            image.color = color;
            image.sprite = sprite;
            image.type = sprite != null && sprite.border.sqrMagnitude > 0.01f
                ? UnityEngine.UI.Image.Type.Sliced
                : UnityEngine.UI.Image.Type.Simple;
            image.preserveAspect = sprite != null && image.type == UnityEngine.UI.Image.Type.Simple;
            return image;
        }

        internal static Text Text(
            GameObject root,
            string value,
            int fontSize,
            TextAnchor anchor,
            Color color,
            bool bestFit = false)
        {
            var text = root.AddComponent<Text>();
            text.font = PreviewTheme.Font;
            text.text = value ?? "";
            text.fontSize = fontSize;
            text.color = color;
            text.alignment = anchor;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Truncate;
            text.resizeTextForBestFit = bestFit;
            text.resizeTextMinSize = 11;
            text.resizeTextMaxSize = fontSize;
            text.raycastTarget = false;
            text.supportRichText = true;
            return text;
        }

        internal static Text FillText(
            string name,
            Transform parent,
            string value,
            int fontSize,
            TextAnchor anchor,
            Color color,
            Vector4 inset,
            bool bestFit = false)
        {
            var root = Stretch(name, parent, inset);
            return Text(root, value, fontSize, anchor, color, bestFit);
        }

        internal static LayoutElement Fixed(GameObject root, float width, float height)
        {
            var element = root.GetComponent<LayoutElement>() ?? root.AddComponent<LayoutElement>();
            if (width > 0f)
            {
                element.minWidth = width;
                element.preferredWidth = width;
                element.flexibleWidth = 0f;
            }
            if (height > 0f)
            {
                element.minHeight = height;
                element.preferredHeight = height;
                element.flexibleHeight = 0f;
            }
            return element;
        }

        internal static LayoutElement Flexible(GameObject root, float width, float height)
        {
            var element = root.GetComponent<LayoutElement>() ?? root.AddComponent<LayoutElement>();
            element.flexibleWidth = width;
            element.flexibleHeight = height;
            return element;
        }

        internal static Button Button(
            string name,
            Transform parent,
            string label,
            UnityAction onClick,
            Color normal,
            Color highlighted,
            int fontSize = 15)
        {
            var root = Rect(name, parent, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
            var image = Image(root, Color.white);
            var button = root.AddComponent<Button>();
            button.targetGraphic = image;
            ApplyButtonColors(button, normal, highlighted);
            if (onClick != null)
            {
                button.onClick.AddListener(onClick);
            }
            FillText("Label", root.transform, label, fontSize, TextAnchor.MiddleCenter, PreviewTheme.Text, new Vector4(7f, 4f, 7f, 4f), true);
            return button;
        }

        internal static Button IconButton(
            string name,
            Transform parent,
            string icon,
            UnityAction onClick,
            float size = 42f)
        {
            var button = Button(name, parent, "", onClick, PreviewTheme.Control, PreviewTheme.ControlHighlighted, 14);
            Fixed(button.gameObject, size, size);
            var iconRoot = Rect(
                "Icon",
                button.transform,
                new Vector2(0.5f, 0.5f),
                new Vector2(0.5f, 0.5f),
                new Vector2(0.5f, 0.5f),
                new Vector2(size * 0.52f, size * 0.52f),
                Vector2.zero);
            var image = Image(iconRoot, PreviewTheme.Text, PreviewAssets.Icon(icon));
            image.raycastTarget = false;
            return button;
        }

        internal static Button NativeButton(
            string name,
            Transform parent,
            string label,
            UnityAction onClick,
            int fontSize = 18)
        {
            var root = Rect(name, parent, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
            var image = Image(root, Color.white, PreviewAssets.NativeButton);
            var button = root.AddComponent<Button>();
            button.targetGraphic = image;
            ApplyButtonColors(button, Color.white, new Color(1f, 0.96f, 0.76f, 1f));
            if (onClick != null)
            {
                button.onClick.AddListener(onClick);
            }
            FillText("Label", root.transform, label, fontSize, TextAnchor.MiddleCenter, PreviewTheme.Text, new Vector4(8f, 4f, 8f, 4f), true);
            return button;
        }

        internal static Button NativeIconButton(
            string name,
            Transform parent,
            string icon,
            UnityAction onClick,
            float size = 46f)
        {
            var button = NativeButton(name, parent, "", onClick, 16);
            Fixed(button.gameObject, size, size);
            var iconRoot = Rect(
                "Icon",
                button.transform,
                new Vector2(0.5f, 0.5f),
                new Vector2(0.5f, 0.5f),
                new Vector2(0.5f, 0.5f),
                new Vector2(size * 0.46f, size * 0.46f),
                Vector2.zero);
            Image(iconRoot, PreviewTheme.Text, PreviewAssets.Icon(icon)).raycastTarget = false;
            return button;
        }

        internal static Button ToolboxIconButton(
            string name,
            Transform parent,
            string icon,
            UnityAction onClick,
            float size = 42f)
        {
            var root = Rect(name, parent, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
            Fixed(root, size, size);
            var image = Image(root, Color.white, PreviewAssets.ToolboxIconButton(0));
            var button = root.AddComponent<Button>();
            button.targetGraphic = image;
            button.transition = Selectable.Transition.SpriteSwap;
            button.spriteState = new SpriteState
            {
                highlightedSprite = PreviewAssets.ToolboxIconButton(1),
                selectedSprite = PreviewAssets.ToolboxIconButton(1),
                pressedSprite = PreviewAssets.ToolboxIconButton(2),
                disabledSprite = PreviewAssets.ToolboxIconButton(3)
            };
            if (onClick != null) button.onClick.AddListener(onClick);
            var iconRoot = Rect(
                "Icon",
                root.transform,
                new Vector2(0.5f, 0.5f),
                new Vector2(0.5f, 0.5f),
                new Vector2(0.5f, 0.5f),
                new Vector2(size * 0.48f, size * 0.48f),
                Vector2.zero);
            Image(iconRoot, PreviewTheme.Text, PreviewAssets.Icon(icon)).raycastTarget = false;
            return button;
        }

        internal static InputField ToolboxInput(
            string name,
            Transform parent,
            string value,
            string placeholderValue,
            UnityAction<string> changed)
        {
            var input = Input(name, parent, value, placeholderValue, changed);
            var image = input.GetComponent<Image>();
            image.sprite = PreviewAssets.ToolboxControl;
            image.type = image.sprite == null
                ? UnityEngine.UI.Image.Type.Simple
                : UnityEngine.UI.Image.Type.Sliced;
            image.color = image.sprite == null ? PreviewTheme.Control : Color.white;
            return input;
        }

        internal static void ApplyButtonColors(Button button, Color normal, Color highlighted)
        {
            var colors = button.colors;
            colors.normalColor = normal;
            colors.highlightedColor = highlighted;
            colors.selectedColor = highlighted;
            colors.pressedColor = Color.Lerp(normal, Color.black, 0.22f);
            colors.disabledColor = new Color(normal.r * 0.55f, normal.g * 0.55f, normal.b * 0.55f, 0.55f);
            colors.colorMultiplier = 1f;
            colors.fadeDuration = 0.08f;
            button.colors = colors;
            if (button.targetGraphic != null)
            {
                button.targetGraphic.CrossFadeColor(normal, 0f, true, true);
            }
        }

        internal static GameObject BorderedPanel(string name, Transform parent, Color border, Color fill, Vector4 inset)
        {
            var outer = Stretch(name, parent, inset);
            Image(outer, border);
            var inner = Stretch("Surface", outer.transform, new Vector4(2f, 2f, 2f, 2f));
            Image(inner, fill);
            return inner;
        }

        internal static PreviewScrollArea Scroll(string name, Transform parent, Vector4 inset, float spacing)
        {
            var root = Stretch(name, parent, inset);
            Image(root, new Color(0f, 0f, 0f, 0.01f));
            var viewportRoot = Stretch("Viewport", root.transform, Vector4.zero);
            var viewport = viewportRoot.GetComponent<RectTransform>();
            Image(viewportRoot, new Color(0f, 0f, 0f, 0.01f));
            viewportRoot.AddComponent<RectMask2D>();
            var contentRoot = Rect(
                "Content",
                viewport,
                new Vector2(0f, 1f),
                new Vector2(1f, 1f),
                new Vector2(0.5f, 1f),
                Vector2.zero,
                Vector2.zero);
            var content = contentRoot.GetComponent<RectTransform>();
            var layout = contentRoot.AddComponent<VerticalLayoutGroup>();
            layout.spacing = spacing;
            layout.padding = new RectOffset(0, 3, 0, 0);
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;
            var fitter = contentRoot.AddComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            var scroll = root.AddComponent<ScrollRect>();
            scroll.viewport = viewport;
            scroll.content = content;
            scroll.horizontal = false;
            scroll.vertical = true;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 30f;
            return new PreviewScrollArea
            {
                Root = root,
                Viewport = viewport,
                Content = content,
                Scroll = scroll
            };
        }

        internal static InputField Input(
            string name,
            Transform parent,
            string value,
            string placeholderValue,
            UnityAction<string> changed,
            bool multiline = false)
        {
            var root = Rect(name, parent, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
            var image = Image(root, Color.white, PreviewAssets.NativePanelSmall);
            var viewport = Stretch("Viewport", root.transform, new Vector4(10f, 6f, 10f, 6f));
            viewport.AddComponent<RectMask2D>();
            var textRoot = Stretch("Text", viewport.transform, Vector4.zero);
            var text = Text(textRoot, value, 14, multiline ? TextAnchor.UpperLeft : TextAnchor.MiddleLeft, PreviewTheme.Text);
            text.raycastTarget = true;
            var placeholderRoot = Stretch("Placeholder", viewport.transform, Vector4.zero);
            var placeholder = Text(placeholderRoot, placeholderValue, 14, multiline ? TextAnchor.UpperLeft : TextAnchor.MiddleLeft, PreviewTheme.MutedText);
            var input = root.AddComponent<InputField>();
            input.targetGraphic = image;
            input.textComponent = text;
            input.placeholder = placeholder;
            input.lineType = multiline ? InputField.LineType.MultiLineNewline : InputField.LineType.SingleLine;
            input.text = value ?? "";
            if (changed != null)
            {
                input.onValueChanged.AddListener(changed);
            }
            return input;
        }

        internal static Slider Slider(
            string name,
            Transform parent,
            float value,
            UnityAction<float> changed)
        {
            var root = Rect(name, parent, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
            var slider = root.AddComponent<Slider>();
            slider.minValue = 0f;
            slider.maxValue = 100f;
            slider.wholeNumbers = true;

            var background = Rect("Track", root.transform, new Vector2(0f, 0.5f), new Vector2(1f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(-22f, 8f), Vector2.zero);
            Image(background, PreviewTheme.ControlHighlighted);
            var fillArea = Stretch("Fill Area", background.transform, new Vector4(2f, 2f, 2f, 2f));
            var fill = Stretch("Fill", fillArea.transform, Vector4.zero);
            var fillImage = Image(fill, PreviewTheme.Accent);
            slider.fillRect = fill.GetComponent<RectTransform>();
            slider.targetGraphic = fillImage;
            var handleArea = Stretch("Handle Slide Area", root.transform, new Vector4(8f, 0f, 8f, 0f));
            var handle = Rect("Handle", handleArea.transform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(18f, 18f), Vector2.zero);
            var handleImage = Image(handle, PreviewTheme.Text, PreviewAssets.Icon("switch-thumb"));
            slider.handleRect = handle.GetComponent<RectTransform>();
            slider.targetGraphic = handleImage;
            slider.value = value;
            if (changed != null)
            {
                slider.onValueChanged.AddListener(changed);
            }
            return slider;
        }
    }

    internal sealed class PreviewSwitchControl : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
    {
        private Button button;
        private Image track;
        private RectTransform thumb;
        private bool value;
        private bool hovered;
        private Action<bool> changed;

        internal static PreviewSwitchControl Create(Transform parent, bool initialValue, Action<bool> onChanged)
        {
            var root = PreviewUi.Rect("Switch", parent, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
            PreviewUi.Fixed(root, 52f, 30f);
            var track = PreviewUi.Image(root, PreviewTheme.Control, PreviewAssets.Icon("switch-track"));
            var button = root.AddComponent<Button>();
            button.targetGraphic = track;
            button.transition = Selectable.Transition.None;
            var thumbRoot = PreviewUi.Rect("Thumb", root.transform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(22f, 22f), Vector2.zero);
            PreviewUi.Image(thumbRoot, PreviewTheme.Text, PreviewAssets.Icon("switch-thumb")).raycastTarget = false;
            var control = root.AddComponent<PreviewSwitchControl>();
            control.button = button;
            control.track = track;
            control.thumb = thumbRoot.GetComponent<RectTransform>();
            control.value = initialValue;
            control.changed = onChanged;
            button.onClick.AddListener(control.Toggle);
            control.Refresh();
            return control;
        }

        internal bool Value
        {
            get { return value; }
            set
            {
                this.value = value;
                Refresh();
            }
        }

        internal bool Interactable
        {
            get { return button.interactable; }
            set
            {
                button.interactable = value;
                Refresh();
            }
        }

        private void Toggle()
        {
            value = !value;
            Refresh();
            changed?.Invoke(value);
        }

        private void Refresh()
        {
            if (track == null || thumb == null || button == null)
            {
                return;
            }

            var color = value ? PreviewTheme.Success : PreviewTheme.ControlHighlighted;
            if (hovered)
            {
                color = Color.Lerp(color, PreviewTheme.Accent, 0.2f);
            }
            if (!button.interactable)
            {
                color = new Color(color.r * 0.55f, color.g * 0.55f, color.b * 0.55f, 0.6f);
            }
            track.color = color;
            thumb.anchoredPosition = new Vector2(value ? 11f : -11f, 0f);
        }

        public void OnPointerEnter(PointerEventData eventData)
        {
            hovered = true;
            Refresh();
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            hovered = false;
            Refresh();
        }
    }

    internal sealed class PreviewCheckboxControl : MonoBehaviour
    {
        private Button button;
        private Text checkmark;
        private bool value;
        private Action<bool> changed;

        internal static PreviewCheckboxControl Create(
            Transform parent,
            bool initialValue,
            Action<bool> onChanged,
            float size = 30f)
        {
            var root = PreviewUi.Rect("Checkbox", parent, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
            PreviewUi.Fixed(root, size, size);
            var image = PreviewUi.Image(root, Color.white, PreviewAssets.NativePanelSmall);
            var button = root.AddComponent<Button>();
            button.targetGraphic = image;
            PreviewUi.ApplyButtonColors(button, Color.white, new Color(1f, 0.96f, 0.76f, 1f));
            var check = PreviewUi.FillText("Checkmark", root.transform, "✓", Mathf.RoundToInt(size * 0.78f), TextAnchor.MiddleCenter, new Color(0.38f, 0.40f, 1f, 1f), new Vector4(1f, 0f, 1f, 2f), true);
            var control = root.AddComponent<PreviewCheckboxControl>();
            control.button = button;
            control.checkmark = check;
            control.value = initialValue;
            control.changed = onChanged;
            button.onClick.AddListener(control.Toggle);
            control.Refresh();
            return control;
        }

        internal bool Value
        {
            get { return value; }
            set
            {
                this.value = value;
                Refresh();
            }
        }

        internal bool Interactable
        {
            get { return button.interactable; }
            set { button.interactable = value; }
        }

        private void Toggle()
        {
            value = !value;
            Refresh();
            changed?.Invoke(value);
        }

        private void Refresh()
        {
            if (checkmark != null)
            {
                checkmark.gameObject.SetActive(value);
            }
        }
    }

    internal sealed class PreviewToolboxCheckboxControl : MonoBehaviour
    {
        private Toggle toggle;
        private Image image;

        internal static PreviewToolboxCheckboxControl Create(
            Transform parent,
            bool initialValue,
            Action<bool> changed,
            float size = 32f)
        {
            var root = PreviewUi.Rect("ToolboxCheckbox", parent, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
            PreviewUi.Fixed(root, size, size);
            var visual = PreviewUi.Rect(
                "Square",
                root.transform,
                new Vector2(0.5f, 0.5f),
                new Vector2(0.5f, 0.5f),
                new Vector2(0.5f, 0.5f),
                new Vector2(size, size),
                Vector2.zero);
            var image = PreviewUi.Image(visual, Color.white, PreviewAssets.ToolboxCheckbox(initialValue ? 1 : 0));
            var toggle = visual.AddComponent<Toggle>();
            toggle.targetGraphic = image;
            toggle.transition = Selectable.Transition.SpriteSwap;
            toggle.SetIsOnWithoutNotify(initialValue);
            var control = root.AddComponent<PreviewToolboxCheckboxControl>();
            control.toggle = toggle;
            control.image = image;
            toggle.onValueChanged.AddListener(value =>
            {
                control.Refresh();
                changed?.Invoke(value);
            });
            control.Refresh();
            return control;
        }

        internal bool Value
        {
            get { return toggle.isOn; }
            set
            {
                toggle.SetIsOnWithoutNotify(value);
                Refresh();
            }
        }

        internal bool Interactable
        {
            get { return toggle.interactable; }
            set
            {
                toggle.interactable = value;
                Refresh();
            }
        }

        private void Refresh()
        {
            var value = toggle.isOn;
            image.sprite = PreviewAssets.ToolboxCheckbox(value ? 1 : 0);
            toggle.spriteState = new SpriteState
            {
                highlightedSprite = PreviewAssets.ToolboxCheckbox(value ? 3 : 2),
                selectedSprite = PreviewAssets.ToolboxCheckbox(value ? 3 : 2),
                pressedSprite = PreviewAssets.ToolboxCheckbox(value ? 3 : 2),
                disabledSprite = PreviewAssets.ToolboxCheckbox(4)
            };
        }
    }

    internal sealed class PreviewBooleanControl : MonoBehaviour
    {
        private bool value;
        private PreviewCheckboxControl enabledBox;
        private PreviewCheckboxControl disabledBox;

        internal static PreviewBooleanControl Create(Transform parent, bool initialValue)
        {
            var root = PreviewUi.Rect("NativeBooleanControl", parent, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
            var layout = root.AddComponent<HorizontalLayoutGroup>();
            layout.spacing = 28f;
            layout.childAlignment = TextAnchor.MiddleCenter;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = false;
            layout.childForceExpandHeight = false;
            var control = root.AddComponent<PreviewBooleanControl>();
            control.value = initialValue;
            control.enabledBox = CreateOption(root.transform, "开启", true, () => control.SetValue(true));
            control.disabledBox = CreateOption(root.transform, "关闭", false, () => control.SetValue(false));
            control.Refresh();
            return control;
        }

        private static PreviewCheckboxControl CreateOption(
            Transform parent,
            string label,
            bool optionValue,
            UnityAction clicked)
        {
            var root = PreviewUi.Rect("Option-" + label, parent, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
            PreviewUi.Fixed(root, 154f, 42f);
            var button = root.AddComponent<Button>();
            var transparent = PreviewUi.Image(root, Color.clear);
            button.targetGraphic = transparent;
            button.transition = Selectable.Transition.None;
            var layout = root.AddComponent<HorizontalLayoutGroup>();
            layout.spacing = 8f;
            layout.childAlignment = TextAnchor.MiddleCenter;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = false;
            layout.childForceExpandHeight = false;
            var labelRoot = PreviewUi.Rect("Label", root.transform, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
            PreviewUi.Fixed(labelRoot, 92f, 40f);
            PreviewUi.Text(labelRoot, label, 22, TextAnchor.MiddleRight, PreviewTheme.Text, true);
            PreviewCheckboxControl box = null;
            box = PreviewCheckboxControl.Create(root.transform, false, _ => clicked());
            button.onClick.AddListener(clicked);
            return box;
        }

        private void SetValue(bool enabled)
        {
            value = enabled;
            Refresh();
        }

        private void Refresh()
        {
            if (enabledBox != null) enabledBox.Value = value;
            if (disabledBox != null) disabledBox.Value = !value;
        }
    }

    internal sealed class PreviewSelectorControl : MonoBehaviour
    {
        private readonly List<string> options = new List<string>();
        private Text label;
        private int index;

        internal static PreviewSelectorControl Create(
            Transform parent,
            IEnumerable<string> values,
            int selectedIndex = 0)
        {
            var root = PreviewUi.Rect("Selector", parent, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
            PreviewUi.Image(root, Color.white, PreviewAssets.NativeSelector);
            var control = root.AddComponent<PreviewSelectorControl>();
            control.options.AddRange(values);
            control.index = Mathf.Clamp(selectedIndex, 0, Mathf.Max(0, control.options.Count - 1));

            var left = PreviewUi.NativeButton("Previous", root.transform, "◀", control.Previous, 16);
            var leftRect = left.GetComponent<RectTransform>();
            leftRect.anchorMin = new Vector2(0f, 0f);
            leftRect.anchorMax = new Vector2(0f, 1f);
            leftRect.pivot = new Vector2(0f, 0.5f);
            leftRect.sizeDelta = new Vector2(52f, 0f);
            leftRect.anchoredPosition = Vector2.zero;

            var right = PreviewUi.NativeButton("Next", root.transform, "▶", control.Next, 16);
            var rightRect = right.GetComponent<RectTransform>();
            rightRect.anchorMin = new Vector2(1f, 0f);
            rightRect.anchorMax = new Vector2(1f, 1f);
            rightRect.pivot = new Vector2(1f, 0.5f);
            rightRect.sizeDelta = new Vector2(52f, 0f);
            rightRect.anchoredPosition = Vector2.zero;

            control.label = PreviewUi.FillText("Value", root.transform, "", 18, TextAnchor.MiddleCenter, PreviewTheme.Text, new Vector4(58f, 4f, 58f, 4f), true);
            control.Refresh();
            return control;
        }

        internal string Value
        {
            get { return options.Count == 0 ? "" : options[index]; }
        }

        private void Previous()
        {
            if (options.Count == 0) return;
            index = (index - 1 + options.Count) % options.Count;
            Refresh();
        }

        private void Next()
        {
            if (options.Count == 0) return;
            index = (index + 1) % options.Count;
            Refresh();
        }

        private void Refresh()
        {
            if (label != null)
            {
                label.text = Value;
            }
        }
    }
}
