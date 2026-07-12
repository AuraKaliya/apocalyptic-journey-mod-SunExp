# 日耀：烬冠天幕：当前效果表

> 本文件按当前 CSV 重新生成，覆盖旧版手写效果表。卡牌、遗物、状态的机制入口仍以 C# 脚本为准；本表用于核对 ID、名称、费用、卡包归属和本地化描述。

## 当前数量

| 类型 | 数量 | 数据来源 |
| --- | ---: | --- |
| 日耀卡牌 | 30 | `SunExp/Data/Card/sunexp.csv` |
| 乌娜职业牌/衍生牌 | 3 | `SunExp/Data/Card/wuna.csv` |
| 遗物 | 13 | `SunExp/Data/Relic/sunexp.csv` |
| 状态与特性 | 16 | `SunExp/Data/Buff/sunexp.csv` |

## 关键术语

| 术语 | 当前含义 |
| --- | --- |
| 日耀 | 核心资源，常用于提高日耀系牌和圣冠机制收益。 |
| 日耀系数 | 由日耀层数等因素换算出的伤害/收益系数。 |
| 聚炎 | 无上限聚能资源，供部分高压牌和 Boss 机制消耗或转化。 |
| 灼热天幕 | 全体燃烧压力来源，配合燃烧立即生效、聚炎回收和圣冠牌。 |
| 烬衣 / 焚身 | 当前实现中的防御与代价型状态。 |
| Boss 特性 | 镜阵、终日态、白曜圣女等 Boss 以 Buff 行为实现战斗特性。 |

## 日耀卡牌

Source: `SunExp/Data/Card/sunexp.csv`, `SunExp/Text/Card/sunexp.csv`

| Full ID | Name | Name_en | Type | Rarity | Cost | Tag | Action | PackBelong | Description |
| --- | --- | --- | --- | ---: | ---: | --- | --- | --- | --- |
| SunExp_sunexp_spark | 星火 | Spark | 攻击牌 | 1 | 0 | 白曜 | Attack | SunExp_sunexp_cardpack_radiant_spark | 造成{0}点伤害。给予目标2层{buff_burn}。获得1层{SunExp_sunexp_solar_radiance}。 |
| SunExp_sunexp_scorching_canopy_card | 灼热天幕 | Scorching Canopy | 技能牌 | 1 | 1 | 白曜 | - | SunExp_sunexp_cardpack_solar_canopy | 铺上1层{SunExp_sunexp_scorching_canopy}场地，全体获得2层{buff_burn}。 |
| SunExp_sunexp_radiant_flame_slash | 耀焰斩 | Radiant Flame Slash | 攻击牌 | 1 | 1 | 白曜 | Attack | SunExp_sunexp_cardpack_radiant_spark | 造成{0}（{1}+{2}*{SunExp_sunexp_solar_coefficient}）点伤害。 |
| SunExp_sunexp_ember_cloak_card | 烬衣 | Ember Cloak | 技能牌 | 1 | 0 | - | - | SunExp_sunexp_cardpack_radiant_spark | 获得等同自身{buff_burn}和{SunExp_sunexp_body_burn}总层数一半的护盾，然后获得{SunExp_sunexp_ember_cloak}。 |
| SunExp_sunexp_draw_flame | 引炎 | Draw Flame | 攻击牌 | 1 | 1 | 白曜 | Attack | SunExp_sunexp_cardpack_ember_crown | 吸收任意目标的所有{buff_burn}，转化为等量的{SunExp_sunexp_gathered_flame}。 |
| SunExp_sunexp_solar_prayer | 太阳圣祷 | Solar Prayer | 技能牌 | 2 | 1 | 白曜 | - | SunExp_sunexp_cardpack_radiant_spark | 获得2层{SunExp_sunexp_solar_radiance}。将自身的{buff_burn}全部转移给随机一名友方单位。 |
| SunExp_sunexp_burning_star_hex | 燃星之咒 | Burning Star Hex | 攻击牌 | 2 | 1 | 白曜 | Attack | SunExp_sunexp_cardpack_ember_crown | 消耗至多5层{SunExp_sunexp_gathered_flame}，造成{0}（{1}+{2}*{SunExp_sunexp_solar_coefficient}）点伤害。给予目标2层{buff_burn}。 |
| SunExp_sunexp_crown_radiance | 冠冕威光 | Crown Radiance | 技能牌 | 3 | 2 | 白曜 | Skill | SunExp_sunexp_cardpack_solar_canopy | 敌方全体获得6层{buff_burn}。若场上存在{SunExp_sunexp_scorching_canopy}，全体目标的{buff_burn}立即生效{SunExp_sunexp_solar_crown_tier}层数等量次数。 |
| SunExp_sunexp_*canopy_return | 天幕再临 | Canopy Return | 技能牌 | 2 | 1 | - | Skill | SunExp_sunexp_cardpack_solar_canopy | 获得2层{SunExp_sunexp_scorching_canopy}。全体获得3层{buff_burn}。敌方全体{buff_burn}立即生效一次。 |
| SunExp_sunexp_solar_phase_tuning | 被珍藏的名字 | Cherished Name | 技能牌 | 3 | 1 | 白曜 | - | SunExp_sunexp_cardpack_radiant_spark | 弃置手中所有牌，获得等同弃置牌数的{SunExp_sunexp_solar_radiance}，然后抽3张牌。 |
| SunExp_sunexp_solar_coronation | 日耀：授冕 | Radiance: Coronation | 能力牌 | 3 | 2 | 白曜 | - | SunExp_sunexp_cardpack_radiant_spark | 获得3层{SunExp_sunexp_solar_radiance}和2层{SunExp_sunexp_solar_crown}。 |
| SunExp_sunexp_blazing_crown_collapse | 炽冕崩落 | Blazing Crown Collapse | 攻击牌 | 3 | 3 | 白曜 | Attack | SunExp_sunexp_cardpack_ember_crown | 对敌方全体造成{0}（40+{SunExp_sunexp_solar_crown_tier}*{SunExp_sunexp_solar_coefficient}）点伤害。若没有{SunExp_sunexp_solar_crown}，自身承受等额反噬。随后结束{SunExp_sunexp_solar_crown}，消耗全部{SunExp_sunexp_gathered_flame}，自身获得消耗聚炎层数一半的{buff_burn}。 |
| SunExp_sunexp_*radiant_oath | 启辉誓言 | Radiant Oath | 技能牌 | 1 | 0 | Burnout | - | SunExp_sunexp_cardpack_radiant_spark | 获得3层{SunExp_sunexp_solar_radiance}。若你没有{SunExp_sunexp_scorching_canopy}，获得1层{SunExp_sunexp_scorching_canopy}；否则抽1张牌。 |
| SunExp_sunexp_solar_ignition | 日耀：引燃 | Radiance: Ignition | 技能牌 | 1 | 1 | 白曜 | Skill | SunExp_sunexp_cardpack_radiant_spark | 敌方全体获得2层{buff_burn}，并立刻生效一次。 |
| SunExp_sunexp_scorching_flow_reclaim | 灼流回收 | Scorching Flow Reclaim | 攻击牌 | 2 | 1 | - | Attack | SunExp_sunexp_cardpack_ember_crown | 目标敌人的{buff_burn}立即生效1次。随后移除其身上所有{buff_burn}，获得等量{SunExp_sunexp_gathered_flame}。 |
| SunExp_sunexp_impurity_purge | 焚污除秽 | Impurity Purge | 技能牌 | 2 | 1 | 白曜 | - | SunExp_sunexp_cardpack_solar_canopy | 移除自身所有负面 Buff，并获得等同于这些负面 Buff 总层数的{buff_burn}。 |
| SunExp_sunexp_flamewheel_recurrence | 炎轮再临 | Flamewheel Recurrence | 技能牌 | 3 | 1 | 白曜 | Skill | SunExp_sunexp_cardpack_ember_crown | 敌方全体的{buff_burn}立即生效2*N次，N为本场战斗已使用此牌次数+1。本次总费用等于N。 |
| SunExp_sunexp_eclipse_hex | 蚀天之咒 | Eclipse Hex | 攻击牌 | 2 | 2 | - | Attack | SunExp_sunexp_cardpack_solar_canopy | 对目标施加等同当前层数的{buff_burn}（最少8层），然后目标的{buff_burn}立即生效一次。 |
| SunExp_sunexp_solar_scorching_light | 浴火 | Bathed in Fire | 技能牌 | 2 | 1 | - | - | SunExp_sunexp_cardpack_radiant_spark | 自身的{buff_burn}立即生效一次，并给予敌方全体翻倍数量的{buff_burn}。 |
| SunExp_sunexp_burning_calamity | 燃灾 | Burning Calamity | 攻击牌 | 2 | 1 | - | Attack | SunExp_sunexp_cardpack_solar_canopy | 选择一个敌人，将其{buff_burn}层数的一半施加给其他所有敌人。随后该目标的{buff_burn}立即生效一次。 |
| SunExp_sunexp_burning_crown_oath | 燃冠誓言 | Burning Crown Oath | 技能牌 | 2 | 1 | 白曜 | - | SunExp_sunexp_cardpack_ember_crown | 消耗自身所有{SunExp_sunexp_gathered_flame}。敌方全体获得等同于消耗层数一半的{buff_burn}，并立即生效一次。 |
| SunExp_sunexp_morning_light_bulwark | 晨光壁垒 | Morninglight Bulwark | 技能牌 | 1 | 1 | 白曜 | - | SunExp_sunexp_cardpack_radiant_spark | 获得{0}点护盾。 |
| SunExp_sunexp_solar_return | 日耀：回转 | Radiance: Return | 技能牌 | 2 | 0 | 白曜 | - | SunExp_sunexp_cardpack_radiant_spark | 获得1层{SunExp_sunexp_solar_radiance}，然后抽1张牌。 |
| SunExp_sunexp_solar_origin_core | 被烧尽的名字 | Burned-Away Name | 技能牌 | 2 | 0 | 白曜 | - | SunExp_sunexp_cardpack_radiant_spark | 焚毁当前所有手牌，获得等量魔能。 |
| SunExp_sunexp_ember_tower | 凝烬成塔 | Ember Tower | 技能牌 | 1 | 1 | 白曜 | - | SunExp_sunexp_cardpack_ember_crown | 将自身所有{buff_burn}转化为等量{SunExp_sunexp_gathered_flame}。若转化满5层，抽1张牌。 |
| SunExp_sunexp_gathered_flame_shield | 聚炎护盾 | Gathered Flame Shield | 技能牌 | 2 | 1 | - | - | SunExp_sunexp_cardpack_ember_crown | 消耗所有{SunExp_sunexp_gathered_flame}，获得{0}点护盾。 |
| SunExp_sunexp_*gathered_flame_cycle | 聚炎轮转 | Gathered Flame Cycle | 能力牌 | 2 | 2 | Burnout | - | SunExp_sunexp_cardpack_ember_crown | 获得{SunExp_sunexp_cycle_gathered_flame}。 |
| SunExp_sunexp_solar_eclipse | 日蚀 | Solar Eclipse | 技能牌 | 1 | 1 | - | - | SunExp_sunexp_cardpack_solar_canopy | 敌方全体获得3层{buff_burn}。若场上存在{SunExp_sunexp_scorching_canopy}，则额外施加1层{buff_rotten}，随机清除一种正面 Buff。 |
| SunExp_sunexp_smoke_erosion | 烟蚀 | Smoke Erosion | 攻击牌 | 1 | 1 | - | Attack | SunExp_sunexp_cardpack_solar_canopy | 造成{0}（{1}+{2}*目标灼烧层数）点伤害。若目标拥有负面 Buff，给予2层{buff_burn}。 |
| SunExp_sunexp_afterglow_omen_card | 圣庭净裁 | Court Purification | 技能牌 | 3 | 5 | Retain,白曜,Annihilation | Attack | SunExp_sunexp_cardpack_radiant_spark | 消除目标除{buff_burn}和{SunExp_sunexp_body_burn}外的所有 Buff，每消除一种，给予目标1层{buff_burn}。 |

## 乌娜职业牌/衍生牌

Source: `SunExp/Data/Card/wuna.csv`, `SunExp/Text/Card/wuna.csv`

| Full ID | Name | Name_en | Type | Rarity | Cost | Tag | Action | PackBelong | Description |
| --- | --- | --- | --- | ---: | ---: | --- | --- | --- | --- |
| SunExp_wuna_card_*wuna_white_sun_prayer | 白曜圣祷 | White Radiance Prayer | 职业技能 | 3 | 0 | - | Skill | - | 获得一张0费的“日耀：授冕”，附着焚毁和凝滞。随后给己方全体的所有手牌添加焚毁和白曜。 |
| SunExp_wuna_card_*wuna_grave_song | 圣庭墓曲 | Court Requiem | 职业技能 | 3 | 0 | - | Skill | - | 余烬大于30时可用。消耗所有余烬，使全体获得余烬/2层数的灼烧，自身获得1层烬衣，然后立刻触发一次灼烧。 |
| SunExp_wuna_card_*wuna_coronation_token | 日耀：授冕 | Radiance: Coronation | 衍生牌 | 3 | 0 | Burnout,Froze | Skill | - | 获得2层{SunExp_sunexp_solar_crown}和2层{SunExp_sunexp_solar_radiance}。 |

## 遗物

Source: `SunExp/Data/Relic/sunexp.csv`, `SunExp/Text/Relic/sunexp.csv`

| Full ID | Name | Name_en | Rarity | Tag | PackBelong | Description |
| --- | --- | --- | ---: | --- | --- | --- |
| SunExp_sunexp_morning_shard | 晨辉碎片 | Morning Shard | 1 |  | SunExp_sunexp_cardpack_radiant_spark | 战斗开始时，获得2层{SunExp_sunexp_solar_radiance}。 |
| SunExp_sunexp_*ember_cloak_lining | 烬衣衬布 | Ember Cloak Lining | 1 |  | SunExp_sunexp_cardpack_ember_crown | 回合开始时，移除1层{buff_burn}，获得2层{SunExp_sunexp_gathered_flame}。 |
| SunExp_sunexp_sun_orbit_mirror | 环日镜 | Sun-Orbit Mirror | 2 |  | SunExp_sunexp_cardpack_ember_crown | 每行动3次，获得1层{SunExp_sunexp_gathered_flame}，对随机敌人施加3层{buff_burn}。 |
| SunExp_sunexp_sun_bottle | 太阳瓶 | Sun Bottle | 2 |  | SunExp_sunexp_cardpack_solar_canopy | 回合开始时，随机一名带有{buff_burn}的敌人，其{buff_burn}立刻生效一次。 |
| SunExp_sunexp_solar_phase_dial | 日相刻盘 | Solar Phase Dial | 3 |  | SunExp_sunexp_cardpack_radiant_spark | 回合开始时，根据{SunExp_sunexp_solar_radiance}层数最多触发三种效果：4+抽1张牌，8+获得1点魔能，12+全体{buff_burn}立刻生效一次。 |
| SunExp_sunexp_miniature_sunwheel | 小型日轮 | Miniature Sunwheel | 3 |  | SunExp_sunexp_cardpack_ember_crown | 回合开始时，若存在{SunExp_sunexp_scorching_canopy}，获得自身负面 Buff 总层数等量的{SunExp_sunexp_gathered_flame}。 |
| SunExp_sunexp_blazing_crown_heart | 炽冠圣心 | Blazing Crown Heart | 4 |  | SunExp_sunexp_cardpack_solar_canopy | 战斗开始时，获得8层{SunExp_sunexp_solar_radiance}、1层{SunExp_sunexp_solar_crown}，为场地铺上2层{SunExp_sunexp_scorching_canopy}。 |
| SunExp_sunexp_solar_prism | 日心棱镜 | Solar Prism | 1 |  | SunExp_sunexp_cardpack_radiant_spark | 战斗开始时，获得1层{SunExp_sunexp_solar_radiance}。每回合第一次获得{SunExp_sunexp_solar_radiance}后，额外获得1层{buff_elements}。 |
| SunExp_sunexp_coronation_throne | 授冕圣座 | Coronation Throne | 2 |  | SunExp_sunexp_cardpack_radiant_spark | 每场战斗第一次获得{SunExp_sunexp_solar_crown}后，抽2张牌并回复2点魔能。 |
| SunExp_sunexp_gathered_flame_charm | 聚炎护符 | Gathered Flame Charm | 3 |  | SunExp_sunexp_cardpack_ember_crown | 自身{buff_burn}层数增加后，获得等量的{SunExp_sunexp_gathered_flame}。 |
| SunExp_sunexp_ash_charm | 灰烬护符 | Ash Charm | 2 |  | SunExp_sunexp_cardpack_ember_crown | 回合开始时，移除自身一半的{buff_burn}，获得等量层数的{SunExp_sunexp_gathered_flame}和护盾。 |
| SunExp_sunexp_blazing_sundial | 曜阳日晷 | Blazing Sundial | 1 |  | SunExp_sunexp_cardpack_solar_canopy | 回合开始时，至多4名带有{buff_burn}的敌人获得1层{buff_weak}和1层{buff_rotten}。 |
| SunExp_sunexp_burning_calamity_wind_belt | 燃灾风带 | Burning Calamity Wind Belt | 2 |  | SunExp_sunexp_cardpack_solar_canopy | 回合开始时，至多4名带有{buff_burn}的敌人各使随机另一名敌人获得3层{buff_burn}。 |

## 状态与特性

Source: `SunExp/Data/Buff/sunexp.csv`, `SunExp/Text/Buff/sunexp.csv`

| Full ID | Name | Name_en | Type | UpperBound | Reduce/Turn | CanZero | Description |
| --- | --- | --- | --- | ---: | ---: | --- | --- |
| SunExp_sunexp_solar_radiance | 日耀 | Solar Radiance | 能力 | 12 | 0 | FALSE | 每次行动时，获得等同于5倍日耀层数的超凡。 |
| SunExp_sunexp_solar_coefficient | 日耀系数 | Solar Coefficient | 能力 | 1 | 0 | FALSE | 等于自身{SunExp_sunexp_solar_radiance}层数*2+{SunExp_sunexp_gathered_flame}层数/3+{buff_burn}层数/2。 |
| SunExp_sunexp_gathered_flame | 聚炎 | Gathered Flame | 能力 | 999 | 0 | FALSE | 回合开始时，自己获得等同于聚炎层数的灼烧和10倍层数的超凡。 |
| SunExp_sunexp_scorching_canopy | 灼热天幕 | Scorching Canopy | 场地 | 9 | 0 | FALSE | 场地。每轮回合开始时，全体获得等同于灼热天幕层数的灼烧；场上存在天幕时，任何目标被施加的灼烧超过上限部分会转化为等量焚身。 |
| SunExp_sunexp_body_burn | 焚身 | Body Burn | 负面 | 999 | 0 | FALSE | 回合开始时，每层受到最大生命值1%+1点真实伤害，随后移除此状态。 |
| SunExp_sunexp_ember | 余烬 | Ember | 能力 | 99 | 0 | FALSE | 每层使自身造成的伤害提高1%。灼烧结算前，消耗等量余烬抵消同等层数的灼烧。 |
| SunExp_sunexp_ember_cloak | 烬衣 | Ember Cloak | 能力 | 1 | 1 | FALSE | 获得时清除自身灼烧和焚身，下回合开始时再次清除自身灼烧和焚身，然后移除此状态。 |
| SunExp_sunexp_solar_crown | 圣冕显化 | Crown Manifestation | 能力 | 2 | 1 | FALSE | 持续期间，{SunExp_sunexp_solar_coefficient}变为原来的2倍。授冕时，根据自身当前的{SunExp_sunexp_solar_radiance}层数确立{SunExp_sunexp_solar_crown_tier}。触发时根据等阶获得最多5个效果：1阶清除自身所有负面 Buff 并转换为等量{buff_burn}；2阶抽1张牌；3阶获得1点魔能；4阶清除自身{buff_burn}并获得等量{SunExp_sunexp_gathered_flame}；5阶全体敌人获得5层{buff_burn}并立即生效一次。结束时消耗2倍等阶层数的{SunExp_sunexp_solar_radiance}。 |
| SunExp_sunexp_solar_crown_tier | 圣冕等阶 | Crown Tier | 能力 | 5 | 0 | FALSE | 当{SunExp_sunexp_solar_crown}时，根据当前{SunExp_sunexp_solar_radiance}层数确立授冕等阶：1/4/8/12/15。 |
| SunExp_sunexp_origin_core_radiance | 源核：日耀 | Origin Core: Radiance | 能力 | 1 | 0 | FALSE | 每回合第一次获得{SunExp_sunexp_solar_radiance}时，额外获得1层{SunExp_sunexp_solar_radiance}。 |
| SunExp_sunexp_cycle_gathered_flame | 轮转：聚炎 | Cycle: Gathered Flame | 能力 | 1 | 0 | FALSE | 自身灼烧每增加1层，获得1层聚炎。 |
| SunExp_sunexp_afterglow_omen | 残光病兆 | Afterglow Omen | 能力 | 1 | 0 | FALSE | 回合开始时，所有带有{buff_burn}的敌人获得等同于其{buff_burn}层数一半的{buff_vulnerability}。 |
| SunExp_sunexp_dusk_afterheat_recovery_trait | 余热回收 | Afterheat Recovery | 特性 | 1 | 0 | FALSE | 每当敌人{buff_burn}触发时，获得其{buff_burn}层数1/3的{SunExp_sunexp_ember}与{SunExp_sunexp_gathered_flame}。 |
| SunExp_sunexp_boss_trait_mirror_array | 三千环日镜 | Three Thousand Orbit Mirrors | 特性 | 1 | 0 | FALSE | 敌方回合开始时，全体目标获得2层{buff_burn}，三千镜按全体{buff_burn}总层数获得护盾。 |
| SunExp_sunexp_boss_trait_merciless_daylight | 无悯白昼 | Merciless Daylight | 特性 | 1 | 0 | FALSE | 敌方回合开始时，若玩家全体{buff_burn}总层数不低于8，焚毁1个保存名字；若没有保存名字，则玩家全体获得10层{SunExp_sunexp_body_burn}。 |
| SunExp_sunexp_boss_trait_white_radiance_saint | 白曜圣女 | White Radiance Saint | 特性 | 1 | 0 | FALSE | 敌方回合开始时，若自身没有{SunExp_sunexp_body_burn}，则将1个保存名字焚尽，获得6层{SunExp_sunexp_solar_radiance}与10%最大生命值的护盾。自身{SunExp_sunexp_solar_radiance}不少于12时，进入{SunExp_sunexp_boss_white_radiance_crown}。 |
