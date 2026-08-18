using System.Collections.Generic;
using UnityEngine;

namespace AuraTools.UnityUiPreview
{
    internal static class PreviewTheme
    {
        internal static readonly Color Stage = Hex("070328");
        internal static readonly Color Window = Hex("04012E");
        internal static readonly Color Background = Hex("08043A");
        internal static readonly Color Panel = Hex("0F0939");
        internal static readonly Color Control = Hex("15133B");
        internal static readonly Color ControlHighlighted = Hex("292451");
        internal static readonly Color CategorySelected = Hex("26204D");
        internal static readonly Color Accent = Hex("D5B36B");
        internal static readonly Color AccentMuted = Hex("8E7C56");
        internal static readonly Color AuraAccent = Hex("AA92DC");
        internal static readonly Color Text = Hex("E9E1B4");
        internal static readonly Color MutedText = Hex("BDB58F");
        internal static readonly Color Success = Hex("69C8A2");
        internal static readonly Color Warning = Hex("DDAA58");
        internal static readonly Color Error = Hex("D87373");
        internal static readonly Color Disabled = Hex("77736C");
        internal static readonly Color Probe = Hex("F143C4");

        internal const float ReferenceWidth = 1280f;
        internal const float ReferenceHeight = 720f;
        internal const float SettingsWidth = 1040f;
        internal const float SettingsHeight = 680f;
        internal const float TabHeight = 60f;
        internal const float CategoryWidth = 168f;
        internal const float ToolboxHeaderHeight = 60f;
        internal const float ModuleRowHeight = 96f;
        internal const float Spacing = 8f;

        private static Font font;

        internal static Font Font
        {
            get
            {
                if (font != null)
                {
                    return font;
                }

                font = Font.CreateDynamicFontFromOSFont(
                    new[] { "Microsoft YaHei UI", "Microsoft YaHei", "SimHei", "Arial" },
                    20);
                if (font == null)
                {
                    font = Resources.GetBuiltinResource<Font>("Arial.ttf");
                }
                return font;
            }
        }

        private static Color Hex(string value)
        {
            ColorUtility.TryParseHtmlString("#" + value, out var color);
            return color;
        }
    }

    internal static class PreviewAssets
    {
        private static readonly Dictionary<string, Sprite> Icons = new Dictionary<string, Sprite>();
        private static Sprite nativeButton;
        private static Sprite nativePanelSmall;
        private static Sprite nativePanelLarge;
        private static Sprite nativeSelector;
        private static Sprite toolboxSurface;
        private static Sprite toolboxControl;
        private static Sprite toolboxCategorySelected;
        private static Sprite[] toolboxCheckbox;
        private static Sprite[] toolboxIconButton;

        internal static Sprite ToolboxSurface
        {
            get
            {
                if (toolboxSurface == null)
                {
                    toolboxSurface = CreateToolboxSprite(
                        "toolbox-surface-9slice",
                        new Vector4(16f, 16f, 16f, 16f),
                        "ToolboxSurface");
                }
                return toolboxSurface;
            }
        }

        internal static Sprite ToolboxControl
        {
            get
            {
                if (toolboxControl == null)
                {
                    toolboxControl = CreateToolboxSprite(
                        "toolbox-control-9slice",
                        new Vector4(8f, 8f, 8f, 8f),
                        "ToolboxControl");
                }
                return toolboxControl;
            }
        }

        internal static Sprite ToolboxCategorySelected
        {
            get
            {
                if (toolboxCategorySelected == null)
                {
                    toolboxCategorySelected = CreateToolboxSprite(
                        "toolbox-category-selected-9slice",
                        new Vector4(8f, 8f, 8f, 8f),
                        "ToolboxCategorySelected");
                }
                return toolboxCategorySelected;
            }
        }

        internal static Sprite ToolboxCheckbox(int state)
        {
            if (toolboxCheckbox == null)
            {
                toolboxCheckbox = CreateVerticalAtlas("toolbox-checkbox-atlas", 5, "ToolboxCheckbox");
            }
            return toolboxCheckbox[Mathf.Clamp(state, 0, toolboxCheckbox.Length - 1)];
        }

        internal static Sprite ToolboxIconButton(int state)
        {
            if (toolboxIconButton == null)
            {
                toolboxIconButton = CreateHorizontalAtlas("toolbox-icon-button-atlas", 4, "ToolboxIconButton");
            }
            return toolboxIconButton[Mathf.Clamp(state, 0, toolboxIconButton.Length - 1)];
        }

        internal static Sprite NativeButton
        {
            get
            {
                if (nativeButton == null)
                {
                    nativeButton = CreateNativeSprite(
                        "native-button",
                        new Rect(17f, 16f, 135f, 49f),
                        new Vector4(14f, 14f, 14f, 14f),
                        "NativeButton");
                }
                return nativeButton;
            }
        }

        internal static Sprite NativePanelSmall
        {
            get
            {
                if (nativePanelSmall == null)
                {
                    nativePanelSmall = CreateNativeSprite(
                        "native-panel-small",
                        null,
                        new Vector4(5f, 5f, 5f, 5f),
                        "NativePanelSmall");
                }
                return nativePanelSmall;
            }
        }

        internal static Sprite NativePanelLarge
        {
            get
            {
                if (nativePanelLarge == null)
                {
                    nativePanelLarge = CreateNativeSprite(
                        "native-panel-large",
                        null,
                        new Vector4(60f, 60f, 60f, 60f),
                        "NativePanelLarge");
                }
                return nativePanelLarge;
            }
        }

        internal static Sprite NativeSelector
        {
            get
            {
                if (nativeSelector == null)
                {
                    nativeSelector = CreateNativeSprite(
                        "native-selector",
                        new Rect(24f, 4f, 342f, 129f),
                        new Vector4(24f, 22f, 24f, 22f),
                        "NativeSelector");
                }
                return nativeSelector;
            }
        }

        internal static Sprite Icon(string key)
        {
            if (string.IsNullOrWhiteSpace(key))
            {
                return null;
            }

            if (Icons.TryGetValue(key, out var cached))
            {
                return cached;
            }

            var texture = Resources.Load<Texture2D>("ToolboxIcons/" + key);
            if (texture == null)
            {
                Icons[key] = null;
                return null;
            }

            var sprite = Sprite.Create(
                texture,
                new Rect(0f, 0f, texture.width, texture.height),
                new Vector2(0.5f, 0.5f),
                100f,
                0,
                SpriteMeshType.FullRect);
            sprite.name = "PreviewIcon-" + key;
            Icons[key] = sprite;
            return sprite;
        }

        private static Sprite CreateNativeSprite(
            string resource,
            Rect? crop,
            Vector4 border,
            string name)
        {
            var texture = Resources.Load<Texture2D>("NativeUi/" + resource);
            if (texture == null)
            {
                return null;
            }
            var rect = crop ?? new Rect(0f, 0f, texture.width, texture.height);
            var sprite = Sprite.Create(
                texture,
                rect,
                new Vector2(0.5f, 0.5f),
                100f,
                0,
                SpriteMeshType.FullRect,
                border);
            sprite.name = name;
            return sprite;
        }

        private static Sprite CreateToolboxSprite(string resource, Vector4 border, string name)
        {
            var texture = Resources.Load<Texture2D>("ToolboxV2/" + resource);
            if (texture == null)
            {
                return null;
            }
            var sprite = Sprite.Create(
                texture,
                new Rect(0f, 0f, texture.width, texture.height),
                new Vector2(0.5f, 0.5f),
                100f,
                0,
                SpriteMeshType.FullRect,
                border);
            sprite.name = name;
            return sprite;
        }

        private static Sprite[] CreateVerticalAtlas(string resource, int count, string name)
        {
            var texture = Resources.Load<Texture2D>("ToolboxV2/" + resource);
            var result = new Sprite[count];
            if (texture == null)
            {
                return result;
            }
            var cell = texture.height / count;
            for (var index = 0; index < count; index++)
            {
                result[index] = Sprite.Create(
                    texture,
                    new Rect(0f, (count - 1 - index) * cell, texture.width, cell),
                    new Vector2(0.5f, 0.5f),
                    100f,
                    0,
                    SpriteMeshType.FullRect);
                result[index].name = name + "-" + index;
            }
            return result;
        }

        private static Sprite[] CreateHorizontalAtlas(string resource, int count, string name)
        {
            var texture = Resources.Load<Texture2D>("ToolboxV2/" + resource);
            var result = new Sprite[count];
            if (texture == null)
            {
                return result;
            }
            var cell = texture.width / count;
            for (var index = 0; index < count; index++)
            {
                result[index] = Sprite.Create(
                    texture,
                    new Rect(index * cell, 0f, cell, texture.height),
                    new Vector2(0.5f, 0.5f),
                    100f,
                    0,
                    SpriteMeshType.FullRect);
                result[index].name = name + "-" + index;
            }
            return result;
        }
    }
}
