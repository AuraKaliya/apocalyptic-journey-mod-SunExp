# MOD 加载流程

本页总结 MOD 如何进入游戏运行时。

源码锚点：

- `开发参考资料/反编译文件夹v1.0.23715745/Witch/GameConfigManager.cs`
- `开发参考资料/反编译文件夹v1.0.23715745/Witch/Mod/ModConfig.cs`
- `apocalyptic-journey-mod-tutorial/ModTemplate/README.zh-CN.md`
- `apocalyptic-journey-mod-tutorial/DllTemplate/readme.zh-CN.md`

```mermaid
flowchart TD
    A["GameConfigManager 加载基础配置"] --> B["发现已启用 MOD 配置"]
    B --> C["按依赖顺序加载"]
    C --> D["合并 MOD Data/Text CSV 行"]
    D --> E["ModConfig.Setup"]
    E --> F{"存在 Scripts/Entry.lua?"}
    F -- "是" --> G["执行 Entry.lua 并调用 Setup"]
    F -- "否" --> H["继续"]
    G --> I{"存在 Scripts/Entry.dll?"}
    H --> I
    I -- "是" --> J["加载 assembly"]
    J --> K["调用 [ModInitialize] 方法"]
    K --> L["注册 [HookBefore]/[HookAfter] 方法"]
    I -- "否" --> M["MOD setup 完成"]
    L --> M
```

## 重要结论

- `ModName` 应与运行时 MOD 文件夹名一致。
- `Scripts/Entry.dll` 是发布态 DLL 的固定文件名。
- C# assembly name 可以也应该保持唯一，以避免运行时冲突。
- Data/Text CSV 行按表和 ID 加载，脚本列稍后由对应生命周期执行。
- 通过 `SetDataConfig`、`ModifyDataConfig` 或 `MergeDataConfig` 改动脚本列时，
  内部会标记脚本数据已变化。

## SunExp 风格流程

SunExp 采用 DLL 优先的桥接方式：

```mermaid
flowchart TD
    A["SunExp/ModConfig.json"] --> B["SunExp/Scripts/Entry.dll"]
    B --> C["SunExp-Dev Entry.Initialize"]
    C --> D["为 XLua 注册 C# assembly"]
    D --> E["导入 SunExp.Dll.Scripting 类"]
    E --> F["CSV 脚本列调用 CS.SunExp.Dll.Scripting.*"]
```

这种方式让内容留在 CSV，同时把行为放进类型明确、可测试的 C#。
