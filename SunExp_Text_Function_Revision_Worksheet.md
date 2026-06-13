# SunExp 文本与功能修订工作表

生成日期：2026-06-12

用途：整理当前 Mod 中卡牌、Buff、火漆/附着词条、遗物、卡包的现有中文文本和当前功能摘要，便于逐条修订。本文档只汇总现状，不代表已经修改游戏数据。

## 待修订重点

- 卡牌词条：`SunExpSolar` 当前在 `Text/KeyWordsDic/sunexp.csv` 中显示为“日耀”。
- 火漆词条：`solar_keyword` 当前在 `Text/EnchTag/sunexp.csv` 中显示为“日耀”，并会给附着卡添加 `SunExpSolar` 标签。
- Buff：`solar_radiance` 当前中文名也是“日耀”。后续若将火漆改为“白曜”，应避免与该 Buff 名称混淆。

## 卡牌

共 33 张/项，包含 SunExp 卡牌和乌娜职业技能/衍生牌。

| ID | 名称 | 类型 | 费用 | 稀有度 | 标签 | 所属 | 当前中文描述 | 当前功能摘要 | 修订稿 |
|---|---|---|---:|---|---|---|---|---|---|
| `spark` | 星火 | 攻击牌 | 0 | 普通/1 | 无 | 【日耀：星火】 (SunExp_sunexp_cardpack_radiant_spark) | 造成{0}点伤害。给予目标1层{buff_burn}。获得1层{SunExp_sunexp_solar_radiance}。 | 动作：Attack；脚本：伤害；施加/转化灼烧；日耀层数；调用 `SunExp_AddDamageDescription`、`SunExp_CalcSparkDamage`、`SunExp_DealDamage` |  |
| `scorching_canopy_card` | 灼热天幕 | 技能牌 | 1 | 普通/1 | SunExpSolar（日耀卡牌词条） | 【日耀：天幕】 (SunExp_sunexp_cardpack_solar_canopy) | 获得1层{SunExp_sunexp_scorching_canopy}。全体获得2层{buff_burn}。 | 动作：无；脚本：施加/转化灼烧；天幕场地；调用 `SunExp_ApplyFieldBuff`、`SunExp_ClearSelfBurnIfProtected`、`SunExp_HandleSolarCardUsed` |  |
| `radiant_flame_slash` | 耀焰斩 | 攻击牌 | 1 | 普通/1 | SunExpSolar（日耀卡牌词条） | 【日耀：星火】 (SunExp_sunexp_cardpack_radiant_spark) | 造成{0}点伤害。额外造成{1}点伤害。 | 动作：Attack；脚本：伤害；调用 `SunExp_AddDamageDescription`、`SunExp_CalcFlareCutDamage`、`SunExp_CalcFlareCutBonusDamage`、`SunExp_DealDamage`、`SunExp_DealSolarKeywordBonusDamage` |  |
| `ember_cloak_card` | 烬衣 | 技能牌 | 0 | 普通/1 | 无 | 【日耀：星火】 (SunExp_sunexp_cardpack_radiant_spark) | 清除自身{buff_burn}，获得等同于清除层数一半的护盾，并获得{SunExp_sunexp_ember_cloak}。 | 动作：无；脚本：护盾；移除/吸收状态 |  |
| `draw_flame` | 引炎 | 攻击牌 | 1 | 普通/1 | SunExpSolar（日耀卡牌词条） | 【日耀：烬冠】 (SunExp_sunexp_cardpack_ember_crown) | 吸收目标至多6层{buff_burn}，移除等量{buff_burn}，获得等量{SunExp_sunexp_gathered_flame}。 | 动作：Attack；脚本：聚炎层数；调用 `SunExp_RemoveStatusBuff`、`SunExp_HandleSolarCardUsed` |  |
| `solar_prayer` | 太阳圣祷 | 技能牌 | 1 | 稀有/2 | SunExpSolar（日耀卡牌词条） | 【日耀：星火】 (SunExp_sunexp_cardpack_radiant_spark) | 获得2层{SunExp_sunexp_solar_radiance}。移除自身至多3层{buff_burn}并转化为等量{SunExp_sunexp_gathered_flame}。 | 动作：无；脚本：移除/吸收状态；日耀层数；聚炎层数；调用 `SunExp_HandleSolarCardUsed` |  |
| `burning_star_hex` | 燃星之咒 | 攻击牌 | 1 | 稀有/2 | SunExpSolar（日耀卡牌词条） | 【日耀：烬冠】 (SunExp_sunexp_cardpack_ember_crown) | 消耗至多5层{SunExp_sunexp_gathered_flame}，造成{0}点伤害，额外造成{1}点伤害，并给予目标2层{buff_burn}。 | 动作：Attack；脚本：伤害；施加/转化灼烧；聚炎层数；调用 `SunExp_AddDamageDescription`、`SunExp_CalcSolarSparkDamage`、`SunExp_CalcSolarSparkBonusDamage`、`SunExp_DealDamage`、`SunExp_DealSolarKeywordBonusDamage` |  |
| `crown_radiance` | 冠冕威光 | 技能牌 | 2 | 史诗/3 | SunExpSolar（日耀卡牌词条） | 【日耀：天幕】 (SunExp_sunexp_cardpack_solar_canopy) | 敌方全体获得6层{buff_burn}。若场上存在{SunExp_sunexp_scorching_canopy}，全体目标的{buff_burn}立即生效一次。获得2层{SunExp_sunexp_solar_radiance}。 | 动作：Skill；脚本：施加/转化灼烧；立即触发灼烧；日耀层数；天幕场地；调用 `SunExp_AddStatusBuff`、`SunExp_TriggerBurnAll`、`SunExp_HandleSolarCardUsed` |  |
| `*canopy_return` | 天幕再临 | 技能牌 | 1 | 稀有/2 | 无 | 【日耀：天幕】 (SunExp_sunexp_cardpack_solar_canopy) | 获得2层{SunExp_sunexp_scorching_canopy}。全体获得3层{buff_burn}。敌方全体{buff_burn}立即生效一次。 | 动作：Skill；脚本：施加/转化灼烧；立即触发灼烧；天幕场地；调用 `SunExp_ApplyFieldBuff`、`SunExp_ApplySelfBurn`、`SunExp_AddStatusBuff`、`SunExp_TriggerBurnAllEnemies` |  |
| `*solar_phase_tuning` | 日相校准 | 技能牌 | 1 | 稀有/2 | 无 | 【日耀：星火】 (SunExp_sunexp_cardpack_radiant_spark) | 获得3层{SunExp_sunexp_solar_radiance}。吸收自身至多6层{buff_burn}，转化为等量{SunExp_sunexp_gathered_flame}。若吸收满6层，抽1+日耀层数/3张牌。 | 动作：无；脚本：抽牌；移除/吸收状态；日耀层数；聚炎层数 |  |
| `solar_coronation` | 日耀：授冕 | 能力牌 | 2 | 史诗/3 | SunExpSolar（日耀卡牌词条） | 【日耀：星火】 (SunExp_sunexp_cardpack_radiant_spark) | 获得3层{SunExp_sunexp_solar_radiance}和2层{SunExp_sunexp_solar_crown}。 | 动作：无；脚本：日耀层数；圣冕；调用 `SunExp_HandleSolarCardUsed` |  |
| `blazing_crown_collapse` | 炽冕崩落 | 攻击牌 | 3 | 史诗/3 | SunExpSolar（日耀卡牌词条） | 【日耀：烬冠】 (SunExp_sunexp_cardpack_ember_crown) | 对敌方全体造成{0}点伤害。若没有{SunExp_sunexp_solar_crown}，自身承受等额反噬。随后消耗全部{SunExp_sunexp_gathered_flame}和一半{SunExp_sunexp_solar_radiance}，自身获得消耗聚炎层数一半的{buff_burn}，并结束{SunExp_sunexp_solar_crown}。 | 动作：Attack；脚本：伤害；施加/转化灼烧；日耀层数；聚炎层数；圣冕；调用 `SunExp_AddDamageDescription`、`SunExp_CalcCrownCoreFlashDamage`、`SunExp_DealSolarKeywordDamageAllEnemies`、`SunExp_HandleSolarCardUsed`、`SunExp_DealDamage` |  |
| `*radiant_oath` | 启辉誓言 | 技能牌 | 0 | 普通/1 | Burnout | 【日耀：星火】 (SunExp_sunexp_cardpack_radiant_spark) | 获得3层{SunExp_sunexp_solar_radiance}。若你没有{SunExp_sunexp_scorching_canopy}，获得1层{SunExp_sunexp_scorching_canopy}；否则抽1张牌。 | 动作：无；脚本：抽牌；日耀层数；天幕场地；调用 `SunExp_ApplyFieldBuff` |  |
| `solar_ignition` | 日耀：引燃 | 技能牌 | 1 | 普通/1 | SunExpSolar（日耀卡牌词条） | 【日耀：星火】 (SunExp_sunexp_cardpack_radiant_spark) | 获得2层{SunExp_sunexp_solar_radiance}。敌方全体获得2层{buff_burn}。若你没有{SunExp_sunexp_scorching_canopy}，获得1层{SunExp_sunexp_scorching_canopy}。 | 动作：Skill；脚本：施加/转化灼烧；日耀层数；天幕场地；调用 `SunExp_AddStatusBuff`、`SunExp_ApplyFieldBuff`、`SunExp_HandleSolarCardUsed` |  |
| `scorching_flow_reclaim` | 灼流回收 | 攻击牌 | 1 | 稀有/2 | 无 | 【日耀：烬冠】 (SunExp_sunexp_cardpack_ember_crown) | 目标敌人的{buff_burn}立即生效一次。随后移除其至多12层{buff_burn}，获得等量{SunExp_sunexp_gathered_flame}。 | 动作：Attack；脚本：立即触发灼烧；移除/吸收状态；聚炎层数；调用 `SunExp_TriggerBurn`、`SunExp_RemoveBuffStacks` |  |
| `impurity_purge` | 焚污除秽 | 技能牌 | 1 | 稀有/2 | SunExpSolar（日耀卡牌词条） | 【日耀：天幕】 (SunExp_sunexp_cardpack_solar_canopy) | 移除自身所有负面 Buff，并获得等同于这些负面 Buff 总层数的{buff_burn}。 | 动作：无；脚本：施加/转化灼烧；移除/吸收状态；调用 `SunExp_GetNegativeBuffTotal`、`SunExp_RemoveAllNegativeBuffs`、`SunExp_HandleSolarCardUsed` |  |
| `flamewheel_recurrence` | 炎轮再临 | 技能牌 | 1 | 史诗/3 | SunExpSolar（日耀卡牌词条） | 【日耀：烬冠】 (SunExp_sunexp_cardpack_ember_crown) | 敌方全体的{buff_burn}立即生效N次，N为本场战斗已使用此牌次数+1。本次总费用等于N。 | 动作：Skill；脚本：立即触发灼烧；调用 `SunExp_SetFlamewheelCost`、`SunExp_SetFlamewheelUsed`、`SunExp_RefreshFlamewheelHand`、`SunExp_GetFlamewheelUsed`、`SunExp_TriggerBurnAllEnemies` |  |
| `eclipse_hex` | 蚀天之咒 | 攻击牌 | 2 | 稀有/2 | 无 | 【日耀：天幕】 (SunExp_sunexp_cardpack_solar_canopy) | 若目标敌人的{buff_burn}低于6层，对其施加6层{buff_burn}；否则将其{buff_burn}层数翻倍，最高49层。随后目标的{buff_burn}立即生效一次。 | 动作：Attack；脚本：施加/转化灼烧；立即触发灼烧；调用 `SunExp_AddStatusBuff`、`SunExp_TriggerBurn` |  |
| `*solar_scorching_light` | 日耀灼光 | 攻击牌 | 1 | 普通/1 | 无 | 【日耀：烬冠】 (SunExp_sunexp_cardpack_ember_crown) | 造成{0}点伤害。伤害为8+目标{buff_burn}层数*X，X为自身{SunExp_sunexp_gathered_flame}层数/4，至少为1。 | 动作：Attack；脚本：伤害；聚炎层数；调用 `SunExp_AddDamageDescription`、`SunExp_CalcFlamePierceDamage`、`SunExp_DealDamage` |  |
| `burning_calamity` | 燃灾 | 攻击牌 | 1 | 稀有/2 | 无 | 【日耀：天幕】 (SunExp_sunexp_cardpack_solar_canopy) | 选择一个敌人，将其{buff_burn}层数的一半施加给其他所有敌人。随后该目标的{buff_burn}立即生效一次。 | 动作：Attack；脚本：施加/转化灼烧；立即触发灼烧；调用 `SunExp_RemoveStatusBuff`、`SunExp_TriggerBurn` |  |
| `burning_crown_oath` | 燃冠誓言 | 技能牌 | 1 | 稀有/2 | SunExpSolar（日耀卡牌词条） | 【日耀：烬冠】 (SunExp_sunexp_cardpack_ember_crown) | 消耗至多12层{SunExp_sunexp_gathered_flame}。敌方全体获得等同于消耗层数一半的{buff_burn}。 | 动作：无；脚本：施加/转化灼烧；聚炎层数；调用 `SunExp_HandleSolarCardUsed` |  |
| `morning_light_bulwark` | 晨光壁垒 | 技能牌 | 1 | 普通/1 | SunExpSolar（日耀卡牌词条） | 【日耀：星火】 (SunExp_sunexp_cardpack_radiant_spark) | 获得{0}点护盾。 | 动作：无；脚本：调用 `SunExp_CalcSolarKeywordBlock`、`SunExp_ApplySolarKeywordSkill`、`SunExp_HandleSolarCardUsed` |  |
| `solar_return` | 日耀：回转 | 技能牌 | 0 | 普通/1 | SunExpSolar（日耀卡牌词条） | 【日耀：星火】 (SunExp_sunexp_cardpack_radiant_spark) | 若你拥有{buff_burn}，自身{buff_burn}立即生效一次，移除1层，并获得1层{SunExp_sunexp_solar_radiance}；否则抽1张牌。 | 动作：无；脚本：抽牌；立即触发灼烧；移除/吸收状态；日耀层数；调用 `SunExp_TriggerBurn`、`SunExp_HandleSolarCardUsed` |  |
| `*solar_origin_core` | 日耀源核 | 能力牌 | 1 | 稀有/2 | Burnout | 【日耀：星火】 (SunExp_sunexp_cardpack_radiant_spark) | 获得{SunExp_sunexp_origin_core_radiance}。 | 动作：无；脚本：self.Vars:set_Item("BaseScript", "CommonCardItem"); self:SetStatus("Self"); self:AddBuff("SunExp_sunexp_origin_core_radiance", "1"); |  |
| `ember_tower` | 凝烬成塔 | 技能牌 | 1 | 普通/1 | SunExpSolar（日耀卡牌词条） | 【日耀：烬冠】 (SunExp_sunexp_cardpack_ember_crown) | 将自身至多5层{buff_burn}转化为等量{SunExp_sunexp_gathered_flame}。若转化满5层，抽1张牌。 | 动作：无；脚本：抽牌；移除/吸收状态；聚炎层数；调用 `SunExp_HandleSolarCardUsed` |  |
| `gathered_flame_shield` | 聚炎护盾 | 技能牌 | 1 | 稀有/2 | 无 | 【日耀：烬冠】 (SunExp_sunexp_cardpack_ember_crown) | 消耗所有{SunExp_sunexp_gathered_flame}，获得{0}点护盾。 | 动作：无；脚本：护盾；聚炎层数 |  |
| `*gathered_flame_cycle` | 聚炎轮转 | 能力牌 | 2 | 稀有/2 | Burnout | 【日耀：烬冠】 (SunExp_sunexp_cardpack_ember_crown) | 获得{SunExp_sunexp_cycle_gathered_flame}。 | 动作：无；脚本：聚炎层数 |  |
| `solar_eclipse` | 日蚀 | 技能牌 | 1 | 普通/1 | 无 | 【日耀：天幕】 (SunExp_sunexp_cardpack_solar_canopy) | 敌方全体获得2层{buff_burn}和1层{buff_weak}；若你拥有{SunExp_sunexp_scorching_canopy}，改为2层{buff_weak}。 | 动作：无；脚本：施加/转化灼烧；天幕场地 |  |
| `smoke_erosion` | 烟蚀 | 攻击牌 | 1 | 普通/1 | 无 | 【日耀：天幕】 (SunExp_sunexp_cardpack_solar_canopy) | 造成{0}点伤害。伤害为7+目标{buff_burn}层数。若目标拥有负面 Buff，给予2层{buff_burn}。 | 动作：Attack；脚本：伤害；施加/转化灼烧；调用 `SunExp_AddDamageDescription`、`SunExp_CalcSmokeErosionDamage`、`SunExp_HasNegativeBuff`、`SunExp_DealDamage`、`SunExp_AddStatusBuff` |  |
| `*afterglow_omen_card` | 残光病兆 | 能力牌 | 2 | 稀有/2 | Burnout | 【日耀：天幕】 (SunExp_sunexp_cardpack_solar_canopy) | 获得{SunExp_sunexp_afterglow_omen}。 | 动作：无；脚本：self.Vars:set_Item("BaseScript", "CommonCardItem"); self:SetStatus("Self"); self:AddBuff("SunExp_sunexp_afterglow_omen", "1"); |  |
| `*wuna_white_sun_prayer` | 白曜圣祷 | 职业技能 | 0 | 史诗/3 | 无 | 无/未归属 | 冷却5回合。获得一张0费的“日耀：授冕”，带有焚毁和凝滞。 | 动作：Skill；脚本：调用 `SunExp_WunaUseWhiteSunPrayer` |  |
| `*wuna_grave_song` | 圣庭墓曲 | 职业技能 | 0 | 史诗/3 | 无 | 无/未归属 | 余烬大于30时可用。消耗所有余烬，移除烬衣，按聚炎层数自燃并立刻触发；若仍存活，回复30%生命，获得烬衣，使全体获得等量灼烧并立刻触发。 | 动作：Skill；脚本：调用 `SunExp_WunaUseGraveSong` |  |
| `*wuna_coronation_token` | 日耀：授冕 | 衍生牌 | 0 | 史诗/3 | Burnout, Froze | 无/未归属 | 获得2层{SunExp_sunexp_solar_crown}和2层{SunExp_sunexp_solar_radiance}。 | 动作：Skill；脚本：日耀层数；圣冕 |  |

## Buff

共 10 个，包含 SunExp 机制 Buff 和乌娜专属资源。

| ID | 名称 | 类型 | 上限 | 衰减 | 当前中文描述 | 当前功能摘要 | 修订稿 |
|---|---|---|---:|---|---|---|---|
| `solar_radiance` | 日耀 | 能力 | 15 | 每回合-0 / 受击-0 / 行动-0 | 核心聚能。每次行动时，获得等同于5倍日耀层数的超凡。日耀牌会按日耀系数获得额外收益。 | 类别：能力；稀有度：稀有/2；脚本：日耀层数；调用 `SunExp_RegisterHook`、`SunExp_IsHookTokenActive`、`SunExp_ClearHook` |  |
| `gathered_flame` | 聚炎 | 能力 | 999 | 每回合-0 / 受击-0 / 行动-0 | 无上限聚能。回合开始时，自己获得等同于聚炎层数的灼烧；烬衣可以抵消这次自燃。 | 类别：能力；稀有度：稀有/2；脚本：施加/转化灼烧；聚炎层数；调用 `SunExp_RegisterHook`、`SunExp_IsHookTokenActive`、`SunExp_ApplySelfBurn`、`SunExp_ClearHook` |  |
| `scorching_canopy` | 灼热天幕 | 能力 | 9 | 每回合-0 / 受击-0 / 行动-0 | 场地聚热，不可被普通效果移除。每轮回合开始时，全体获得等同于灼热天幕层数的灼烧；场上存在天幕时，任何目标被施加的灼烧超过上限部分会转化为等量焚身。 | 类别：能力；稀有度：稀有/2；脚本：天幕场地；调用 `SunExp_OnFieldBuffApplied`、`SunExp_OnFieldBuffCleared` |  |
| `body_burn` | 焚身 | 负面 | 999 | 每回合-0 / 受击-0 / 行动-0 | 负面状态。回合开始时，每层受到最大生命值0.5%+1点真实伤害，随后移除此状态。 | 类别：负面；稀有度：稀有/2；脚本：调用 `SunExp_RegisterHook`、`SunExp_IsHookTokenActive`、`SunExp_TriggerBodyBurn`、`SunExp_ClearHook` |  |
| `ember_cloak` | 烬衣 | 能力 | 1 | 每回合-1 / 受击-0 / 行动-0 | 临时避灼。获得时清除自身灼烧；下回合开始时再次清除自身灼烧，然后移除此状态。 | 类别：能力；稀有度：稀有/2；脚本：移除/吸收状态；调用 `SunExp_SetBurnWardPending`、`SunExp_RegisterHook`、`SunExp_IsHookTokenActive`、`SunExp_IsBurnWardPending`、`SunExp_ClearHook` |  |
| `solar_crown` | 圣冕显化 | 能力 | 2 | 每回合-1 / 受击-0 / 行动-0 | 持续2回合。期间日耀倍率变为2。打出日耀牌时按日耀层数触发：1+清除自身所有负面 Buff 并转化为等量灼烧；4+抽1张牌；8+获得1点魔能；12+清除自身灼烧并获得等量聚炎；15+全体获得5层灼烧并立即生效一次。高层包含低层。 | 类别：能力；稀有度：史诗/3；脚本：无特殊脚本/见描述 |  |
| `origin_core_radiance` | 源核：日耀 | 能力 | 1 | 每回合-0 / 受击-0 / 行动-0 | 每回合第一次获得{SunExp_sunexp_solar_radiance}时，额外获得1层{SunExp_sunexp_solar_radiance}。 | 类别：能力；稀有度：稀有/2；脚本：日耀层数；调用 `SunExp_RegisterHook`、`SunExp_IsHookTokenActive`、`SunExp_ClearHook` |  |
| `cycle_gathered_flame` | 轮转：聚炎 | 能力 | 1 | 每回合-0 / 受击-0 / 行动-0 | 存在时，自身{buff_burn}每增加1层，获得1层{SunExp_sunexp_gathered_flame}。 | 类别：能力；稀有度：稀有/2；脚本：聚炎层数；调用 `SunExp_RegisterHook`、`SunExp_IsHookTokenActive`、`SunExp_ClearHook` |  |
| `afterglow_omen` | 残光病兆 | 能力 | 1 | 每回合-0 / 受击-0 / 行动-0 | 回合开始时，所有带有{buff_burn}的敌人获得等同于其{buff_burn}层数一半的{buff_vulnerability}。 | 类别：能力；稀有度：稀有/2；脚本：调用 `SunExp_RegisterHook`、`SunExp_IsHookTokenActive`、`SunExp_AddStatusBuff`、`SunExp_ClearHook` |  |
| `wuna_ember` | 余烬 | 能力 | 99 | 每回合-0 / 受击-0 / 行动-0 | 乌娜专属资源。每层使乌娜造成的伤害提高1%；回合结束时层数减半。 | 类别：能力；稀有度：史诗/3；脚本：无特殊脚本/见描述 |  |

## 火漆 / 附着词条

| ID | 当前名称 | 添加标签 | 稀有度 | 所属 | 当前中文描述 | 当前功能摘要 | 修订稿 |
|---|---|---|---|---|---|---|---|
| `solar_keyword` | 日耀 | SunExpSolar（日耀卡牌词条） | 稀有/2 | 【日耀：星火】 (SunExp_sunexp_cardpack_radiant_spark) | 附着卡获得日耀词条。打出时按日耀规则结算。 | 附着卡获得 SunExpSolar（日耀卡牌词条）；使用时脚本：调用 `SunExp_HandleSolarEnchCardUsed` |  |

## 卡牌关键词

| ID | 当前关键词 | 是否显示 | 当前中文说明 | 修订稿 |
|---|---|---|---|---|
| `SunExpSolar` | 日耀 | TRUE | 带有此词条的卡牌打出后，若你没有{SunExp_sunexp_solar_crown}，获得等同于本次费用的{SunExp_sunexp_solar_radiance}；若你拥有{SunExp_sunexp_solar_crown}，触发一次{SunExp_sunexp_solar_crown}。部分卡牌会在卡面写明日耀系数伤害。 |  |

## 遗物 / 火漆包遗物

共 13 个遗物。

| ID | 名称 | 系列 | 稀有度 | 所属 | 当前中文描述 | 当前剧情文本 | 当前功能摘要 | 修订稿 |
|---|---|---|---|---|---|---|---|---|
| `morning_shard` | 晨辉碎片 | 日耀遗物 | 普通/1 | 【日耀：星火】 (SunExp_sunexp_cardpack_radiant_spark) | 战斗开始时，获得2层{SunExp_sunexp_solar_radiance}。每场战斗第一次回合开始时，若你拥有{SunExp_sunexp_solar_radiance}，获得5+日耀层数的护盾。 | 白曜圣庭从腐坏天幕中提炼出的第一缕无污染光。它曾嵌在曜日冕冠边缘，仪式失败后仍在余烬里发亮。<br>“若明日只剩这一点光，就先把它藏进掌心。” | 获得脚本：无特殊脚本/见描述；战斗脚本：护盾；日耀层数 |  |
| `ember_cloak_lining` | 烬衣衬布 | 日耀遗物 | 普通/1 | 【日耀：烬冠】 (SunExp_sunexp_cardpack_ember_crown) | 回合开始时，若你拥有{buff_burn}，移除1层{buff_burn}，并获得2层{SunExp_sunexp_gathered_flame}。每回合最多触发一次。 | 缝在圣女礼衣最内侧的衬布，用来吸收穿过乌娜身体的灼烧。布面越薄，灰金色的灼纹越密。<br>“火先经过我，再决定是否抵达众人。” | 获得脚本：无特殊脚本/见描述；战斗脚本：移除/吸收状态；聚炎层数 |  |
| `sun_orbit_mirror` | 环日镜 | 日耀遗物 | 稀有/2 | 【日耀：星火】 (SunExp_sunexp_cardpack_radiant_spark) | 每行动3次，若你拥有{SunExp_sunexp_solar_radiance}，对随机敌人施加2层{buff_burn}；否则获得2层{SunExp_sunexp_solar_radiance}。 | 控制第二日轮照射角度的巨型镜阵核心。乌娜曾借它安排晨钟、净化时辰和守夜人的警戒线。<br>“光落在哪里，城中的时间就从哪里开始。” | 获得脚本：无特殊脚本/见描述；战斗脚本：施加/转化灼烧；日耀层数；调用 `SunExp_AddBurnToRandomEnemy` |  |
| `sun_bottle` | 太阳瓶 | 日耀遗物 | 稀有/2 | 【日耀：天幕】 (SunExp_sunexp_cardpack_solar_canopy) | 回合开始时，随机一名带有{buff_burn}的敌人，其{buff_burn}立刻生效一次；随后移除其1层{buff_burn}，你获得2层{SunExp_sunexp_gathered_flame}。若没有敌人拥有{buff_burn}，改为对随机敌人施加2层{buff_burn}。 | 曜日魔女用来收集敌阵余焰的器皿。瓶口会寻找腐坏之物身上的灼烧，先让火生效，再把残热带回乌娜手中。<br>“腐坏借光蔓延，我便借它们身上的火归还终焉。” | 获得脚本：无特殊脚本/见描述；战斗脚本：施加/转化灼烧；立即触发灼烧；移除/吸收状态；聚炎层数；调用 `SunExp_GetRandomEnemyTarget`、`SunExp_TriggerBurn`、`SunExp_RemoveBuffStacks`、`SunExp_AddBurnToRandomEnemy` |  |
| `solar_phase_dial` | 日相刻盘 | 日耀遗物 | 史诗/3 | 【日耀：星火】 (SunExp_sunexp_cardpack_radiant_spark) | 每场战斗中，{SunExp_sunexp_solar_radiance}首次达到4/8/12层时分别触发：抽1张牌、获得1点魔能、清除自身{buff_burn}并使敌方全体{buff_burn}立刻生效一次。 | 记录第二日轮晨、午、冕三相的刻盘。每一格刻度都对应一段祈祷时辰，也对应乌娜能承受的一次日耀偏移。<br>“把光推过下一道刻度，愿灾厄晚一步抵达。” | 获得脚本：无特殊脚本/见描述；战斗脚本：抽牌；立即触发灼烧；移除/吸收状态；调用 `SunExp_TriggerBurnAllEnemies` |  |
| `miniature_sunwheel` | 小型日轮 | 日耀遗物 | 史诗/3 | 【日耀：烬冠】 (SunExp_sunexp_cardpack_ember_crown) | 回合开始时，若你拥有{SunExp_sunexp_scorching_canopy}，获得等同于其层数×3的护盾；随后若自身拥有{buff_burn}，将1层转化为{SunExp_sunexp_gathered_flame}，否则获得1层{SunExp_sunexp_solar_radiance}。 | 乌娜从第二日轮仪式盘上拆下的缩影。她把它贴在胸前，让天幕坠下的热先绕成一圈可以承受的墙。<br>“正午缩进掌中，至少还能挡住一轮灾光。” | 获得脚本：无特殊脚本/见描述；战斗脚本：护盾；移除/吸收状态；日耀层数；聚炎层数；天幕场地 |  |
| `blazing_crown_heart` | 炽冠圣心 | 日耀遗物 | 传说/4 | 【日耀：天幕】 (SunExp_sunexp_cardpack_solar_canopy) | 战斗开始时，获得1层{SunExp_sunexp_solar_crown}、4层{SunExp_sunexp_solar_radiance}、2层{SunExp_sunexp_scorching_canopy}。回合开始时，全体获得来自炽灼天幕的{buff_burn}；若你拥有{SunExp_sunexp_ember_cloak}或{SunExp_sunexp_solar_radiance}达到12层，本次自身不获得该{buff_burn}，且敌方全体额外获得1层{buff_burn}。 | 破碎曜日冕冠与乌娜心脏熔合后的魔女核心。每一次跳动，都会让炽灼天幕承认她仍在裁定灾厄的流向。<br>“圣女的名字烧穿之后，剩下这颗太阳替她回答。” | 获得脚本：无特殊脚本/见描述；战斗脚本：施加/转化灼烧；日耀层数；天幕场地；圣冕；调用 `SunExp_ApplyFieldBuff`、`SunExp_IsSelfBurnProtected`、`SunExp_AddStatusBuff` |  |
| `solar_prism` | 日心棱镜 | 日耀遗物 | 普通/1 | 【日耀：星火】 (SunExp_sunexp_cardpack_radiant_spark) | 战斗开始时，获得1层{SunExp_sunexp_solar_radiance}。每回合第一次获得{SunExp_sunexp_solar_radiance}后，获得1层{buff_extraordinary}。 | 封有微型日核的棱镜，用于检验第二日轮的光是否被深渊污染。纯净的光会折出晨色，带污的光会在棱面里发黑。<br>“只要还能折出晨光，圣庭就还有一次祈祷。” | 获得脚本：无特殊脚本/见描述；战斗脚本：日耀层数 |  |
| `coronation_throne` | 授冕圣座 | 日耀遗物 | 稀有/2 | 【日耀：星火】 (SunExp_sunexp_cardpack_radiant_spark) | 每场战斗第一次获得{SunExp_sunexp_solar_crown}后，抽1张牌并获得2点护盾。 | 乌娜与第二日轮相连的仪式节点。灾厄入冠的那一夜，她坐在圣座上承受炽冕崩落，直到座基和名字一同烧穿。<br>“请让冠冕落下。若它太重，就由我先承受。” | 获得脚本：无特殊脚本/见描述；战斗脚本：护盾；抽牌；圣冕 |  |
| `gathered_flame_charm` | 聚炎护符 | 日耀遗物 | 普通/1 | 【日耀：烬冠】 (SunExp_sunexp_cardpack_ember_crown) | 每回合第一次自身{buff_burn}层数增加后，获得2层{SunExp_sunexp_gathered_flame}。 | 贴着乌娜脉搏运转的护符，收拢新增的灼痕和祈愿残响。火势被一层层压入符心，沉成更重的聚炎。<br>“不要让痛散开。收住它，它就还能替别人发光。” | 获得脚本：无特殊脚本/见描述；战斗脚本：聚炎层数 |  |
| `ash_charm` | 灰烬护符 | 日耀遗物 | 稀有/2 | 【日耀：烬冠】 (SunExp_sunexp_cardpack_ember_crown) | 回合开始时，若你拥有至少4层{buff_burn}，移除2层，获得2点护盾和2层{SunExp_sunexp_gathered_flame}。 | 由烧尽的礼拜签和圣庭灰烬压成的护符。乌娜体内灼烧过盛时，它会从火里剥出一层可用的余温，留下短暂护持。<br>“灰里仍有名字。别让它们第二次熄灭。” | 获得脚本：无特殊脚本/见描述；战斗脚本：护盾；移除/吸收状态；聚炎层数 |  |
| `blazing_sundial` | 曜阳日晷 | 日耀遗物 | 普通/1 | 【日耀：天幕】 (SunExp_sunexp_cardpack_solar_canopy) | 回合开始时，若敌方全体拥有{buff_burn}，敌方全体获得1层{buff_weak}。 | 立在炽灼天幕影下的日晷，刻针只在所有敌影都沾火时转动。它会压低战场的呼吸，让腐坏之物在迟缓的正午里暴露。<br>“等每一道影子都燃起，审判才开始计时。” | 获得脚本：无特殊脚本/见描述；战斗脚本：调用 `SunExp_AddStatusBuff` |  |
| `burning_calamity_wind_belt` | 燃灾风带 | 日耀遗物 | 稀有/2 | 【日耀：天幕】 (SunExp_sunexp_cardpack_solar_canopy) | 回合开始时，至多4名带有{buff_burn}的敌人各使随机另一名敌人获得1层{buff_burn}。 | 环绕曜日魔女而行的风带，专门搬运战场上已经点燃的灾厄。它绕开未燃之物，把一处腐坏的火势送向下一处阴影。<br>“起火之处无须追问，终焉要看它流向谁。” | 获得脚本：无特殊脚本/见描述；战斗脚本：施加/转化灼烧；调用 `SunExp_AddStatusBuff` |  |

## 卡包

| ID | 当前名称 | 当前中文描述 | 修订稿 |
|---|---|---|---|
| `cardpack_radiant_spark` | 【日耀：星火】 | 【日耀：星火】基础卡包。提供日耀、聚炎、烬衣与圣冕入口，并保留低复杂度的灼烧转换。 |  |
| `cardpack_ember_crown` | 【日耀：烬冠】 | 【日耀：烬冠】扩展卡包。围绕自身灼烧、聚炎叠层与圣冕爆发展开，需要管理自燃压力。 |  |
| `cardpack_solar_canopy` | 【日耀：天幕】 | 【日耀：天幕】扩展卡包。围绕炽灼天幕、敌方灼烧、负面状态与持续扩散展开。 |  |

## 源文件索引

- 卡牌：`SunExp/Data/Card/sunexp.csv`、`SunExp/Text/Card/sunexp.csv`、`SunExp/Data/Card/wuna.csv`、`SunExp/Text/Card/wuna.csv`
- Buff：`SunExp/Data/Buff/sunexp.csv`、`SunExp/Text/Buff/sunexp.csv`、`SunExp/Data/Buff/wuna.csv`、`SunExp/Text/Buff/wuna.csv`
- 火漆/附着词条：`SunExp/Data/EnchTag/sunexp.csv`、`SunExp/Text/EnchTag/sunexp.csv`
- 卡牌关键词：`SunExp/Text/KeyWordsDic/sunexp.csv`
- 遗物：`SunExp/Data/Relic/sunexp.csv`、`SunExp/Text/Relic/sunexp.csv`
- 卡包：`SunExp/Data/CardPack/sunexp.csv`、`SunExp/Text/CardPack/sunexp.csv`
