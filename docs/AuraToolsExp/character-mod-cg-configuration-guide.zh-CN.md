# 角色 MOD 的 CG 配置手册

适用对象：希望通过“固定文件夹＋JSON 配置”给自己的角色提供 CG 的 MOD 作者。  
核对日期：2026-09-09；按本仓库当前实现编写。示例使用发现清单 schema 1、资源清单 schema 4、CG 清单 schema 4。

完成本手册后，玩家安装并启用你的角色 MOD 与 AuraToolsExp，即可在“角色 CG”页面选择、预览你的 CG，并在对应角色使用指定技能时自动播放。

## 1. 先准备什么

| 项目 | 要求 |
| --- | --- |
| 角色 MOD | 已能正常加载，角色可被游戏识别、选择和使用 |
| 工具 MOD | 测试端和希望播放 CG 的玩家端，安装并启用支持本手册协议的 AuraToolsExp |
| MOD 身份文件 | 保留你已有的根目录 `*.modproj`，必须恰好一份，内容为正整数 MOD ID；不要使用本手册中另一个 MOD 的 ID |
| 发布身份 | `ModConfig.json` 若填写了 `PublishedFileId` 或 `WorkshopPublishedFileId`，须与 `.modproj` 的 ID 一致 |
| 素材 | 首次接入准备一张 PNG；JPEG 也可用，但要同步修改 `source`、`fileName` 及 CG 共享路径中的扩展名 |
| 注册代码 | 本手册的发现路径不需要你新增 C# 注册代码，也不需要为了这项功能向角色 MOD 额外复制 `Aura.Shared.dll` |
| MOD 依赖配置 | 本手册不要求修改角色 MOD 的 `Dependencies`。玩家启用 AuraToolsExp 后，由它发现这些可选 CG |

首次测试建议只配置“一个角色、一个技能、一张图片”。先验证基本播放，再添加美餐、低生命或序列帧。

这条路径针对共享工具已能观测到的游戏行为。常规原生技能使用可以自动触发；自定义按钮直接执行脚本、完全绕过原生技能使用流程时，仅增加 JSON 不会自动创造技能信号，须单独处理该技能的运行时接入。

## 2. 先确定四个标识

下面所有文件采用同一组虚构示例值。它们不是游戏内现成的角色，也不是可以照抄的命名公式。

| 标识 | 示例 | 你应填写什么 |
| --- | --- | --- |
| 资源所属 MOD | `MoonlightMod` | 你为 MOD 选定的稳定所有者标识，建议使用简短英文、数字、下划线；在各配置中保持一致 |
| 角色 ID | `MoonlightMod_luna_luna` | 游戏最终加载后的完整角色 ID |
| 技能 ID | `MoonlightMod_luna_moon_burst` | 对应主动技能的实际数据 ID |
| CG／资源 ID | `luna.moon-burst` | 你自定义的稳定 CG 标识。本手册让 `cgId` 和 `resourceId` 相同，便于维护 |

**角色 ID、技能 ID 都不是显示名称，也不是“技能 1”“技能 2”这样的槽位编号。**

角色 ID 以游戏中的 Career 数据身份为准。技能 ID 对应实际技能的 `dataConfig.Id`，可能包含 MOD 加载时添加的前缀。如果作者手中的原始表 ID 与运行时 ID 不同，应使用最终运行时 ID；不要根据角色名称自行拼接。

本手册填写完整 ID，不依赖短别名匹配。无需另外添加 `role.registry.json` 来完成 CG 发现；角色本身应由你的角色 MOD 正常加载。

## 3. 建立目录

在已有 MOD 根目录中新增或合并以下文件：

```text
你的角色MOD/
├─ 你已有的.modproj
├─ ModConfig.json
├─ ……原有角色数据与文件……
└─ SharedResources/
   ├─ aura.discovery.json
   ├─ aura.registration.json
   ├─ cg.registry.json
   └─ CG/
      └─ Luna/
         └─ skill.png
```

只有 `SharedResources/aura.discovery.json` 是本流程固定查找的入口。其它清单由入口的 `path` 指定；本手册统一采用上面的名字，方便照着操作。

如果你的 MOD 已有这些文件，请合并数组条目并保持所有者一致，不要直接覆盖已有声明。

同目录的 [示例文件包](examples/character-mod-cg/README.md)提供三份 JSON。它不包含角色数据、身份文件或 CG 图片，不能作为独立 MOD 安装。将自己的图片放入 `CG/Luna/skill.png` 后再测试。

## 4. 配置发现入口：aura.discovery.json

保存到 `SharedResources/aura.discovery.json`：

```json
{
  "schemaVersion": 1,
  "ownerModId": "MoonlightMod",
  "participantKind": "Content",
  "contributions": [
    {
      "kind": "resources",
      "id": "role-cg-files",
      "path": "aura.registration.json",
      "required": true
    },
    {
      "kind": "cg",
      "id": "role-cg-rules",
      "path": "cg.registry.json",
      "required": true
    }
  ]
}
```

| 字段 | 说明 |
| --- | --- |
| `schemaVersion` | 发现清单的结构版本，按当前模板填写 `1` |
| `ownerModId` | 声明这些资源的 MOD；后面的两份清单必须一致 |
| `participantKind` | 角色内容 MOD 填 `Content` |
| `kind: resources` | 安装、登记实际图片文件和默认资源配置 |
| `kind: cg` | 登记 CG 的名称、触发条件和播放表现 |
| `id` | 该贡献的稳定标识；同一入口下 `kind + id` 不得重复 |
| `path` | 相对于 `SharedResources` 的配置文件路径 |
| `required` | 本模板两份文件都必须存在，因此均填 `true` |

只接入 CG 不需要填写 `audio` 贡献，也不需要提供 `audio.registry.json`。

**当前可选文件的注意事项：**不要声明一份不存在的配置并依赖 `required: false` 跳过。当前实现先读文件属性，再判断文件存在，缺失文件可能提前导致发现失败。没有某项功能时，直接不写对应 contribution；需要该功能时，把文件一并提供。

## 5. 配置图片资源：aura.registration.json

保存到 `SharedResources/aura.registration.json`：

```json
{
  "schemaVersion": 4,
  "ownerModId": "MoonlightMod",
  "participantKind": "Content",
  "packageSourceKind": "ModPackage",
  "packageId": "MoonlightMod.RoleCg",
  "packageVersion": 1,
  "resources": [
    {
      "moduleId": "CG",
      "featureId": "SkillCg",
      "scopeType": "Role",
      "scopeId": "MoonlightMod_luna_luna",
      "scopeOwnerModId": "MoonlightMod",
      "resourceId": "luna.moon-burst",
      "kind": "File",
      "source": "CG/Luna/skill.png",
      "fileName": "content.png",
      "originKind": "ContentRegistered",
      "writerId": "MoonlightMod",
      "defaultEnabled": true,
      "priority": 20,
      "effectMode": "Additive",
      "missingPolicy": "Skip",
      "tags": [
        "role-cg",
        "skill-cg"
      ]
    }
  ],
  "defaults": [
    {
      "moduleId": "CG",
      "featureId": "SkillCg",
      "scopeType": "Role",
      "scopeId": "MoonlightMod_luna_luna",
      "profileId": "content-default",
      "enabled": true,
      "priority": 20,
      "resourceOwnerModId": "MoonlightMod",
      "resourceId": "luna.moon-burst"
    }
  ]
}
```

这份文件把 `CG/Luna/skill.png` 登记为该角色的技能 CG 资源，并提供默认资源选择。

| 字段组 | 用途与修改方式 |
| --- | --- |
| `ownerModId`、`writerId` | 都改成你的资源所有者标识 |
| `packageId` | 这组资源包的稳定 ID；同一 MOD 的多个资源包应使用不同值 |
| `packageVersion` | 正整数资源包版本；修改图片内容后递增，详见第 11 节 |
| `moduleId / featureId` | 技能示例使用 `CG / SkillCg` |
| `scopeType / scopeId` | `Role` 与实际角色 ID |
| `scopeOwnerModId` | 该角色内容所属 MOD；给自己角色提供资源时与 `ownerModId` 相同 |
| `resourceId` | 图片资源的稳定标识，本例为 `luna.moon-burst` |
| `kind / source` | `File` 表示单文件；`source` 相对于本清单所在目录 |
| `fileName` | 注册后的逻辑文件名。本例统一为 `content.png`，可以与原图名字不同 |
| `originKind` | 内容 MOD 的资源填写 `ContentRegistered` |
| `defaultEnabled` | 默认允许该资源参与使用；玩家仍可通过工具设置覆盖 |
| `priority` | 资源默认优先级；一个示例资源使用 `20` 即可 |
| `defaults` | 默认配置指向同一资源，相关范围和所有者、资源 ID 必须与上方一致 |

`effectMode: Additive` 是通用资源协议的声明字段，不表示必定叠加播放多张 CG。角色 CG 的选择仍受当前角色／类型／技能上下文的工具配置控制。

`missingPolicy: Skip` 也不是“允许你漏发 skill.png”的开关。当前资源包注册要求声明的源文件有效且存在，漏发图片可能使本包注册失败。

## 6. 配置技能触发与表现：cg.registry.json

保存到 `SharedResources/cg.registry.json`：

```json
{
  "schemaVersion": 4,
  "ownerModId": "MoonlightMod",
  "contributionId": "role-cg",
  "protocol": {
    "minVersion": 4,
    "preferredVersion": 4
  },
  "entries": [
    {
      "cgId": "luna.moon-burst",
      "displayName": "露娜 · 月华绽放",
      "subjectType": "role",
      "subjectIds": [
        "MoonlightMod_luna_luna"
      ],
      "signals": [
        "aura.role.skill.committed"
      ],
      "match": {
        "facts": {
          "skillId": [
            "MoonlightMod_luna_moon_burst"
          ]
        },
        "minimumMetrics": {},
        "maximumMetrics": {}
      },
      "media": {
        "type": "image",
        "resource": "CG/Role/MoonlightMod_luna_luna/SkillCg/MoonlightMod/luna.moon-burst/content.png",
        "fallbackImage": "CG/Role/MoonlightMod_luna_luna/SkillCg/MoonlightMod/luna.moon-burst/content.png"
      },
      "defaultPresentation": {
        "mode": "fullscreenFade",
        "fit": "contain",
        "fadeIn": 0.25,
        "hold": 1.2,
        "fadeOut": 0.35,
        "focusX": 0.5,
        "focusY": 0.5,
        "safeScale": 1
      },
      "defaultActivation": {
        "enabled": true,
        "consumerMode": "toolManaged",
        "consumerModId": "AuraToolsExp"
      },
      "priority": 20,
      "enabled": true,
      "tags": [
        "role-cg",
        "skill-cg"
      ]
    }
  ]
}
```

| 字段 | 应如何填写 |
| --- | --- |
| `schemaVersion` | 当前 CG 清单使用 `4`；不要与发现清单的版本混淆 |
| `contributionId` | 本份 CG 清单的稳定 ID。同一 MOD 如拆成多份 CG 清单，各自必须不同 |
| `protocol` | 本模板使用当前 CG 注册协议 `4`。这里不是 MOD 版本，也不是 CG 网络协议版本 |
| `cgId` | CG 身份；一份 MOD 的不同 CG 使用不同值 |
| `displayName` | 玩家在工具中看到的名称，可以使用中文 |
| `subjectType` | 给角色配置时填 `role`，注意与资源范围的 `Role` 分属不同字段 |
| `subjectIds` | 实际角色 ID 数组 |
| `signals` | 技能触发填写 `aura.role.skill.committed` |
| `match.facts.skillId` | 要触发 CG 的技能数据 ID 数组；匹配其中一个即可 |
| `media.resource` | 已注册的共享资源逻辑路径，不能填本机绝对路径 |
| `media.fallbackImage` | 单图示例与 `resource` 填相同值 |
| `defaultPresentation` | 默认显示方式、缩放和时长，详见第 8 节 |
| `defaultActivation` | 本流程使用 `toolManaged`，并由 `AuraToolsExp` 管理 |
| `enabled` | 此 CG 条目是否参与注册；调试时保留 `true` |
| `defaultActivation.enabled` | 初始默认选择的依据之一；不会强制覆盖玩家已有选择 |

### 源文件路径与共享路径如何对应

这两种路径的用途不同，修改时应一起核对：

| 所在位置 | 本例填写内容 |
| --- | --- |
| MOD 内的原图 | `SharedResources/CG/Luna/skill.png` |
| 资源清单的 `source` | `CG/Luna/skill.png` |
| CG 清单的 `media.resource` | `CG/Role/MoonlightMod_luna_luna/SkillCg/MoonlightMod/luna.moon-burst/content.png` |

共享逻辑路径的组成是：

```text
moduleId/scopeType/scopeId/featureId/ownerModId/resourceId/fileName
```

因此更换角色 ID、资源 ID 或所有者时，必须同步更新共享路径。CG 配置中无需加 `Mods/`、`SharedResources/`、`ModsData/AuraShared/` 或 `Shared:` 前缀，也不要填写 `D:\...` 这样的路径。

工具可能为了路径长度限制改变实际落盘目录。CG 配置应继续使用上述逻辑路径，不需要跟着改成内部存储目录。

### 一个技能、一张图，或多个技能共用一张图

- 一个技能使用本例的单元素 `skillId` 数组。
- 多个技能共用一张图：把这些技能的实际 ID 都放进同一个 `skillId` 数组。
- 希望该角色的任意技能可选这张图：将 `match.facts` 改为 `{}`，去掉技能限定；保留 `subjectIds` 对角色的限定。
- 不同技能各自用图：为每张图添加独立资源声明和独立 CG 条目，并分别填写 `skillId`。

首次接入不要把 `subjectIds` 写成 `["*"]`，这样会把适用角色范围扩大到所有角色。

## 7. 添加美餐和低生命 CG

角色 CG 的三类现有信号如下：

| 场景 | `signals` 的值 | 触发条件 | 推荐资源 `featureId` |
| --- | --- | --- | --- |
| 使用技能 | `aura.role.skill.committed` | 原生技能实际执行了使用脚本并提交 | `SkillCg` |
| 美餐完成 | `aura.role.feast.completed` | AuraToolsExp 的“一键美餐”完成；需要开启对应功能 | `Feast` |
| 原生濒危 | `aura.role.low-health.entered` | 游戏发出原生 `Dying` 判定，每场战斗首次进入时触发 | `SkillCg` |

`featureId` 是资源分类路径的一部分；真正决定触发的是 CG 清单的 `signals`。上表沿用当前产品的分类方式。

添加美餐或低生命图时，按以下步骤扩展，发现入口无需新增一种 `kind`：

1. 新增图片，例如 `CG/Luna/feast.png` 或 `CG/Luna/low-health.png`。
2. 在资源清单 `resources` 中新增一条，使用新的 `resourceId`，例如 `luna.feast`、`luna.low-health`；修改 `source` 和需要的 `featureId`。
3. 在 CG 清单 `entries` 中新增一条，使用新的 `cgId` 和名称，将 `signals` 替换为上表对应信号。
4. 将这条 CG 的 `match.facts` 改为 `{}`。美餐、濒危信号不提供某个技能 ID，不能继续保留技能限定。
5. 按新资源声明重新填写 `media.resource` 与 `fallbackImage`；需要声明默认资源时，在 `defaults` 中增加相同范围的对应项，使用合适的 `profileId`。
6. 递增资源包 `packageVersion`，刷新后在相应标签页选择和测试。

当前低生命触发跟随原生濒危判定，不提供可由此配置自定义的 HP 百分比阈值。受到任意一次伤害不一定触发；同一战斗再次濒危也不会重复触发。

## 8. 调整显示方式和时长

先使用本例的 `fullscreenFade + contain`，确认原图能完整显示。

| 字段／值 | 效果 |
| --- | --- |
| `mode: fullscreenFade` | 使用全屏区域，淡入、停留、淡出 |
| `mode: centerFade` | 居中显示并淡入淡出 |
| `mode: slide` | 图片横向滑过画面 |
| `fit: contain` | 保持比例，完整显示；可能有留白 |
| `fit: cover` | 保持比例，裁切填满 |
| `fit: stretch` | 拉伸填满，可能使人物变形 |
| `fadeIn / hold / fadeOut` | 单位为秒；单图全屏淡入示例总时长为 `0.25 + 1.2 + 0.35 = 1.8` 秒 |
| `focusX / focusY` | 裁切填满时的构图焦点，范围 `0–1`，`0.5` 为居中 |
| `safeScale` | 裁切填满时的额外放大倍数，首次保留 `1` |

`fit` 与构图焦点主要用于全屏显示；`slide`、`centerFade` 使用各自布局，不应假定所有模式都按同一方式裁切。

图片建议从 1280×720 或 1920×1080 横图开始测试。这是制作建议，不是图片分辨率的硬性限制。需要透明轮廓时使用带 Alpha 的 PNG。

玩家在工具里保存的表现覆盖可能优先于作者默认值。调试作者参数时，在当前资源的表现设置中使用恢复默认，再重新预览。

## 9. 使用序列帧动画

当前普通角色媒体支持单图 `image` 和序列帧 `sequence`。**MP4、WebM、GIF 动画不能直接作为本手册的 CG 媒体配置。** 视频素材请先转成 PNG／JPEG 序列帧。本手册不涉及高级 `scene` 场景合成或 Unity AssetBundle 制作。

先保留已跑通的单图，再新增一个动画资源，避免直接改变既有资源身份的类型：

```text
SharedResources/CG/Luna/moon-burst-frames/
├─ 0001.png
├─ 0002.png
├─ 0003.png
└─ ……
```

| 配置位置 | 新增动画资源时的填写方式 |
| --- | --- |
| 资源 `resourceId` | 新值，例如 `luna.moon-burst-sequence` |
| 资源 `kind` | `Directory` |
| 资源 `source` | `CG/Luna/moon-burst-frames` |
| 资源 `fileName` | 目录资源不依靠该字段定位，可省略 |
| CG `cgId` | 新值，例如 `luna.moon-burst-sequence` |
| CG `media.type` | `sequence` |
| CG `media.resource` | `CG/Role/MoonlightMod_luna_luna/SkillCg/MoonlightMod/luna.moon-burst-sequence/content` |
| CG `media.fallbackImage` | `CG/Role/MoonlightMod_luna_luna/SkillCg/MoonlightMod/luna.moon-burst-sequence/content/0001.png` |
| CG `media.frameSeconds` | 每帧秒数。例如 `0.05` 表示每秒 20 帧 |
| CG `media.alphaMode` | 普通 PNG Alpha 保留默认 `none`；需要去除黑底时可用 `blackKey`，应先检查暗色主体是否被误去除 |

帧文件直接放在被引用目录下，使用等宽编号。播放器按文件名字典序读取该目录的 `.png / .jpg / .jpeg`，不会递归寻找更深层文件夹中的帧。`1.png、2.png、10.png` 会产生不符合数字顺序的排序。

动画主体时长由“帧数 × `frameSeconds`”决定；当前播放顺序为淡入、播放所有帧、停留 `hold` 秒、淡出。新动画保留原有角色和技能匹配；刷新后到当前技能上下文中选择动画条目。

**不要直接把已安装的同一 `resourceId` 从 `File` 改成 `Directory`。** 当前安装器不允许相同资源身份更换类型；应像本节这样新增资源 ID，并更新 CG 的资源引用。

全部 `SharedResources` 合计最多 4,096 个文件、1 GiB，配置文件和未使用文件也计入。不要把原始视频、制作工程和大量备用帧放进这个目录随 MOD 发布。资源路径也不能越界或依赖符号链接／目录联接。

## 10. 在游戏中验证

1. 确认角色 MOD 与 AuraToolsExp 已启用且加载成功；选择你的角色进行测试。
2. 打开 AuraToolsExp 工具箱，点击顶部“刷新共享资源”，等待本次发现完成。
3. 开启“角色 CG”，打开“角色 CG 配置”。
4. 顶部选择你的角色，切换“技能”标签，再选择配置的技能。
5. 找到你填写的 `displayName`，选择该资源并点击预览。
6. 在实际战斗中使用该技能，确认会播放。
7. 若添加了其它信号，再测试“一键美餐”完成和新战斗首次原生濒危。
8. 联机验证时，参与播放的客户端都安装相应角色资源与兼容的 AuraToolsExp，并启用所需的联机同步和本地 CG 功能。

预览成功只能确认资源与显示链路可用，不能证明技能 ID 或实际触发链路正确。必须补做第 6 步。

角色 CG 的技能／低生命资源选择按“角色＋类型＋技能上下文”保存。同一技能挂了多张默认 CG，不表示会依次全部播放；玩家可选择当前上下文使用哪张。美餐选择在同一页面管理，但还受“一键美餐”功能启用状态影响。

联机传输的是注册身份和播放请求，不会把你的图片文件发给队友。对方未安装相应资源时，不能靠主机自动补齐图片。

## 11. 更新与发布

| 修改内容 | 操作 |
| --- | --- |
| 修改已有图片或动画帧的内容 | 提高 `packageVersion`，随 MOD 一起发布配置与素材；刷新后复测 |
| 新增 CG | 新增资源与 CG 条目，使用新的稳定 ID；本手册建议同时提高包版本 |
| 只修改名称、技能匹配或默认时长 | 更新 CG 清单并刷新；无需更改 CG schema 版本，包版本可保持不变 |
| 删除某项 CG | 同步移除 CG 条目、对应资源声明和不再适用的默认引用；刷新会对比并停用移除项 |
| 更换媒体类型或资源身份 | 使用新的 `resourceId`；本手册建议也使用新的 `cgId`，并更新所有引用 |
| 发现“更新默认值后没有变化” | 检查玩家是否保存过当前资源／技能上下文的选择或表现覆盖，恢复默认后复测 |

`schemaVersion` 是协议结构版本，不随每次发布递增。`packageVersion` 才是这份资源包的内容版本，也不要求与 `ModConfig.ModVersion` 使用相同编号。

重复副本由工具按 MOD 身份与内容指纹去重。若同一个 MOD ID 的不同共享资源副本同时加载，可能报冲突；排查时只保留一个预期版本处于加载状态。

作者只需维护自己 MOD 内的声明与素材，不需要向发布包附带个人 `ModsData/AuraShared` 配置。不要通过清空整个共享数据目录来让别人安装你的新版本，那会影响其它 MOD 和玩家设置。

## 12. 常见问题

| 现象 | 优先检查 |
| --- | --- |
| 工具中没有这张 CG | 入口位置是否正确；MOD 是否实际加载；两份贡献文件是否存在；所有者和 schema 是否一致；是否执行过“刷新共享资源” |
| 注册失败 | `.modproj` 是否恰好一份且内容为正整数；发布 ID 是否一致；资源源文件是否缺失；是否越界、含链接目录或超出预算 |
| 只添加图片但没出现 | 图片必须有 `resources` 声明，播放规则必须有 `cg` 声明，并由发现入口引用两份清单 |
| 当前角色不在角色选择器里 | 先检查角色 MOD 的原生角色数据是否加载成功；CG 注册不会替你创建角色 |
| CG 在列表中，但预览失败 | 检查共享逻辑路径的每一段、源图片能否打开、资源是否成功注册且仍处于活动状态 |
| 预览成功，技能不触发 | 检查真实技能 ID、角色 ID、当前技能上下文的选中项、“角色 CG”总开关；确认该技能走原生技能提交流程 |
| 另一技能也显示了这张图 | 检查是否去掉了 `skillId` 限定，或同一条目明确匹配了多个技能 |
| 美餐或低生命不触发 | 不应残留 `match.facts.skillId`；美餐要开启“一键美餐”；低生命跟随原生 `Dying` 且每场首次触发 |
| 替换图片后仍是旧图或注册冲突 | 检查是否提高 `packageVersion`；是否有另一个同 ID 的 MOD 副本；是否仍选中了旧的 CG |
| 相同 ID 从单图改动画失败 | 为 `Directory` 资源创建新 `resourceId`，不要复用旧 `File` 身份 |
| 新的默认时长不生效 | 当前玩家可能保存了表现覆盖；在工具中恢复默认并重新预览 |
| 第 10 帧跑到了第 2 帧前面 | 文件名字典序问题，改为 `0001.png、0002.png、0010.png` |
| 队友看不到 CG | 双方资源与工具版本是否兼容；对方是否安装并注册了该 CG；本地 CG 与联机同步是否开启 |

日志可以搜索 `[Resources]`、`discovery`、`CG registry`、`Shared discovery`、`higher packageVersion` 等关键词。工具提示“发现完成”后仍应通过资源条目和实际技能验证，不要仅凭一个成功日志判断所有玩法都已接入。

## 13. 源码与现成实例索引

以下链接供本仓库维护者复核；配置作者按前面的模板操作即可。

| 内容 | 依据 |
| --- | --- |
| 发现入口、身份与目录限制 | [AuraSharedDiscovery.cs](../../AuraSharedCore/AuraSharedDiscovery.cs) |
| 三类贡献分发、注册顺序与停用 | [AuraToolsSharedResourceDiscoveryRuntime.cs](../../AuraToolsExp-Dev/Features/SharedResources/AuraToolsSharedResourceDiscoveryRuntime.cs) |
| 通用资源字段与逻辑路径 | [资源模型](../../AuraSharedCore/AuraSharedResourceProtocolModels.cs)、[路径规则](../../AuraSharedCore/AuraSharedResourcePathPolicy.cs) |
| CG 清单与当前注册 schema | [AuraCgRegistry.cs](../../AuraCgShared/AuraCgRegistry.cs) |
| 角色信号与条件匹配 | [信号定义](../../AuraCgShared/AuraCgSignalContracts.cs)、[查询匹配](../../AuraCgShared/AuraCgRegistryQueryService.cs) |
| 原生技能提交接入 | [技能事务路由](../../AuraSharedCore/AuraSkillActionTransactionRouter.cs)、[工具技能运行时](../../AuraToolsExp-Dev/Features/SkillCg/AuraToolsSkillCgRuntime.cs) |
| 媒体类型与帧排序 | [媒体与表现类型](../../AuraCgShared/AuraCgPresentationContracts.cs)、[序列帧路径](../../AuraCgShared/AuraCgMediaPathResolver.cs) |
| 当前玩家操作界面 | [角色 CG 页面](../../AuraToolsExp-Dev/Features/Cg/AuraToolsRoleCgSettingsPage.cs) |
| 实际内容 MOD 范例 | [Terrias 发现清单](../../Terrias/SharedResources/aura.discovery.json)、[资源清单](../../Terrias/SharedResources/aura.registration.json)、[CG 清单](../../Terrias/SharedResources/cg.registry.json) |
| 统一 CG 系统契约 | [统一 CG 系统契约](unified-cg-system-contract.md) |

本手册的三份完整 JSON 已与模板文件逐一核对；在临时目录补入测试图片后，通过当前共享 DLL 的发现、资源注册、逻辑路径解析和重复注册检查，也通过正确技能匹配、错误技能及错误角色不匹配检查。图片测试只验证文件与注册契约，没有执行 Unity 画面播放。作者仍需完成自己的游戏内、技能触发及联机验收。示例包中的身份为虚构值，正式素材由作者提供。
