# Visual Registry And Bundle

Use this reference when editing `Terrias/visual.registry.json`,
`Terrias-Dev/VisualAssets/*`, VisualBundle shaders/materials, or bundle-loading
runtime code.

## Registry Ownership

`Terrias/visual.registry.json` is the declaration surface for runtime visuals.
Prefer adding a registry entry over embedding resource paths in hook code. Keep
stable ids, resource paths, shader ids, material ids, and enabled flags
machine-readable.

Common registry domains include:

- frame animations for buff, blessing, and enemy dictionary icons;
- shader declarations such as Star Score HUD, Wuna orbit fire, and card frame
  flow;
- material declarations with bundle path, material path, textures, and numeric
  parameters;
- mode entry and map-node title/card art assets.

## Bundle Pipeline

VisualBundle sources live under `Terrias-Dev/VisualAssets`. The shipped bundle is
`Terrias/ModResource/VisualBundles/terrias_visuals`.

Use `tools/Build-TerriasVisualBundle.ps1` after changing:

- Unity-side bundle builder templates;
- shader source files;
- bundle pipeline JSON;
- bundled CG frames, materials, or textures.

Do not treat source edits under `VisualAssets` as shipped behavior until the
bundle has been rebuilt.

## Runtime Loading

Use the existing cache and loader boundaries:

- `VisualRegistry` and `VisualRegistryModels` parse declarations.
- `AssetBundleCache` owns bundle lifetime.
- `ShaderAssetLoader` resolves bundled shaders.
- `EffectMaterialFactory`, `EffectTextureCache`, and material-specific helpers
  own repeated material/texture creation.
- `TerriasResourceCache` owns game resource loads and `ResourceLoader.LoadAll`
  calls.

Avoid direct repeated `AssetBundle.LoadFromFile`, `Resources.Load`, or
`ResourceLoader.Load/LoadAll` calls outside the cache boundaries.

## Validation

Check that architecture tests still require registry files, bundle builders,
shaders, and visual runtime helpers. For bundle changes, run:

```powershell
tools\Build-TerriasVisualBundle.ps1
tools\Test-TerriasArchitecture.ps1
.codex\skills\terrias-mod-dev\scripts\validate-terrias.ps1
```
