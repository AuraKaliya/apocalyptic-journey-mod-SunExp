# AudioArbiter 第五阶段收口审查与网络权威边界迁移（第二十轮开发记录）

> 日期：2026-07-18
>
> 前置工作：第十六至十九轮已完成 Hook Catalog、观察模型、请求工厂、GameState Reader、Context Mapper、LowHealth Coordinator 与 Hook Adapter 迁移
>
> 范围：Runtime 剩余职责审查、RPC authority 初始化迁移、初始化步骤隔离、测试边界去固定文件化与完整发布闭环

## 1. 本轮结论

本轮完成 AudioArbiter 拆分方案第五阶段的收口开发。审查确认 `AudioArbiterComponent` 中仍有一项属于网络适配边界的职责：RPC 接收权威注册及服务端发送者绑定。该职责现已迁移到 `AudioNetworkRuntime`。

Component 当前保留的职责为：

1. 保存 owner 与组装各边界对象；
2. 按命名步骤启动网络权威和 Hook Adapter；
3. 接收已经映射的观察结果并编排请求、解析、播放与替换协调；
4. 在销毁时释放 Hook Adapter。

本轮未继续机械拆分播放主流程。当前剩余逻辑存在明显的编排内聚性，若没有新的行为模型或替换协调需求，继续按行数移动只会制造跨文件跳转，不能形成更清晰的职责边界。

## 2. RPC authority 迁入 AudioNetworkRuntime

`AudioNetworkRuntime` 新增 `RegisterAuthority`，统一负责：

- 调用 `AuraRpcAuthorityRuntime.Register`；
- 声明 `IAudioArbiterServerBoundRpcCommand` 接收范围；
- 将可信服务端 sender 绑定到命令；
- 接收 Component 提供的 info/warn 诊断回调。

`AudioArbiterComponent` 不再直接引用 `AuraRpcAuthorityRuntime.Register` 或服务端命令绑定接口。网络发送、远端接收、播放 claim、sender ownership 校验和 authority 初始化因此全部归入同一网络边界。

## 3. 初始化步骤隔离

`InitializeOwner` 在构造 `AudioHookAdapter` 后，通过 `AuraSharedHooks.RunStep` 启动两个独立步骤：

- `audio-rpc-authority`：调用 `networkRuntime.RegisterAuthority`；
- `audio-hooks`：调用 `hookAdapter.Register`。

任一步骤失败都会由 `OnInitializationStepFailed` 输出带步骤名的诊断，另一项仍可继续执行。执行顺序保持为先注册 RPC authority、后注册 Hook，与迁移前一致。

Component 仍以 `hookAdapter != null` 作为一次性初始化边界，避免重复注册 authority 或 Hook；销毁时继续释放 Adapter 的全部订阅。

## 4. 测试边界去固定 Runtime 文件化

`AuraToolsExp-Dev.Tests` 的 Audio 集成断言不再读取固定的 `AudioArbiterRuntime.cs` 文件。消费者测试现在只验证专用边界文件暴露的结构契约：

- `AudioNetworkRuntime` 拥有 authority 注册、服务端展示应用和 RPC 发送职责；
- Provider、Hook、LowHealth 等职责分别位于已有专用边界。

必须检查 Component 委派关系和禁止职责回流的源码规则，集中保留在：

- `Test-SharedArchitectureGuidelines.ps1`；
- `Test-NetworkRpcAuthority.ps1`。

这使后续 Component 改名、目录分组或继续拆文件时，不会被跨消费者的固定 Runtime 文件读取阻塞，同时没有降低共享架构门禁强度。

## 5. 护栏更新

共享架构门禁新增约束：

- `AudioNetworkRuntime` 必须提供 `RegisterAuthority` 并委派到共享 authority runtime；
- Component 必须调用 `networkRuntime.RegisterAuthority`；
- Component 必须以 `audio-rpc-authority` 和 `audio-hooks` 两个命名步骤初始化；
- Component 禁止重新出现直接 authority 注册或服务端 RPC 命令绑定职责。

RPC authority 专项门禁同步验证迁移后的正向委派和 Component 负向约束。共享兼容基线新增网络 authority 边界锚点；公共 API 条目仍为 1228 项。

## 6. 兼容性

本轮没有修改：

- 19 个 Hook 定义、阶段与 callback kind；
- Hook Adapter 的 routed 注册和释放语义；
- Career、Combat、Buff、Vocal、LowHealth、Battle 请求字段；
- Provider identity、Manifest、解析、播放、替换或冷却策略；
- RPC 协议版本、命令形状、sender ownership 或播放 claim 规则；
- `CurrentBuildId = audio-arbiter-2026-07-11-v8`；
- `ProtocolVersion = 6` 与 minimum protocol 6；
- 公共 API。

本轮是 authority 初始化代码的边界迁移，网络行为和注册顺序保持不变。

## 7. 完整验证结果

- AuraSharedCore：92 项断言通过；
- AuraCgShared：153 项断言通过；
- AudioArbiterShared：401 项断言通过；
- AuraDirector：20 项断言通过；
- AuraToolsExp：635 项断言通过；
- Aura.Shared：1228 项公共 API 兼容基线通过；
- shared write、content/tool/shared、RPC authority 与架构门禁通过；
- SunExp：架构检查、282 项 C# 断言与内容验证通过；
- SunExp、SanGuoShaExp、AuraToolsExp 三个消费者均以 0 警告、0 错误构建；
- 完整共享发布矩阵与 DLL 打包检查通过；
- 构建产物及三个打包副本 SHA-256 一致：`9613BD499416481D98BF5724D4C4E6BA576B8B4C3ABED799D9BA91264B2007B9`。

## 8. 后续建议

AudioArbiter 当前既定的 Hook Adapter、请求映射和游戏对象读取拆分方案已经闭环。下一轮不建议继续以 Runtime 行数为唯一目标拆分；更合理的选择是：

1. 先提交第十六至二十轮形成的完整 Audio Hook/读取边界；
2. 结合实际变更频率决定是否进行 `AudioArbiterShared` 目录分组整理；
3. 若继续复杂模块路线，转入 `BattleBgmArbiterShared` 的 Manifest、Provider Resolver、Playback Service、Presentation Policy、Network Runtime 与 Hook Adapter 拆分；
4. 目录迁移时保持类型和公共 API 不变，并优先调整架构门禁路径，避免重新引入消费者固定源码文件断言。
