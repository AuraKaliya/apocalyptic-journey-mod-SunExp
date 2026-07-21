# Terrias Card Frame Debug

This Unity project previews the Terrias solar card frame with a foil/holo effect applied only to the frame image alpha.

## Open

Open this folder in Unity 2022.3.62f3c1:

```text
D:\workfile\project\Mod_1\apocalyptic-journey-mod-Terrias\TerriasCardFrameDebug
```

The preview scene is:

```text
Assets/TerriasCardFrameDebug/Scenes/CardFrameHoloDebug.unity
```

The generated static preview is:

```text
Assets/TerriasCardFrameDebug/Export/card_frame_preview.png
```

## Tune

Enter Play Mode and use the runtime sliders in the Game view. The material is assigned only to `SunFrameWithFoil`; `CardBackground` stays on the default UI material so the foil effect does not leak onto the card face background.

Use `Export Profile` after tuning. It writes:

```text
Assets/TerriasCardFrameDebug/Export/card_frame_foil_profile.json
```

## Terrias Integration Notes

The debug shader is copied from:

```text
Terrias-Dev/VisualAssets/Shaders/CardFrameHoloFlow.shader
```

The shader property names match the Terrias runtime material path used by `CardFrameEffectMaterials`. To apply the tuned result to the shipped mod, move the exported float/color values into the visual registry or VisualBundle material defaults, then wire the effect to the card-frame target in the runtime registration path.

Current repo note: `CardFrameHoloFlow.shader` exists, while the active visual bundle builder currently creates `CardFaceEffect.mat` but not a dedicated card-frame material asset. If the final effect should ship as a separate frame material, update the VisualAssets pipeline/builder and the registry together.
