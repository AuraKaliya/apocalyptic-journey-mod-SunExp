# AuraCg Provider 与注册请求解析第十轮开发记录

> 日期：2026-07-18
> 前置工作：第六至九轮完成 Playback Coordinator、媒体 Repository、Overlay Presenter 与 Network Runtime 拆分
> 范围：Provider 注册/反射调用、注册 CG 身份解析、本地激活、资源解析、测试与发布护栏

## 1. 本轮目标

关闭 AuraCg 重构的最后两项明确职责缺口：

1. 从 `SkillCgArbiterComponent` 提取 Provider Coordinator；
2. 从 `AuraCgRuntime.cs` 提取 Registered Request Resolver；
3. 保持公共 API、全局组件类型名、BuildId、协议版本、RPC 结构及消费者行为不变；
4. 让新增策略模块保持无 Unity/Witch 依赖并可独立测试；
5. 用源码门禁防止 Provider 反射、网络注册解析重新回流组件。

## 2. Provider Coordinator

新增 `AuraCgProviderCoordinator.cs`，集中负责：

- 读取 ProviderId、OwnerModId 与 Priority；
- Owner 缺失时保持原有程序集名回退；
- 生成并保留 owner-qualified provider identity；
- 相同 qualified identity 的注册替换；
- Provider 优先级和稳定标识排序；
- 反射调用公开 `BuildRequests`；
- 将异构返回对象转换为 `SkillCgRequest`；
- 按 action sequence、priority、qualified provider id 对请求作确定性排序；
- 单个 Provider 失败时隔离异常并继续处理其他 Provider。

Coordinator 通过请求转换委托接入 `SkillCgRequest.FromObject`，自身不依赖 Unity、Witch 或组件生命周期。组件只接收结构化注册结果和构建失败，再沿用原有日志键与消息语义。

## 3. Registered Request Resolver

新增 `AuraCgRegisteredRequestResolver.cs`，集中负责：

- 普通注册请求的 kind、role、card 与 consumer activation 匹配；
- registry owner/cg identity 查找；
- 网络 ProviderId 与 `ownerModId.SkillCG.cgId` 的严格一致性校验；
- host 校验不应用 recipient-local activation；
- recipient 解析应用本机 effective activation；
- 本地 image resource/path 解析；
- image、sequence 与 bundle 媒体存在性检查；
- `SkillCgRequest` 构造及 issuer/playId 网络身份回填。

Resolver 通过注册表、激活状态、路径解析和时钟委托接入运行时环境。网络仍只传注册身份与 action/session identity，不传媒体路径、资源正文或表现参数。

## 4. Runtime 收敛结果

`AuraCgRuntime.cs` 从第九轮约 1597 行降至 1379 行。`SkillCgArbiterComponent` 不再：

- 声明或排序 ProviderHandle 列表；
- 反射调用 Provider `BuildRequests`；
- 执行 Provider 去重和请求排序；
- 校验网络 registry/provider/card identity；
- 区分 host 与 recipient activation；
- 检查注册媒体存在性；
- 构造网络注册请求。

组件目前保留的核心职责是：初始化各协作者、Unity `Update`、预加载 Coroutine 启动、播放 Coroutine 编排、清理入口和公共反射适配方法。

新增模块规模：

- `AuraCgProviderCoordinator.cs`：291 行；
- `AuraCgRegisteredRequestResolver.cs`：155 行；
- `AuraCgRuntime.cs`：1379 行。

## 5. 测试与架构护栏

`AuraCgShared.Tests` 从 140 项增加到 153 项，新增覆盖：

- null 与空 ProviderId 注册拒绝；
- owner-qualified identity 替换；
- action/priority 确定性排序；
- Provider 反射失败隔离；
- host 忽略 recipient-local activation；
- recipient 应用本机 activation；
- ProviderId 替换攻击拒绝；
- 注册网络请求的时钟、issuer 与 playId 投影；
- bundle 媒体本地解析语义。

共享架构门禁新增要求：

- Provider 反射与排序必须留在 `AuraCgProviderCoordinator`；
- 注册身份、激活和本地资源校验必须留在 `AuraCgRegisteredRequestResolver`；
- 两个模块必须无 Unity/Witch 依赖；
- Component 不得重新出现 ProviderHandle 列表、`BuildRequests` 反射或旧注册网络解析方法；
- Network Runtime 必须继续通过 resolver 委托区分 host/recipient 激活语义。

`Test-NetworkRpcAuthority.ps1` 与共享兼容基线已同步到新边界，避免固定旧方法名阻碍后续文件拆分。

## 6. 兼容性

本轮未修改：

- `AuraCg.Shared.SkillCgArbiterRuntime+SkillCgArbiterComponent` 完整类型名；
- `CurrentBuildId`、`CurrentProtocolVersion` 与 minimum protocol；
- 公共方法、属性、事件、字段和序列化类型；
- Provider identity 语义与排序规则；
- RPC 命令、sender authority、payload 限制和 duplicate suppression；
- 媒体缓存、Overlay、播放时序与预加载预算。

因此无需协议或 BuildId 提升。

## 7. 完整验证结果

- AuraCg：153 项断言通过；
- AuraSharedCore：92 项断言通过；
- Aura.Shared 公共 API：1228 项兼容基线通过；
- AuraDirector：20 项断言通过；
- AuraToolsExp：632 项断言通过；
- SunExp：架构检查及 282 项 C# 断言通过；
- shared write、content/tool/shared、RPC authority 与架构边界门禁通过；
- SunExp、SanGuoShaExp、AuraToolsExp 三个消费者均以 0 警告、0 错误构建；
- 共享发布矩阵与 DLL 打包检查通过；
- 构建产物和三个打包副本 SHA-256 一致：`8AC735F52C550BFE70F3EBE4EC72790F8CC4EE093A51CB403A0C23C6BC1372B2`。

## 8. 下一轮建议

AuraCg 原计划中的 Playback、媒体、Overlay、Network、Provider 与注册请求解析边界已经全部形成，并由测试和架构门禁保护。下一轮应按既定顺序进入 `AudioArbiterShared`：

1. 先盘点 Manifest、Provider Resolver、Playback Service、Presentation Policy、Network Runtime 与 Hook Adapter 的现有耦合；
2. 优先提取无 Unity/Witch 依赖的 manifest/query/policy 与 provider arbitration；
3. 再拆网络会话和播放服务；
4. 最后把 Unity/Witch hook、AudioSource 与原生音效替换留在 adapter；
5. 每轮同步兼容基线、消费者构建和打包哈希验证。
