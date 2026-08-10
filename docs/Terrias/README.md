# Terrias 技术文档

> 文档基线：2026-07-21
> Terrias 版本：`0.5.0`
> 反编译参考：`开发参考资料/反编译文件夹v1.0.23816797`
> 当前阶段：批次 C，大型模式与地图奖励

## 文档目标

这套文档从当前仓库实现重新建立，不继承已删除旧文档的结论。它同时描述：

- `Terrias/` 中游戏实际加载的内容、文本、资源、清单和 DLL；
- `Terrias-Dev/` 中内容脚本、机制服务、宿主适配、Hook、UI、视觉和网络实现；
- `Aura.Shared.dll` 中 Aura 核心与共享领域组件；
- Terrias 对 `Witch`、`Witch.Core`、`Mirror` 和 Unity 运行时的接入方式；
- 当前源码与反编译参考之间可以验证的调用链。

## 分层术语

| 术语 | 含义 |
| --- | --- |
| 内容交付层 | `Terrias/`，游戏直接读取的 MOD 目录 |
| Terrias 实现层 | `Terrias-Dev/`，编译为 `Terrias/Scripts/Entry.dll` |
| Aura 共享领域层 | Journey、Audio、CG、Skin、Online、UI、Arbiter 等跨 MOD 领域协议 |
| Aura 核心层 | `AuraSharedCore/` 中不理解 Terrias 业务语义的注册、包、存储、Hook、调度等基础设施 |
| 游戏主体或宿主层 | `Witch`、`Witch.Core`、`Mirror` 与 Unity 运行时 |

游戏的 `ModConfig.ModId` 与 Aura 注册所有者不是同一标识域。当前游戏 MOD id 由 `ModName + "." + ModAuthor` 形成，即 `Terrias.Aura`；Aura 共享注册通常使用稳定 owner id `Terrias`。文档在涉及所有权时会明确标注所属标识域。

## 基础架构文档

1. [整体架构与运行全景](01-整体架构与运行全景.md)
2. [MOD 内容数据与加载链](02-MOD内容数据与加载链.md)
3. [C# 分层与依赖规则](03-CSharp分层与依赖规则.md)
4. [Aura 共享层与核心层接入](04-Aura共享层与核心层接入.md)
5. [游戏主体接入机制](05-游戏主体接入机制.md)

## 规划与覆盖

- [技术文档蓝图](00-documentation-blueprint.md)
- [模块覆盖矩阵](00-module-coverage-matrix.md)
- [Aura/Terrias 复杂模块拆分评审](../architecture-complex-module-review-2026-07-16.md)
- [复杂模块治理首轮开发记录](../architecture-complex-module-development-round-1-2026-07-17.md)
- [AuraCg 模块治理第二轮开发记录](../architecture-complex-module-development-round-2-2026-07-17.md)
- [AuraCg Preload 与媒体缓存第三轮开发记录](../architecture-complex-module-development-round-3-2026-07-17.md)
- [AuraCg 媒体缓存预算与安全淘汰第四轮开发记录](../architecture-complex-module-development-round-4-2026-07-17.md)
- [AuraCg 预加载背压与帧预算第五轮开发记录](../architecture-complex-module-development-round-5-2026-07-17.md)

## 功能模块文档

1. [卡牌、Buff、遗物与卡包](modules/01-卡牌BUFF遗物与卡包.md)
2. [战斗事件、场地与特殊标签](modules/02-战斗事件场地与特殊标签.md)
3. [乌娜角色与白曜体系](modules/03-乌娜角色与白曜体系.md)
4. [洛奈尔角色与晨星星谱体系](modules/04-洛奈尔角色与晨星星谱体系.md)
5. [日耀回忆模式](modules/05-日耀回忆模式.md)
6. [无尽之海模式与地图循环](modules/06-无尽之海模式与地图循环.md)
7. [无尽深渊压力与奖励体系](modules/07-无尽深渊压力与奖励体系.md)
8. [精灵收集、培养与出战系统](modules/08-精灵球捕获与精灵召唤.md)
9. [DPS 伤害归属与精灵专属意图池](modules/09-DPS伤害归属与精灵专属意图池.md)
10. [游戏主体敌人与精灵专属意图总表](modules/10-游戏主体敌人与精灵专属意图总表.md)
11. [投影、精灵与心变机制完整说明](modules/11-投影精灵与心变的Partner战斗流程.md)

## 专题设计与复核

1. [无尽之渊阶段结算与海域轮换方案](design/01-无尽之渊阶段结算与海域轮换方案.md)
2. [精灵种族值、成长曲线与雷达图数据规范](design/02-精灵种族值成长曲线与雷达图数据规范.md)
3. [游戏主体精灵种族值表](design/04-游戏主体精灵种族值表.md)
4. [首领与最终首领精灵档案人工复核记录](design/05-首领与最终首领精灵档案人工复核记录.md)
5. [精灵 Schema 2 成长与仓库界面实现记录](design/06-精灵Schema2成长与仓库界面实现记录.md)

设计文档记录方案、数值审计和实现决策；是否已经进入代码基线以各文档顶部状态为准。模块文档描述当前玩家可见行为。

后续扩展玩法、视觉、网络、宿主映射表、内容实体清单和术语表按覆盖矩阵分批补充。

## 推荐阅读路线

- 第一次维护 Terrias：`01 -> 02 -> 03 -> 对应功能模块`。
- 修改共享能力：`01 -> 04 -> 03 -> 构建发布模块`。
- 追踪游戏调用：`05 -> 对应功能模块 -> 宿主映射表`。
- 排查 CSV 脚本：`02 -> 03 的 Scripting/GameApi -> 对应内容模块`。
- 排查联机：`01 的状态所有权 -> 04 的共享协议 -> 网络模块`。

## 证据标记

- **代码确认**：当前仓库源码、配置或测试直接证明。
- **反编译确认**：已定位并阅读反编译程序集中的具体类型和方法。
- **签名确认**：当前 `Managed/` 程序集可用于编译该签名。
- **合理推断**：调用关系可信，但尚未完成宿主方法体核验。
- **设计约束**：当前架构门禁或共享协议要求。

反编译用于解释宿主流程，`Managed/` 才是当前编译契约。两者冲突时，文档必须记录差异与兼容分派，不能用反编译快照覆盖当前签名。

## 文档维护

- 新增 CSV 类型、公共脚本入口、Hook 目标、共享组件或 RPC 时，先更新覆盖矩阵。
- 正式页面记录代码基线和反编译版本，避免把版本相关行为写成永恒规则。
- 删除或重命名源码后检查文档中的路径和类型名。
- 共享源码变化后验证所有消费者及已打包 `Aura.Shared.dll` 的一致性。
- 已删除旧文档不作为本套文档的当前事实来源。
