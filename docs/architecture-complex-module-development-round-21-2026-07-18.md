# ConfigModels 领域拆分与兼容护栏（第二十一轮开发记录）

> 日期：2026-07-18
>
> 范围：AuraTools 配置 DTO 领域拆分、测试工程迁移、序列化兼容验证与架构护栏更新
>
> 不包含：BattleBgm 拆分，以及 SolarMemory、DamageMeter、StarterDeck Runtime 的职责迁移

## 1. 本轮结论

本轮完成复杂模块路线中 ConfigModels 的第一轮完整开发。原 `AuraToolsConfigModels.cs` 从同时承载全部配置 DTO 的约 1250 行文件，收缩为只负责根配置索引和模块文件引用的 52 行边界。

配置类型按实际 JSON 文件和加载生命周期拆为五个领域文件：

- `AuraToolsAudioSettings.cs`：Audio、BattleBgm 和 CardUse 音频配置；
- `AuraToolsMatchExperienceSettings.cs`：StarterDeck、SafeBox、ModSync、Feast、DamageMeter 和 CardRefresh 配置；
- `AuraToolsSkillCgSettings.cs`：Skill CG 角色、规则和展示配置；
- `AuraToolsSkinSettings.cs`：皮肤模块配置；
- `AuraToolsLoggingSettings.cs`：日志、级别和堆栈策略配置。

此次仅移动类型的物理归属，没有更改命名空间、类型名、可见性、属性名、JSON 字段名、默认值或 `Normalize` 规则。

## 2. 根配置边界

`AuraToolsConfigModels.cs` 现在只保留：

1. `AuraToolsRootConfig`；
2. `ModuleFileConfig`；
3. 根配置对 Audio、MatchExperience、SkillCg、Skin、Logging 五个独立 JSON 文件的引用和缺省恢复。

根文件不再了解各模块的内部 DTO。这使 `AuraToolsConfigService` 仍可承担加载与发布编排，而配置模型的修改可以限制在对应功能域内。

## 3. 测试与源码护栏迁移

`AuraToolsExp-Dev.Tests` 已从只编译固定的 `AuraToolsConfigModels.cs`，迁移为同时编译根模型和 `AuraTools*Settings.cs` 领域模型。

新增序列化兼容断言覆盖：

- 根配置从空模块引用恢复默认文件名；
- 根配置既有 JSON 属性名往返保持；
- Audio 旧资源路径迁移和空 CardUse 配置恢复；
- MatchExperience 旧 schema 升级和嵌套配置补全；
- Skin 内置皮肤自动安装策略保持启用。

`Test-MainSharedFramework.ps1` 不再把单一文件当成全部配置模型的载体。新护栏会检查五个领域文件存在、各自拥有对应根类型，并禁止领域 Settings 类型回流根配置文件。

`Test-NetworkRpcAuthority.ps1` 的日志权限检查改为读取 `AuraToolsLoggingSettings.cs`，使该检查绑定语义边界而非旧文件布局。

## 4. 兼容性约束

本轮保持以下契约不变：

- `AuraToolsExp.Dll.Config` 命名空间；
- 所有既有公开类型和成员；
- `JsonProperty` 名称和 JSON 文件名；
- schema 最低版本及迁移分支；
- StarterDeck、Skin、DamageMeter、Logging 和 SkillCg 的归一化规则；
- `AuraToolsConfigService` 的调用方式与消费者访问入口；
- 共享 DLL 和跨 MOD 协议。

## 5. 本轮验证

已通过：

- AuraSharedCore：92 项断言；
- AuraCgShared：153 项断言；
- AudioArbiterShared：401 项断言；
- AuraDirector：20 项断言；
- AuraToolsExp：640 项断言；
- Aura.Shared：1228 项公共 API 兼容基线；
- shared write、共享架构、content/tool/shared 和 Network RPC authority 护栏；
- Terrias 架构检查、282 项 C# 断言和源码检查；
- Terrias、SanGuoShaExp、AuraToolsExp 三个消费者 Release 构建：0 警告、0 错误；
- Aura.Shared DLL 打包与三个副本哈希一致性检查；
- 完整 AuraShared release gate。

最终构建产物与三个打包副本的 SHA-256 均为 `4AEAF5F6005F9107E317CEA7DCFB23069AD8D79A9F153E1E657FBC971C981A8A`。

独立首次运行 `Test-MainSharedFramework.ps1` 时，AuraSharedCore 测试项目编译报告一条既有的 `AuraChatCatalogCrypto.cs` 可空性警告；后续正式 release gate 构建为 0 警告、0 错误。本轮没有修改该文件。

## 6. 后续方向

ConfigModels 已达到“根文件只负责索引，领域 DTO 随功能边界演进”的目标。下一轮建议进入 SolarMemory，但先建立状态转换、固定节点内容隔离和同步修复的纯逻辑护栏，再迁移 Runtime 中对应职责。

DamageMeter 和 StarterDeck 继续按之前讨论的顺序推进：先拆纯模型/策略与会话对象，最后移动 Unity 展示和 Hook 适配代码，避免在缺少行为基线时直接按行数切文件。
