# SkinExp 皮肤包

推荐按“角色目录 → 多套皮肤目录”组织资源。SkinExp 会扫描所有 MOD 中的 `Skins/<角色>/character.json`，每个角色目录可以包含任意数量的皮肤子目录。

## 推荐目录

```text
MySkinPack/
├─ ModConfig.json
└─ Skins/
   └─ TargetMod_FullCareerId/
      ├─ character.json
      ├─ summer_cool/
      │  ├─ skin.json
      │  ├─ Character.png
      │  └─ Idle/
      │     ├─ config.json
      │     ├─ Idle_00.png
      │     └─ Idle_01.png
      └─ winter_night/
         ├─ skin.json
         └─ ...
```

角色目录名建议直接使用完整 Career Id。`character.json` 负责把整个目录绑定到角色：

```json
{
  "schemaVersion": 2,
  "enabled": true,
  "targetCareerId": "TargetMod_FullCareerId"
}
```

如果省略 `targetCareerId`，SkinExp 会使用角色目录名；显式配置更容易发现拼写错误。

## 单套皮肤配置

每个皮肤子目录包含自己的 `skin.json`，资源路径只允许指向该皮肤目录内部：

```json
{
  "schemaVersion": 2,
  "enabled": true,
  "skinId": "Author.TargetCareer.summer_cool",
  "name": "夏日清凉",
  "author": "作者名",
  "preview": "Character.png",
  "assets": {
    "CareerImage": "Character.png",
    "Character": "Character.png",
    "Avatar": "Avatar.png",
    "DollIcon": "DollIcon.png",
    "ChoiceIcon": "ChoiceIcon.png",
    "Animation": "."
  }
}
```

`Animation: "."` 表示 `Idle/Attack/...` 状态目录直接位于当前皮肤目录。如果采用 `Animation/Idle` 结构，则填写 `"Animation": "Animation"`。

`skinId` 必须在所有已安装皮肤包中唯一。推荐格式为 `作者或MOD.完整角色Id.皮肤名`，显示名称由 `name` 决定，因此目录名和 ID 可以使用稳定的英文标识。

## 资源字段

| 字段 | 显示位置 | 是否必需 |
|---|---|---|
| `CareerImage` | 备战职业详情大立绘 | 可选 |
| `Avatar` | 战斗顶部和队友头像 | 可选 |
| `Character` | 状态页背景立绘 | 可选 |
| `DollIcon` | 状态页角色小像 | 可选 |
| `ChoiceIcon` | 职业选择缩略图 | 可选 |
| `Animation` | 动态小人动画根目录 | 可选 |

至少应提供一个有效资源。未提供或不存在的资源会逐项回退到角色默认皮肤。同一张图片可以同时配置给 `CareerImage` 和 `Character`。

## 动画目录

SkinExp 支持：

```text
Idle Attack Hit Buff Debuff Skill Special Special1 Special2 Defend
```

只制作 `Idle` 时，备战动态小人使用皮肤，攻击、受击等战斗动作继续使用默认动画。每个状态目录可包含 PNG/JPG 序列帧和 `config.json`：

```json
{
  "AnimationPerFrame": 0.12,
  "Direction": "Left",
  "isLoop": true,
  "Size": "Normal",
  "YOffset": 0,
  "FightYOffset": 0,
  "FightXOffset": 0,
  "SoundPath": ""
}
```

序列帧按自然文件名排序，推荐 `Idle_00.png、Idle_01.png...`，并保持画布尺寸和落脚点一致。

## 旧版独立清单

SkinExp 继续兼容散布在 Mods 目录中的 `*.skin.json`。旧版 schemaVersion 1 必须在每个文件里填写 `targetCareerId`。新皮肤推荐使用角色文件夹结构，避免多套皮肤的资源混在一起。

## 运行规则

- “默认皮肤”由目标角色加载后的 Career 数据动态生成，兼容官方和其它角色 MOD。
- 选择记录保存在 `Application.persistentDataPath/SkinExp/selections.json`。
- 皮肤仅影响本地显示，不通过 RPC 同步。
- 卸载皮肤包后自动回退默认；重新安装同一 `skinId` 后原选择继续生效。
- 两个职业复用同一动画源路径时，SkinExp 保留先应用者并记录冲突日志。
