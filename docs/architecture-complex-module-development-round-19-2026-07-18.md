# AudioArbiter Hook Adapter 第十九轮开发记录

> 日期：2026-07-18
>
> 前置工作：第十六至十八轮已建立 Catalog、观察/请求边界、GameState Reader、Context Mapper 与 LowHealth Coordinator
>
> 范围：Catalog 回调绑定、Hook Adapter、routed 注册、生命周期释放、legacy 清理与发布护栏

## 1. 本轮目标

本轮完成 Hook Adapter 重构的第四阶段：

1. 让 `AudioHookCatalog` 同时声明 target、注册阶段和 callback kind；
2. 新增 `AudioHookAdapter`，统一注册全部 19 个 Hook；
3. Before/After Hook 统一通过 `AuraHookRegistry` 的 routed API 注册；
4. Combat Hook 继续通过 `AuraCombatActionRouter` 注册；
5. Adapter 持有并释放全部订阅句柄；
6. Component 不再直接依赖 Hook 注册 API；
7. 删除从未注册的 legacy combat handler 及其专用读取路径。

## 2. Catalog CallbackKind

`AudioHookCatalog.cs` 新增纯枚举 `AudioHookCallbackKind`，包含 13 种处理入口：

- CareerSessionReset；
- FightStartBefore / FightStartAfter；
- CareerDetailShown；
- CombatActionBefore；
- NativeEffectBefore；
- BuffApplied；
- VocalState；
- NarrationPlay；
- PotentialHpChanged；
- StatusHpChanged；
- FightWin / FightEscape。

每个 `AudioHookDefinition` 现在包含：

- `HandlerId`；
- `Target`；
- `RegistrationKind`；
- `CallbackKind`。

6 个 ScriptExecutor HP Hook 共享 `PotentialHpChanged`，2 个 Status setter Hook 共享 `StatusHpChanged`。Catalog 仍保持 19 个定义、18 个唯一 target、2 个 Before、16 个 After 和 1 个 CombatActionBefore。

## 3. AudioHookAdapter

新增 `AudioHookAdapter.cs`，包含：

- `AudioHookCallbacks`：Component 命名处理入口集合；
- `AudioHookAdapter`：Catalog 驱动的注册与生命周期适配器。

Adapter 的职责限定为：

- 遍历 `AudioHookCatalog.All`；
- 根据 `CallbackKind` 选择命名回调；
- 根据 `RegistrationKind` 选择 BeforeRouted、AfterRouted 或 Combat Router；
- 单项隔离注册异常，继续处理后续定义；
- 输出 owner、definition、registered 和 skipped 诊断；
- 幂等处理重复 `Register`；
- 在 `Dispose` 中逆序释放 Combat subscriptions，并释放 `AuraHookRegistry`。

Adapter 不读取游戏状态，不创建播放请求，不访问 Network、Provider、Playback 或 Coroutine。

## 4. Component 生命周期迁移

`AudioArbiterComponent.InitializeOwner` 当前只负责：

1. 保存 owner；
2. 注册 RPC authority；
3. 构造 `AudioHookCallbacks`；
4. 构造并启动 `AudioHookAdapter`。

Component 已移除：

- `hooksRegistered` 标志；
- 18 个硬编码 Hook target 注册调用；
- 直接 `AuraCombatActionRouter.RegisterBefore`；
- `RegisterBefore` / `RegisterAfter` 辅助方法；
- `AddMethodHookBefore` / `AddMethodHookAfter` 依赖；
- 未注册的 `OnActionAnimationBefore`。

新增 `OnDestroy`，负责调用 `hookAdapter.Dispose()`。

Runtime 从第十八轮的 1074 行降至 1037 行。`AudioHookAdapter` 为 192 行，Catalog 为 79 行。

## 5. Legacy 路径清理

由于生产 Combat Hook 已稳定使用 `AuraCombatActionRouter`，本轮同步移除：

- `AudioHookContextMapper.MapLegacyCombatAction`；
- `AudioGameStateReader.IsCardScriptExecutor`；
- `ReadExecutorDataId`；
- `ReadExecutorDataValue`；
- `ReadExecutorOwnerInstanceId`。

当前 Combat 路径只有一条：

`AuraCombatActionRouter -> AudioHookAdapter -> OnCombatActionBefore -> ContextMapper -> Network play id -> RequestFactory`

因此不会再存在一个未注册但可能被误恢复的第二套 CardUse/SkillVoice 请求路径。

## 6. 行为测试与护栏

`AudioArbiterShared.Tests` 从 393 项增加到 401 项，新增断言锁定：

- 13 种 callback kind；
- 6 个 Script HP Hook 共享同一 callback kind；
- 2 个 Status HP Hook 共享同一 callback kind；
- Combat、NativeEffect、Vocal、Win、Escape 的 callback kind。

共享架构门禁与 AuraTools 跨消费者检查新增约束：

- Adapter 必须遍历 `AudioHookCatalog.All`；
- Before/After 必须使用 `AuraHookRegistry` routed API；
- Combat 必须使用 `AuraCombatActionRouter`；
- Adapter 必须实现 `IDisposable` 并释放订阅；
- Adapter 不得硬编码 18 个 target；
- Component 必须构造、启动和释放 Adapter；
- Component 不得出现 raw Hook API、注册辅助方法、注册标志或 legacy handler；
- Adapter 不得依赖游戏状态、请求工厂、Network、Provider 或 Playback。

共享兼容基线已加入 CallbackKind、Adapter、Catalog 遍历、Hook Registry 和 Combat Router 锚点。

## 7. 兼容性

本轮没有修改：

- 19 个 Hook 定义及其 Before/After 阶段；
- Combat 本地 owner 筛选和 event id；
- Career、Buff、Vocal、LowHealth、Battle 请求字段；
- Provider identity、Manifest、Network/RPC 或播放策略；
- `CurrentBuildId = audio-arbiter-2026-07-11-v8`；
- ProtocolVersion 6 与 minimum protocol 6；
- 公共 API。

注册实现从 Component 的 raw ModConfig Hook 切换为共享 routed Hook 基础设施；处理入口和执行阶段保持一致，并新增订阅释放能力。

## 8. 完整验证结果

- AudioArbiterShared：401 项断言通过；
- AuraCgShared：153 项断言通过；
- AuraSharedCore：92 项断言通过；
- Aura.Shared：1228 项公共 API 兼容基线通过；
- AuraDirector：20 项断言通过；
- AuraToolsExp：635 项断言通过；
- Terrias：架构检查、282 项 C# 断言与内容验证通过；
- shared write、content/tool/shared、RPC authority 与架构门禁通过；
- Terrias、SanGuoShaExp、AuraToolsExp 三个消费者均以 0 警告、0 错误构建；
- 完整共享发布矩阵与 DLL 打包检查通过；
- 构建产物与三个打包副本 SHA-256 一致：`B0227893B87C2037C922F90A8EA056C31A1BA61B0E2DC6DF294D323B6E788990`。

## 9. 下一轮建议

下一轮进入第五阶段的 AudioArbiter 收口审查：

1. 重新统计 Runtime 剩余职责与方法分布；
2. 检查 Component 是否只保留初始化、请求编排、播放/替换协调和委派；
3. 评估 RPC authority 初始化是否需要独立生命周期适配器；
4. 检查旧文档、门禁和固定源码断言是否可以迁移到行为/结构契约；
5. 执行完整公共 API、消费者、打包和哈希闭环；
6. 在职责稳定后决定是否进行 AudioArbiterShared 目录分组整理。
