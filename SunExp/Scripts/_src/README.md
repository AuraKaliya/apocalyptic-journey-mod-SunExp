# SunExp Lua Source Modules

`SunExp/Scripts/Entry.lua` is the only Lua entry file the game runtime is expected to load. Files in this `_src` folder are development sources; they are concatenated into `Entry.lua` by:

```powershell
tools\Build-SunExpEntry.ps1
```

## Workflow

1. Edit the relevant module under `_src`.
2. Keep `manifest.txt` in load order if adding or moving modules.
3. Run `tools\Build-SunExpEntry.ps1`.
4. Run `tools\Test-SunExpEntryLoad.ps1`.
5. Run `.codex\skills\sunexp-mod-dev\scripts\validate-sunexp.ps1`.

## Runtime Rules

- Do not rely on the game to auto-load `_src` files.
- Keep CSV-callable helpers as `SunExp_*` globals and register them in `registry.lua`.
- Keep `ModConfig:Setup()` in `setup.lua`; it remains the load-time registration point.
- Each module is wrapped in `do ... end` in the generated entry file, so cross-module state must be global or stored under a shared table.
