# C# Authoring Boundaries

Use this reference when deciding where new SunExp production code belongs or
when checking game API shape through the indexed game reference project.

## SunExp Code Boundaries

- `SunExp-Dev/Scripting/*Scripts.cs`: public static methods called directly by CSV script columns.
- `SunExp-Dev/GameApi/*`: wrappers around game objects, `ScriptExecutor`, player APIs, buffs, cards, vars, audio, and safe runtime access.
- `SunExp-Dev/Infrastructure/*`: constants, logging, dictionary helpers, parsing helpers, field IDs, and other low-level support.
- `SunExp-Dev/Mechanics/*`: reusable implementation code shared by multiple scripting entry points. This directory is currently mostly flat; prefer a focused service/model file name over a new subdirectory unless the current repo already has a stable grouped area.
- `SunExp-Dev/Hooks/*`: code that attaches to game methods, event listeners, UI points, map behavior, or lifecycle points.
- `SunExp/Data/**/*.csv`: configuration rows and short `CS.SunExp.Dll.Scripting.*` calls.
- `SunExp/Text/**/*.csv`: localized player-facing text that must match the Data rows when the table has a Text side.
- `SunExp/audio.registry.json`: declarative audio and BGM provider registration used by the audio runtimes.

Do not put long implementation logic in CSV script columns. Add or reuse a C#
entry point, then call that entry point from CSV.

## Host Bridge

`SunExp-Dev/Entry.cs` may use the game's XLua host objects, such as
`ScriptExecutor.luaEnv`, to expose the SunExp C# assembly to CSV script calls.
This bridge is necessary interop. It must not grow into production `.lua` files
or old dynamic helper registration.

## Game Reference Routes

Use the indexed decompiled project only to verify production boundaries, method
names, signatures, and comparable official script shape. Do not copy large
chunks of decompiled code into the mod.

The repository `Managed/` assemblies are authoritative for compilation. When
they disagree with the decompiled snapshot, follow current `Managed/` and add a
compatibility wrapper if older signatures must remain supported.

Load `references/game-reference-index.md` before searching the decompiled
reference. That file records the current decompile version, high-frequency
search routes, and how to record versioned corrections when the decompiled
snapshot disagrees with the current game or `Managed/` assemblies.

## Managed Signature Drift

- Compile against repository `Managed/` after it is updated.
- Interpret `MissingMethodException` as binary API drift first. Locate every
  direct caller before changing UI or gameplay logic.
- Keep reflection in one `GameApi/` wrapper. Resolve the current signature,
  support known legacy signatures, then use a deterministic table/API fallback.
- Return an empty safe result only after supported paths fail, and log failures
  without exposing exceptions to UI flow.

## Hook Failure Containment

- Wrap hook entry points so game flow survives mod failures.
- Run independent lifecycle actions in separately named/logged steps. One failed
  HP adjustment must not block listeners, tags, or later setup.
- Do not borrow an unrelated active `ScriptExecutor` for a global effect or call
  a path requiring a data-config `Id` from a synthetic/missing config. Use the
  resolved synchronized status or manager API when verified.

## Multiplayer And Runtime Objects

- Load `sunexp-shared-runtime-dev/references/sync-scenario-model.md` before
  choosing network event shape, RPC authority fields, duplicate suppression, or
  payload limits.
- Keep player preparation and deck choices player-scoped in multiplayer.
- When a non-host settlement reports failure, first check whether the code calls
  host-only role persistence, shared progression writers, or native APIs that
  mutate all players instead of the local player.
- Repair both the authoritative map tree and `maps`/`mapData` sync arrays when a
  custom map contract can be polluted or version-skewed.
- Keep fallback ordering deterministic so host and clients choose the same row.
- Assign `NodeDice` to every custom `MapTree.Node`; prefer the owning tree's dice
  cursor and use `Dice.Default` only for fixed nodes that do not draw.
- Clone mutable map dictionaries. Never persist battle/map/UI fallback state by
  mutating global config rows; restore any temporary row change after native use.

## Placement Rules

- Add a new `Scripting` method when CSV needs a new callable operation.
- Add a `GameApi` wrapper when multiple scripts need the same game-object access or null-safe call.
- Add `Infrastructure` constants before repeating string IDs or variable keys.
- Add `Hooks` code only after verifying the target method or event name through
  the game reference index.
- Keep decompiled-reference findings out of `SKILL.md`; use them only to guide
  the current edit or record versioned corrections in the index.

## Validation

After C# changes, run:

```powershell
tools\Build-SunExpDll.ps1
tools\Test-SunExpCSharp.ps1
.codex\skills\sunexp-mod-dev\scripts\validate-sunexp.ps1
```
