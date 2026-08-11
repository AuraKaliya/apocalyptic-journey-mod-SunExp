# Terrias 扩展内容总览

- 版本：Terrias `0.5.0`；本地内容更新于 2026-08-09；主体表基线为游戏构建 `1.0.23816797`。
- 口径：仅统计完整运行时 ID 以 `Terrias_` 开头的已加载内容；技术实现、Hook 和联机协议另见 [Terrias 技术文档](../Terrias/README.md)。
- 卡牌说明、Buff 公式和角色技能均保留当前中文显示文本。带有动态占位符的数值由实战状态或初始化脚本计算。
- 本页将【公开卡包内容】和【角色技能牌／系统模板／模式专用牌】分开，后者不应默认视为普通奖励池内容。

## 内容规模

| 类型 | 数量 | 玩家侧定位 |
|---|---:|---|
| 角色 | 3 | 乌娜、洛奈尔、哥伦比娅 |
| 使魔 | 3 | 黄昏、星泥人傀、桑多涅喵 |
| 卡包 | 3 | 1 个日耀、1 个晨星、1 个异次元卡包 |
| 卡牌 | 60 | 43 张卡包归属牌；17 张角色／系统／模式牌 |
| Buff | 46 | 正面、负面、能力、契印、特性与场地 |
| 遗物 | 13 | 日耀合并卡包配套遗物 |
| 祝福 | 11 | 3 个伙伴占位、4 个本源升华与 4 个日耀祝福 |
| 火漆 | 3 | 白曜、阳炣、启明星 |
| 难度词条 | 10 | Terrias 与异次元主题规则 |
| 专属敌人／意图 | 3／19 | 日耀回忆固定 Boss 与专属出招 |

## 核心玩法

| 体系 | 核心资源 | 主要循环 |
|---|---|---|
| 日耀 | 日耀、聚炎、余烬、烬衣、圣冕、炽灼天幕 | 施加或触发灼烧，转化聚炎与余烬，再以圣冕和场地完成爆发。 |
| 晨星 | 星谱、伏谱、谱句、连音、启明星、星石袋 | 控制牌序与费用，完成【启承转合】谱句并复奏，借白石／黑石管理奇迹时钟。 |
| 月之少女 | 重力涟漪、月之领域、月感电／月绽放／月结晶 | 以获得卡牌压缩技能冷却，在月之领域中强化月系联动。 |
| 更多次元 | 百变、投影、心变、精灵球 | 复制角色、召唤投影、控制敌人与捕获精灵，扩展战斗单位与身份玩法。 |
| 无尽之渊 | 注视、深渊震荡、裂隙、绝灭、进化 | 每层或每战承担代价换取成长；第 7 层起进入无尽阶段。 |

## 角色技能

### 哥伦比娅 · 月の少女（`Terrias_columbina_columbina`）

- SAN 上限：95。

| 主动技能 | 对应技能牌 | 冷却 | 效果 |
|---|---|---:|---|
| 万古潮汐 | `Terrias_columbina_columbina_eternal_tide` | 3 | 获得20层{Terrias_terrias_gravity_ripple}。 |
| 她的乡愁 | `Terrias_columbina_columbina_homesickness` | 7 | 对敌方全体造成自身最大生命值30%的水元素伤害，然后铺设1层{Terrias_terrias_moon_domain}。 |

| 被动 | 效果 |
|---|---|
| 新月法则 | 每获得1张牌，【她的乡愁】冷却-1。月之领域中：月感电额外重复1次月伤害；月绽放获得2点魔能；月结晶额外增加1次计数。 |

### 洛奈尔 · 晨星魔女（`Terrias_loneer_loneer`）

- SAN 上限：72。

| 主动技能 | 对应技能牌 | 冷却 | 效果 |
|---|---|---:|---|
| 晨星祈愿 | `Terrias_loneer_loneer_morning_star_prayer` | 2 | 触发【自然晨星】，然后令【星石袋】中黑石的上限在本场战斗中-2，最低为1。 |

| 被动 | 效果 |
|---|---|
| 奇迹的时钟 | 战斗开始时，获得12层【奇迹时钟】。 |
| 星石定轨 | 战斗开始时，获得【星石袋】并从抽牌堆与弃牌堆中选择一张牌作为【指引牌】；若没有可选牌，则指引为隐藏的【魔女的星谱】。当【星石袋】抽中白石，触发【自然晨星】；抽中黑石，则令【奇迹时钟】层数-1。 |

### 乌娜 · 曜日魔女（`Terrias_wuna_wuna`）

- SAN 上限：70。

| 主动技能 | 对应技能牌 | 冷却 | 效果 |
|---|---|---:|---|
| 白曜圣祷 | `Terrias_wuna_wuna_white_sun_prayer` | 5 | 获得一张0费的“日耀：授冕”，附着焚毁和凝滞。随后给己方全体的所有手牌添加焚毁和白曜。 |
| 圣庭墓曲 | `Terrias_wuna_wuna_grave_song` | 4 | 余烬大于30时可用。消耗所有余烬，使全体获得余烬/2层数的灼烧，自身获得1层烬衣，然后立刻触发一次灼烧。 |

| 被动 | 效果 |
|---|---|
| 于灰烬中重生 | 每回合一次，当敌方灼烧总层数增加时，获得1层日耀，日耀上限扩展至15层。回合开始时，获得全体灼烧总层数一半的余烬。当消耗余烬时，恢复消耗的余烬层数百分比的最大血量，并提高余烬层数等量的血量上限。 |

## 使魔与绑定祝福

| 使魔 | 使魔 ID | 绑定祝福 | 实际效果 |
|---|---|---|---|
| 黄昏 | `Terrias_terrias_dusk` | 余热回收（`Terrias_terrias_dusk_afterheat_recovery`） | 每当敌人{buff_burn}触发时，获得其{buff_burn}层数1/3的{Terrias_terrias_ember}与{Terrias_terrias_gathered_flame}。 |
| 桑多涅喵 | `Terrias_terrias_sandrone_cat` | 哥！伦！比！娅！（`Terrias_terrias_sandrone_cat_placeholder`） | 每场战斗结束时，增加自身1+4%生命值上限。 |
| 星泥人傀 | `Terrias_terrias_star_clay_doll` | 星泥傀身（`Terrias_terrias_star_clay_doll_placeholder`） | 战斗开始时，获得1层{Terrias_terrias_star_clay_body}。每次行动后，获得1层{Terrias_terrias_starlight}。 |

## 三个卡包

| 卡包 | 运行时 ID | 卡牌数 | 定位 |
|---|---|---:|---|
| 【日耀：烬冠天幕】 | `Terrias_terrias_cardpack_solar_ember_crown_canopy` | 30 | 整合日耀、聚焰、烬衣、圣冕与炽灼天幕体系，兼顾自身灼烧管理、授冕爆发与敌方灼烧扩散。 |
| 【晨星：序曲】 | `Terrias_terrias_cardpack_morning_star_overture` | 8 | 【晨星：序曲】机制卡包。围绕星谱、伏谱、连音、谱曲与启明星展开，强调牌序、费用节奏和谱句复奏。 |
| 【更多的次元】 | `Terrias_terrias_cardpack_more_dimensions` | 5 | 异次元机制卡包。提供百变、投影、心变、精灵球与特殊奖励牌入口。 |

### 【日耀：烬冠天幕】（30 张）

以下按原设计分组保留阅读结构，30 张牌均归属同一个合并卡包。

【耀焰斩】【太阳圣祷】【炎轮再临】【浴火】均附着二阶火漆【阳炣】。

#### 星火组（13 张）

| 卡牌 ID | 名称 | 类型 | 稀有度 | 费用 | 效果 |
|---|---|---|---:|---:|---|
| `Terrias_terrias_ember_cloak_card` | 烬衣 | 技能牌 | 1 | 0 | 获得等同自身{buff_burn}和{Terrias_terrias_body_burn}总层数一半的护盾，然后获得{Terrias_terrias_ember_cloak}。 |
| `Terrias_terrias_morning_light_bulwark` | 晨光壁垒 | 技能牌 | 1 | 1 | 获得{0}点护盾。 |
| `Terrias_terrias_radiant_flame_slash` | 耀焰斩 | 攻击牌 | 1 | 1 | 造成{0}（{1}+{2}*{Terrias_terrias_solar_coefficient}）点伤害。 |
| `Terrias_terrias_radiant_oath` | 启辉誓言 | 技能牌 | 1 | 0 | 获得3层{Terrias_terrias_solar_radiance}。若你没有{Terrias_terrias_scorching_canopy}，获得1层{Terrias_terrias_scorching_canopy}；否则抽1张牌。 |
| `Terrias_terrias_solar_ignition` | 日耀：引燃 | 技能牌 | 1 | 1 | 敌方全体获得2层{buff_burn}，并立刻生效一次。 |
| `Terrias_terrias_spark` | 星火 | 攻击牌 | 1 | 0 | 造成{0}点伤害。给予目标2层{buff_burn}。获得1层{Terrias_terrias_solar_radiance}。 |
| `Terrias_terrias_solar_origin_core` | 被燃尽的名字 | 技能牌 | 2 | 0 | 焚毁当前所有手牌，获得等量魔能。 |
| `Terrias_terrias_solar_prayer` | 太阳圣祷 | 技能牌 | 2 | 1 | 获得2层{Terrias_terrias_solar_radiance}。将自身的{buff_burn}全部转移给随机一名友方单位。 |
| `Terrias_terrias_solar_return` | 日耀：回转 | 技能牌 | 2 | 0 | 获得1层{Terrias_terrias_solar_radiance}，然后抽1张牌。 |
| `Terrias_terrias_solar_scorching_light` | 浴火 | 技能牌 | 2 | 1 | 自身的{buff_burn}立即生效一次，并给予敌方全体翻倍数量的{buff_burn}。 |
| `Terrias_terrias_afterglow_omen_card` | 圣庭净裁 | 技能牌 | 3 | 5 | 消除目标除{buff_burn}和{Terrias_terrias_body_burn}外的所有 Buff，每消除一种，给予目标1层{buff_burn}；若没有{Terrias_terrias_solar_crown}，自身承受同样的效果。 |
| `Terrias_terrias_solar_coronation` | 日耀：授冕 | 能力牌 | 3 | 2 | 获得3层{Terrias_terrias_solar_radiance}和2层{Terrias_terrias_solar_crown}。 |
| `Terrias_terrias_solar_phase_tuning` | 被珍藏的名字 | 技能牌 | 3 | 1 | 弃置手中所有牌，获得等同弃置牌数的{Terrias_terrias_solar_radiance}，然后抽3张牌。 |

#### 烬冠组（9 张）

| 卡牌 ID | 名称 | 类型 | 稀有度 | 费用 | 效果 |
|---|---|---|---:|---:|---|
| `Terrias_terrias_draw_flame` | 引炎 | 攻击牌 | 1 | 1 | 吸收任意目标的所有{buff_burn}，转化为等量的{Terrias_terrias_gathered_flame}。 |
| `Terrias_terrias_ember_tower` | 凝烬成塔 | 技能牌 | 1 | 1 | 将自身所有{Terrias_terrias_ember}和{buff_burn}转化为等量{Terrias_terrias_gathered_flame}。每转化满5层，抽1张牌。 |
| `Terrias_terrias_burning_crown_oath` | 燃冠誓言 | 技能牌 | 2 | 1 | 消耗自身所有{Terrias_terrias_gathered_flame}。敌方全体获得等同于消耗层数一半的{buff_burn}，并立即生效一次。 |
| `Terrias_terrias_burning_star_hex` | 燃星之咒 | 攻击牌 | 2 | 1 | 获得5层{Terrias_terrias_gathered_flame}，造成{0}（{1}+{2}*{Terrias_terrias_solar_coefficient}）点伤害。给予目标2层{buff_burn}。 |
| `Terrias_terrias_gathered_flame_cycle` | 聚炎轮转 | 能力牌 | 2 | 2 | 获得{Terrias_terrias_cycle_gathered_flame}。 |
| `Terrias_terrias_gathered_flame_shield` | 聚炎护盾 | 技能牌 | 2 | 1 | 消耗所有{Terrias_terrias_gathered_flame}，获得{0}点护盾。 |
| `Terrias_terrias_scorching_flow_reclaim` | 灼流回收 | 攻击牌 | 2 | 1 | 目标敌人的{buff_burn}立即生效1次。随后移除其身上所有{buff_burn}，获得等量{Terrias_terrias_gathered_flame}。 |
| `Terrias_terrias_blazing_crown_collapse` | 炽冕崩落 | 攻击牌 | 3 | 3 | 对敌方全体造成{0}（40+{Terrias_terrias_solar_crown_tier}*{Terrias_terrias_solar_coefficient}）点伤害。若没有{Terrias_terrias_solar_crown}，自身承受等额反噬。随后结束{Terrias_terrias_solar_crown}，消耗全部{Terrias_terrias_gathered_flame}，自身获得消耗聚炎层数一半的{buff_burn}。 |
| `Terrias_terrias_flamewheel_recurrence` | 炎轮再临 | 技能牌 | 3 | 1 | 敌方全体的{buff_burn}立即生效2*N次，N为本场战斗已使用此牌次数+1。本次总费用等于N。 |

#### 天幕组（8 张）

| 卡牌 ID | 名称 | 类型 | 稀有度 | 费用 | 效果 |
|---|---|---|---:|---:|---|
| `Terrias_terrias_scorching_canopy_card` | 灼热天幕 | 技能牌 | 1 | 1 | 铺上1层{Terrias_terrias_scorching_canopy}场地，全体获得2层{buff_burn}。 |
| `Terrias_terrias_smoke_erosion` | 烟蚀 | 攻击牌 | 1 | 1 | 造成{0}（{1}+{2}*目标灼烧层数）点伤害。若目标拥有负面 Buff，给予2层{buff_burn}。 |
| `Terrias_terrias_solar_eclipse` | 日蚀 | 技能牌 | 1 | 1 | 敌方全体获得3层{buff_burn}。若场上存在{Terrias_terrias_scorching_canopy}，则额外施加1层{buff_rotten}，随机清除一种正面 Buff。 |
| `Terrias_terrias_burning_calamity` | 燃灾 | 攻击牌 | 2 | 1 | 选择一个敌人，将其{buff_burn}层数的一半施加给其他所有敌人。随后该目标的{buff_burn}立即生效一次。 |
| `Terrias_terrias_canopy_return` | 天幕再临 | 技能牌 | 2 | 1 | 获得2层{Terrias_terrias_scorching_canopy}。全体获得3层{buff_burn}。敌方全体{buff_burn}立即生效一次。 |
| `Terrias_terrias_eclipse_hex` | 蚀天之咒 | 攻击牌 | 2 | 2 | 对目标施加等同当前层数的{buff_burn}（最少8层），然后目标的{buff_burn}立即生效一次。 |
| `Terrias_terrias_impurity_purge` | 焚污除秽 | 技能牌 | 2 | 1 | 移除自身所有负面 Buff，并获得等同于这些负面 Buff 总层数的{buff_burn}。 |
| `Terrias_terrias_crown_radiance` | 冠冕威光 | 技能牌 | 3 | 2 | 敌方全体获得6层{buff_burn}。若场上存在{Terrias_terrias_scorching_canopy}，全体目标的{buff_burn}立即生效{Terrias_terrias_solar_crown_tier}层数等量次数。 |

### 【晨星：序曲】（8 张）

| 卡牌 ID | 名称 | 类型 | 稀有度 | 费用 | 效果 |
|---|---|---|---:|---:|---|
| `Terrias_terrias_blank_star_score` | 空白星谱 | 技能牌 | 1 | 1 | 清空当前{Terrias_terrias_star_score}，获得1层{Terrias_terrias_star_blessing}，然后抽1张牌。 |
| `Terrias_terrias_meter_rewrite` | 星律重订 | 技能牌 | 1 | 1 | 若当前{Terrias_terrias_star_score}不为空，将最后一音按启→承→转→合→启改写。 |
| `Terrias_terrias_prewritten_measure` | 星律锚定 | 技能牌 | 2 | 1 | 将【星辰序曲·承】写入伏谱。连音：将【星辰序曲·转】写入伏谱。 |
| `Terrias_terrias_rest_mark` | 休止符 | 技能牌 | 2 | 0 | 清空当前{Terrias_terrias_star_score}。每清空1音，获得1层{Terrias_terrias_resonance}，抽1张牌。 |
| `Terrias_terrias_star_orbit_transpose` | 星轨换位 | 技能牌 | 2 | 1 | 将当前{Terrias_terrias_star_score}清空，获得对应的【星辰序曲】。 |
| `Terrias_terrias_morning_star_stage` | 晨星：星台 | 技能牌 | 3 | 2 | 获得1层{Terrias_terrias_star_stage}。 |
| `Terrias_terrias_star_map` | 星图 | 技能牌 | 3 | 0 | 抽3张牌并附着【启明星】，然后选择3张卡牌焚毁。 |
| `Terrias_terrias_star_score_echo` | 晨星：复奏 | 技能牌 | 3 | 2 | 复奏最近一篇完成的谱句，然后进行【谱曲】。若尚未完成谱句，则获得【星辰序曲·启】、【星辰序曲·承】、【星辰序曲·转】各1张。 |

### 【更多的次元】（5 张）

| 卡牌 ID | 名称 | 类型 | 稀有度 | 费用 | 效果 |
|---|---|---|---:|---:|---|
| `Terrias_terrias_fate_star` | 命星 | 技能牌 | 3 | 1 | 若{Terrias_terrias_constellation}未达上限，点亮1层；否则四大本源上限增加10点。 |
| `Terrias_terrias_heart_change` | 心变 | 技能牌 | 3 | 1 | 仅当敌方场上有两名及以上敌人时生效。给予目标1层心变。 |
| `Terrias_terrias_polymorph` | 百变 | 技能牌 | 3 | 1 | 选择一个已注册角色，获得对应的一次性化身牌。化身只在本场战斗内生效。 |
| `Terrias_terrias_spirit_ball` | 精灵球 | 技能牌 | 3 | 1 | 对一名可捕获敌人进行捕获检定：基础成功率为10%，目标每损失1%生命，成功率提高0.8%，最高90%。成功时获得对应的【精灵卡】。 |
| `Terrias_terrias_witch_projection` | 拜托了 | 技能牌 | 3 | 1 | 获得一张【另一个我】，作为友方单位加入战斗。 |

### 角色、系统与模式专用牌（17 张）

这些牌包括角色主动技能、星谱派生牌、无尽诅咒、动态模板和特殊奖励。它们没有常规 `PackBelong`，不应按普通卡包掉落理解。

| 卡牌 ID | 名称 | 类型 | 费用 | 效果 |
|---|---|---|---:|---|
| `Terrias_columbina_columbina_eternal_tide` | 万古潮汐 | 职业技能 | 0 | 获得20层{Terrias_terrias_gravity_ripple}。 |
| `Terrias_columbina_columbina_homesickness` | 她的乡愁 | 职业技能 | 0 | 对敌方全体造成自身最大生命值30%的水元素伤害，然后铺设1层{Terrias_terrias_moon_domain}。 |
| `Terrias_cursecard_abyss_deficit` | 亏空 | 诅咒 | 0 | 抽到时，失去{0}点魔能。 |
| `Terrias_cursecard_abyss_life_theft` | 生机窃取 | 诅咒 | 0 | 抽到时，失去最大生命值{0}%的当前生命，敌方全体生命上限提高{1}%。丢弃时，失去最大生命值{2}%的当前生命。 |
| `Terrias_loneer_loneer_morning_star_prayer` | 晨星祈愿 | 职业技能 | 0 | 触发【自然晨星】。本场战斗中，【星石袋】黑石上限-2，最低为1。 |
| `Terrias_terrias_lucky_jackpot_b` | 幸运大奖B | 技能牌 | 0 | 打出时进行检定：结果达到95时，获得1个4阶遗物；否则抽1张牌。焚毁。 |
| `Terrias_terrias_polymorph_role_template` | 百变化身 | 衍生牌 | 0 | 百变：目标角色 |
| `Terrias_terrias_projection_role_template` | 另一个我 | 衍生牌 | 0 | 啧......真拿你没办法。 |
| `Terrias_terrias_spirit_card_template` | 精灵 | 衍生牌 | 0 | 召唤这张卡记录的精灵。若已有精灵，将其换下并生成对应精灵卡加入手牌；每次换下后，该卡耗费+1。 |
| `Terrias_terrias_stellar_overture_close` | 星辰序曲·合 | 衍生牌 | 0 | 造成{0}点伤害，数值为10+自身Buff种类数+目标Buff种类数。推进{Terrias_terrias_star_score}：合。 |
| `Terrias_terrias_stellar_overture_start` | 星辰序曲·启 | 衍生牌 | 0 | 抽2张牌。推进{Terrias_terrias_star_score}：启。 |
| `Terrias_terrias_stellar_overture_sustain` | 星辰序曲·承 | 衍生牌 | 0 | 获得{0}点护盾，并使自身所有正面Buff+1层。推进{Terrias_terrias_star_score}：承。 |
| `Terrias_terrias_stellar_overture_turn` | 星辰序曲·转 | 衍生牌 | 0 | 给予目标2层{buff_vulnerability}。推进{Terrias_terrias_star_score}：转。 |
| `Terrias_terrias_witch_star_score` | 魔女的星谱 | 衍生牌 | 0 | 根据本场战斗自己已完成的{Terrias_terrias_star_score}，将对应谱句效果再打出一次。 |
| `Terrias_wuna_wuna_coronation_token` | 日耀：授冕 | 衍生牌 | 0 | 获得2层{Terrias_terrias_solar_crown}和2层{Terrias_terrias_solar_radiance}。 |
| `Terrias_wuna_wuna_grave_song` | 圣庭墓曲 | 职业技能 | 0 | 余烬大于30时可用。消耗所有余烬，使全体获得余烬/2层数的灼烧，自身获得1层烬衣，然后立刻触发一次灼烧。 |
| `Terrias_wuna_wuna_white_sun_prayer` | 白曜圣祷 | 职业技能 | 0 | 获得一张0费的“日耀：授冕”，附着焚毁和凝滞。随后给己方全体的所有手牌添加焚毁和白曜。 |

## Buff 总表

### 正面（3 条）

| Buff ID | 名称 | 稀有度 | 上限 | 衰减（回合／受击／行动） | 效果 |
|---|---|---:|---:|---|---|
| `Terrias_terrias_gathered_flame` | 聚焰 | 2 | 999 | 0／0／0 | 回合开始时，自身获得等同于聚焰层数的{buff_burn}和10倍层数的{buff_extraordinary}。 |
| `Terrias_terrias_moonlight` | 月华 | 3 | 5 | 0／0／0 | 正面Buff，上限5层，不随回合递减。回合结束时，获得等同月华层数的{buff_keenedge}和{buff_resilient}。 |
| `Terrias_terrias_solar_radiance` | 日耀 | 2 | 12 | 0／0／0 | 每次行动时，获得等同于5倍日耀层数的超凡。 |

### 负面（6 条）

| Buff ID | 名称 | 稀有度 | 上限 | 衰减（回合／受击／行动） | 效果 |
|---|---|---:|---:|---|---|
| `Terrias_terrias_abyss_gaze_i` | 深渊凝视Ⅰ | 3 | 20 | 20／0／0 | 本回合每获得1张卡牌获得1层。达到10层时，将1张随机诅咒置入卡组。回合结束时消除。 |
| `Terrias_terrias_abyss_gaze_ii` | 深渊凝视Ⅱ | 3 | 20 | 20／0／0 | 本回合每获得1张卡牌获得1层。达到10层和15层时各将1张随机诅咒置入卡组；15层时下一张卡牌耗费+1。回合结束时消除。 |
| `Terrias_terrias_abyss_gaze_iii` | 深渊凝视Ⅲ | 3 | 20 | 20／0／0 | 本回合每获得1张卡牌获得1层。达到10层和15层时各将1张随机诅咒置入卡组；15层时下一张卡牌耗费+1；20层时强制结束回合。回合结束时消除。 |
| `Terrias_terrias_body_burn` | 焚身 | 2 | 999 | 0／0／0 | 回合开始时，每层受到最大生命值1%+1点真实伤害，随后移除此状态。 |
| `Terrias_terrias_dendro_core` | 草原核 | 2 | 5 | 0／0／0 | 上限5层。回合开始时，受到10×层数点真实伤害，然后清除此状态。受到火或雷元素时，消耗全部层数并触发烈绽放或超绽放。 |
| `Terrias_terrias_frozen` | 冻结 | 4 | 1 | 1／0／0 | 轮到该单位行动时，消耗此状态并跳过本次行动。受到火元素时触发融化，受到雷元素时触发超导。 |

### 能力（17 条）

| Buff ID | 名称 | 稀有度 | 上限 | 衰减（回合／受击／行动） | 效果 |
|---|---|---:|---:|---|---|
| `Terrias_terrias_afterglow_omen` | 残光病兆 | 2 | 1 | 0／0／0 | 回合开始时，所有带有{buff_burn}的敌人获得等同于其{buff_burn}层数一半的{buff_vulnerability}。 |
| `Terrias_terrias_boss_white_radiance_crown` | 圣冕显化·白曜 | 3 | 5 | 0／0／0 | 白曜圣冕阶层。初始1阶，每回合+1，最高5阶。回合开始时获得阶层*8层{buff_extraordinary}，玩家全体获得阶层层{buff_burn}。1阶清除自身负面并转为等量{Terrias_terrias_ember}；2阶增加1个行动意图；3阶触发玩家全体{buff_burn}；4阶每回合随机湮灭3张玩家卡牌；5阶每次行动后对玩家全体造成100%最大生命值的真实伤害。 |
| `Terrias_terrias_cycle_gathered_flame` | 轮转：聚焰 | 2 | 1 | 0／0／0 | 自身{buff_burn}每增加1层，获得1层{Terrias_terrias_gathered_flame}。 |
| `Terrias_terrias_ember` | 余烬 | 3 | 99 | 0／0／0 | 每层使自身造成的伤害提高1%。{buff_burn}结算前，消耗等量余烬抵消同等层数的{buff_burn}。 |
| `Terrias_terrias_ember_cloak` | 烬衣 | 2 | 1 | 1／0／0 | 获得时清除自身{buff_burn}和{Terrias_terrias_body_burn}，下回合开始时再次清除自身{buff_burn}和{Terrias_terrias_body_burn}，然后移除此状态。 |
| `Terrias_terrias_gravity_ripple` | 引力涟漪 | 3 | 20 | 0／0／0 | 每次行动后，对随机敌方造成自身最大生命值3%的水元素伤害，获得伤害个位数层{Terrias_terrias_gravity_value}，然后减少1层。 |
| `Terrias_terrias_gravity_value` | 引力值 | 3 | 100 | 0／0／0 | 达到50层时获得1点魔能；达到75层时抽1张牌；达到100层时触发引力干涉并清除。 |
| `Terrias_terrias_origin_core_radiance` | 源核：日耀 | 2 | 1 | 0／0／0 | 每回合第一次获得{Terrias_terrias_solar_radiance}时，额外获得1层{Terrias_terrias_solar_radiance}。 |
| `Terrias_terrias_resonance` | 余音 | 2 | 9 | 0／0／0 | 使用需要消耗魔能的牌时，优先消耗等量余音代替魔能。 |
| `Terrias_terrias_solar_coefficient` | 日耀系数 | 2 | 1 | 0／0／0 | 等于自身{Terrias_terrias_solar_radiance}层数*2+{Terrias_terrias_gathered_flame}层数/3+目标{buff_burn}层数/2。 |
| `Terrias_terrias_solar_crown` | 圣冕显化 | 3 | 2 | 1／0／0 | 持续期间，{Terrias_terrias_solar_coefficient}变为原来的2倍。授冕时，根据自身当前{Terrias_terrias_solar_radiance}层数确定{Terrias_terrias_solar_crown_tier}。触发时，根据{Terrias_terrias_solar_crown_tier}触发额外效果。1阶清除自身所有负面 Buff，并转化为等量 {buff_burn}；2阶抽 1 张牌；3阶获得 1 点魔能；4 阶清除自身所有 {buff_burn}，获得等量 {Terrias_terrias_gathered_flame}；5 阶敌方全体获得 5 层 {buff_burn}，并立即触发敌方全体 {buff_burn} 一次。结束时消耗2倍等阶层数的{Terrias_terrias_solar_radiance}。 |
| `Terrias_terrias_solar_crown_tier` | 圣冕等阶 | 3 | 5 | 0／0／0 | 持有{Terrias_terrias_solar_crown}时，根据当前{Terrias_terrias_solar_radiance}层数确定授冕等阶：1/4/8/12/15。 |
| `Terrias_terrias_star_blessing` | 星辰祝福 | 3 | 9 | 0／0／0 | 下一张非【星辰序曲】的卡牌耗费减半（向上取整）打出，打出后随机获得1张【星辰序曲·启】、【星辰序曲·承】或【星辰序曲·转】。 |
| `Terrias_terrias_star_clay_body` | 星泥傀身 | 3 | 9 | 0／0／0 | 没有其它复生效果可用时，受到致命伤害会消耗1层，将最大生命值减半并恢复至新的最大生命值。 |
| `Terrias_terrias_star_score` | 星谱 | 2 | 3 | 0／0／0 | 记录最近3张星辰序曲。每打出星辰序曲都会推进星谱；形成谱句时触发全体抽牌、护盾翻倍、Buff层数翻倍、重生、超凡或全体伤害等效果。 |
| `Terrias_terrias_star_stage` | 星台 | 3 | 9 | 0／0／0 | 每打出1张【星辰序曲】，抽1张牌。 |
| `Terrias_terrias_starlight` | 星辉 | 2 | 30 | 0／0／0 | 达到10/20/30层时获得1/1/1层{Terrias_terrias_star_blessing}；达到30层时额外获得1张【星辰序曲·合】，然后消除此Buff。 |

### 契印（5 条）

| Buff ID | 名称 | 稀有度 | 上限 | 衰减（回合／受击／行动） | 效果 |
|---|---|---:|---:|---|---|
| `Terrias_terrias_element_cryo` | 冰元素附着 | 2 | 1 | 0／0／0 | 冰元素附着。 |
| `Terrias_terrias_element_dendro` | 草元素附着 | 2 | 1 | 0／0／0 | 草元素附着。 |
| `Terrias_terrias_element_electro` | 雷元素附着 | 2 | 1 | 0／0／0 | 雷元素附着。 |
| `Terrias_terrias_element_hydro` | 水元素附着 | 2 | 1 | 0／0／0 | 水元素附着。 |
| `Terrias_terrias_element_pyro` | 火元素附着 | 2 | 1 | 0／0／0 | 火元素附着。 |

### 特性（12 条）

| Buff ID | 名称 | 稀有度 | 上限 | 衰减（回合／受击／行动） | 效果 |
|---|---|---:|---:|---|---|
| `Terrias_terrias_abyss_blessing` | 深渊祝福 | 4 | 999 | 0／0／0 | 回合开始时，每层随机获得1层强韧、1层锋锐、1层吸收、5层重生或10层超凡。 |
| `Terrias_terrias_boss_trait_merciless_daylight` | 无悯白昼 | 3 | 1 | 0／0／0 | 敌方回合开始时，若玩家全体{buff_burn}总层数不低于8，焚毁1个保存名字；若没有保存名字，则玩家全体获得10层{Terrias_terrias_body_burn}。 |
| `Terrias_terrias_boss_trait_mirror_array` | 三千环日镜 | 3 | 1 | 0／0／0 | 敌方回合开始时，全体目标获得2层{buff_burn}，三千镜按全体{buff_burn}总层数获得护盾。 |
| `Terrias_terrias_boss_trait_white_radiance_saint` | 白曜圣女 | 3 | 1 | 0／0／0 | 敌方回合开始时，若自身没有{Terrias_terrias_body_burn}，则将1个保存名字焚尽，获得6层{Terrias_terrias_solar_radiance}与10%最大生命值的护盾。自身{Terrias_terrias_solar_radiance}不少于12时，进入{Terrias_terrias_boss_white_radiance_crown}。 |
| `Terrias_terrias_constellation` | 命之座 | 4 | 6 | 0／0／0 | 每点亮一颗命星，都会获得一层专属增益。 |
| `Terrias_terrias_dusk_afterheat_recovery_trait` | 余热回收 | 2 | 1 | 0／0／0 | 每当敌人{buff_burn}触发时，获得其{buff_burn}层数1/3的{Terrias_terrias_ember}与{Terrias_terrias_gathered_flame}。 |
| `Terrias_terrias_heart_change_control` | 心变 | 3 | 1 | 0／0／0 | 你的场做的很好，现在是我的了~ |
| `Terrias_terrias_miracle_clock` | 奇迹时钟 | 2 | 12 | 0／0／0 | 当奇迹时钟恢复至上限时，获得上限层数的{Terrias_terrias_starlight}。 |
| `Terrias_terrias_polymorph_trait` | 百变 | 3 | 1 | 1／0／0 | 变身成为目标角色。百变结束时恢复原角色；原角色的职业状态与技能冷却在此期间冻结。 |
| `Terrias_terrias_sandrone_cat_trait` | 哥！伦！比！娅！ | 2 | 1 | 0／0／0 | 每场战斗结束时，增加自身1+4%生命值上限。 |
| `Terrias_terrias_star_clay_doll_trait` | 星泥人傀 | 2 | 1 | 0／0／0 | 战斗开始时，获得1层{Terrias_terrias_star_clay_body}。每次行动后，获得1层{Terrias_terrias_starlight}。 |
| `Terrias_terrias_star_stone_pouch` | 星石袋 | 2 | 9 | 0／0／0 | 初始内置9个黑石和1个白石。每次行动后抽取一个星石；若抽中黑石，获得1层{Terrias_terrias_starlight}；若抽中白石，获得当前黑石数量的{Terrias_terrias_starlight}。 |

### 场地（3 条）

| Buff ID | 名称 | 稀有度 | 上限 | 衰减（回合／受击／行动） | 效果 |
|---|---|---:|---:|---|---|
| `Terrias_terrias_moon_domain` | 月之领域 | 4 | 1 | 0／0／0 | 场地。感电、绽放、结晶分别变为月感电、月绽放、月结晶，并保留原反应效果。 |
| `Terrias_terrias_samsara_garden` | 轮回花庭 | 2 | 5 | 0／0／0 | 场地。每轮回合开始时，所有存活单位恢复等同于最大生命值5%×轮回花庭层数的生命；处于5层时，每次结算额外获得30层{buff_rebirth}。 |
| `Terrias_terrias_scorching_canopy` | 灼热天幕 | 2 | 9 | 0／0／0 | 场地。每轮回合开始时，全体获得等同于灼热天幕层数的灼烧；场上存在天幕时，任何目标获得的灼烧超过上限部分会转化为等量焚身。 |

## 遗物总表

| 遗物 ID | 名称 | 稀有度 | 所属卡包 | 效果 |
|---|---|---:|---|---|
| `Terrias_terrias_blazing_sundial` | 曜阳日晷 | 1 | 【日耀：烬冠天幕】 | 回合开始时，至多4名带有{buff_burn}的敌人获得1层{buff_weak}和1层{buff_rotten}。 |
| `Terrias_terrias_ember_cloak_lining` | 烬衣衬布 | 1 | 【日耀：烬冠天幕】 | 回合开始时，移除1层{buff_burn}，获得2层{Terrias_terrias_gathered_flame}。 |
| `Terrias_terrias_morning_shard` | 晨辉碎片 | 1 | 【日耀：烬冠天幕】 | 战斗开始时，获得2层{Terrias_terrias_solar_radiance}。 |
| `Terrias_terrias_solar_prism` | 日心棱镜 | 1 | 【日耀：烬冠天幕】 | 战斗开始时，获得1层{Terrias_terrias_solar_radiance}。每回合第一次获得{Terrias_terrias_solar_radiance}后，额外获得1层{buff_elements}。 |
| `Terrias_terrias_burning_calamity_wind_belt` | 燃灾风带 | 2 | 【日耀：烬冠天幕】 | 回合开始时，至多4名带有{buff_burn}的敌人各使随机另一名敌人获得3层{buff_burn}。 |
| `Terrias_terrias_coronation_throne` | 授冕圣座 | 2 | 【日耀：烬冠天幕】 | 每场战斗第一次获得{Terrias_terrias_solar_crown}后，抽2张牌并回复2点魔能。 |
| `Terrias_terrias_sun_bottle` | 太阳瓶 | 2 | 【日耀：烬冠天幕】 | 回合开始时，随机一名带有{buff_burn}的敌人，其{buff_burn}立刻生效一次。 |
| `Terrias_terrias_sun_orbit_mirror` | 环日镜 | 2 | 【日耀：烬冠天幕】 | 每行动3次，获得1层{Terrias_terrias_gathered_flame}，对随机敌人施加3层{buff_burn}。 |
| `Terrias_terrias_ash_charm` | 灰烬护符 | 3 | 【日耀：烬冠天幕】 | 回合结束时，获得等于自身{buff_burn}层数的{Terrias_terrias_ember}和护盾。 |
| `Terrias_terrias_gathered_flame_charm` | 聚炎护符 | 3 | 【日耀：烬冠天幕】 | 自身{buff_burn}层数增加后，获得等量的{Terrias_terrias_gathered_flame}。 |
| `Terrias_terrias_miniature_sunwheel` | 小型日轮 | 3 | 【日耀：烬冠天幕】 | 回合开始时，获得自身负面 Buff 总层数等量的{Terrias_terrias_gathered_flame}，敌方全体获得你{Terrias_terrias_solar_radiance}层数的{buff_burn}。 |
| `Terrias_terrias_solar_phase_dial` | 日相刻盘 | 3 | 【日耀：烬冠天幕】 | 回合开始时，根据{Terrias_terrias_solar_radiance}层数最多触发三种效果：4+抽1张牌，8+获得1点魔能，12+全体{buff_burn}立刻生效一次。 |
| `Terrias_terrias_blazing_crown_heart` | 炽冠圣心 | 4 | 【日耀：烬冠天幕】 | 战斗开始时，获得8层{Terrias_terrias_solar_radiance}、1层{Terrias_terrias_solar_crown}，为场地铺上2层{Terrias_terrias_scorching_canopy}。 |

## 祝福与火漆

### 祝福（11 条）

| 祝福 ID | 名称 | 类型 | 稀有度 | 所属卡包 | 效果 |
|---|---|---|---:|---|---|
| `Terrias_terrias_origin_fortune_50` | 幸运·本源升华 | 本源超凡 | 3 | 隐藏条目 | 幸运达到50：检定骰与数值骰获得50点加成；达到150点数时，额外触发2次。 |
| `Terrias_terrias_origin_perceive_50` | 感知·本源升华 | 本源超凡 | 3 | 隐藏条目 | 感知达到50：每场战斗结束后，生命值恢复至上限。 |
| `Terrias_terrias_origin_spirit_50` | 精神·本源升华 | 本源超凡 | 3 | 隐藏条目 | 精神达到50：每场战斗开始时，魔能上限提高3点。 |
| `Terrias_terrias_origin_strength_50` | 魔力·本源升华 | 本源超凡 | 3 | 隐藏条目 | 魔力达到50：每场战斗开始时，获得2张附着【绝灭】火漆的【不稳定思绪】。 |
| `Terrias_terrias_forgotten_one` | 遗忘者 | 正面 | 2 | 【日耀：烬冠天幕】 | 战斗开始时，将1张【遗忘】放入弃牌堆。回合开始时，随机获得2层{buff_keenedge}或{buff_resilient}。 |
| `Terrias_terrias_sun_priest` | 太阳祭司 | 正面 | 3 | 【日耀：烬冠天幕】 | 战斗开始时，获得3层{Terrias_terrias_solar_radiance}。 |
| `Terrias_terrias_white_radiance_saint` | 白曜圣女 | 正面 | 3 | 【日耀：烬冠天幕】 | 主次本源上限+10。 |
| `Terrias_terrias_solar_witch` | 曜日魔女 | 正面 | 4 | 【日耀：烬冠天幕】 | 回合开始时，随机清除一名敌方的随机一种正面 Buff；自身在本轮冒险中获得等同于所清除层数的生命值上限。 |
| `Terrias_terrias_dusk_afterheat_recovery` | 余热回收 | 伙伴占位 | 5 | 隐藏条目 | 每当敌人{buff_burn}触发时，获得其{buff_burn}层数1/3的{Terrias_terrias_ember}与{Terrias_terrias_gathered_flame}。 |
| `Terrias_terrias_sandrone_cat_placeholder` | 哥！伦！比！娅！ | 伙伴占位 | 5 | 隐藏条目 | 每场战斗结束时，增加自身1+4%生命值上限。 |
| `Terrias_terrias_star_clay_doll_placeholder` | 星泥傀身 | 伙伴占位 | 5 | 隐藏条目 | 战斗开始时，获得1层{Terrias_terrias_star_clay_body}。每次行动后，获得1层{Terrias_terrias_starlight}。 |

四个本源 50 里程碑采用主体隐藏祝福机制，只由冒险内本源阈值授予，不进入普通祝福商店或 Terrias 自定义奖励池。【曜日魔女】为四阶祝福，也不会进入普通祝福商店。

### 火漆（3 条）

| 火漆 ID | 名称 | 稀有度 | 所属卡包 | 效果 |
|---|---|---:|---|---|
| `Terrias_terrias_morning_star_seal` | 启明星 | 3 | 【晨星：序曲】 | 附着卡打出后，根据实际支付费用获得等量{Terrias_terrias_star_blessing}。 |
| `Terrias_terrias_solar_flame_seal` | 阳炣 | 2 | 【日耀：烬冠天幕】 | 附着卡打出时，自身获得打出费用+1的{Terrias_terrias_gathered_flame}。 |
| `Terrias_terrias_solar_keyword` | 白曜 | 2 | 【日耀：烬冠天幕】 | 附着卡获得“白曜”。 |

## 难度词条

| 词条 ID | 名称 | 分类 | 最大层数 | 效果 |
|---|---|---|---:|---|
| `Terrias_terrias_terrias_abyss_gaze` | 深渊凝视 | Normal | 3 | 最高3层。玩家在同一回合内每获得1张卡牌，获得1层对应层级的深渊凝视。Ⅰ：10层将1张随机诅咒置入卡组；Ⅱ：10层和15层各将1张随机诅咒置入卡组，15层时下一张卡牌耗费+1；Ⅲ：20层时强制结束回合。回合结束时清空。 |
| `Terrias_terrias_terrias_abyssal_shock` | 深渊震荡 | Normal | 1 | 每过6层，随机触发以下效果之一：给予卡组内随机2张牌【碎裂】；敌方全体生命值提高30%；随机销毁1件已装备遗物。 |
| `Terrias_terrias_terrias_black_sun_calamity` | 黑日之灾 | Normal | 1 | 每场战斗中，玩家每行动5回合，随机焚毁自己现有卡组、手牌、弃牌堆中的1张卡。 |
| `Terrias_terrias_terrias_morning_star_dimmed` | 晨星晦暗 | Normal | 1 | 本场战斗中，魔能上限+1，所有卡牌的耗费魔能+1。 |
| `Terrias_terrias_terrias_other_dimension_stagnant_water` | 异次元-迟滞之水 | Normal | 1 | 本场战斗中，技能的冷却回合数翻倍。 |
| `Terrias_terrias_terrias_rebirth` | 重生 | Normal | 1 | 进入战斗时，所有敌方获得50层{buff_rebirth}。 |
| `Terrias_terrias_terrias_samsara_garden` | 永恒花园 | Normal | 4 | 战斗开始时，为场上铺上选择层数的{Terrias_terrias_samsara_garden}。 |
| `Terrias_terrias_terrias_scorched_world` | 焦枯世界 | Normal | 4 | 战斗开始时，为场上铺上选择层数的{Terrias_terrias_scorching_canopy}。 |
| `Terrias_terrias_terrias_sunset_expedition` | 落日远征 | Normal | 1 | 进入战斗时，扣除已经历战斗场数*1%的当前生命值，最高50%。至少保留1点生命。 |
| `Terrias_terrias_terrias_white_radiance_court` | 白曜圣庭 | Normal | 1 | 敌人每行动一次，获得1层{Terrias_terrias_solar_radiance}。 |

## 专属模式与系统

| 内容 | 当前玩家流程 | 详细文档 |
|---|---|---|
| 日耀回忆 | 选择 11 张开局卡、分配 50 点本源、选满 15 个祝福，经历三层固定回忆；是否持有【炽冕崩落】决定是否进入白曜圣女隐藏终局。 | [日耀回忆模式](../Terrias/modules/05-日耀回忆模式.md) |
| 无尽之渊 | 每层配置 6 槽地图，1-6 层为潜行阶段，第 7 层起进入无尽阶段；战斗、奖励、注视和深渊震荡持续累积。 | [地图循环](../Terrias/modules/06-无尽之海模式与地图循环.md)、[压力与奖励](../Terrias/modules/07-无尽深渊压力与奖励体系.md) |
| 精灵系统 | 使用精灵球按目标已损失生命检定捕获，成功后生成可持久化精灵卡；精灵使用固定附着位并可与投影同时存在。 | [精灵球捕获与精灵召唤](../Terrias/modules/08-精灵球捕获与精灵召唤.md) |
| 投影、百变、心变 | 分别提供角色投影、角色形态复制和敌方控制入口；对应卡牌位于【更多的次元】。 | [模块覆盖矩阵](../Terrias/00-module-coverage-matrix.md) |

## 专属敌人

| 敌人 ID | 名称 | 等级 | 基础生命 | 主要出现位置 |
|---|---|---:|---:|---|
| `Terrias_terrias_boss_orbit_mirror_array` | 白曜镜阵 | 2 | 180 | 日耀回忆第二层固定首领 |
| `Terrias_terrias_boss_saint_wuna` | 白曜圣女·乌娜 | 3 | 320 | 持有【炽冕崩落】时开启的隐藏终局 |
| `Terrias_terrias_boss_second_sun_last_day` | 无慈第二日轮 | 3 | 360 | 日耀回忆第三层终局前首领 |

## 阅读与维护口径

- 本页是玩家侧内容目录；行为细节有冲突时，以当前 `Terrias/Data`、`Terrias/Text`、`Terrias-Dev` 与已打包 DLL 为准。
- 卡牌、Buff、遗物和祝福数量来自同一运行时快照，避免把说明行、未加载行或其他 MOD 内容混入统计。
- `更多的次元` 当前表内说明仍为占位文本，本页只按实际卡牌能力概括其定位，不把占位文案当成正式设计说明。
- 技术文档中的旧数量可能早于本次运行时导出；更新内容规模时应重新运行 `tools/Export-GameAndTerriasContentDocs.ps1`。
