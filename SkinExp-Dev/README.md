# SkinExp development

SkinExp is a standalone local-cosmetic mod. Production behavior lives in `SkinExp/Scripts/Entry.dll`; the source project is `SkinExp-Dev/SkinExp.Dll.csproj`.

## Runtime layout

- `Services/SkinRegistry.cs`: discovers structured `Skins/<career>/<skin>` packages and legacy external `*.skin.json` manifests.
- `Services/SkinSelectionStore.cs`: persists per-career local selections.
- `GameApi/ResourceRedirectApi.cs`: owns reversible animation-state resource redirects.
- `Mechanics/SkinRuntime.cs`: resolves the selected skin and applies animation mappings.
- `Hooks/SkinRuntimeHooks.cs`: registers game lifecycle hooks.
- `Hooks/SkinUiRuntime.cs`: refreshes official career/status/avatar surfaces.
- `Hooks/SkinPanelController.cs`: constructs the external skin drawer in `GameEntryUI`.

Static images are refreshed only on their owning UI objects. Animated sprites use reversible per-state `ResourceLoader.RedirectPath` mappings so missing states naturally fall back to the target career's original animation.

## Build and validation

```powershell
tools\Build-SkinExpDll.ps1
tools\Test-SkinExp.ps1
```

Manual game verification should cover normal-mode gating, repeated opening of the preparation window, career switching, default restoration, entering a fight, status UI, missing skin resources, two careers sharing an animation path, and multiplayer clients with different local selections.
