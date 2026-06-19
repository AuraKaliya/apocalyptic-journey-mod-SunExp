# Witch's Apocalyptic Journey MOD Developer Notes

This directory is a practical API and flow reference for making MODs in this
workspace. It is grounded in three source layers:

- the official tutorial and templates under `apocalyptic-journey-mod-tutorial/`
- the decompiled game snapshot under `开发参考资料/反编译文件夹v1.0.23693118/`
- the working MOD projects such as `SunExp/` plus their `*-Dev/` C# projects

The goal is not to mirror the full decompiled game. The goal is to document the
stable surfaces a MOD author needs: CSV tables, script entry points, host APIs,
hook points, resources, localization, and the major gameplay flows.

## Reading Order

1. [Source Map](00-source-map.md): where each fact should come from.
2. [Quickstart](01-quickstart.md): the minimum path for adding content.
3. API:
   - [ModConfig API](api/mod-config.md)
   - [ScriptExecutor API](api/script-executor.md)
   - [Status and Events](api/status-and-events.md)
   - [SunExp C# Wrapper API](api/sunexp-csharp-wrapper-api.md)
   - [Runtime Arbiters and Extension Points](api/runtime-arbiters.md)
4. Flows:
   - [MOD Load Flow](flows/mod-load-flow.md)
   - [Card Combat Flow](flows/card-combat-flow.md)
   - [Event, Dialogue, and Map Flow](flows/event-dialogue-map-flow.md)
5. Cookbook:
   - [Add a Card](cookbook/add-card.md)
   - [Add a Map Event](cookbook/add-map-event.md)

## Generated Indexes

Run this from the repository root:

```powershell
tools\Export-ModDevDocs.ps1
```

It writes generated references to `docs/mod-dev/generated/`:

- `csv-schema-index.md`
- `public-api-index.md`
- `script-hook-point-index.md`

Generated files are lookup aids. If a generated line conflicts with a hand-written
rule, inspect the source files and update the rule or the generator.

## Current Workspace Convention

The official DLL template puts the development project under a `Dev/` folder
inside a MOD. In this workspace the published MOD and development project are
split into sibling directories:

- published/runtime surface: `SunExp/`, `GoldExp/`, `StarExp/`, etc.
- C# implementation surface: `SunExp-Dev/`, `GoldExp-Dev/`, `StarExp-Dev/`, etc.

For SunExp-style work, keep CSV script columns short and delegate behavior to
`CS.<Mod>.Dll.Scripting.*` entry points. Put reusable game-object access in
`GameApi/`, hooks in `Hooks/`, and shared gameplay logic in `Mechanics/`.
