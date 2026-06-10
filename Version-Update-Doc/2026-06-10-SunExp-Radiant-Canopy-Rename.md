# SunExp 版本更新文档：日耀：烬冠天幕命名与 ID 统一

版本：v0.1.3

日期：2026-06-10

## 更新目标

本轮更新将 SunExp 当前卡包统一为总主题 **【日耀：烬冠天幕】**，并围绕四条机制轴线进行命名、文本与 ID 收束：

- **日耀**：资源入口、基础增益与灼烧转化。
- **圣冕**：爆发窗口、授冕与冠冕威光。
- **聚炎**：吸收灼烧、积蓄层数、转化爆发。
- **天幕**：炽灼天幕、敌方灼烧、负面状态扩散。

本次更新不仅调整显示文本，也同步修改了卡包、卡牌、状态、遗物的内部 ID、脚本引用、图标路径与发布说明。

## 卡包调整

| 原 ID | 新 ID | 新名称 | 定位 |
| --- | --- | --- | --- |
| `cardpack_sunexp_base` | `cardpack_radiant_spark` | 【日耀：星火】 | 基础包，提供日耀、聚炎、烬衣与圣冕入口。 |
| `cardpack_sunexp_burst` | `cardpack_ember_crown` | 【日耀：烬冠】 | 聚爆包，围绕自身灼烧、聚炎叠层与圣冕爆发。 |
| `cardpack_sunexp_canopy` | `cardpack_solar_canopy` | 【日耀：天幕】 | 天幕包，围绕炽灼天幕、敌方灼烧、负面状态与持续扩散。 |

## 核心状态调整

| 原 ID | 新 ID | 新名称 | 备注 |
| --- | --- | --- | --- |
| `solar_radiance` | `solar_radiance` | 日耀 | 保留核心资源名。 |
| `gathered_flame` | `gathered_flame` | 聚炎 | 保留核心资源名。 |
| `solar_field` | `scorching_canopy` | 炽灼天幕 | 天幕轴核心状态。 |
| `burn_ward` | `ember_cloak` | 烬衣 | 防护状态，文本与卡牌同步。 |
| `solar_crown_state` | `solar_crown` | 圣冕显化 | 与卡牌 `solar_coronation` 区分。 |
| `miniature_corona_state` | `origin_core_radiance` | 源核：日耀 | 源核类状态。 |
| `melting_wheel_charge_state` | `cycle_gathered_flame` | 轮转：聚炎 | 聚炎轮转状态。 |
| `afterglow_syndrome_state` | `afterglow_omen` | 残光病兆 | 末日残光主题负面状态。 |

## 卡牌调整

| 原 ID | 新 ID | 新名称 | 所属卡包 |
| --- | --- | --- | --- |
| `spark` | `spark` | 星火 | 【日耀：星火】 |
| `flare_cut` | `radiant_flame_slash` | 耀焰斩 | 【日耀：星火】 |
| `burn_ward_card` | `ember_cloak_card` | 烬衣 | 【日耀：星火】 |
| `solar_focus` | `solar_prayer` | 太阳圣祷 | 【日耀：星火】 |
| `solar_phase_tuning` | `solar_phase_tuning` | 日相校准 | 【日耀：星火】 |
| `solar_crown` | `solar_coronation` | 日耀：授冕 | 【日耀：星火】 |
| `dawn_calibration` | `radiant_oath` | 启辉誓言 | 【日耀：星火】 |
| `heliostat_ignition` | `solar_ignition` | 日耀：引燃 | 【日耀：星火】 |
| `dawnline_guard` | `morning_light_bulwark` | 晨光壁垒 | 【日耀：星火】 |
| `spectrum_return` | `solar_return` | 日耀：回转 | 【日耀：星火】 |
| `miniature_corona` | `solar_origin_core` | 日耀源核 | 【日耀：星火】 |
| `draw_flame` | `draw_flame` | 引炎 | 【日耀：烬冠】 |
| `solar_spark` | `burning_star_hex` | 燃星之咒 | 【日耀：烬冠】 |
| `crown_core_flash` | `blazing_crown_collapse` | 炽冕崩落 | 【日耀：烬冠】 |
| `flare_reclaim` | `scorching_flow_reclaim` | 灼流回收 | 【日耀：烬冠】 |
| `flamewheel_recurrence` | `flamewheel_recurrence` | 炎轮再临 | 【日耀：烬冠】 |
| `flame_pierce` | `solar_scorching_light` | 日耀灼光 | 【日耀：烬冠】 |
| `backdraft_cycle` | `burning_crown_oath` | 燃冠誓言 | 【日耀：烬冠】 |
| `ember_compression` | `ember_tower` | 凝烬成塔 | 【日耀：烬冠】 |
| `flame_shell` | `gathered_flame_shield` | 聚炎护盾 | 【日耀：烬冠】 |
| `melting_wheel_charge` | `gathered_flame_cycle` | 聚炎轮转 | 【日耀：烬冠】 |
| `scorching_canopy` | `scorching_canopy_card` | 灼热天幕 | 【日耀：天幕】 |
| `crown_pressure` | `crown_radiance` | 冠冕威光 | 【日耀：天幕】 |
| `field_ignition` | `canopy_return` | 天幕再临 | 【日耀：天幕】 |
| `impurity_pyrolysis` | `impurity_purge` | 焚污除秽 | 【日耀：天幕】 |
| `burn_multiplier` | `eclipse_hex` | 蚀天之咒 | 【日耀：天幕】 |
| `ember_conduction` | `burning_calamity` | 燃灾 | 【日耀：天幕】 |
| `low_pressure_canopy` | `solar_eclipse` | 日蚀 | 【日耀：天幕】 |
| `smoke_erosion` | `smoke_erosion` | 烟蚀 | 【日耀：天幕】 |
| `afterglow_syndrome` | `afterglow_omen_card` | 残光病兆 | 【日耀：天幕】 |

## 遗物调整

| 原 ID | 新 ID | 新名称 | 所属卡包 |
| --- | --- | --- | --- |
| `morning_shard` | `morning_shard` | 晨辉碎片 | 【日耀：星火】 |
| `thermal_lining` | `ember_cloak_lining` | 烬衣衬布 | 【日耀：星火】 |
| `sun_orbit_mirror` | `sun_orbit_mirror` | 环日镜 | 【日耀：星火】 |
| `flame_siphon` | `sun_bottle` | 太阳瓶 | 【日耀：星火】 |
| `zodiac_dial` | `solar_phase_dial` | 日相刻盘 | 【日耀：星火】 |
| `daylight_dome` | `miniature_sunwheel` | 小型日轮 | 【日耀：烬冠】 |
| `stellar_furnace_core` | `blazing_crown_heart` | 炽冠圣心 | 【日耀：烬冠】 |
| `solar_prism` | `solar_prism` | 日心棱镜 | 【日耀：烬冠】 |
| `corona_cradle` | `coronation_throne` | 授冕圣座 | 【日耀：烬冠】 |
| `molten_core_charm` | `gathered_flame_charm` | 聚炎护符 | 【日耀：天幕】 |
| `ember_pressure_valve` | `ash_charm` | 灰烬护符 | 【日耀：天幕】 |
| `low_pressure_dome` | `blazing_sundial` | 曜阳日晷 | 【日耀：天幕】 |
| `equatorial_wind_belt` | `burning_calamity_wind_belt` | 燃灾风带 | 【日耀：天幕】 |

## 同步修改范围

本轮已同步调整以下内容：

- `SunExp/Data/**/sunexp.csv` 中的卡包、状态、卡牌、遗物 ID 与引用。
- `SunExp/Text/**/sunexp.csv` 中的中文、繁中、英文、日文名称与描述。
- `SunExp/Scripts/Entry.lua` 中的运行时 ID 引用与状态判断。
- `SunExp/ModResource/Images/Card/SunExp` 与 `SunExp/ModResource/Images/Relic/SunExp` 中的图标文件名。
- `SunExp/README.md`、`SunExp/ModConfig.json`、Workshop 描述、反馈帖与效果表等发布说明文本。

特别注意：

- 卡牌 `solar_coronation`（日耀：授冕）与状态 `solar_crown`（圣冕显化）已分离。
- 卡牌 `scorching_canopy_card`（灼热天幕）与状态 `scorching_canopy`（炽灼天幕）已分离。
- `burn_ward` 系列已统一为 `ember_cloak` / `ember_cloak_card` / `ember_cloak_lining`。

## 校验结果

本轮修改后已完成以下检查：

```text
Lua syntax check passed: 91 snippet(s) checked with C:\Users\75601\AppData\Local\Programs\Lua\bin\lua.exe.
SunExp validation passed: cards=30, relics=13, buffs=8, packs=3, warnings=0.
```

内容清点：

```text
Cards:  30
Relics: 13
Buffs:  8
Packs:  3
```

卡牌分布：

```text
SunExp_sunexp_cardpack_ember_crown: 10
SunExp_sunexp_cardpack_radiant_spark: 11
SunExp_sunexp_cardpack_solar_canopy: 9
```

遗物分布：

```text
SunExp_sunexp_cardpack_ember_crown: 4
SunExp_sunexp_cardpack_radiant_spark: 5
SunExp_sunexp_cardpack_solar_canopy: 4
```

额外检查：

- Data/Text 四类表 ID 已同步。
- 卡牌与遗物图标路径均可解析到实际 PNG 文件。
- 关键旧 ID 未在 `SunExp` 正式内容中残留。
