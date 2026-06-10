# Errors

## 2026-06-10 - image generation output size mismatch

- **Context:** Batch-generating SunExp card-face PNGs with the built-in image generation tool.
- **Expected:** Prompted 512x512 square assets could be copied directly into `SunExp/ModResource/Images/Card/SunExp/`.
- **Observed:** Generated PNGs were 1254x1254, so the first direct copy failed the asset-size validation.
- **Fix:** Treat generated dimensions as non-authoritative and run a deterministic center-crop/resize pass to 512x512 before replacing project assets.

## 2026-06-10 - large PNG atlas optimized save timeout

- **Context:** Regenerating `_sunexp_atlas.png` and `_sunexp_source_atlas.png` as a 2560x3072 contact atlas.
- **Expected:** Pillow `save(..., optimize=True)` would finish within the default shell timeout.
- **Observed:** Optimized PNG save exceeded the 10s command timeout.
- **Fix:** Use a longer timeout or normal PNG compression such as `compress_level=6` for large generated preview atlases.
