# 无尽之渊机制落地记录（2026-07-04）

本轮开发将玩家可见玩法名从【无尽之海之塔】切换为【无尽之渊】。内部 `EndlessSea*` 类名、存档键和 Hook 入口继续保留，用于兼容旧存档和已有架构断言。

## 配置

- 新增 `SunExp/endless_abyss.config.json`。
- `gaze.initialLevel = 1`，即【注视等级】初始为 1。
- 必选决策数公式：`requiredChoices = clamp(1 + floor((gaze - 1) / choiceStep), 1, maxRequiredChoices)`。
- 当前默认：`choiceStep = 3`，因此注视 1-3 选 1 项，4-6 选 2 项，7+ 选 3 项。
- 进入【无尽模式】时注视等级至少提升到 5；无尽战斗震荡结算后默认额外 +1 注视，用于让无尽阶段较快进入 3 选压力。

## 深渊震荡

【深渊震荡】统一承载旧潜行/无尽惩罚：

- 【潜行模式】：第 1-6 层，每层地图场景触发一次。
- 【无尽模式】：第 7 层起，每场战斗结束后记录待处理震荡，回到地图场景触发一次。
- UI 仅在地图场景弹出，提供 3 个互斥决策项，可按当前注视等级要求选择 1-3 项：
  - 随机销毁 1 件已装备遗物。
  - 给当前卡组内随机 3 张卡添加【湮灭】。
  - 【注视等级】+1。

震荡和里程碑均写入 `EndlessAbyssRunLedger`，待处理震荡写入 `SunExp_EndlessAbyssPendingShock`，避免 Hook 重入或 UI 重开导致重复结算。

## 节点和无尽首领

地图节点类型显式拆分为：

- `Monster`：普通怪。
- `Elite`：精英。
- `Boss`：首领。
- `EndlessBoss`：无尽首领。

第 7 层起固定终点使用 `EndlessBoss`；普通怪候选不再混入首领，精英节点由独立池筛选。无尽模式下额外敌人注入会读取当前节点类型。

## 里程碑奖励

从第 2 层开始，每层地图场景会弹出一次【深渊里程碑】奖励选择 UI，提供 4 类奖励选项卡：

- 任意挑选 1 件 1/2/3 阶遗物。
- 随机获得 1 张异次元卡。
- 选择 1 张当前卡组卡牌清除【焚毁】。
- 选择 1 张当前卡组卡牌添加【绝灭】。

当前未接入使魔成长系统，奖励/难度/玩法/机制框架保持独立。

## 本轮验证

```powershell
powershell -ExecutionPolicy Bypass -File tools\Build-SunExpDll.ps1
powershell -ExecutionPolicy Bypass -File tools\Test-SunExpArchitecture.ps1
powershell -ExecutionPolicy Bypass -File tools\Test-SunExpCSharp.ps1
powershell -ExecutionPolicy Bypass -File .codex\skills\sunexp-mod-dev\scripts\validate-sunexp.ps1
```

结果：

- DLL 构建：0 warnings / 0 errors。
- 架构断言：通过。
- C# 源码断言：183 assertions 通过。
- SunExp 校验：`cards=51, relics=13, buffs=27, packs=5, enemies=3, warnings=0`。
