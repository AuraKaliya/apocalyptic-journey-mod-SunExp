# SunExp 卡牌与遗物简要清单

## 概览

- Mod：SunExp v0.1.2
- 作者：Aura
- 卡牌数量：30
- 遗物数量：13
- 核心机制：日耀、炽灼天幕、聚炎、灼烧、自燃管理、圣冕显化。

## 卡包

- 【日耀：星火】 (`cardpack_radiant_spark`)：日耀：烬冠天幕的基础卡包。提供日耀、聚炎、基础防护与日耀：授冕入口，并保留少量低复杂度灼烧转换。
- 【日耀：烬冠】 (`cardpack_ember_crown`)：围绕自身灼烧、聚炎叠层和爆发兑现展开。需要管理自燃压力，并借助圣冕阶段提高收益与安全性。
- 【日耀：天幕】 (`cardpack_solar_canopy`)：围绕敌方灼烧、负面 Buff 与 DOT 扩散展开。通过天幕场域压低敌方状态，并把灼烧变成持续收益。

## 术语

- 日耀 (`{SunExp_sunexp_solar_radiance}`)：核心聚能。每次行动时，获得等同于5倍日耀层数的超凡。圣冕显化期间，日耀阶段向下包括，高层同时拥有低层收益。
- 聚炎 (`{SunExp_sunexp_gathered_flame}`)：无上限聚能。回合开始时，自己获得等同于聚炎层数的灼烧；烬衣或圣冕顶层阶段可抵消这次自燃。
- 炽灼天幕 (`{SunExp_sunexp_scorching_canopy}`)：场地聚热。每轮回合开始时，全体获得等同于炽灼天幕层数的灼烧。
- 烬衣 (`{SunExp_sunexp_ember_cloak}`)：临时避灼。获得时清除自身灼烧；下回合开始时再次清除自身灼烧，然后移除此状态。
- 圣冕显化 (`{SunExp_sunexp_solar_crown}`)：持续2回合。期间日耀阶段向下包括：1+吸收灼烧额外聚炎；4+日耀伤害增幅；8+行动时获得魔能，若未达12层则自燃；12层清除并免疫自身灼烧。炽冕崩落会强制结束。
- 源核：日耀 (`{SunExp_sunexp_origin_core_radiance}`)：每回合第一次获得{SunExp_sunexp_solar_radiance}时，额外获得1层{SunExp_sunexp_solar_radiance}。
- 轮转：聚炎 (`{SunExp_sunexp_cycle_gathered_flame}`)：当存在时，自身{buff_burn}每增加1层，获得1层{SunExp_sunexp_gathered_flame}。
- 残光病兆 (`{SunExp_sunexp_afterglow_omen}`)：回合开始时，所有带有{buff_burn}的敌人获得1层{buff_vulnerability}。

## 卡牌

1. 星火 (`spark`)
   - 基本数据：攻击牌；稀有度 1；费用 0；标签 无；卡包 【日耀：星火】。
   - 效果：造成5点伤害，给予目标1层{buff_burn}。获得1层{SunExp_sunexp_solar_radiance}。

2. 灼热天幕 (`scorching_canopy_card`)
   - 基本数据：技能牌；稀有度 1；费用 1；标签 无；卡包 【日耀：天幕】。
   - 效果：获得1层{SunExp_sunexp_scorching_canopy}。全体获得2层{buff_burn}。

3. 耀焰斩 (`radiant_flame_slash`)
   - 基本数据：攻击牌；稀有度 1；费用 1；标签 无；卡包 【日耀：星火】。
   - 效果：造成10点伤害。每有4层{SunExp_sunexp_solar_radiance}，额外造成3点伤害。圣冕显化且日耀不少于4层时，再额外增加等同日耀层数的伤害。

4. 烬衣 (`ember_cloak_card`)
   - 基本数据：技能牌；稀有度 1；费用 0；标签 无；卡包 【日耀：星火】。
   - 效果：获得{SunExp_sunexp_ember_cloak}：清除自身{buff_burn}，获得等同于清除层数一半的护盾，并在下回合开始时再次清除自身{buff_burn}。

5. 引炎 (`draw_flame`)
   - 基本数据：技能牌；稀有度 1；费用 1；标签 无；卡包 【日耀：烬冠】。
   - 效果：吸收目标至多6层{buff_burn}，移除等量{buff_burn}，获得等量{SunExp_sunexp_gathered_flame}。圣冕显化期间，日耀阶段会向下包括并额外获得聚炎。

6. 日耀聚焦 (`solar_prayer`)
   - 基本数据：技能牌；稀有度 2；费用 0；标签 无；卡包 【日耀：星火】。
   - 效果：获得3层{SunExp_sunexp_solar_radiance}。若已有{SunExp_sunexp_scorching_canopy}，抽1张牌；若已有{SunExp_sunexp_solar_crown}，获得1层{SunExp_sunexp_gathered_flame}。

7. 燃星之咒 (`burning_star_hex`)
   - 基本数据：攻击牌；稀有度 2；费用 1；标签 无；卡包 【日耀：烬冠】。
   - 效果：造成6点伤害。消耗至多5层{SunExp_sunexp_gathered_flame}，每层额外造成4点伤害。给予目标2层{buff_burn}，每有4层{SunExp_sunexp_solar_radiance}额外+1层。圣冕显化且日耀不少于4层时，额外增加等同日耀层数的伤害。

8. 冠冕威光 (`crown_radiance`)
   - 基本数据：技能牌；稀有度 2；费用 1；标签 无；卡包 【日耀：天幕】。
   - 效果：敌方全体获得4层{buff_burn}。若{SunExp_sunexp_solar_radiance}不少于8层，改为6层。若你拥有{SunExp_sunexp_scorching_canopy}，随机敌人的{buff_burn}立刻生效一次。

9. 天幕再临 (`canopy_return`)
   - 基本数据：技能牌；稀有度 2；费用 1；标签 无；卡包 【日耀：天幕】。
   - 效果：获得2层{SunExp_sunexp_scorching_canopy}。全体获得3层{buff_burn}。若{SunExp_sunexp_solar_radiance}不少于4层，敌方全体的{buff_burn}立刻生效一次。

10. 日相校准 (`solar_phase_tuning`)
   - 基本数据：技能牌；稀有度 2；费用 1；标签 无；卡包 【日耀：星火】。
   - 效果：获得3层{SunExp_sunexp_solar_radiance}。吸收自身至多6层{buff_burn}，转化为等量{SunExp_sunexp_gathered_flame}。若吸收满6层，抽1张牌。

11. 日耀：授冕 (`solar_coronation`)
   - 基本数据：能力牌；稀有度 3；费用 2；标签 Ability；卡包 【日耀：星火】。
   - 效果：获得2层{SunExp_sunexp_solar_crown}和2层{SunExp_sunexp_solar_radiance}。圣冕阶段向下包括：高层会同时触发低层效果。

12. 炽冕崩落 (`blazing_crown_collapse`)
   - 基本数据：攻击牌；稀有度 3；费用 3；标签 无；卡包 【日耀：烬冠】。
   - 效果：爆发性一击。对所有敌人造成伤害；若没有{SunExp_sunexp_solar_crown}，自身也承受相同伤害。消耗全部{SunExp_sunexp_gathered_flame}和一半{SunExp_sunexp_solar_radiance}，基础40点；每消耗1层{SunExp_sunexp_gathered_flame}额外造成6点，每消耗1层{SunExp_sunexp_solar_radiance}额外造成8点。随后敌方全体的{buff_burn}立刻生效一次，结束{SunExp_sunexp_solar_crown}，并获得等同于消耗{SunExp_sunexp_gathered_flame}一半的{buff_burn}。

13. 破晓校准 (`radiant_oath`)
   - 基本数据：技能牌；稀有度 1；费用 0；标签 Burnout；卡包 【日耀：星火】。
   - 效果：获得3层{SunExp_sunexp_solar_radiance}。若你没有{SunExp_sunexp_scorching_canopy}，获得1层{SunExp_sunexp_scorching_canopy}；否则抽1张牌。

14. 聚光引燃 (`solar_ignition`)
   - 基本数据：技能牌；稀有度 1；费用 1；标签 无；卡包 【日耀：星火】。
   - 效果：获得1层{SunExp_sunexp_solar_radiance}。所有敌人获得2层{buff_burn}。若你拥有{SunExp_sunexp_scorching_canopy}，抽1张牌；若{SunExp_sunexp_solar_radiance}不少于4层，随机敌人的{buff_burn}立刻生效一次。

15. 灼流回收 (`scorching_flow_reclaim`)
   - 基本数据：攻击牌；稀有度 2；费用 0；标签 无；卡包 【日耀：烬冠】。
   - 效果：目标敌人的{buff_burn}立刻生效一次。随后吸收该目标所有{buff_burn}，移除这些{buff_burn}，你获得等量{SunExp_sunexp_gathered_flame}。若吸收不少于10层，抽1张牌。

16. 焚污除秽 (`impurity_purge`)
   - 基本数据：技能牌；稀有度 2；费用 1；标签 无；卡包 【日耀：天幕】。
   - 效果：移除自身所有负面 Buff，并获得等同于这些负面 Buff 总层数的{buff_burn}。若成功转化，获得1层{SunExp_sunexp_solar_radiance}。

17. 炎轮再临 (`flamewheel_recurrence`)
   - 基本数据：技能牌；稀有度 2；费用 1；标签 无；卡包 【日耀：烬冠】。
   - 效果：敌方全体的{buff_burn}立刻生效N次，N为本场战斗已使用此牌次数+1。本次总耗费等于N。

18. 蚀天之咒 (`eclipse_hex`)
   - 基本数据：技能牌；稀有度 2；费用 2；标签 无；卡包 【日耀：天幕】。
   - 效果：使目标敌人的{buff_burn}层数翻倍，最高不超过49层；若目标没有{buff_burn}，改为施加6层。随后目标的{buff_burn}立刻生效一次。

19. 日耀灼光 (`solar_scorching_light`)
   - 基本数据：攻击牌；稀有度 1；费用 1；标签 无；卡包 【日耀：烬冠】。
   - 效果：造成8点伤害。目标每有1层{buff_burn}，额外造成X点伤害，X为自身{SunExp_sunexp_gathered_flame}层数/4，至少为1。

20. 燃灾 (`burning_calamity`)
   - 基本数据：技能牌；稀有度 2；费用 1；标签 无；卡包 【日耀：天幕】。
   - 效果：选择一个敌人，将其{buff_burn}层数的一半施加给其他所有敌人。随后该目标的{buff_burn}立刻生效一次。

21. 燃冠誓言 (`burning_crown_oath`)
   - 基本数据：技能牌；稀有度 2；费用 1；标签 无；卡包 【日耀：烬冠】。
   - 效果：消耗至多12层{SunExp_sunexp_gathered_flame}。敌方全体获得等同于消耗层数一半的{buff_burn}。若消耗不少于8层，抽1张牌并获得1点魔能。

22. 晨线护持 (`morning_light_bulwark`)
   - 基本数据：技能牌；稀有度 1；费用 1；标签 无；卡包 【日耀：星火】。
   - 效果：获得2层{SunExp_sunexp_solar_radiance}。获得4+当前日耀层数的护盾。

23. 光谱折返 (`solar_return`)
   - 基本数据：技能牌；稀有度 1；费用 0；标签 无；卡包 【日耀：星火】。
   - 效果：若你拥有{buff_burn}，移除1层并获得1层{SunExp_sunexp_solar_radiance}；否则抽1张牌。

24. 源核：日耀 (`solar_origin_core`)
   - 基本数据：能力牌；稀有度 2；费用 1；标签 Ability；卡包 【日耀：星火】。
   - 效果：获得{SunExp_sunexp_origin_core_radiance}。

25. 凝烬成塔 (`ember_tower`)
   - 基本数据：技能牌；稀有度 1；费用 1；标签 无；卡包 【日耀：烬冠】。
   - 效果：将自身至多5层{buff_burn}转化为等量{SunExp_sunexp_gathered_flame}。若转化了5层，抽1张牌。

26. 聚炎护盾 (`gathered_flame_shield`)
   - 基本数据：技能牌；稀有度 1；费用 1；标签 无；卡包 【日耀：烬冠】。
   - 效果：获得{SunExp_sunexp_gathered_flame}层数×2的护盾，随后消耗至多4层{SunExp_sunexp_gathered_flame}。

27. 轮转：聚炎 (`gathered_flame_cycle`)
   - 基本数据：能力牌；稀有度 2；费用 2；标签 Ability；卡包 【日耀：烬冠】。
   - 效果：获得{SunExp_sunexp_cycle_gathered_flame}。

28. 日蚀 (`solar_eclipse`)
   - 基本数据：技能牌；稀有度 1；费用 1；标签 无；卡包 【日耀：天幕】。
   - 效果：敌方全体获得2层{buff_burn}和1层{buff_weak}。

29. 烟蚀 (`smoke_erosion`)
   - 基本数据：攻击牌；稀有度 1；费用 1；标签 无；卡包 【日耀：天幕】。
   - 效果：造成7点伤害。若目标拥有负面 Buff，给予3层{buff_burn}。

30. 残光病兆 (`afterglow_omen_card`)
   - 基本数据：能力牌；稀有度 2；费用 2；标签 Ability；卡包 【日耀：天幕】。
   - 效果：获得{SunExp_sunexp_afterglow_omen}。

## 遗物

1. 晨辉碎片 (`morning_shard`)
   - 基本数据：系列 日耀遗物；标签 无；稀有度 1；卡包 【日耀：星火】。
   - 剧情短句：清晨第一缕光凝成的晶片，握在掌心时仍有温度。
   - 效果：战斗开始时，获得2层{SunExp_sunexp_solar_radiance}。每场战斗第一次回合开始时，若你拥有{SunExp_sunexp_solar_radiance}，获得5+日耀层数的护盾。

2. 烬衣衬布 (`ember_cloak_lining`)
   - 基本数据：系列 日耀遗物；标签 无；稀有度 1；卡包 【日耀：烬冠】。
   - 剧情短句：薄得像一层晨雾，却能把火焰折回光里。
   - 效果：回合开始时，若你拥有{buff_burn}，移除1层{buff_burn}，并获得2层{SunExp_sunexp_gathered_flame}。每回合最多触发一次。

3. 环日镜 (`sun_orbit_mirror`)
   - 基本数据：系列 日耀遗物；标签 无；稀有度 2；卡包 【日耀：星火】。
   - 剧情短句：镜面里没有倒影，只有一颗永远绕行的太阳。
   - 效果：每行动3次，若你拥有{SunExp_sunexp_solar_radiance}，对随机敌人施加2层{buff_burn}；否则获得2层{SunExp_sunexp_solar_radiance}。

4. 太阳瓶 (`sun_bottle`)
   - 基本数据：系列 日耀遗物；标签 无；稀有度 2；卡包 【日耀：天幕】。
   - 剧情短句：漏斗内壁刻着倒流的火舌，专门收拢失控的热。
   - 效果：回合开始时，随机一名带有{buff_burn}的敌人，其{buff_burn}立刻生效一次；随后移除其1层{buff_burn}，你获得2层{SunExp_sunexp_gathered_flame}。若没有敌人拥有{buff_burn}，改为对随机敌人施加2层{buff_burn}。

5. 日相刻盘 (`solar_phase_dial`)
   - 基本数据：系列 日耀遗物；标签 无；稀有度 3；卡包 【日耀：星火】。
   - 剧情短句：刻盘的每一格都对应一次太阳角度的偏移。
   - 效果：每场战斗中，{SunExp_sunexp_solar_radiance}首次达到4/8/12层时分别触发：抽1张牌、获得1点魔能、清除自身{buff_burn}并使敌方全体{buff_burn}立刻生效一次。

6. 小型日轮 (`miniature_sunwheel`)
   - 基本数据：系列 日耀遗物；标签 无；稀有度 3；卡包 【日耀：烬冠】。
   - 剧情短句：罩住城邦的不是玻璃，而是被固定下来的正午。
   - 效果：回合开始时，若你拥有{SunExp_sunexp_scorching_canopy}，获得等同于其层数×3的护盾；随后若自身拥有{buff_burn}，将1层转化为{SunExp_sunexp_gathered_flame}，否则获得1层{SunExp_sunexp_solar_radiance}。

7. 炽冠圣心 (`blazing_crown_heart`)
   - 基本数据：系列 日耀遗物；标签 无；稀有度 4；卡包 【日耀：天幕】。
   - 剧情短句：它不像遗物，更像一颗被迫保持安静的小型太阳。
   - 效果：战斗开始时，获得1层{SunExp_sunexp_solar_crown}、4层{SunExp_sunexp_solar_radiance}、2层{SunExp_sunexp_scorching_canopy}。回合开始时，全体获得来自炽灼天幕的{buff_burn}；若你拥有{SunExp_sunexp_ember_cloak}或{SunExp_sunexp_solar_radiance}达到12层，本次自身不获得该{buff_burn}，且敌方全体额外获得1层{buff_burn}。

8. 日心棱镜 (`solar_prism`)
   - 基本数据：系列 日耀遗物；标签 无；稀有度 1；卡包 【日耀：星火】。
   - 剧情短句：棱镜中心封着一枚微型日核，转动时会折出第二道晨光。
   - 效果：战斗开始时，获得1层{SunExp_sunexp_solar_radiance}。每回合第一次获得{SunExp_sunexp_solar_radiance}后，获得1层{buff_extraordinary}。

9. 授冕圣座 (`coronation_throne`)
   - 基本数据：系列 日耀遗物；标签 无；稀有度 2；卡包 【日耀：星火】。
   - 剧情短句：它不产生光，只负责让真正的冠冕安稳降下。
   - 效果：每场战斗第一次获得{SunExp_sunexp_solar_crown}后，抽1张牌并获得2点护盾。

10. 聚炎护符 (`gathered_flame_charm`)
   - 基本数据：系列 日耀遗物；标签 无；稀有度 1；卡包 【日耀：烬冠】。
   - 剧情短句：护符里的火没有出口，只能向内凝成更密的热。
   - 效果：每回合第一次自身{buff_burn}层数增加后，获得2层{SunExp_sunexp_gathered_flame}。

11. 灰烬护符 (`ash_charm`)
   - 基本数据：系列 日耀遗物；标签 无；稀有度 2；卡包 【日耀：烬冠】。
   - 剧情短句：阀门每次开启都像一次短促的日出。
   - 效果：回合开始时，若你拥有至少4层{buff_burn}，移除2层，获得2点护盾和2层{SunExp_sunexp_gathered_flame}。

12. 曜阳日晷 (`blazing_sundial`)
   - 基本数据：系列 日耀遗物；标签 无；稀有度 1；卡包 【日耀：天幕】。
   - 剧情短句：它把天空压低一点，让火焰和呼吸都变得迟缓。
   - 效果：回合开始时，若敌方全体拥有{buff_burn}，敌方全体获得1层{buff_weak}。

13. 燃灾风带 (`burning_calamity_wind_belt`)
   - 基本数据：系列 日耀遗物；标签 无；稀有度 2；卡包 【日耀：天幕】。
   - 剧情短句：环形热风总会把一处火星带到另一处阴影里。
   - 效果：回合开始时，至多4名带有{buff_burn}的敌人各使随机另一名敌人获得1层{buff_burn}。

