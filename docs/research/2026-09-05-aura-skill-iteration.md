# Aura 项目 skill 迭代记录

日期：2026-09-05。范围：项目知识入口、skill、相关文档和开发验证工具。
本文记录本轮结果；日常开发使用[项目入口](../../README.md)和
[验证指南](../../.codex/skills/aura-project-dev/references/validation.md)。

## 最终结构

项目 skill 从 11 项调整为 14 项。入口文件合计从 1,288 行降至 901 行，
减少约 30%；当前有 45 份按任务读取的参考资料。缩减的是重复路由、固定验证链
和过时操作说明，保留所有权、执行路由、生命周期、完整修复、实机验收和美术约定。

新增三个入口：

- aura-project-dev：项目地图、责任路由与公共验证选择。
- aura-tools-dev：工具模块、设置、方案库、发现与 Unity UI。
- aura-combat-ai-dev：在线决策、权威模拟、独立训练及恢复。

四个跨产品技能完成名称切换，活动引用同步更新，不保留重复运行的旧入口：

| 原名称 | 当前名称 |
| --- | --- |
| terrias-complete-solution-gate | aura-complete-solution-gate |
| terrias-shared-runtime-dev | aura-shared-runtime-dev |
| terrias-visual-runtime-dev | aura-visual-runtime-dev |
| terrias-skill-evolution | aura-skill-evolution |

Terrias 内容、架构、事件、日耀回忆、卡图、海报，以及 Aura 战斗回放继续保持专项。
旧名称在 Git 和历史学习记录中保留为历史证据。

## 工具与调用关系

四个内容工具从 skill 目录迁入工程 tools，生产矩阵同步更新：

| 原文件名 | 当前入口 |
| --- | --- |
| validate-terrias.ps1 | tools/Test-TerriasContent.ps1 |
| validate-terrias-events.ps1 | tools/Test-TerriasEvents.ps1 |
| extract-terrias-inventory.ps1 | tools/Get-TerriasInventory.ps1 |
| inspect-event-chain.ps1 | tools/Get-TerriasEventChain.ps1 |

脚本定位仓库根目录的层级随迁移调整，内容验证语义保留。
旧陈旧词扫描与重复的 Terrias 验证说明已退役。

[Get-AuraProjectContext](../../tools/Get-AuraProjectContext.ps1) 只读消费者清单、
测试矩阵和公开源码常量，列出反编译候选。候选的版本排序不代表指纹匹配。

[Test-ProjectSkills](../../tools/Test-ProjectSkills.ps1) 校验 YAML、UI 元数据、
本地链接、资源可达性、脚本路径和产品矩阵的工具归属。它拒绝操作性 skill 中的
本机绝对路径，历史资料显式标记。工具依赖锁定在 requirements-skills.txt，
本轮仅安装到被 Git 忽略的 .venv/skills。

Build-AuraToolsExpDll 现在只进入产品统一构建事务，不再隐式构建训练器，
其 StopRunningFoundationTrainer 参数随该职责移除。训练器仍使用
Build-AuraFoundationTrainer 的 StopRunningTrainer 参数；
Rebuild-All 保持显式构建产品和训练器的完整流程。

## 契约与文档校准

- CG 指引切换到统一主体/信号/场景模型，版本引用当前源码，移除旧 schema/协议指导。
- 修正角色设置说明：技能/低生命使用 roleSelections，美餐仍走 Feast 的配置路径。
- 明确 Features/Cg 与 Features/SkillCg 的实际职责。
- 统一内容、共享和工具构建说明，说明矩阵选择不自动补齐依赖，List 展示完整清单。
- 更新 Terrias/Core README，清理 Terrias 技术首页七处失效链接。
- 反编译工作流改为候选发现与指纹核对，避免把固定版本当作永久当前版本。
- 美术继续承认当前对话中已批准的基准，补充配色来源和纯缩放边界。
- 海报保留设计与文本要求，工具选择遵循当前能力及用户选择，移除旧用户目录命令。
- 共享 DLL 一致性明确限定正式产品，避免把归档 TestMods 拉进产品发布。

## 已执行验证

| 检查 | 结果 |
| --- | --- |
| 项目 skill 检查 | 14 项通过，0 问题 |
| 当前安装的 skill-creator quick_validate | 14 项全部通过 |
| 隔离工具行为测试 | 17 项通过 |
| PowerShell 语法检查 | 7 个迁移/新增/修改入口通过 |
| Terrias 内容验证 | 75 卡牌、18 遗物、55 Buff、4 卡包、3 敌人，0 警告 |
| Terrias 事件验证 | 6 事件、10 Map 行，0 警告 |
| 正式矩阵调用迁移后两个验证器 | 2 步通过 |
| 内容清单与事件链查询 | 已执行并核对 |
| 两个矩阵的清单查询 | 已执行，入口可解析 |
| 共享 DLL 打包一致性 | 通过 |
| 原有二进制保护 | 10 个文件 SHA-256 与迭代开始前相同 |
| git diff --check | 通过 |

工具行为测试覆盖损坏/重复 YAML、无效元数据、丢失/越界链接、孤立资源、
矩阵路径和归属错误、动态源码版本读取及缺失声明失败，以及产品构建只委托一次、
不运行训练器和正确传播构建失败。测试使用临时夹具，不接触真实训练数据。

## 独立任务演练

两名独立只读评估者根据真实仓库推演了五类请求：

1. 卡牌文案润色：只选择内容证据与验证，不增加构建。
2. 两产品共用公共接口修改：审查 ABI、迁移消费者、一次构建和唯一发布器。
3. 已批准基准图后的续作：沿用批准，保留三次图像操作与纯缩放阶段。
4. CG 选择在重开后错误：区分页面上下文、保存、资源身份和共享播放。
5. 中断后 checkpoint 恢复失败：走训练恢复契约，不误用视觉回放或重置真实数据。

演练发现的 CG 文档漂移、美术配色定位和共享副本范围歧义已修正；
相关评估者复查后未报告会改变决策的剩余矛盾。这是只读前向演练，
不等同于修复上述假设故障或完成游戏/训练实机验收。

## 本轮边界

未修改 gameplay C#、玩家数据、素材、原有二进制或未提交的教程目录。
未运行全量发布矩阵、实际训练或 Unity/联机验收，也未向真实游戏目录部署。
工具构建入口通过隔离委托测试验证，本轮无需重建未变化的产品程序集。

64 条架构例外仍由现有台账管理，数量与实现保持原样；
本轮不宣称完成架构债务清零。已登记的里程碑字符串不等于自动执行的截止机制，
后续处理相应架构边界时仍须给出具体移除证据。
