# AudioArbiter GameState Reader 与 Hook Context Mapper 第十七轮开发记录

> 日期：2026-07-18
>
> 前置工作：第十六轮已建立 Hook Catalog、纯观察模型与 Request Factory
>
> 范围：游戏对象读取边界、Hook Context 映射、Runtime 委派、架构与发布护栏

## 1. 本轮目标

本轮完成 Hook Adapter 重构的第二阶段：

1. 将 `IDataConfig`、当前 Career、Status id/role、HP、local ownership 和 Fight status 枚举集中到 `AudioGameStateReader`；
2. 将 `ModHookContext` 与 `AuraCombatActionContext` 的字段拆解集中到 `AudioHookContextMapper`；
3. 按 Career/Battle、Buff/Vocal、Combat、HP 的顺序迁移生产 Runtime；
4. 让生产路径使用第十六轮建立的 `AudioRequestFactory`；
5. 保持 Hook 注册位置、网络事件身份、低血量状态机、公共 API 和协议版本不变。

## 2. AudioGameStateReader

新增 `AudioGameStateReader.cs`，作为 Audio 域读取游戏对象的唯一集中边界，负责：

- 读取 `ShowCareer.dataConfig` 与当前 Career；
- 识别 Card ScriptExecutor，并读取 Id、Action、Effects；
- 读取 BuffId；
- 解析 Status instance id 与 father role id；
- 区分 routed combat 的本地 owner 和 HP 观察的本地玩家 Status；
- 兼容读取 `CurHp`、`Hp`、`MaxHp`，并缓存反射成员；
- 将 Status 转换为不含游戏对象的 `AudioStatusSnapshot`；
- 枚举 ScriptExecutor Self/Target 与 FightManager statuses。

Reader 不接收 `ModHookContext`，不创建 `SoundPlaybackRequest`，也不依赖 Provider、Network、Playback 或 Hook 注册服务。

本地归属的两种既有比较语义被分别保留：

- routed combat 的 player id 比较继续使用 ordinal；
- HP 本地玩家识别继续使用 ordinal-ignore-case，并保留 `FightPlayer.Status` 引用回退。

## 3. AudioHookContextMapper

新增 `AudioHookContextMapper.cs`，负责把 Hook 输入映射为第十六轮的纯观察模型：

- `MapCareerDetail`；
- `MapLegacyCombatAction`；
- `MapCombatAction`；
- `MapBuffApplied`；
- `MapVocalState`；
- `MapNarration`；
- `MapExecutorHpChanges`；
- `MapStatusHpChange`；
- `MapFightStatusSnapshots`；
- `MapBattleCompleted`。

Mapper 可以认识 Hook Context 和游戏对象类型，但不能直接访问 Player/Fight/Role/GameEntry 单例，不能创建播放请求，也不拥有网络、Provider、播放或 Hook 生命周期。

## 4. Runtime 迁移结果

`AudioArbiterRuntime.AudioArbiterComponent` 已完成以下委派：

- Career detail 经 Mapper 生成 observation，再经 Factory 生成 CareerSelected；
- Win/Escape 经 Mapper 与 Factory 生成 BattleCompleted；
- Buff/Vocal 经 Mapper 与 Factory 生成请求；
- routed combat 经 Mapper 做本地 owner 筛选，Network Runtime 只负责生成 CardUse play id，Factory 固定生成 CardUse + SkillVoice；
- legacy combat 方法也改用相同 Mapper/Factory 字段基线；
- Narration 参数经 Mapper 转为纯数组 observation；
- Fight start HP seed、ScriptExecutor HP 和 Status setter HP 均先转换为 `AudioStatusSnapshot`；
- LowHealth 请求由 Factory 创建，Runtime 继续只保留跨观察的状态机与 Provider threshold 决策。

Runtime 中已移除：

- `ReadDataId` / `ReadDataValue`；
- `ReadCurrentCareerId`；
- `ReadStatusRoleId` / `ResolveStatusId`；
- `ReadHpRatio` / `ReadIntMember` 与成员缓存；
- `IsCardScriptExecutor`；
- `IsLocalOwnerStatus` / `IsLocalPlayerStatus`；
- 所有内联 `new SoundPlaybackRequest`。

文件从 1596 行降至 1289 行。新增 Reader 为 287 行，Mapper 为 159 行。

## 5. 行为兼容性

本轮保持以下行为不变：

- Career selection 去重与 session reset；
- CardUse 只由本地 owner 观察触发；
- CardUse 继续使用 Network Runtime 的 play id，SkillVoice 不复制该权威 event id；
- Buff role 继续跟随当前 Career；
- Vocal role 继续优先来自 Status father role；
- HP <= 0 或 MaxHP 无效时不产生 LowHealth observation；
- 第一次有效 HP 观察只建立 previous ratio；
- LowHealth recovery margin、announced 去重、无 Provider 冷却与 threshold 逻辑不变；
- Win/Escape result 与 source 字符串不变；
- Component 仍然拥有播放、替换状态和 Coroutine 编排。

`CurrentBuildId` 继续为 `audio-arbiter-2026-07-11-v8`，ProtocolVersion 与 minimum protocol 继续为 6。新增类型均为 internal，1228 项公共 API 基线未发生变化。

## 6. 新增架构护栏

共享架构门禁与 AuraTools 跨消费者检查新增以下约束：

- Reader 必须保留 Career、Status snapshot、Fight statuses、Executor statuses 与 HP 反射缓存边界；
- Reader 不得接收 Hook Context 或创建播放请求；
- Mapper 不得直接访问 Player/Fight/Role/GameEntry 单例；
- Mapper 不得创建播放请求或持有 Network/Provider/Playback/Hook 注册职责；
- Component 不得重新出现 Career/Status/HP 游戏对象读取和内联请求构造；
- Component 的 Career、Combat、Buff、Vocal、HP 必须委派给 Mapper；
- LowHealth 必须委派给 Request Factory；
- 19 个 Hook Catalog 定义及 18 个唯一 target 继续完整保留。

共享兼容基线也已加入 Reader 与 Mapper 的关键类型和方法锚点。

## 7. 完整验证结果

- AudioArbiterShared：360 项断言通过；
- AuraCgShared：153 项断言通过；
- AuraSharedCore：92 项断言通过；
- Aura.Shared：1228 项公共 API 兼容基线通过；
- AuraDirector：20 项断言通过；
- AuraToolsExp：635 项断言通过；
- Terrias：架构检查、282 项 C# 断言与内容验证通过；
- shared write、content/tool/shared、RPC authority 与架构门禁通过；
- Terrias、SanGuoShaExp、AuraToolsExp 三个消费者均以 0 警告、0 错误构建；
- 完整共享发布矩阵与 DLL 打包检查通过；
- 构建产物与三个打包副本 SHA-256 一致：`F4E8B60AD64C0057342B0602BA223C8AE79D02B84B78DEBD4AA9C5DD2ADAA9AB`。

## 8. 下一轮建议

下一轮进入第三阶段，拆出 `AudioLowHealthCoordinator`：

1. 迁移 `lastHpRatioByStatus` 与 `lowHealthAnnounced`；
2. 迁移首次观察 seed、下降判断、recovery margin reset；
3. 迁移 Provider threshold crossing 与 legacy fallback 判断；
4. 迁移 LowHealth no-provider cooldown；
5. 保持 Reader 只输出当前快照，Coordinator 只处理跨观察状态，Component 只提交最终请求；
6. Coordinator 稳定后，再进入第四阶段的 `AudioHookAdapter` 注册与生命周期拆分。
