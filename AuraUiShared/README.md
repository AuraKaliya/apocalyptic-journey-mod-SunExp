# AuraUiShared

`AuraUiShared` is the shared, semantic-free UI foundation shipped inside `Aura.Shared.dll`.
Terrias and AuraToolsExp consume it as sibling mods; neither mod owns the shared runtime.

## Style model

- `Aura.Shared:default`: stable Aura fallback visuals.
- `Aura.Shared:witch.native`: game-font and native-host capabilities.
- `Terrias:solar`: Terrias-owned derived theme.
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

`AuraUiNativeButtonCloneAdapter` provides a fail-closed bridge for exact game-native buttons. It
clones one `ButtonManager` visual shell, replaces its normal/highlighted/disabled labels with
Aura-owned TMP nodes, clears inherited action events, and verifies that changing the clone did not
alter the template label. A label owner rejects later template/localization writes without polling
layout work when the text is already correct. Consumers supply any game-specific behaviour cleanup
callback and retain their own fallback UI when validation fails.
Consumers may provide `TextSizeOverride` and `MinimumTextSizeOverride` when a
longer custom label must fit a native visual shell. The adapter applies that
range to `ButtonManager` and all three owned TMP state labels, so later native
refreshes cannot restore the template's larger size.

`AuraUiButtonFeedback` supplies the shared Hover/Press/Disabled color state, synchronizes the
initial normal/disabled tint before first render to prevent foldout activation flashes, and reuses the game's
button sounds through an interactability-aware relay. It intentionally skips native
`ButtonManager` controls so native fades, ripples, and audio remain single-owned. Consumer-specific
button factories should apply this helper instead of defining unrelated `ColorBlock` and sound
behavior at each call site.

`AuraUiNativeButtonBinding` adopts a `ButtonManager` that already belongs to a consumer-cloned native
visual tree. It replaces inherited click, right-click, hover, and leave events, then exposes stable label,
text-color, and interactability updates while leaving the native Normal/Highlight/Disabled transition
under `ButtonManager` ownership. Use the clone adapter when cloning an individual button; use the binding
when a larger native window or item template has already been cloned. Pass a non-null label when the
consumer owns the button text. Pass `null` for icon-only controls such as a native close button; the
binding then preserves the cloned icon and serialized text settings. Missing optional text or disabled
visual states do not reject the native shell, so a local prefab variation cannot force the whole window
onto a fallback layout.

`AuraUiPointerSurface` supplies semantic-free pointer enter, exit, left-click, and right-click callbacks.
It ensures an explicit raycast graphic on the bound object without disabling descendant graphics, so
native tooltip and scroll-view event chains can keep working. Consumers provide the action meaning and
remain responsible for content-specific validation and settlement.

`AuraUiNativeItemSurface` combines that pointer lifecycle with an optional cloned `ButtonManager`.
It clears inherited actions but keeps the manager interactable so native Normal/Highlight/Disabled,
ripple, and audio presentation continues to respond. `SetIcon` delegates to `ButtonManager.SetIcon`,
which updates every native visual state. The consumer still owns tooltip content and all purchase,
sale, equipment, or other domain actions.

`AuraUiNativeItemAnchor` preserves the exact event target, serialized host `KeywordDisplay`, and
exact native `ButtonManager` before a consumer removes unsafe game-business behaviours from a cloned
item template. Cloned anchors keep Unity-remapped references. Binding an item surface through the
anchor installs actions on both the original event target and its exact native manager with same-frame
duplicate suppression, while explicitly re-enabling the captured tooltip. Consumers should capture
these anchors before sanitizing a native item tree instead of guessing a parent holder or first
descendant button after the native components are gone.

`AuraUiNativeGameItemAdapter` is the preferred route when a host item component can be retained.
It preserves the real `ShopItem` initializer for products and replaces unsafe backpack settlement
components with `AuraUiSafeSellItem : SellItem` and
`AuraUiSafeRelicItem : RelicItemConfig`. Consumers call the inherited native `Init` method so the
game remains responsible for localized text, card/relic icons, rarity state, `KeywordDisplay`, and
the serialized event target. The safe subclasses override only pointer action meaning and never call
the game's gold purchase/sale implementations. `ApplyButtonIcon` accepts `null` and updates the
normal, highlighted, and disabled `ButtonManager` states, preventing pooled or template sprites from
leaking into an empty state.

Native screens that use the game's global `Tooltip` or `Floating Window` must be hosted through
`AuraUiModalHost.NativeUiParent` / `CreateNativeFullscreenRoot`. Those overlays and ordinary
`UIManager.ShowUI` screens live on `UIManager.canvasTf`; placing the owning screen on
`upperCanvasTf` makes callbacks fire while the overlay remains hidden underneath the upper Canvas.
Normal Aura modal windows continue to use `ModalParent`. `AuraUiNativeOverlayVisibility` can verify
that an invoked native overlay is active, shares the anchor's root Canvas, and renders above the
anchor branch; event entry alone is not visibility proof.

## View-state preservation

`AuraUiStableId` assigns a logical identity to rows and controls that may be
recreated. `AuraUiViewState` captures the focused identity, the first visible
anchor and its viewport-relative offset, then restores them after the next
layout pass. Use the normalized scroll position only as a fallback because it
cannot preserve the same visible row when content height changes.

`AuraUiKeyedListReconciler<TKey, TModel>` updates lists by stable key. It reuses
unchanged rows, creates only new rows, removes stale rows and restores the
captured view state after structural changes. Toggle callbacks should update
their row in place; do not clear and rebuild an entire scroll content merely to
change one enabled state.

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
