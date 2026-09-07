# Aura MOD workspace

本仓库开发《魔女的终末旅途》的 Terrias 内容 MOD、AuraToolsExp 工具 MOD、
Aura 共享运行时，以及独立战斗模拟与训练工具。

## 项目地图

| 目录 | 职责 |
| --- | --- |
| Terrias / Terrias-Dev | 游戏加载的内容包 / C# 实现 |
| AuraToolsExp / AuraToolsExp-Dev | 游戏加载的工具包 / C# 实现 |
| AuraSharedRuntime-Dev | 将共享组件编译为 Aura.Shared.dll |
| AuraSharedCore、各 Aura*Shared 与 ArbiterShared | 通用基础设施与共享领域运行时 |
| AuraFoundationTrainer.*、AuraCombatSimulation.* | 独立训练、控制台与模拟工具 |
| Managed / 开发参考资料 | 当前编译程序集 / 本地宿主反编译资料 |
| tools / *Tests | 构建、发布、内容检查和行为验证 |
| TestMods | 显式维护的历史原型，不进入产品发布 |
| docs / .codex/skills | 产品契约与领域文档 / 开发任务指引 |

正式消费者以[消费者清单](tools/shared-consumers.json)为准。
生成目录与本地训练数据不等于待维护的产品源码；清理前核对调用方和归属。

## 开发入口

- [项目 skill 与任务路由](.codex/skills/aura-project-dev/SKILL.md)
- [验证选择与发布规则](.codex/skills/aura-project-dev/references/validation.md)
- [Terrias 技术文档](docs/Terrias/README.md)
- [AuraTools 模块与配置](docs/AuraToolsExp/toolbox-settings-and-module-architecture-design.md)
- [自动战斗、仿真与训练](docs/AuraCombatAI/README.md)
- [架构决策与遗留边界](docs/Terrias/13-架构决策与迁移门禁.md)
- [skill 维护](.codex/skills/aura-skill-evolution/SKILL.md)

读取当前消费者、测试入口、CG 协议与反编译候选：

```powershell
tools/Get-AuraProjectContext.ps1
```

该命令只读。反编译候选按版本排序，不表示它与当前游戏或 Managed 自动匹配。

## 构建与验证

产品 C# 修改使用统一事务构建：

```powershell
tools/Build-MainSharedConsumers.ps1
```

它构建共享程序集与两个产品，随后发布到仓库内的 MOD 包；直接
`dotnet build <project>` 只编译，不发布游戏包。游戏安装目录的部署是另一个步骤。

Terrias 常规 C# 构建和测试可统一选择：

```powershell
tools/Test-TerriasGate.ps1 -Profile csharp
```

无需先单独 Build。其它检查按验证指南选择，完整发布矩阵不作为日常默认检查。
训练器发布使用独立的 tools/Build-AuraFoundationTrainer.ps1。

skill 检查工具的依赖与命令见
[维护验证](.codex/skills/aura-skill-evolution/references/validation.md)。
