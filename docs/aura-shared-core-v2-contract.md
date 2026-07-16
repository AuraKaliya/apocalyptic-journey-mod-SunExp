# AuraSharedCore v2 契约

> 当前校验日期：2026-07-13。本文以 `AuraSharedCore/*.cs`、
> `AuraSharedRuntime-Dev/Aura.Shared.csproj` 和 `tools/shared-release-matrix.json`
> 为准，描述当前可发布实现，而不是 2026-06 初版设计草案。

AuraSharedCore v2 是参与 Aura 共享生态的 MOD DLL 使用的无业务语义基础层。
产品 MOD 通过项目引用使用真实的 `Aura.Shared.dll`，并在发布包中携带哈希一致的
DLL；不得各自编译私有共享源码。首个兼容消费者创建持久化的
`AuraShared.Global`，后续消费者通过反射 JSON 协议复用该组件。

Core 负责共享路径、存储、资源注册、包事务、变更序列、诊断、通用 Hook、
生命周期、调度和网络安全基础；Audio、CG、Skin、Journey、Mode、StarterDeck 等领域
组件负责各自业务协议与仲裁；SunExp、AuraToolsExp 等消费者负责内容和工具语义。

## Compatibility（兼容性）

- `CurrentProtocolVersion`: 2
- `MinimumSupportedProtocolVersion`: 2
- `BuildId`: `aura-shared-core-v2-<assembly-mvid>`

运行时复用条件是：

1. 已存在组件的 `ProtocolVersion` 不低于本地最小支持版本；
2. 已存在组件的 `MinimumSupportedProtocolVersion` 不高于本地当前版本；
3. 稳定反射方法全部存在。

`BuildId` 是构建身份、诊断和发布矩阵锚点，不是运行时拒绝条件。协议区间和方法
形状兼容时，即使 `BuildId` 不同也应复用组件并记录警告。只有协议区间或必要方法
不兼容时，才为当前消费者禁用共享服务并记录错误；不得让无关游戏初始化崩溃。

`BuildId` 由实际加载程序集的 MVID 派生，因此实现产物变化后不再依赖人工更新常量。
改变反射方法、JSON 字段语义或兼容区间时，仍应同步评估并更新协议版本；MVID 只
标识构建身份，不代替协议版本。

## Stable Component Methods（稳定组件方法）

当前必要反射方法集为：

- `InitializeOwner(modConfig, ownerModId, options)`
- `RegisterResource(resource)`
- `RegisterManifestPath(ownerModId, manifestPath, baseDirectory)`
- `RegisterManifestJson(ownerModId, manifestJson, baseDirectory)`
- `GetResourcesJson(system)`
- `ReadStorageJson(requestJson)`
- `WriteStorageJson(requestJson)`
- `InstallResourceJson(requestJson)`
- `GetInstalledResourcesJson(system)`
- `GetChangesJson(sinceSequence)`
- `GetOwners()`

反射边界使用宽松的 `object` 参数和 JSON 字符串，避免消费者 DLL 之间传递私有 CLR
类型。业务代码应优先使用 `AuraSharedStorage`、`AuraSharedConfigStore`、
`AuraSharedPackageEngine` 和 `AuraSharedRegistry` 的类型化包装，不应直接拼装组件调用。

`GetChangesJson` 返回当前进程内、最多保留 256 条的有序变更记录，用于跨 DLL 轮询；
它不是持久化事件总线，也不能替代领域状态快照。

## Storage Request Template（存储请求模板）

```json
{
  "scope": "Owner",
  "system": "AuraTools",
  "ownerModId": "AuraToolsExp",
  "writerId": "AuraToolsExp",
  "authorityId": "AuraToolsExp",
  "fileName": "AudioSettings.json",
  "schemaVersion": 1,
  "expectedRevision": 3,
  "payloadJson": "{\"enabled\":true}",
  "createBackup": true
}
```

规则：

- `Shared` 文档只有一个 authority writer；已有 authority 不能被其他 writer 接管。
- `Owner` 文档只能由 `ownerModId` 对应 owner 写入。
- `Runtime` 文档是可重建状态，不是用户配置。
- `Registry` 文档位于 `Registries/<System>`，只应由共享注册/包协调器维护。
- 非负 `expectedRevision` 必须匹配当前 revision；冲突必须显式返回。
- 写入按文档 key 加进程内锁，再进入跨进程 mutex。
- JSON 通过 write-through 临时文件和原子替换写入；失败时保留或恢复原文件。

## Package Install Request Template（资源包安装请求模板）

```json
{
  "ownerModId": "SunExp",
  "system": "Audio",
  "logicalId": "SunExp.WuNa.VoicePack",
  "packageId": "SunExp.SharedResources",
  "packageVersion": 1,
  "kind": "Directory",
  "sourcePath": "D:/.../SunExp/SharedResources/Audio/WuNa",
  "destinationRelativePath": "Audio/SunExp/WuNa"
}
```

规则：

- 安装身份是规范化的 `system::logicalId`。
- 相同内容哈希合并来源，并更新该来源的最高 `packageVersion`。
- 不同内容只有在资源仅由同一 owner 持有且新版本更高时才能替换。
- 不同 owner 的不同内容，或多 owner 资源的隐式替换，必须返回冲突。
- `kind` 与规范目标路径不可在同一资源身份下悄然改变。
- 安装使用 staging、事务 journal、注册表提交、完成/回滚与启动恢复。

`SharedResources/package.json` 使用
`AuraSharedCore/Schemas/resource-package.schema.json`。当前 schema version 1 支持
`ownerModId`、`packageKind`、`capabilities`、`dependencies`，以及资源级
`targetRoleIds`、`tags`、`metadata`。包引擎仍只安装文件或目录，不解释领域语义。

## Adapter Manifest Shape（适配器清单形状）

领域适配器把自己的 manifest 转换为 Core 请求，例如：

```json
{
  "system": "Audio",
  "adapterVersion": 1,
  "ownerModId": "SunExp",
  "capabilities": ["PackageInstall", "RuntimeResolve"],
  "resources": [
    {
      "logicalId": "SunExp.WuNa.VoicePack",
      "kind": "Directory",
      "source": "Audio/WuNa",
      "destination": "Audio/SunExp/WuNa"
    }
  ]
}
```

适配器可以理解 Skin、Audio、CG、Log 或 Journey 语义，Core 不可以。领域共享仲裁器
在 Core 上建立自己的协议；例如 StarterDeck 的 profile priority、editability 和角色
归属属于 `StarterDeckArbiterShared`，不属于 Core。

## Core Capability Surface（核心能力面）

除稳定反射组件外，`Aura.Shared.dll` 当前还提供这些直接类型化的无语义基础：

| 类别 | 当前能力 | 约束 |
| --- | --- | --- |
| Hook/生命周期 | routed hook、Battle/Card/Combat router、session、step runner、operation ledger | 订阅必须可释放；单步失败不得中断无关步骤 |
| 主线程调度 | `AuraSharedFrameScheduler`、`AuraSharedFrameStepRunner` | Unity/Witch/Mirror/UI 工作只在主线程执行；遵守 phase、预算和分片 |
| 后台工作 | `AuraSharedBackgroundWorkScheduler` | 仅纯 CPU、文件 I/O 和不可变快照；按 owner 限流，完成回主线程 |
| 网络基础 | RPC sender/authority、authoritative sync、payload budget、secure envelope | 不携带 SunExp 内容语义；状态变化仍由领域权威验证 |
| 性能基础 | resource cache、object pool、combat card-zone snapshot | 容量有界；不得缓存业务所有权策略 |
| 通用工具 | identity、JSON、diagnostics、log store、feature switch | owner/domain identity 必须稳定且可诊断 |

后台调度不得修改进程级 CLR thread-pool 上限，也不得在 worker 线程访问 Unity 对象。
`AuraAuthoritativeSyncRuntime` 只处理 session、token、快照请求节流和去重，不决定任何
具体玩法状态是否有效。

## Ownership And Mutability（所有权与可变性）

- 每个注册项必须有稳定的 `ownerModId` 和 owner-qualified domain id。
- 内容 MOD 安装并注册自己拥有的资源；工具 MOD 可以读取注册表、注册工具自有扩展和
  保存本地覆盖，但不能改写外部 MOD 的注册源或伪造所有权。
- Core 只维护 identity、ownership、revision 和事务，不解释资源内容。
- Shared/Registry 写入必须通过 `AuraSharedStorage`、`AuraSharedConfigStore`、
  `AuraSharedPackageEngine` 或协调器入口；消费者不得直接写共享目录。
- Tool-owned runtime caches 若写入共享根目录，也必须通过
  `AuraSharedStorageCoordinator.ExecuteWrite` 使用稳定 lock key 协调并发。
- 共享缓存 payload 使用 `WriteTextAtomic`；cache metadata 也必须原子写入，不能让
  元数据先于主体提交而暴露半完成状态。

## Conflict And Candidate Policy（冲突与候选策略）

Core 只拒绝 identity、owner、目标路径或内容一致性冲突。候选是否满足场景、优先级
如何比较、fallback 如何选择，属于对应领域组件。适配器负责安装、桥接和委托，不能
把领域仲裁策略藏在某个消费者的适配层中。

provider identity 语义发生变化时必须更新对应共享组件的 `BuildId`；若还改变跨 DLL
请求或响应语义，则同时评估协议版本。provider id 必须 owner-qualified，远端请求在
owner/provider 不匹配时 fail closed。

## Resolution Priority（解析优先级）

通用配置优先级为：内容 owner 注册默认值 -> 工具随包默认值 -> 工具本地持久化覆盖。
工具覆盖决定本机的有效配置，但不回写外部 owner 的声明。领域候选的具体优先级、
冲突合并和 fallback 次序由 Audio/CG/Skin/Journey/StarterDeck 等领域协议分别定义，
Core 不提供一个跨领域的万能优先级。

## Operation Log（操作日志）

操作日志是追加写 JSONL：

```text
Logs/Operations/yyyyMMdd.jsonl
```

记录形状：

```json
{
  "timestampUtc": "2026-07-13T10:00:00Z",
  "operationId": "op",
  "transactionId": "tx",
  "ownerModId": "SunExp",
  "system": "Audio",
  "logicalId": "SunExp.WuNa.VoicePack",
  "kind": "InstallResource",
  "phase": "RegistryCommitted",
  "result": "Success",
  "revision": 0,
  "message": "Registry committed.",
  "elapsedMs": 18
}
```

`Transactions/<id>.json` 是中断恢复的事实来源。操作日志仅用于诊断与自动断言；日志
写入失败不得改变存储或安装结果。

## Lock Keys（锁键）

- 配置文档：`Config/<Scope>/<Owner>/<System>/<File>`
- 资源安装：`Resource/<System>/<LogicalId>`
- 注册表：`Registry/<System>`
- 恢复：`Transactions/Recovery`

资源安装必须按以下顺序获取锁：

```text
Resource -> Registry -> cross-process write mutex
```

不得在持有 Registry 锁时反向获取 Resource 锁。

## Release Gate（发布门禁）

顶层门禁为 `tools/Test-SharedReleaseGate.ps1`，由
`tools/shared-release-matrix.json` 驱动。当前覆盖：Core 契约、共享写入口扫描、架构
边界、内容/工具边界、网络 RPC authority、AuraTools 功能、主要消费者构建和共享 DLL
打包一致性。

`tools/Test-SharedDllPackaging.ps1` 校验：

- `SunExp`、`SanGuoShaExp`、`AuraToolsExp` 以及仍参与组合测试的共享运行时原型所打包的
  `Aura.Shared.dll`，都与共享构建产物 SHA-256 一致；
- 产品和测试消费者引用 `AuraSharedRuntime-Dev/Aura.Shared.csproj`；
- 消费者项目不私自链接共享源码。

共享源码变更后的发布顺序为：构建共享运行时与受影响消费者、刷新所有打包 DLL、运行
领域测试、运行 `Test-SharedDllPackaging.ps1`，最后关闭
`Test-SharedReleaseGate.ps1`。仅编译 SunExp 不能证明共享发布完成。

SunExp 当前接入全景见 `docs/SunExp/04-Aura共享层与核心层接入.md`；同步、authority、
payload 和去重的细化规则见
`.codex/skills/sunexp-shared-runtime-dev/references/sync-scenario-model.md`。
