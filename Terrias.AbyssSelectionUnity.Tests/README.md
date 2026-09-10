# Abyss selection rendering acceptance

Run `tools/Test-TerriasAbyssSelectionUnity.ps1 -UnityPath <Unity 6000.0.46f1/Editor/Unity.exe>` from the repository root.

The runner mirrors the current production contour rasterizer, sprite cache, source projection, selection component and UI rectangle helper. It copies both actual Abyss option artworks and verifies their hashes after the run. Generated mirrors, fixture copies and Unity caches are ignored.

The three PlayMode cases render with URP 17 and a real GPU. They cover selected/unselected artwork pixels, a native-style MeshRenderer frame with flipped UVs and perspective depth, transparent raycast targets, pointer exit, pooling reset, stencil clipping and unreadable texture cache reuse. The mesh frame is a synthetic geometry fixture; the test does not load the game's DictionaryUI or claim in-game end-to-end acceptance.

Results and screenshots are written to `output/abyss-selection-unity`. No source bitmap, native material or game installation is modified.
