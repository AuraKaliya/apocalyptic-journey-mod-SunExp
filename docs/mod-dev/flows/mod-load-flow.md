# MOD Load Flow

This flow summarizes how a MOD enters the game runtime.

Source anchors:

- `开发参考资料/反编译文件夹v1.0.23715745/Witch/GameConfigManager.cs`
- `开发参考资料/反编译文件夹v1.0.23715745/Witch/Mod/ModConfig.cs`
- `apocalyptic-journey-mod-tutorial/ModTemplate/README.zh-CN.md`
- `apocalyptic-journey-mod-tutorial/DllTemplate/readme.zh-CN.md`

```mermaid
flowchart TD
    A["GameConfigManager loads base configs"] --> B["discover enabled MOD configs"]
    B --> C["load dependencies in order"]
    C --> D["merge MOD Data/Text CSV rows"]
    D --> E["ModConfig.Setup"]
    E --> F{"Scripts/Entry.lua exists?"}
    F -- "yes" --> G["run Entry.lua and call Setup"]
    F -- "no" --> H["continue"]
    G --> I{"Scripts/Entry.dll exists?"}
    H --> I
    I -- "yes" --> J["load assembly"]
    J --> K["invoke [ModInitialize] methods"]
    K --> L["register [HookBefore]/[HookAfter] methods"]
    I -- "no" --> M["finish MOD setup"]
    L --> M
```

## Important Consequences

- `ModName` should match the runtime MOD folder name.
- `Scripts/Entry.dll` is the required published DLL filename.
- The C# assembly name can and should be unique to avoid runtime conflicts.
- Data/Text CSV rows are loaded by table and ID, then script columns execute later
  through the relevant lifecycle.
- Changing script columns through `SetDataConfig`, `ModifyDataConfig`, or
  `MergeDataConfig` marks script data as changed internally.

## SunExp-Style Flow

SunExp uses a DLL-first bridge:

```mermaid
flowchart TD
    A["SunExp/ModConfig.json"] --> B["SunExp/Scripts/Entry.dll"]
    B --> C["SunExp-Dev Entry.Initialize"]
    C --> D["register C# assembly for XLua"]
    D --> E["import SunExp.Dll.Scripting classes"]
    E --> F["CSV script columns call CS.SunExp.Dll.Scripting.*"]
```

This keeps content in CSV while keeping behavior in typed, testable C#.
