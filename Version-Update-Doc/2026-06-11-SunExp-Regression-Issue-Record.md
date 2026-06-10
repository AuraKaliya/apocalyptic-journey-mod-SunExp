# 2026-06-11 SunExp 回归问题记录

## 背景

本轮检查集中处理了最近卡包名、卡牌名、文本与脚本整理后暴露的三类回归问题。这些问题都不是单纯数值错误，而是“脚本触发方式”和“文本/标签展示边界”在改名或批量整理后没有被同步复查导致的。

## 问题一：炎轮再临的“灼烧立即生效”未生效

### 表现

`炎轮再临` 应在给予敌方灼烧后，立即触发一次敌方灼烧结算；当前版本中灼烧层数可以添加，但“立即生效”没有按预期结算。

### 原因

此前封装的全体灼烧结算逻辑按敌方单位逐个调用目标型 helper。这个写法依赖当前目标状态能正确切到单个目标，但官方模板里类似的全体灼烧立即结算不是逐目标调用，而是在当前执行器上设置 `AllTarget` 后直接触发 `buff_burn` 的 `StartRound`：

- `apocalyptic-journey-mod-tutorial/ModTemplate/Scripts/Lib/DataConfigs/Data/Buff/buff.csv`
- `apocalyptic-journey-mod-tutorial/ModTemplate/Scripts/Lib/DataConfigs/Data/Card/elementscard.csv`

这些官方模板中的脚本片段是 C# 写法；SunExp 的 CSV 脚本列需要转成 Lua，例如：

```lua
self:SetStatus("AllTarget")
self:RunImmediately("buff_burn", "StartRound")
```

### 已采用修复

`SunExp_TriggerBurnAllEnemies` 改为官方同型逻辑：按次数循环，每次设置 `AllTarget`，然后调用 `self:RunImmediately("buff_burn", "StartRound")`。这样“全体敌方灼烧结算”由游戏原生 buff 事件处理，避免逐目标 helper 造成状态丢失或事件未命中。

### 后续预防

修改“立即结算某个 buff”的脚本时，必须同时检查目标状态、事件名和官方模板中的触发方式。全体结算优先使用 `SetStatus("AllTarget") + RunImmediately(...)`，不要在没有验证的情况下改成逐目标遍历。

## 问题二：轮转：聚炎未在自身灼烧增加时触发

### 表现

旧设计下，`轮转：聚炎` 无论在自己的回合还是敌方回合，只要自身灼烧增加，都没有稳定获得聚炎。新设计调整为：当该 buff 存在时，自身灼烧每增加 1 层，获得 1 层聚炎。

### 原因

旧实现依赖 `Action` 事件轮询自身灼烧层数，并在 `StartRound` 重置计数。这个事件不是“灼烧层数变化”的来源事件，因此会漏掉敌方回合、回合开始、卡牌结算中途等非 `Action` 时机的灼烧增加。

官方模板中类似“某个 buff 层数变化时触发”的写法使用 `buff_xxxOnLevelChange` 事件，例如：

- `apocalyptic-journey-mod-tutorial/ModTemplate/Scripts/ScriptSample.lua`
- `apocalyptic-journey-mod-tutorial/ModTemplate/Scripts/Lib/DataConfigs/Data/Relic/relic.csv`
- `apocalyptic-journey-mod-tutorial/ModTemplate/Scripts/Lib/DataConfigs/Data/Buff/buff.csv`

其中 `DataConfigs` 下的示例多为 C# 片段；SunExp 中应写为 Lua 事件注册：

```lua
self:AddEvent("buff_burnOnLevelChange", function()
    -- compare current and previous burn levels here
end)
```

### 已采用修复

`轮转：聚炎` 的 `ApplyScript` 改为注册 `buff_burnOnLevelChange`，并记录上一帧自身 `buff_burn` 层数。每次事件触发时只计算增加量：

- 当前灼烧大于记录值时，增加差值层数的 `SunExp_sunexp_gathered_flame`。
- 当前灼烧小于或等于记录值时，只同步记录值，不给聚炎。

同时移除了旧的 `Action` 轮询和每回合 3 次上限，使行为与新设计一致。

### 后续预防

凡是描述为“当某 buff 增加/减少/变化时”的效果，应优先检查是否应该监听 `buff_xxxOnLevelChange`，而不是用 `Action`、`StartRound` 或其他泛事件间接轮询。

## 问题三：焚毁重复描述与遗物描述末尾多余标签

### 表现

部分带 `Burnout` 特性的卡牌，如 `残光病兆`、`聚炎轮转`，描述中出现两次“焚毁”。部分遗物描述末尾出现额外文本：

- `曜阳日晷` 末尾多出“天幕”
- `日心棱镜` 末尾多出“日耀”
- `烬衣衬布` 末尾多出“防火”
- `晨辉碎片` 没有多余文本

### 原因

卡牌的 `Data/Card.Tag` 已经携带 `Burnout`，界面会按 tag 展示关键字。此前又在 `Text/Card` 描述里手写了一份本地化“焚毁”，导致重复。

遗物的 `Text/Relic.Tag` 属于展示文本字段，不是逻辑字段。部分遗物在文本表中填入了“日耀”“天幕”“防火”等标签后，界面路径会把它们追加到描述末尾。官方 `Text/Relic/relic.csv` 的遗物 tag 通常保持为空；SunExp 的遗物逻辑归属应由 `Data/Relic` 和 `PackBelong` 表达，而不是写入 `Text/Relic.Tag`。

### 已采用修复

- 保留卡牌 `Data/Card.Tag=Burnout`，移除 `Text/Card` 描述中手写的“焚毁/Burnout/焼却/焚毀”。
- 清空 SunExp 遗物的 `Text/Relic.Tag` 字段，避免 UI 自动追加到描述末尾。
- 同步更新文本汇总与评审草稿中的相关说明。

### 后续预防

修改卡牌和遗物文本时，要把“逻辑标签”和“展示描述”分开检查。`Data/Card.Tag` 这类自动展示的关键字不应在描述里重复书写；`Text/Relic.Tag` 除非明确需要 UI 显示，否则保持为空。

## 本轮新增检查清单

- 改动灼烧、流血、时锁等立即结算逻辑时，对照官方 `RunImmediately` 用法，确认状态选择和事件名。
- 改动“层数变化触发”效果时，优先检查是否要监听 `buff_xxxOnLevelChange`。
- 改动 `Data/Card.Tag`、`Text/Card.Description`、`Text/Relic.Tag` 时，检查 UI 是否会自动展示关键字或标签。
- 引用官方 `Scripts/Lib/DataConfigs` 下的脚本时，先确认示例语言；C# 片段必须转换为 SunExp CSV 可用的 Lua 写法。
- 完成修复后运行 Lua 片段 lint 和 SunExp validation，并记录已知的 CSV 注释行误报。
