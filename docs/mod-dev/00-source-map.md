# Source Map

Use this page to decide which source of truth to check before editing or
documenting a MOD feature.

## Official Tutorial Layer

Path: `apocalyptic-journey-mod-tutorial/`

Use it for:

- official folder structure
- `ModConfig.json` fields
- Lua `ModConfig:Setup()` examples
- CSV table templates under `ModTemplate/Data` and `ModTemplate/Text`
- publish/upload workflow
- official guidance on IDs, resources, and localization suffixes

Important files:

- `apocalyptic-journey-mod-tutorial/README.zh-CN.md`
- `apocalyptic-journey-mod-tutorial/ModTemplate/README.zh-CN.md`
- `apocalyptic-journey-mod-tutorial/DllTemplate/readme.zh-CN.md`

## Decompiled Snapshot Layer

Path: `开发参考资料/反编译文件夹v1.0.23693118/`

Use it for:

- real method names and signatures
- lifecycle call sites for `RunScript(...)`
- `ScriptExecutor`, `IScriptExecutor`, `IStatusManager`, `EventCenter`
- `ModConfig` loading, Lua setup, DLL setup, and hook registration
- map, event, dialogue, card, buff, relic, and role runtime flows

High-value routes:

- `Witch/Mod/ModConfig.cs`
- `Witch/ScriptExecutor.cs`
- `Witch.Core/IScriptExecutor.cs`
- `Witch.Core/IStatusManager.cs`
- `Witch.Core/EventCenter.cs`
- `Witch/CardItem.cs`
- `Witch/CommonCardItem.cs`
- `Witch/AttackCardItem.cs`
- `Witch/BuffItem.cs`
- `Witch/BlessingRelic.cs`
- `Witch/UI/Window/EventUI.cs`
- `Witch/UI/Window/DialogueUI.cs`
- `Witch/NormalMapManager.cs`
- `Witch/MapManager.cs`
- `AllScripts/AllScripts.cs`

Do not paste large decompiled implementations into MOD code or docs. Extract
method names, signatures, flow edges, and practical constraints.

## Runtime MOD Layer

Paths: `SunExp/`, `GoldExp/`, `StarExp/`, `SanGuoShaExp/`, etc.

Use it for:

- published `Data/`, `Text/`, `ModResource/`, and `Scripts/Entry.dll`
- real CSV rows and script-column calls
- resource path conventions
- Data/Text ID synchronization
- localized wording and player-facing descriptions

`SunExp/` is the largest current example and includes cards, buffs, relics,
career/role data, partner content, enemy/enemy-card content, map entries, and
EventList rows.

## C# Implementation Layer

Paths: `SunExp-Dev/`, `GoldExp-Dev/`, `StarExp-Dev/`, etc.

Use it for:

- DLL entry initialization
- C# entry points called by CSV
- host API wrappers
- hook registration
- reusable mechanics
- compile-time references to `Managed/` DLLs

Recommended folder responsibilities:

- `Scripting/`: public static methods called directly by CSV script columns.
- `GameApi/`: safe wrappers around game objects and host APIs.
- `Hooks/`: method hooks, UI hooks, runtime lifecycle patch points.
- `Mechanics/`: reusable gameplay logic.
- `Infrastructure/`: IDs, logging, parsing helpers, and low-level utilities.

## Generated References

The generated files under `docs/mod-dev/generated/` should be refreshed with:

```powershell
tools\Export-ModDevDocs.ps1
```

Use them to find likely source files quickly, then verify the exact behavior in
the source layer above.
