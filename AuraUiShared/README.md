# AuraUiShared

`AuraUiShared` is the shared, semantic-free UI foundation shipped inside `Aura.Shared.dll`.
SunExp and AuraToolsExp consume it as sibling mods; neither mod owns the shared runtime.

## Style model

- `Aura.Shared:default`: stable Aura fallback visuals.
- `Aura.Shared:witch.native`: game-font and native-host capabilities.
- `SunExp:solar`: SunExp-owned derived theme.
- `AuraToolsExp:arcane`: AuraToolsExp-owned derived theme.

Styles are resolved by `AuraUiContext`. Do not mutate a process-wide current theme. A window chooses
its style when its context is created, so different UI styles can coexist and future components do
not need to imitate the game UI.

Consumer styles should be registered through `AuraUiStyleRegistry.RegisterDerived`. The shared layer
contains only rendering primitives, theme values, native compatibility adapters, and safe UI hosts;
gameplay meaning stays in the consumer mod.

## Component surfaces

The standard TMP renderer provides text, panel, button, toggle, input, dropdown, scroll/list,
tooltip, toast, and modal-root surfaces. Handles expose stable text, value, listener, and interaction
operations without leaking the concrete Unity control used by the renderer.

Existing `UnityEngine.UI.Text` call sites remain supported through `AuraUiComponents.ConfigureText`.
That compatibility path resolves the source font from the game's TMP font asset. New or migrated UI
should use `ConfigureTmpText` or `AuraUiContext`.

## Font policy

The native bridge loads the game's `HarmonyOS_Sans_Medium SDF` through the shared resource cache.
The game owns language fallback ordering, so Aura UI automatically follows Simplified Chinese,
Traditional Chinese, Japanese, and special-glyph fallback changes. Extracted TTF files are not copied
into individual mods.

## Minimal use

```csharp
var ui = new AuraUiContext(parent, AuraUiStyleIds.WitchNative);
var panel = ui.CreatePanel("Settings", new Vector2(720f, 520f));
var panelUi = ui.For(panel.transform);
panelUi.CreateText("Title", "Settings", AuraUiTextRole.Title);
panelUi.CreateButton("Confirm", "Confirm", Confirm);
```
