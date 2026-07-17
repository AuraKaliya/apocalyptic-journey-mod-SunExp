# AudioArbiter Hook Catalog 与 Request Factory 第十六轮开发记录

> 日期：2026-07-18
>
> 前置工作：第十一至十五轮已拆出 Audio Contracts、Manifest、Provider、Presentation、Network、Unity Playback 与 File Loader
>
> 范围：Hook 接入点基线、纯观察模型、请求工厂、黄金字段测试、架构与发布护栏

## 1. 本轮目标

本轮是 Hook Adapter 重构的第一阶段，先在不切换生产 Hook 的情况下冻结现状：

1. 把当前 19 个 Hook 接入点、目标和 Before/After 类型写入纯 Catalog；
2. 建立不依赖 Witch/Unity 的 Hook observation DTO；
3. 将 Career、Combat、Buff、Vocal、LowHealth、Battle 的请求字段映射集中到纯 Factory；
4. 用行为测试锁定 Combat 双请求顺序和全部字段；
5. 保持 Runtime、Hook 注册顺序、公开 API 和协议不变。

## 2. AudioHookCatalog

新增 `AudioHookCatalog.cs`，包含 19 个稳定定义：

- 2 个 Before Hook：`Fight_Start.Init`、`EffectSound.Start`；
- 16 个 After Hook；
- 1 个 `AuraCombatActionRouter` Before 路由；
- 6 个 `ScriptExecutor` HP Hook；
- 2 个 `StatusManager` HP setter Hook；
- Win/Escape 两个 battle-completed Hook。

每项包含稳定 `HandlerId`、目标字符串和 `AudioHookRegistrationKind`。Catalog 不引用 `ModConfig`、`ModHookContext` 或游戏对象。

架构门禁要求定义数量必须恰好为 19，并逐项把 18 个唯一 target 与当前 Component 基线对照。后续迁移到 Hook Adapter 时，漏掉一个 Hook 会直接失败。

## 3. AudioHookModels

新增 `AudioHookModels.cs`，提供纯观察模型：

- `AudioCareerObservation`；
- `AudioCombatActionObservation`；
- `AudioBuffObservation`；
- `AudioVocalObservation`；
- `AudioStatusSnapshot`；
- `AudioBattleObservation`；
- `AudioNarrationObservation`。

这些模型只保留字符串、数值、布尔值和数组，不保存 `StatusManager`、`IDataConfig`、`IScriptExecutor`、`ModHookContext` 或 Unity 对象。

`AudioStatusSnapshot` 已固定后续 HP reader 的输出形状：status id、role、career、HP、MaxHP、ratio、local owner 和 source。

## 4. AudioRequestFactory

新增 `AudioRequestFactory.cs`，集中定义：

- `CreateCareerSelected`；
- `CreateCombatActionBatch`；
- `CreateBuffApplied`；
- `CreateVocalState`；
- `CreateLowHealth`；
- `CreateBattleCompleted`。

Combat batch 固定返回 `CardUse` 和 `SkillVoice` 两个请求：

- CardUse 接收 Network Runtime 生成的 play id；
- SkillVoice 不复制 CardUse 的权威 event id；
- 两者保持相同 card、career、role、status、effect、action 和 source。

Factory 不读取时钟、不生成 Guid、不访问 Player/Fight/Role 单例，也不处理网络、Provider 或播放。

## 5. 行为基线

`AudioArbiterShared.Tests` 从 292 项增至 360 项，新增 68 项断言：

- Catalog 总数、唯一 HandlerId、Before/After/Combat 数量；
- Fight Start 双阶段；
- Script HP 和 Status setter 数量；
- Combat、Effect、Vocal、Win、Escape 目标与阶段；
- CareerSelected 的 career/role/source；
- CardUse 和 SkillVoice 的事件身份与完整字段；
- BuffApplied 继续以当前 Career 作为 Role；
- VocalState 的 state/career/role/status；
- LowHealth 的 HP、ratio、owner 和 role fallback；
- BattleCompleted 的 result/career/role/source；
- Narration observation 的空数组默认值。

当前生产 Runtime 尚未委派给 Factory，这是刻意的阶段边界。下一轮 Context Mapper 迁移时，将逐类替换现有内联请求，并由这些黄金断言验证字段不变。

## 6. 架构门禁

新增约束：

- Hook Catalog 不得依赖 Unity、Witch、ModConfig 或 ModHookContext；
- observation DTO 不得保存原始游戏对象；
- Request Factory 不得依赖 Unity/Witch、Manager、Time、Guid、Hook 或 AudioClip；
- Catalog 必须保留准确的 19 项定义；
- Catalog 与当前 Runtime 必须同时包含全部 18 个唯一 Hook target；
- 共享兼容基线必须包含 Catalog、Combat/Status observation 和 Factory 方法；
- AuraTools 跨消费者测试必须识别这些新边界。

## 7. 兼容性

本轮未修改：

- `AudioArbiterRuntime.cs` 的生产 Hook 注册；
- Hook 执行顺序和同步行为；
- 全局组件类型名；
- `CurrentBuildId = audio-arbiter-2026-07-11-v8`；
- ProtocolVersion 6 与 minimum protocol；
- RPC、Manifest、Provider 和 `SoundPlaybackRequest` 公开形状。

新增类型均为 internal，1228 项公共 API 基线保持一致，无需提升 BuildId 或 ProtocolVersion。

## 8. 完整验证结果

- AudioArbiterShared：360 项断言通过；
- AuraCgShared：153 项断言通过；
- AuraSharedCore：92 项断言通过；
- Aura.Shared：1228 项公共 API 兼容基线通过；
- AuraDirector：20 项断言通过；
- AuraToolsExp：635 项断言通过；
- SunExp：架构检查与 282 项 C# 断言通过；
- shared write、content/tool/shared、RPC authority 与架构门禁通过；
- SunExp、SanGuoShaExp、AuraToolsExp 三个消费者均以 0 警告、0 错误构建；
- 完整共享发布矩阵与 DLL 打包检查通过；
- 构建产物与三个打包副本 SHA-256 一致：`20C5482C0119F8F6CA6900CBACFF65DBAC09F7CC1D1D18ED096E4F3E03BA47C0`。

## 9. 下一轮建议

下一轮进入第二阶段：

1. 新增 `AudioGameStateReader`，集中读取 Career、DataConfig、Status id/role、HP 与 local ownership；
2. 新增 `AudioHookContextMapper`，把 `ModHookContext` 和 `AuraCombatActionContext` 转为本轮 observation DTO；
3. 先迁移 Career/Battle，再迁移 Buff/Vocal、Combat，最后迁移 HP；
4. Component 暂时继续注册 Hook，只把 context 解析和请求创建委派出去；
5. 等字段 parity 全部通过后，再单独切换 Hook Adapter 生命周期。
