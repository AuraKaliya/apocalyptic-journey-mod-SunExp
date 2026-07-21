# AudioArbiter LowHealth Coordinator 第十八轮开发记录

> 日期：2026-07-18
>
> 前置工作：第十六轮建立 Hook Catalog、观察模型和 Request Factory；第十七轮完成 GameState Reader 与 Hook Context Mapper 迁移
>
> 范围：LowHealth 跨观察状态、Provider 阈值索引、恢复策略、无 Provider 冷却、行为测试与发布护栏

## 1. 本轮目标

本轮完成 Hook Adapter 重构的第三阶段：

1. 将 HP 历史、首次 seed、已播报状态和恢复重置移出 Component；
2. 将 LowHealth Provider 类型/阈值索引移出 Component；
3. 将 legacy fallback 与显式 threshold crossing 策略集中到纯状态机；
4. 将 no-provider ratio bucket、过期和清理集中到纯状态机；
5. 使用真实行为测试锁定状态转换，不依赖 Unity/Witch 或固定 Runtime 文件；
6. 保持 Request Factory、Provider Resolver、Network Runtime 与播放路径不变。

## 2. AudioLowHealthCoordinator

新增 `AudioLowHealthCoordinator.cs`，包含以下纯模型与状态机：

- `AudioLowHealthObservationOutcome`；
- `AudioLowHealthObservationDecision`；
- `AudioLowHealthProviderDescriptor`；
- `AudioLowHealthCoordinator`。

Coordinator 只接收：

- `AudioStatusSnapshot`；
- `SoundPlaybackRequest`；
- 普通 Provider descriptor；
- 调用方提供的当前时间。

它不依赖 Unity、Witch、Hook Context、`StatusManager`、`SoundProviderHandle`、Network Runtime、Playback Service 或系统时钟。

## 3. 状态职责

Coordinator 当前负责：

- `lastHpRatioByStatus`；
- announced status id 去重；
- 首次 observation seed；
- ratio increase/equal/decrease 分类；
- role/career identity 缺失决策；
- 最低显式 Provider threshold 与 recovery margin；
- unknown Provider 的 legacy `0.35f` crossing；
- 显式 Provider threshold crossing；
- 存在未配置 threshold 的 LowHealth Provider 时允许任意下降；
- no-provider cooldown 与 ratio bucket key；
- Provider 刷新时清理 no-provider cooldown；
- fight reset 时清理 HP、announced 与 cooldown，同时保留 Provider 配置。

默认值保持不变：

- no-provider cooldown：`0.75f`；
- recovery margin：`0.05f`；
- legacy fallback threshold：`0.35f`。

## 4. Runtime 迁移

`AudioArbiterRuntime.AudioArbiterComponent` 已改为：

- Provider 注册后只投影 `Kind + LowHealthCrossDownThreshold` descriptor；
- fight start 调用 `ResetFight`；
- fight status seed 调用 `Seed`；
- HP Hook observation 调用 `Observe`；
- 使用 observation decision 的 previous ratio 调用 Request Factory；
- 提交前调用 `IsNoProviderSuppressed` 与 `ShouldAttempt`；
- Resolve 无结果时调用 `RememberNoProvider`；
- 播放请求成功后调用 `MarkAnnounced`。

Component 已移除：

- `lowHealthAnnounced`；
- `lastHpRatioByStatus`；
- `lowHealthNoProviderUntil`；
- `LowHealthProviderIndex` 与 dirty flag；
- recovery、threshold crossing、legacy fallback 方法；
- no-provider key、记忆和过期方法；
- LowHealth 三个策略常量。

Runtime 从第十七轮的 1289 行降至 1074 行；Coordinator 为 308 行。相较拆分前的 1596 行，Runtime 已减少 522 行。

## 5. 行为测试

`AudioArbiterShared.Tests` 从 360 项增加到 393 项，新增 33 项断言，覆盖：

- 默认 cooldown、recovery margin 和 legacy threshold；
- 首次 seed、相等 HP 和普通下降；
- 无 Provider 时拒绝请求；
- unknown Provider 的 legacy crossing；
- 显式 Provider threshold crossing；
- 非 LowHealth Provider 隔离；
- 混合 threshold/unthresholded Provider；
- announced 去重及大小写不敏感；
- 未达到 recovery margin 时继续抑制；
- 达到 recovery threshold 后重新开放；
- role/career 缺失；
- cooldown 即时生效、边界过期和 ratio bucket 隔离；
- Provider 刷新清除 cooldown；
- fight reset 清理临时状态并保留 Provider 配置。

测试过程中确认并保留了既有浮点语义：`0.3f + 0.05f` 略高于精确 `0.35f`，因此恢复测试使用 `0.36f`，没有在重构中调整算法或阈值。

## 6. 架构护栏

共享架构门禁和 AuraTools 跨消费者检查新增约束：

- Coordinator 必须保留显式 observation decision；
- Provider 阈值索引、no-provider cooldown 与 fight reset 必须由 Coordinator 管理；
- Coordinator 不得访问 Unity/Witch、Hook、游戏对象、Provider handle、Network 或 Playback；
- Coordinator 不得读取 `Time`、`DateTime.UtcNow` 或生成 Guid；
- Component 必须委派 Configure、Observe、Remember 和 MarkAnnounced；
- Component 不得重新出现旧 LowHealth 状态容器、ProviderIndex、恢复方法或 cooldown key。

共享兼容基线已加入 Coordinator 的类型和方法锚点。

## 7. 兼容性

本轮没有修改：

- `CurrentBuildId = audio-arbiter-2026-07-11-v8`；
- ProtocolVersion 6 与 minimum protocol 6；
- `SoundPlaybackRequest` 公共形状；
- Provider identity、Manifest schema、RPC payload 或网络 authority；
- Career、Combat、Buff、Vocal、Battle 的请求语义；
- Component 的播放、替换和 Coroutine 所有权。

新增类型均为 internal，1228 项公共 API 基线保持一致。

## 8. 完整验证结果

- AudioArbiterShared：393 项断言通过；
- AuraCgShared：153 项断言通过；
- AuraSharedCore：92 项断言通过；
- Aura.Shared：1228 项公共 API 兼容基线通过；
- AuraDirector：20 项断言通过；
- AuraToolsExp：635 项断言通过；
- Terrias：架构检查、282 项 C# 断言与内容验证通过；
- shared write、content/tool/shared、RPC authority 与架构门禁通过；
- Terrias、SanGuoShaExp、AuraToolsExp 三个消费者均以 0 警告、0 错误构建；
- 完整共享发布矩阵与 DLL 打包检查通过；
- 构建产物与三个打包副本 SHA-256 一致：`A3FFF8EF106D9F6C39B36C837AE1B7E69D48B030A29DC0972EFB2E2323CFDFDC`。

## 9. 下一轮建议

下一轮进入第四阶段，拆出 `AudioHookAdapter`：

1. 使用 `AudioHookCatalog` 驱动全部 Hook 注册；
2. Adapter 集中持有 `AuraSharedHooks` 和 `AuraCombatActionRouter` 注册句柄；
3. Component 只提供命名处理入口，不再直接调用 `AddMethodHookBefore/After`；
4. 移除未注册的 legacy `OnActionAnimationBefore`；
5. 明确初始化幂等、注册失败隔离和生命周期释放策略；
6. 保持 19 个 Hook 定义、执行阶段和处理顺序不变。
