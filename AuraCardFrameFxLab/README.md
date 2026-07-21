# AuraCardFrameFxLab

Unity 2022.3.62f3c1 lab project for tuning the Terrias solar card-frame foil effect.

Open `Assets/AuraCardFrameFxLab/Scenes/CardFrameFxLab.unity` and press Play. The preview uses:

- `Assets/AuraCardFrameFxLab/Art/CardBackground.png`
- `Assets/AuraCardFrameFxLab/Art/SunCardFrame.png`
- `Assets/AuraCardFrameFxLab/Shaders/CardFaceEffect.shader`
- `Assets/AuraCardFrameFxLab/Materials/TerriasFoilHoloFrameOverlay.mat`

The `LabController` object exposes the same `_Terrias...` material properties used by `Terrias/visual.registry.json`. In Play mode, use the floating panel to tune values and `Log Registry` to print a registry-ready parameter block to the Unity Console.
