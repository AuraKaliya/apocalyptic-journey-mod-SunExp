# SkinExp development

SkinExp is a thin local-cosmetic consumer of `AuraSkinShared`. Its bundled `career_1` skin is published from
`TestMods/SkinExp/SharedResources/Skins` and installed into `ModsData/AuraShared/Skins` at load time.

Production behavior lives in `TestMods/SkinExp/Scripts/Entry.dll`. Shared runtime behavior, package installation,
deduplication, selection persistence, redirects, and UI hooks live in `AuraSkinShared`.

WuNa skins are owned and published by Terrias. SkinExp no longer carries or directly scans them. All consumers read only
the persistent shared skin directory, so an installed skin remains discoverable across restarts without its original
provider being loaded again.

## Build and validation

```powershell
tools\Test-TestMods.ps1
```

`Test-SkinExp.ps1` is an archived prototype/content check invoked by that
isolated TestMods entry. Shared Skin behavior is validated separately by
`tools\Test-AuraSkinShared.ps1`.

Manual game verification should cover first install, duplicate registration, provider-only load, default restoration,
career switching, preparation/status UI refresh, animation fallback, package update, and conflict logging.
