# Audio/BGM Owner-Qualified Provider 修复方案

## 背景

在共享组件架构对齐后，`AudioArbiterShared`、`BattleBgmArbiterShared` 和 `AuraCgShared` 都开始使用 owner-qualified provider identity，以避免不同 MOD 注册同名 provider 时互相覆盖。

当前检查发现 Audio/BGM 仍有两个需要补齐的风险点：

1. Audio/BGM 的 provider identity 语义已经变化，但 `CurrentBuildId` 没有随之升级，可能导致新消费者复用旧全局 arbiter。
2. Audio 本地解析时已经选中了 owner-qualified provider，但联机 RPC 发送前又把 `ProviderId` 降回裸 ID，远端在多 MOD 同名 provider 场景下可能选错 provider。

本方案目标是把 Audio/BGM 的 owner-qualified provider 语义闭环，同时尽量保持现有公开 API 和 RPC payload 形状兼容。

## 修改目标

- 新版本消费者不能误复用旧版本全局 Audio/BGM arbiter。
- Audio 联机远端播放必须能根据 `OwnerModId + ProviderId` 确定性匹配 provider。
- 裸 `ProviderId` 仍保留为旧调用方式的兼容入口。
- 旧 RPC payload 形状尽量不变，避免引入不必要的跨版本破坏。
- 文档、静态规则、构建验证和 SunExp 验证同步更新。

## 非目标

- 不新增 Audio/BGM 公开协议字段。
- 不改变 `SoundPlaybackRequest` 的序列化字段形状。
- 不把 `CurrentProtocolVersion` 作为这次修复的主要兼容边界。
- 不在本轮处理 `AuraOnlineShared` 的 `ConfirmPlayerMessage` release gate 阻塞；该问题需要单独修复。

## 风险 1：BuildId 未升级

### 问题

全局共享 arbiter 会通过反射检查 `ProtocolVersion`、`MinimumSupportedProtocolVersion`、`BuildId` 和公开方法形状。Audio/BGM 这次改变的是 provider replacement、排序、匹配和 active provider 记录语义，公开方法形状没有变化。

如果 `CurrentBuildId` 不升级，新 DLL 可能接受旧全局 arbiter，并继续运行旧的裸 `ProviderId` 覆盖逻辑。这会让 owner-qualified provider identity 在实际游戏会话中失效。

### 修改方案

更新以下文件：

- `AudioArbiterShared/AudioArbiterRuntime.cs`
- `BattleBgmArbiterShared/BattleBgmArbiterRuntime.cs`

建议 BuildId：

```csharp
// AudioArbiterShared/AudioArbiterRuntime.cs
public const string CurrentBuildId = "audio-arbiter-2026-06-23-v5";

// BattleBgmArbiterShared/BattleBgmArbiterRuntime.cs
public const string CurrentBuildId = "battle-bgm-arbiter-2026-06-23-v4";
```

说明：

- Audio 继续沿用 `audio-arbiter-*` 命名。
- BGM 建议从泛化的 `shared-runtime-*` 改为 `battle-bgm-arbiter-*`，让日志和测试断言能明确定位组件。
- 不建议升级 `CurrentProtocolVersion`，因为本轮不改变公开方法或 payload 字段，只需要阻断旧全局组件复用。

### 测试同步

更新 `tools/Test-SunExpCSharp.ps1` 中的 BuildId 断言：

- Audio 断言改为 `audio-arbiter-2026-06-23-v5`。
- BGM 断言改为 `battle-bgm-arbiter-2026-06-23-v4`。

如果 `tools/Test-SharedArchitectureGuidelines.ps1` 中有 BuildId 文本规则，不需要绑定具体版本号，只需要继续检查共享运行时暴露 `CurrentBuildId` 和 `BuildId => CurrentBuildId`。

## 风险 2：Audio RPC 远端 provider 匹配不确定

### 问题

Audio 本地解析时，`Resolve` 已经把选中的 provider 写回请求：

```csharp
request.ProviderId = provider.QualifiedProviderId;
request.OwnerModId = provider.OwnerModId;
```

但发送 RPC 前，当前逻辑又会把 `ProviderId` 改回裸 ID：

```csharp
request.ProviderId = provider.ProviderId;
request.OwnerModId = provider.OwnerModId;
```

远端执行 RPC 后，`Resolve` 只使用 `request.ProviderId` 调用 `MatchesProviderId`。如果多个 MOD 都注册了同名裸 provider，远端可能按优先级或排序命中错误 MOD 的 provider。

### 设计选择

本方案建议保留 RPC payload 中的裸 `ProviderId`，并让新远端结合 `OwnerModId` 做确定性匹配。

不建议直接把 RPC `ProviderId` 改成 qualified id，原因是：

- 新远端可以识别 qualified id，但旧远端只认识裸 `ProviderId`。
- 直接发送 qualified id 会提高旧接收端播放失败风险。
- 保留裸 id 加 `OwnerModId` 严格匹配，可以在新版本之间解决歧义，同时最大限度保持旧 payload 兼容。

### 修改方案

#### 1. 扩展 provider 匹配方法

在 `AudioArbiterShared/AudioArbiterRuntime.cs` 的 `SoundProviderHandle` 中新增 owner-aware 匹配方法，例如：

```csharp
public bool MatchesProviderRequest(string requestedProviderId, string requestedOwnerModId, bool ownerStrict)
{
    var request = (requestedProviderId ?? "").Trim();
    var owner = (requestedOwnerModId ?? "").Trim();

    if (request.Length == 0)
    {
        return true;
    }

    if (string.Equals(request, QualifiedProviderId, StringComparison.OrdinalIgnoreCase))
    {
        return true;
    }

    if (!string.Equals(request, ProviderId, StringComparison.OrdinalIgnoreCase))
    {
        return false;
    }

    return !ownerStrict
           || string.IsNullOrWhiteSpace(owner)
           || string.Equals(owner, OwnerModId, StringComparison.OrdinalIgnoreCase);
}
```

保留现有 `MatchesProviderId` 作为裸兼容 API，内部可委托到新方法：

```csharp
public bool MatchesProviderId(string requestedProviderId)
{
    return MatchesProviderRequest(requestedProviderId, "", ownerStrict: false);
}
```

#### 2. 调整 Audio Resolve 逻辑

在 `AudioArbiterComponent.Resolve` 中，把原来的单次匹配：

```csharp
if (!string.IsNullOrWhiteSpace(request.ProviderId)
    && !provider.MatchesProviderId(request.ProviderId))
{
    continue;
}
```

调整为 owner-aware 匹配策略：

1. 如果 `request.ProviderId` 是 qualified id，直接按 qualified id 匹配。
2. 如果 `request.ProviderId` 是裸 id 且 `request.OwnerModId` 非空，优先严格匹配同 owner。
3. 如果请求来自远端，即 `request.IsRemote == true`，严格匹配失败后直接失败并输出 warning。
4. 如果请求来自本地旧调用，可以在严格匹配失败后 fallback 到裸 id 匹配，并输出一次 warning。
5. 如果 `request.ProviderId` 为空，保持现有按 provider 排序评估的行为。

#### 3. 保留 RPC payload 的裸 ProviderId

`SendRemoteAudioEvent` 发送前可以继续保留：

```csharp
request.ProviderId = provider.ProviderId;
request.OwnerModId = provider.OwnerModId;
```

但需要增加注释，说明：

- RPC payload 保留裸 `ProviderId` 是为了旧接收端兼容。
- 新接收端必须通过 `OwnerModId` 做确定性 provider 匹配。
- `request.OwnerModId` 不应在 RPC 发送前丢失。

#### 4. 增强远端失败日志

远端 owner-strict 匹配失败时，日志应包含：

- `request.ProviderId`
- `request.OwnerModId`
- `request.Kind`
- `request.RoleId`
- `request.StatusInstanceId`
- 是否 `IsRemote`

建议日志文案：

```text
Remote sound provider mismatch: providerId=<id>, owner=<owner>, kind=<kind>, role=<role>, status=<status>.
```

这类日志应该使用 once/限频策略，避免战斗中反复刷屏。

## BGM 影响确认

`BattleBgmArbiterShared` 当前没有类似 Audio 的联机 `RpcAudioEvent` payload 路径。本轮 BGM 主要需要处理风险 1：

- provider replacement 改为 `QualifiedProviderId`。
- active provider 已记录为 `QualifiedProviderId`。
- 显式切换 `providerId` 继续支持裸 id 和 qualified id。
- BuildId 必须升级，避免复用旧全局 BGM arbiter。

不需要为 BGM 新增 OwnerModId RPC 匹配逻辑，除非后续加入跨端 BGM 切换事件。

## 文档更新

需要同步更新：

- `docs/audio-shared-implementation-notes.md`
  - 记录 Audio RPC payload 保留裸 `ProviderId`，新接收端通过 `OwnerModId` 做确定性匹配。
  - 记录 owner-qualified provider 语义变化必须 bump BuildId。

- `docs/shared-component-architecture-audit.md`
  - 将 Audio/BGM 的 owner-qualified provider identity 状态更新为已闭环。
  - 备注 Audio 远端同步已使用 owner-aware matching。

- `docs/shared-component-architecture-guidelines.md`
  - 如需要，补充一条：当 provider identity 语义变化会影响全局 arbiter 复用时，即使公开方法形状不变，也必须 bump BuildId。

## 测试计划

### 静态测试

更新 `tools/Test-SunExpCSharp.ps1`：

- 断言 Audio 新 BuildId。
- 断言 BGM 新 BuildId。
- 断言 Audio 存在 owner-aware provider matching 方法。
- 断言 Audio RPC 路径保留 `OwnerModId`。
- 断言 Audio Resolve 路径使用 `request.OwnerModId` 参与匹配。

更新 `tools/Test-SharedArchitectureGuidelines.ps1`：

- 继续检查 Audio/BGM/CG 暴露 `QualifiedProviderId`。
- 可增加 Audio 专项检查：远端 provider 选择路径必须具备 owner-aware matching 入口。
- 不建议把测试绑定到过窄的方法名，避免未来重构时测试变成实现细节负担。

### 构建与验证

执行：

```powershell
tools\Build-MainSharedConsumers.ps1
tools\Build-SunExpDll.ps1
tools\Test-SunExpCSharp.ps1
.codex\skills\sunexp-mod-dev\scripts\validate-sunexp.ps1
tools\Test-SharedArchitectureGuidelines.ps1
git diff --check
```

如果本轮也修改了 `SanGuoShaExp-Dev` 或 `AuraToolsExp-Dev` 的 shipped DLL，需要确认：

```powershell
tools\Build-MainSharedConsumers.ps1
```

已经重新生成：

- `SunExp/Scripts/Entry.dll`
- `SanGuoShaExp/Scripts/Entry.dll`
- `AuraToolsExp/Scripts/Entry.dll`

### Release Gate 注意事项

`tools\Test-SharedReleaseGate.ps1` 当前可能仍会在 `core-contract` 阶段被 `AuraOnlineShared` 的 `ConfirmPlayerMessage` 测试契约问题阻塞。

该阻塞不属于本轮 Audio/BGM owner-qualified provider 修复范围，但最终发版前需要单独解决。

## 验收标准

本轮修复完成后，应满足：

1. Audio/BGM 的 `CurrentBuildId` 已升级。
2. 新消费者不会复用旧版本全局 Audio/BGM arbiter。
3. Audio 本地请求仍兼容裸 `ProviderId`。
4. Audio 远端 RPC 请求在 `OwnerModId` 存在时不会匹配到其它 MOD 的同名 provider。
5. 多 MOD 同名 Audio provider 的联机远端播放具备确定性。
6. BGM 显式切换仍支持裸 provider id 和 qualified provider id。
7. 静态测试、主消费者构建、SunExp C# 测试、SunExp 验证和 diff 检查通过。

## 推荐开发顺序

1. 升级 Audio/BGM `CurrentBuildId`。
2. 更新 `tools/Test-SunExpCSharp.ps1` 的 BuildId 断言。
3. 在 Audio `SoundProviderHandle` 中新增 owner-aware matching。
4. 调整 Audio `Resolve`，优先 owner-strict 匹配远端请求。
5. 保留 RPC 裸 `ProviderId` payload，并确保 `OwnerModId` 不丢失。
6. 增加远端 provider mismatch warning。
7. 更新文档和架构扫描规则。
8. 重新构建主消费者 DLL。
9. 执行验证命令。

## 后续可选增强

- 为 Audio provider matching 增加更细的单元测试夹具，模拟两个 MOD 注册同名 provider。
- 为 BGM 显式切换增加 owner-qualified provider id 的静态断言或轻量行为测试。
- 在共享架构文档中建立统一规则：任何改变全局 arbiter 复用语义、注册替换语义或跨 MOD 身份解析语义的改动，都必须升级 BuildId。
