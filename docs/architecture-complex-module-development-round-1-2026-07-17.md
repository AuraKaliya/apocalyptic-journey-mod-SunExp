# 复杂模块治理首轮开发记录

> 日期：2026-07-17  
> 对应评审：`architecture-complex-module-review-2026-07-16.md`  
> 范围：阶段 0“重构护栏”与阶段 1 的共享工程组织前置工作

## 1. 本轮目标

本轮不移动运行时状态，也不改变 CG、Audio、BGM 或 StarterDeck 的协议和行为。目标是先建立可以安全拆分巨型源文件的工程条件：

1. 冻结高风险共享模块的程序集公开契约。
2. 锁定跨 MOD 全局组件身份、RPC sender authority 和去重清理边界。
3. 解除共享工程和源码扫描对单个巨型文件名的依赖。
4. 把兼容性检查加入统一共享发布门禁。

## 2. 已完成实现

### 2.1 程序集公开 API 基线

新增 `AuraSharedCompatibility.Tests`。测试直接读取编译后的 `Aura.Shared.dll` CLR 元数据，不加载 Unity、Witch 或 Mirror 运行时依赖。

当前基线覆盖以下命名空间：

| 模块 | 命名空间 | 冻结内容 |
| --- | --- | --- |
| Aura CG | `AuraCg.Shared` | 公开类型、嵌套公开类型、方法、属性、事件、字段与常量 |
| Audio Arbiter | `AudioArbiter.Shared` | 同上 |
| Battle BGM Arbiter | `BattleBgmArbiter.Shared` | 同上 |
| StarterDeck Arbiter | `StarterDeckArbiter.Shared` | 同上 |

基线文件为 `tools/shared-runtime-compatibility-baseline.json`，首轮共记录 1228 个公开 API 条目。源码拆分可以改变文件位置和内部私有类型，但不能无意改变这些外部契约。

### 2.2 运行时身份与网络边界

元数据基线无法读取私有常量，因此门禁额外扫描目录内全部 C# 源文件，保持以下约束：

- `AuraCg.Global` 与 `SkillCgArbiterRuntime+SkillCgArbiterComponent` 不变；
- `AudioArbiter.Global` 与 `AudioArbiterRuntime+AudioArbiterComponent` 不变；
- `BattleBgmArbiter.Global` 与 `BattleBgmArbiterRuntime+BattleBgmArbiterComponent` 不变；
- CG 与 Audio 的 server-bound RPC 接口和 sender 绑定仍存在；
- CG 与 Audio 的播放去重集合仍有上限，并在战斗生命周期清理。

这组检查保护多 MOD 加载时通过完整类型名复用全局组件的兼容路径，也避免拆文件时丢失发送者授权和重复抑制。

### 2.3 共享工程多文件编译

`Aura.Shared.csproj` 已将以下编译入口从固定单文件改为目录级 `*.cs`：

- `AudioArbiterShared`；
- `BattleBgmArbiterShared`；
- `StarterDeckArbiterShared`。

兼容门禁同时禁止这些目录重新引入文件级 Compile 项。后续增加 Contracts、Network、Resolver、Playback 等源文件时，无须修改共享项目文件。

### 2.4 测试去文件名耦合

以下测试已改为按目录聚合源码，再验证契约和禁止项：

- `Test-AuraSharedCore.ps1`；
- `Test-SharedArchitectureGuidelines.ps1`；
- `Test-NetworkRpcAuthority.ps1`；
- `Test-SunExpCSharp.ps1`；
- `AuraToolsExp-Dev.Tests` 的 Audio 与 Aura CG 架构断言。

因此，把 RPC、resolver 或 component 的实现移动到同目录其他文件，不会因为旧文件名假设产生伪回归。

## 3. 发布门禁

`tools/shared-release-matrix.json` 新增 `shared-runtime-compatibility` 步骤，执行：

```powershell
pwsh -NoProfile -File tools/Test-SharedRuntimeCompatibility.ps1 -Configuration Release
```

只有在经过评审的公开契约变化中才能重新捕获基线：

```powershell
pwsh -NoProfile -File tools/Test-SharedRuntimeCompatibility.ps1 -Configuration Release -Capture
```

重新捕获不是修复测试的常规手段。提交前必须审查基线 diff；涉及跨版本语义变化时，还要同步判断是否升级 `BuildId` 或 `ProtocolVersion`。

## 4. 本轮验收结论

本轮保持所有目标运行时源码和协议常量不变，只修改编译组织、测试读取方式和发布门禁。当前结果满足后续机械拆分的前置条件：

- 允许同命名空间多文件组织；
- 能检测公共 API 与嵌套全局组件身份变化；
- 能继续检测 RPC authority、去重和生命周期清理边界；
- 统一发布门禁可以阻止兼容性回退。

本轮没有宣称复杂模块已经完成职责拆分。下一轮应在这些护栏下先拆 Aura CG 的 Contracts、Network、Registry Query 和 Unity Playback 边界，保持 `SkillCgArbiterRuntime` Facade 与嵌套组件完整类型名不变。
