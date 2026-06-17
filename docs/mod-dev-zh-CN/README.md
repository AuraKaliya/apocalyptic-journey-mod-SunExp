# 《魔女：终末旅途》MOD 开发参考

本目录是 `docs/mod-dev/` 的中文备份版，用于在本工作区内制作 MOD
时快速查 API、流程和落地范式。内容基于三层资料：

- `apocalyptic-journey-mod-tutorial/` 下的官方教程与模板
- `开发参考资料/反编译文件夹v1.0.23715745/` 下的反编译游戏快照
- `SunExp/` 及各个 `*-Dev/` C# 工程中的实际 MOD 实现

目标不是复刻完整反编译工程，而是整理 MOD 作者最常用、最稳定的表面：
CSV 表、脚本入口、宿主 API、Hook 点、资源、本地化，以及主要玩法流程。

## 阅读顺序

1. [资料源地图](00-source-map.md)：每类结论应该从哪里验证。
2. [快速开始](01-quickstart.md)：新增内容的最小安全路径。
3. API：
   - [ModConfig API](api/mod-config.md)
   - [ScriptExecutor API](api/script-executor.md)
   - [状态与事件](api/status-and-events.md)
   - [SunExp C# 封装 API](api/sunexp-csharp-wrapper-api.md)
4. 流程：
   - [MOD 加载流程](flows/mod-load-flow.md)
   - [卡牌战斗流程](flows/card-combat-flow.md)
   - [祝福与 Buff 机制](flows/blessing-buff-flow.md)
   - [事件、对话与地图流程](flows/event-dialogue-map-flow.md)
5. Cookbook：
   - [添加一张卡牌](cookbook/add-card.md)
   - [添加一个地图事件](cookbook/add-map-event.md)

## 生成索引

英文主文档的生成脚本是：

```powershell
tools\Export-ModDevDocs.ps1
```

本中文备份目录下的 `generated/` 文件是对应生成索引的中文化备份：

- `csv-schema-index.md`
- `public-api-index.md`
- `script-hook-point-index.md`

生成索引主要用于检索。字段名、方法名、类名和路径会保留原样，因为这些名字
需要和代码、CSV、反编译工程精确对应。

## 当前工作区约定

官方 DLL 模板把开发工程放在 MOD 内部的 `Dev/` 目录。本工作区采用兄弟目录
拆分：

- 发布/运行面：`SunExp/`、`GoldExp/`、`StarExp/` 等
- C# 实现面：`SunExp-Dev/`、`GoldExp-Dev/`、`StarExp-Dev/` 等

SunExp 风格的项目中，CSV 脚本列应保持短调用，并把行为委托给
`CS.<Mod>.Dll.Scripting.*` 入口。可复用的宿主访问放在 `GameApi/`，
Hook 放在 `Hooks/`，共享玩法逻辑放在 `Mechanics/`。
