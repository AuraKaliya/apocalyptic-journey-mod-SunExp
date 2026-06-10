# SunExp 卡牌与遗物文案整理稿

> 生成用途：集中检查并修改 SunExp 卡包 Mod 中卡牌、遗物、卡包名和术语的文字内容。
> 注意：`{...}` 是游戏内文本占位符，改描述时建议保留完整 ID，不要删掉花括号。

## 基本信息

- ModName：SunExp
- ModVersion：0.1.2
- ModAuthor：Aura
- 当前卡牌数量：30
- 当前遗物数量：13
- 主要文案源文件：
  - `SunExp/Text/Card/sunexp.csv`
  - `SunExp/Text/Relic/sunexp.csv`
  - `SunExp/Text/CardPack/sunexp.csv`
  - `SunExp/Text/Buff/sunexp.csv`

### Mod 描述

当前：《日耀：烬冠天幕》新增30张高风险高爆发卡牌与13件日耀遗物，分为基础、聚爆、天幕三个卡包分支，可与官方卡包混合游玩。围绕日耀、炽灼天幕、聚炎、全场灼烧与圣冕显化构筑：先铺开灼烧并吸收为聚炎，再在圣冕窗口兑现强力爆发；若过早引爆，则可能承受同等反噬。

修改稿：

---

## 卡包文案

### 1. 【日耀：星火】 (`cardpack_radiant_spark`)

- Name：【日耀：星火】
- Name 修改稿：
- Name_zh-Hant：【日耀：星火】
- Name_zh-Hant 修改稿：
- Name_en：Radiance: Spark
- Name_en 修改稿：
- Name_ja：日耀：星火
- Name_ja 修改稿：
- Description：日耀：烬冠天幕的基础卡包。提供日耀、聚炎、基础防护与日耀：授冕入口，并保留少量低复杂度灼烧转换。
- Description 修改稿：
- Description_zh-Hant：日耀：烬冠天幕的基礎卡包。提供日耀、聚炎、基礎防護與日耀：授冕入口，並保留少量低複雜度灼燒轉換。
- Description_zh-Hant 修改稿：
- Description_en：The base Solar Charge pack. Provides Solar Radiance, Gathered Flame, basic defense, the Radiance: Coronation entry point, and a small amount of low-complexity Burn conversion.
- Description_en 修改稿：
- Description_ja：日耀集能の基礎パック。日耀、集炎、基本防御、日耀：授冕への入口を提供し、低複雑度の燃焼変換を含む。
- Description_ja 修改稿：

### 2. 【日耀：烬冠】 (`cardpack_ember_crown`)

- Name：【日耀：烬冠】
- Name 修改稿：
- Name_zh-Hant：【日耀：烬冠】
- Name_zh-Hant 修改稿：
- Name_en：Radiance: Ember Crown
- Name_en 修改稿：
- Name_ja：日耀：燼冠
- Name_ja 修改稿：
- Description：围绕自身灼烧、聚炎叠层和爆发兑现展开。需要管理自燃压力，并借助圣冕阶段提高收益与安全性。
- Description 修改稿：
- Description_zh-Hant：圍繞自身灼燒、聚炎疊層和爆發兌現展開。需要管理自燃壓力，並借助聖冕階段提高收益與安全性。
- Description_zh-Hant 修改稿：
- Description_en：A burst extension focused on self-Burn, Gathered Flame stacking, and explosive payoff. Manage ignition pressure and use Crown phases to improve safety and reward.
- Description_en 修改稿：
- Description_ja：自身の燃焼、集炎スタック、爆発的な換金を軸にする。自燃圧を管理し、聖冕段階で收益と安全性を高める。
- Description_ja 修改稿：

### 3. 【日耀：天幕】 (`cardpack_solar_canopy`)

- Name：【日耀：天幕】
- Name 修改稿：
- Name_zh-Hant：【日耀：天幕】
- Name_zh-Hant 修改稿：
- Name_en：Radiance: Canopy
- Name_en 修改稿：
- Name_ja：日耀：天幕
- Name_ja 修改稿：
- Description：围绕敌方灼烧、负面 Buff 与 DOT 扩散展开。通过天幕场域压低敌方状态，并把灼烧变成持续收益。
- Description 修改稿：
- Description_zh-Hant：圍繞敵方灼燒、負面 Buff 與 DOT 擴散展開。透過天幕場域壓低敵方狀態，並把灼燒變成持續收益。
- Description_zh-Hant 修改稿：
- Description_en：A canopy extension focused on enemy Burn, debuffs, and DOT spread. Pressure enemy status through solar fields and convert Burn into ongoing value.
- Description_en 修改稿：
- Description_ja：敵の燃焼、デバフ、DOT拡散を軸にする。天幕の場で敵状態を抑え、燃焼を継続收益へ変える。
- Description_ja 修改稿：

---

## 术语与 Buff 占位符对照

这些名称经常出现在卡牌/遗物描述的 `{...}` 占位符中。改卡牌描述时建议保留占位符 ID，只改周围自然语言。

### 1. 日耀 (`{SunExp_sunexp_solar_radiance}`)

- Name：日耀
- Name 修改稿：
- Name_en：Solar Radiance
- Name_en 修改稿：
- Description：核心聚能。每次行动时，获得等同于5倍日耀层数的超凡。圣冕显化期间，日耀阶段向下包括，高层同时拥有低层收益。
- Description 修改稿：
- Description_en：Core charge. Whenever you act, gain Extraordinary equal to 5 times Solar Radiance stacks. During Crown Manifestation, phases are inclusive: higher phases also gain lower-phase benefits.
- Description_en 修改稿：

### 2. 聚炎 (`{SunExp_sunexp_gathered_flame}`)

- Name：聚炎
- Name 修改稿：
- Name_en：Gathered Flame
- Name_en 修改稿：
- Description：无上限聚能。回合开始时，自己获得等同于聚炎层数的灼烧；烬衣或圣冕顶层阶段可抵消这次自燃。
- Description 修改稿：
- Description_en：Uncapped heat. At the start of round, gain Burn equal to Gathered Flame stacks. Ember Cloak or the highest Crown phase can prevent this self-ignition.
- Description_en 修改稿：

### 3. 炽灼天幕 (`{SunExp_sunexp_scorching_canopy}`)

- Name：炽灼天幕
- Name 修改稿：
- Name_en：Scorching Canopy
- Name_en 修改稿：
- Description：场地聚热。每轮回合开始时，全体获得等同于炽灼天幕层数的灼烧。
- Description 修改稿：
- Description_en：Solar terrain. At the start of each round, all combatants gain Burn equal to Scorching Canopy stacks.
- Description_en 修改稿：

### 4. 烬衣 (`{SunExp_sunexp_ember_cloak}`)

- Name：烬衣
- Name 修改稿：
- Name_en：Ember Cloak
- Name_en 修改稿：
- Description：临时避灼。获得时清除自身灼烧；下回合开始时再次清除自身灼烧，然后移除此状态。
- Description 修改稿：
- Description_en：Temporary burn ward. Clear your Burn on gain; at next round start, clear your Burn again, then remove this status.
- Description_en 修改稿：

### 5. 圣冕显化 (`{SunExp_sunexp_solar_crown}`)

- Name：圣冕显化
- Name 修改稿：
- Name_en：Crown Manifestation
- Name_en 修改稿：
- Description：持续2回合。期间日耀阶段向下包括：1+吸收灼烧额外聚炎；4+日耀伤害增幅；8+行动时获得魔能，若未达12层则自燃；12层清除并免疫自身灼烧。炽冕崩落会强制结束。
- Description 修改稿：
- Description_en：Lasts 2 rounds. Crown phases are inclusive: 1+ improves Burn absorption; 4+ boosts solar damage; 8+ grants mana on action and self-burn unless at 12; 12 clears and prevents your Burn. Blazing Crown Collapse ends it.
- Description_en 修改稿：

### 6. 源核：日耀 (`{SunExp_sunexp_origin_core_radiance}`)

- Name：源核：日耀
- Name 修改稿：
- Name_en：Origin Core: Radiance
- Name_en 修改稿：
- Description：每回合第一次获得{SunExp_sunexp_solar_radiance}时，额外获得1层{SunExp_sunexp_solar_radiance}。
- Description 修改稿：
- Description_en：The first time each round you gain {SunExp_sunexp_solar_radiance}, gain 1 extra stack of it.
- Description_en 修改稿：

### 7. 轮转：聚炎 (`{SunExp_sunexp_cycle_gathered_flame}`)

- Name：轮转：聚炎
- Name 修改稿：
- Name_en：Cycle: Gathered Flame
- Name_en 修改稿：
- Description：每回合前3次自身{buff_burn}层数增加后，获得1层{SunExp_sunexp_gathered_flame}。
- Description 修改稿：
- Description_en：Up to 3 times each round, after your {buff_burn} stacks increase, gain 1 stack of {SunExp_sunexp_gathered_flame}.
- Description_en 修改稿：

### 8. 残光病兆 (`{SunExp_sunexp_afterglow_omen}`)

- Name：残光病兆
- Name 修改稿：
- Name_en：Afterglow Omen
- Name_en 修改稿：
- Description：回合开始时，所有带有{buff_burn}的敌人获得1层{buff_vulnerability}。
- Description 修改稿：
- Description_en：At round start, all enemies with {buff_burn} gain 1 stack of {buff_vulnerability}.
- Description_en 修改稿：

---

## 卡牌文案

### 1. 星火 (`spark`)

- 元数据：类型=攻击牌；稀有度=1；费用=0；标签=无；所属卡包=【日耀：星火】
- Name：星火
- Name 修改稿：
- Name_zh-Hant：星火
- Name_zh-Hant 修改稿：
- Name_en：Spark
- Name_en 修改稿：
- Name_ja：星火
- Name_ja 修改稿：
- Description：造成5点伤害，给予目标1层{buff_burn}。获得1层{SunExp_sunexp_solar_radiance}。
- Description 修改稿：
- Description_zh-Hant：造成5點傷害，給予目標1層{buff_burn}。獲得1層{SunExp_sunexp_solar_radiance}。
- Description_zh-Hant 修改稿：
- Description_en：Deal 5 damage, apply 1 stack of {buff_burn}, and gain 1 stack of {SunExp_sunexp_solar_radiance}.
- Description_en 修改稿：
- Description_ja：5ダメージを与え、対象に{buff_burn}を1スタック付与する。{SunExp_sunexp_solar_radiance}を1スタック得る。
- Description_ja 修改稿：

### 2. 灼热天幕 (`scorching_canopy_card`)

- 元数据：类型=技能牌；稀有度=1；费用=1；标签=无；所属卡包=【日耀：天幕】
- Name：灼热天幕
- Name 修改稿：
- Name_zh-Hant：灼热天幕
- Name_zh-Hant 修改稿：
- Name_en：Scorching Canopy
- Name_en 修改稿：
- Name_ja：灼热天幕
- Name_ja 修改稿：
- Description：获得1层{SunExp_sunexp_scorching_canopy}。全体获得2层{buff_burn}。
- Description 修改稿：
- Description_zh-Hant：獲得1層{SunExp_sunexp_scorching_canopy}。全體獲得2層{buff_burn}。
- Description_zh-Hant 修改稿：
- Description_en：Gain 1 stack of {SunExp_sunexp_scorching_canopy}. All combatants gain 2 stacks of {buff_burn}.
- Description_en 修改稿：
- Description_ja：{SunExp_sunexp_scorching_canopy}を1スタック得る。全員が{buff_burn}を2スタック得る。
- Description_ja 修改稿：

### 3. 耀焰斩 (`radiant_flame_slash`)

- 元数据：类型=攻击牌；稀有度=1；费用=1；标签=无；所属卡包=【日耀：星火】
- Name：耀焰斩
- Name 修改稿：
- Name_zh-Hant：耀焰斩
- Name_zh-Hant 修改稿：
- Name_en：Flare Cut
- Name_en 修改稿：
- Name_ja：耀焰斩
- Name_ja 修改稿：
- Description：造成10点伤害。每有4层{SunExp_sunexp_solar_radiance}，额外造成3点伤害。圣冕显化且日耀不少于4层时，再额外增加等同日耀层数的伤害。
- Description 修改稿：
- Description_zh-Hant：造成10點傷害。每有4層{SunExp_sunexp_solar_radiance}，額外造成3點傷害。聖冕顯化且日耀不少於4層時，再額外增加等同日耀層數的傷害。
- Description_zh-Hant 修改稿：
- Description_en：Deal 10 damage. For every 4 stacks of {SunExp_sunexp_solar_radiance}, deal 3 extra damage. During Crown Manifestation at 4+ Radiance, add Radiance as extra damage.
- Description_en 修改稿：
- Description_ja：10ダメージを与える。{SunExp_sunexp_solar_radiance}4スタックごとに追加で3ダメージ。聖冠顕現中かつ日耀4以上なら、日耀の値だけさらに追加ダメージ。
- Description_ja 修改稿：

### 4. 烬衣 (`ember_cloak_card`)

- 元数据：类型=技能牌；稀有度=1；费用=0；标签=无；所属卡包=【日耀：星火】
- Name：烬衣
- Name 修改稿：
- Name_zh-Hant：烬衣
- Name_zh-Hant 修改稿：
- Name_en：Ember Cloak
- Name_en 修改稿：
- Name_ja：烬衣
- Name_ja 修改稿：
- Description：获得{SunExp_sunexp_ember_cloak}：清除自身{buff_burn}，获得等同于清除层数一半的护盾，并在下回合开始时再次清除自身{buff_burn}。
- Description 修改稿：
- Description_zh-Hant：獲得{SunExp_sunexp_ember_cloak}：清除自身{buff_burn}，獲得等同於清除層數一半的護盾，並在下回合開始時再次清除自身{buff_burn}。
- Description_zh-Hant 修改稿：
- Description_en：Gain {SunExp_sunexp_ember_cloak}: clear your {buff_burn}, gain Block equal to half the cleared stacks, and clear your {buff_burn} again at the start of next round.
- Description_en 修改稿：
- Description_ja：{SunExp_sunexp_ember_cloak}を得る。自身の{buff_burn}を消し、消したスタックの半分に等しい護盾を得る。次ターン開始時にも自身の{buff_burn}を消す。
- Description_ja 修改稿：

### 5. 引炎 (`draw_flame`)

- 元数据：类型=技能牌；稀有度=1；费用=1；标签=无；所属卡包=【日耀：烬冠】
- Name：引炎
- Name 修改稿：
- Name_zh-Hant：引炎
- Name_zh-Hant 修改稿：
- Name_en：Draw Flame
- Name_en 修改稿：
- Name_ja：引炎
- Name_ja 修改稿：
- Description：吸收目标至多6层{buff_burn}，移除等量{buff_burn}，获得等量{SunExp_sunexp_gathered_flame}。圣冕显化期间，日耀阶段会向下包括并额外获得聚炎。
- Description 修改稿：
- Description_zh-Hant：吸收目標至多6層{buff_burn}，移除等量{buff_burn}，獲得等量{SunExp_sunexp_gathered_flame}。聖冕顯化期間，日耀階段會向下包括並額外獲得聚炎。
- Description_zh-Hant 修改稿：
- Description_en：Absorb up to 6 stacks of {buff_burn} from the target, removing them and gaining that much {SunExp_sunexp_gathered_flame}. Crown phases are inclusive and grant extra flame.
- Description_en 修改稿：
- Description_ja：対象から最大6スタックの{buff_burn}を吸収して取り除き、同量の{SunExp_sunexp_gathered_flame}を得る。聖冠顕現中、日耀段階は下位効果を含み追加の聚炎を得る。
- Description_ja 修改稿：

### 6. 日耀聚焦 (`solar_prayer`)

- 元数据：类型=技能牌；稀有度=2；费用=0；标签=无；所属卡包=【日耀：星火】
- Name：日耀聚焦
- Name 修改稿：
- Name_zh-Hant：日耀聚焦
- Name_zh-Hant 修改稿：
- Name_en：Solar Focus
- Name_en 修改稿：
- Name_ja：日耀聚焦
- Name_ja 修改稿：
- Description：获得3层{SunExp_sunexp_solar_radiance}。若已有{SunExp_sunexp_scorching_canopy}，抽1张牌；若已有{SunExp_sunexp_solar_crown}，获得1层{SunExp_sunexp_gathered_flame}。
- Description 修改稿：
- Description_zh-Hant：獲得3層{SunExp_sunexp_solar_radiance}。若已有{SunExp_sunexp_scorching_canopy}，抽1張牌；若已有{SunExp_sunexp_solar_crown}，獲得1層{SunExp_sunexp_gathered_flame}。
- Description_zh-Hant 修改稿：
- Description_en：Gain 3 stacks of {SunExp_sunexp_solar_radiance}. If you have {SunExp_sunexp_scorching_canopy}, draw 1 card. If you have {SunExp_sunexp_solar_crown}, gain 1 stack of {SunExp_sunexp_gathered_flame}.
- Description_en 修改稿：
- Description_ja：{SunExp_sunexp_solar_radiance}を3スタック得る。{SunExp_sunexp_scorching_canopy}を持つなら1枚引く。{SunExp_sunexp_solar_crown}を持つなら{SunExp_sunexp_gathered_flame}を1スタック得る。
- Description_ja 修改稿：

### 7. 燃星之咒 (`burning_star_hex`)

- 元数据：类型=攻击牌；稀有度=2；费用=1；标签=无；所属卡包=【日耀：烬冠】
- Name：燃星之咒
- Name 修改稿：
- Name_zh-Hant：燃星之咒
- Name_zh-Hant 修改稿：
- Name_en：Solar Spark
- Name_en 修改稿：
- Name_ja：燃星之咒
- Name_ja 修改稿：
- Description：造成6点伤害。消耗至多5层{SunExp_sunexp_gathered_flame}，每层额外造成4点伤害。给予目标2层{buff_burn}，每有4层{SunExp_sunexp_solar_radiance}额外+1层。圣冕显化且日耀不少于4层时，额外增加等同日耀层数的伤害。
- Description 修改稿：
- Description_zh-Hant：造成6點傷害。消耗至多5層{SunExp_sunexp_gathered_flame}，每層額外造成4點傷害。給予目標2層{buff_burn}，每有4層{SunExp_sunexp_solar_radiance}額外+1層。聖冕顯化且日耀不少於4層時，額外增加等同日耀層數的傷害。
- Description_zh-Hant 修改稿：
- Description_en：Deal 6 damage. Consume up to 5 stacks of {SunExp_sunexp_gathered_flame} for +4 damage each. Apply 2 stacks of {buff_burn}, +1 for every 4 stacks of {SunExp_sunexp_solar_radiance}. During Crown at 4+ Radiance, add Radiance as damage.
- Description_en 修改稿：
- Description_ja：6ダメージを与える。最大5スタックの{SunExp_sunexp_gathered_flame}を消費し、1スタックごとに+4ダメージ。対象に{buff_burn}を2スタック付与し、{SunExp_sunexp_solar_radiance}4ごとにさらに+1。聖冠中かつ日耀4以上なら日耀分の追加ダメージ。
- Description_ja 修改稿：

### 8. 冠冕威光 (`crown_radiance`)

- 元数据：类型=技能牌；稀有度=2；费用=1；标签=无；所属卡包=【日耀：天幕】
- Name：冠冕威光
- Name 修改稿：
- Name_zh-Hant：冠冕威光
- Name_zh-Hant 修改稿：
- Name_en：Crown Radiance
- Name_en 修改稿：
- Name_ja：冠冕威光
- Name_ja 修改稿：
- Description：敌方全体获得4层{buff_burn}。若{SunExp_sunexp_solar_radiance}不少于8层，改为6层。若你拥有{SunExp_sunexp_scorching_canopy}，随机敌人的{buff_burn}立刻生效一次。
- Description 修改稿：
- Description_zh-Hant：敵方全體獲得4層{buff_burn}。若{SunExp_sunexp_solar_radiance}不少於8層，改為6層。若你擁有{SunExp_sunexp_scorching_canopy}，隨機敵人的{buff_burn}立刻生效一次。
- Description_zh-Hant 修改稿：
- Description_en：All enemies gain 4 stacks of {buff_burn}, or 6 if you have at least 8 stacks of {SunExp_sunexp_solar_radiance}. If you have {SunExp_sunexp_scorching_canopy}, trigger a random enemy's {buff_burn} once immediately.
- Description_en 修改稿：
- Description_ja：すべての敵が{buff_burn}を4スタック得る。{SunExp_sunexp_solar_radiance}が8以上なら6スタックになる。{SunExp_sunexp_scorching_canopy}を持つなら、ランダムな敵の{buff_burn}をただちに1回発動する。
- Description_ja 修改稿：

### 9. 天幕再临 (`canopy_return`)

- 元数据：类型=技能牌；稀有度=2；费用=1；标签=无；所属卡包=【日耀：天幕】
- Name：天幕再临
- Name 修改稿：
- Name_zh-Hant：天幕再临
- Name_zh-Hant 修改稿：
- Name_en：Canopy Return
- Name_en 修改稿：
- Name_ja：天幕再临
- Name_ja 修改稿：
- Description：获得2层{SunExp_sunexp_scorching_canopy}。全体获得3层{buff_burn}。若{SunExp_sunexp_solar_radiance}不少于4层，敌方全体的{buff_burn}立刻生效一次。
- Description 修改稿：
- Description_zh-Hant：獲得2層{SunExp_sunexp_scorching_canopy}。全體獲得3層{buff_burn}。若{SunExp_sunexp_solar_radiance}不少於4層，敵方全體的{buff_burn}立刻生效一次。
- Description_zh-Hant 修改稿：
- Description_en：Gain 2 stacks of {SunExp_sunexp_scorching_canopy}. All combatants gain 3 stacks of {buff_burn}. If you have at least 4 stacks of {SunExp_sunexp_solar_radiance}, trigger all enemies' {buff_burn} once immediately.
- Description_en 修改稿：
- Description_ja：{SunExp_sunexp_scorching_canopy}を2スタック得る。全員が{buff_burn}を3スタック得る。{SunExp_sunexp_solar_radiance}が4以上なら、すべての敵の{buff_burn}をただちに1回発動する。
- Description_ja 修改稿：

### 10. 日相校准 (`solar_phase_tuning`)

- 元数据：类型=技能牌；稀有度=2；费用=1；标签=无；所属卡包=【日耀：星火】
- Name：日相校准
- Name 修改稿：
- Name_zh-Hant：日相校准
- Name_zh-Hant 修改稿：
- Name_en：Solar Phase Tuning
- Name_en 修改稿：
- Name_ja：日相校准
- Name_ja 修改稿：
- Description：获得3层{SunExp_sunexp_solar_radiance}。吸收自身至多6层{buff_burn}，转化为等量{SunExp_sunexp_gathered_flame}。若吸收满6层，抽1张牌。
- Description 修改稿：
- Description_zh-Hant：獲得3層{SunExp_sunexp_solar_radiance}。吸收自身至多6層{buff_burn}，轉化為等量{SunExp_sunexp_gathered_flame}。若吸收滿6層，抽1張牌。
- Description_zh-Hant 修改稿：
- Description_en：Gain 3 stacks of {SunExp_sunexp_solar_radiance}. Absorb up to 6 stacks of your own {buff_burn} and convert them into {SunExp_sunexp_gathered_flame}. If you absorb 6, draw 1 card.
- Description_en 修改稿：
- Description_ja：{SunExp_sunexp_solar_radiance}を3スタック得る。自身の{buff_burn}を最大6スタック吸収し、同量の{SunExp_sunexp_gathered_flame}に変換する。6吸収したなら1枚引く。
- Description_ja 修改稿：

### 11. 日耀：授冕 (`solar_coronation`)

- 元数据：类型=能力牌；稀有度=3；费用=2；标签=Ability；所属卡包=【日耀：星火】
- Name：日耀：授冕
- Name 修改稿：
- Name_zh-Hant：日耀：授冕
- Name_zh-Hant 修改稿：
- Name_en：Radiance: Coronation
- Name_en 修改稿：
- Name_ja：日耀：授冕
- Name_ja 修改稿：
- Description：获得2层{SunExp_sunexp_solar_crown}和2层{SunExp_sunexp_solar_radiance}。圣冕阶段向下包括：高层会同时触发低层效果。
- Description 修改稿：
- Description_zh-Hant：獲得2層{SunExp_sunexp_solar_crown}和2層{SunExp_sunexp_solar_radiance}。聖冕階段向下包括：高層會同時觸發低層效果。
- Description_zh-Hant 修改稿：
- Description_en：Gain 2 stacks of {SunExp_sunexp_solar_crown} and 2 stacks of {SunExp_sunexp_solar_radiance}. Crown phases are inclusive: higher phases also trigger lower-phase benefits.
- Description_en 修改稿：
- Description_ja：{SunExp_sunexp_solar_crown}を2スタック、{SunExp_sunexp_solar_radiance}を2スタック得る。聖冠段階は下位効果を含み、高段階は低段階効果も同時に発動する。
- Description_ja 修改稿：

### 12. 炽冕崩落 (`blazing_crown_collapse`)

- 元数据：类型=攻击牌；稀有度=3；费用=3；标签=无；所属卡包=【日耀：烬冠】
- Name：炽冕崩落
- Name 修改稿：
- Name_zh-Hant：炽冕崩落
- Name_zh-Hant 修改稿：
- Name_en：Blazing Crown Collapse
- Name_en 修改稿：
- Name_ja：炽冕崩落
- Name_ja 修改稿：
- Description：爆发性一击。对所有敌人造成伤害；若没有{SunExp_sunexp_solar_crown}，自身也承受相同伤害。消耗全部{SunExp_sunexp_gathered_flame}和一半{SunExp_sunexp_solar_radiance}，基础40点；每消耗1层{SunExp_sunexp_gathered_flame}额外造成6点，每消耗1层{SunExp_sunexp_solar_radiance}额外造成8点。随后敌方全体的{buff_burn}立刻生效一次，结束{SunExp_sunexp_solar_crown}，并获得等同于消耗{SunExp_sunexp_gathered_flame}一半的{buff_burn}。
- Description 修改稿：
- Description_zh-Hant：爆發性一擊。對所有敵人造成傷害；若沒有{SunExp_sunexp_solar_crown}，自身也承受相同傷害。消耗全部{SunExp_sunexp_gathered_flame}和一半{SunExp_sunexp_solar_radiance}，基礎40點；每消耗1層{SunExp_sunexp_gathered_flame}額外造成6點，每消耗1層{SunExp_sunexp_solar_radiance}額外造成8點。隨後敵方全體的{buff_burn}立刻生效一次，結束{SunExp_sunexp_solar_crown}，並獲得等同於消耗{SunExp_sunexp_gathered_flame}一半的{buff_burn}。
- Description_zh-Hant 修改稿：
- Description_en：A burst strike against all enemies. If you do not have {SunExp_sunexp_solar_crown}, you also take the same damage. Consume all {SunExp_sunexp_gathered_flame} and half of {SunExp_sunexp_solar_radiance}. Deal 40 damage, +6 per flame consumed and +8 per radiance consumed. Then trigger all enemies' {buff_burn} once, end {SunExp_sunexp_solar_crown}, and gain {buff_burn} equal to half the flame consumed.
- Description_en 修改稿：
- Description_ja：すべての敵への爆発的な一撃。{SunExp_sunexp_solar_crown}を持たない場合、自身も同じダメージを受ける。すべての{SunExp_sunexp_gathered_flame}と半分の{SunExp_sunexp_solar_radiance}を消費する。40ダメージ、消費した聚炎1につき+6、日耀1につき+8。続けて全敵の{buff_burn}を1回発動し、{SunExp_sunexp_solar_crown}を終了し、消費した聚炎の半分に等しい{buff_burn}を得る。
- Description_ja 修改稿：

### 13. 破晓校准 (`radiant_oath`)

- 元数据：类型=技能牌；稀有度=1；费用=0；标签=Burnout；所属卡包=【日耀：星火】
- Name：破晓校准
- Name 修改稿：
- Name_zh-Hant：破曉校準
- Name_zh-Hant 修改稿：
- Name_en：Dawn Calibration
- Name_en 修改稿：
- Name_ja：暁光校準
- Name_ja 修改稿：
- Description：获得3层{SunExp_sunexp_solar_radiance}。若你没有{SunExp_sunexp_scorching_canopy}，获得1层{SunExp_sunexp_scorching_canopy}；否则抽1张牌。焚毁。
- Description 修改稿：
- Description_zh-Hant：獲得3層{SunExp_sunexp_solar_radiance}。若你沒有{SunExp_sunexp_scorching_canopy}，獲得1層{SunExp_sunexp_scorching_canopy}；否則抽1張牌。焚毀。
- Description_zh-Hant 修改稿：
- Description_en：Gain 3 stacks of {SunExp_sunexp_solar_radiance}. If you do not have {SunExp_sunexp_scorching_canopy}, gain 1 stack of it; otherwise draw 1 card. Burnout.
- Description_en 修改稿：
- Description_ja：{SunExp_sunexp_solar_radiance}を3スタック得る。{SunExp_sunexp_scorching_canopy}を持っていないなら1スタック得る。そうでなければ1枚引く。焼却。
- Description_ja 修改稿：

### 14. 聚光引燃 (`solar_ignition`)

- 元数据：类型=技能牌；稀有度=1；费用=1；标签=无；所属卡包=【日耀：星火】
- Name：聚光引燃
- Name 修改稿：
- Name_zh-Hant：聚光引燃
- Name_zh-Hant 修改稿：
- Name_en：Heliostat Ignition
- Name_en 修改稿：
- Name_ja：集光点火
- Name_ja 修改稿：
- Description：获得1层{SunExp_sunexp_solar_radiance}。所有敌人获得2层{buff_burn}。若你拥有{SunExp_sunexp_scorching_canopy}，抽1张牌；若{SunExp_sunexp_solar_radiance}不少于4层，随机敌人的{buff_burn}立刻生效一次。
- Description 修改稿：
- Description_zh-Hant：獲得1層{SunExp_sunexp_solar_radiance}。所有敵人獲得2層{buff_burn}。若你擁有{SunExp_sunexp_scorching_canopy}，抽1張牌；若{SunExp_sunexp_solar_radiance}不少於4層，隨機敵人的{buff_burn}立刻生效一次。
- Description_zh-Hant 修改稿：
- Description_en：Gain 1 stack of {SunExp_sunexp_solar_radiance}. All enemies gain 2 stacks of {buff_burn}. If you have {SunExp_sunexp_scorching_canopy}, draw 1 card. If you have at least 4 Radiance, trigger a random enemy's {buff_burn} once.
- Description_en 修改稿：
- Description_ja：{SunExp_sunexp_solar_radiance}を1スタック得る。すべての敵が{buff_burn}を2スタック得る。{SunExp_sunexp_scorching_canopy}を持つなら1枚引く。日耀4以上ならランダムな敵の{buff_burn}を1回発動する。
- Description_ja 修改稿：

### 15. 灼流回收 (`scorching_flow_reclaim`)

- 元数据：类型=攻击牌；稀有度=2；费用=0；标签=无；所属卡包=【日耀：烬冠】
- Name：灼流回收
- Name 修改稿：
- Name_zh-Hant：灼流回收
- Name_zh-Hant 修改稿：
- Name_en：Flare Reclaim
- Name_en 修改稿：
- Name_ja：灼流回収
- Name_ja 修改稿：
- Description：目标敌人的{buff_burn}立刻生效一次。随后吸收该目标所有{buff_burn}，移除这些{buff_burn}，你获得等量{SunExp_sunexp_gathered_flame}。若吸收不少于10层，抽1张牌。
- Description 修改稿：
- Description_zh-Hant：目標敵人的{buff_burn}立刻生效一次。隨後吸收該目標所有{buff_burn}，移除這些{buff_burn}，你獲得等量{SunExp_sunexp_gathered_flame}。若吸收不少於10層，抽1張牌。
- Description_zh-Hant 修改稿：
- Description_en：Trigger the target enemy's {buff_burn} once immediately. Then absorb all {buff_burn} from that target, removing it and gaining that much {SunExp_sunexp_gathered_flame}. If you absorb at least 10 stacks, draw 1 card.
- Description_en 修改稿：
- Description_ja：対象の敵の{buff_burn}をただちに1回発動する。その後、その対象の{buff_burn}をすべて吸収して取り除き、同量の{SunExp_sunexp_gathered_flame}を得る。10スタック以上吸収したなら1枚引く。
- Description_ja 修改稿：

### 16. 焚污除秽 (`impurity_purge`)

- 元数据：类型=技能牌；稀有度=2；费用=1；标签=无；所属卡包=【日耀：天幕】
- Name：焚污除秽
- Name 修改稿：
- Name_zh-Hant：焚污除穢
- Name_zh-Hant 修改稿：
- Name_en：Impurity Purge
- Name_en 修改稿：
- Name_ja：焚汚転相
- Name_ja 修改稿：
- Description：移除自身所有负面 Buff，并获得等同于这些负面 Buff 总层数的{buff_burn}。若成功转化，获得1层{SunExp_sunexp_solar_radiance}。
- Description 修改稿：
- Description_zh-Hant：移除自身所有負面 Buff，並獲得等同於這些負面 Buff 總層數的{buff_burn}。若成功轉化，獲得1層{SunExp_sunexp_solar_radiance}。
- Description_zh-Hant 修改稿：
- Description_en：Remove all negative buffs from yourself, then gain {buff_burn} equal to their total stacks. If any buff is converted, gain 1 stack of {SunExp_sunexp_solar_radiance}.
- Description_en 修改稿：
- Description_ja：自身のすべての負面 Buff を取り除き、その合計スタック数に等しい{buff_burn}を得る。変換に成功したなら{SunExp_sunexp_solar_radiance}を1スタック得る。
- Description_ja 修改稿：

### 17. 炎轮再临 (`flamewheel_recurrence`)

- 元数据：类型=技能牌；稀有度=2；费用=1；标签=无；所属卡包=【日耀：烬冠】
- Name：炎轮再临
- Name 修改稿：
- Name_zh-Hant：炎輪再臨
- Name_zh-Hant 修改稿：
- Name_en：Flamewheel Recurrence
- Name_en 修改稿：
- Name_ja：炎輪再臨
- Name_ja 修改稿：
- Description：敌方全体的{buff_burn}立刻生效N次，N为本场战斗已使用此牌次数+1。本次总耗费等于N。
- Description 修改稿：
- Description_zh-Hant：敵方全體的{buff_burn}立刻生效N次，N為本場戰鬥已使用此牌次數+1。本次總耗費等於N。
- Description_zh-Hant 修改稿：
- Description_en：Trigger all enemies' {buff_burn} N times immediately, where N is the number of times this card has been used this combat plus 1. This play's total Mana cost equals N.
- Description_en 修改稿：
- Description_ja：すべての敵の{buff_burn}をただちにN回発動する。Nはこの戦闘でこのカードを使用した回数+1。この使用の合計マナ消費はNに等しい。
- Description_ja 修改稿：

### 18. 蚀天之咒 (`eclipse_hex`)

- 元数据：类型=技能牌；稀有度=2；费用=2；标签=无；所属卡包=【日耀：天幕】
- Name：蚀天之咒
- Name 修改稿：
- Name_zh-Hant：蚀天之咒
- Name_zh-Hant 修改稿：
- Name_en：Eclipse Hex
- Name_en 修改稿：
- Name_ja：灼化倍増
- Name_ja 修改稿：
- Description：使目标敌人的{buff_burn}层数翻倍，最高不超过49层；若目标没有{buff_burn}，改为施加6层。随后目标的{buff_burn}立刻生效一次。
- Description 修改稿：
- Description_zh-Hant：使目標敵人的{buff_burn}層數翻倍，最高不超過49層；若目標沒有{buff_burn}，改為施加6層。隨後目標的{buff_burn}立刻生效一次。
- Description_zh-Hant 修改稿：
- Description_en：Double the target enemy's {buff_burn}, up to 49 stacks. If it has none, apply 6 stacks instead. Then trigger its {buff_burn} once immediately.
- Description_en 修改稿：
- Description_ja：対象の敵の{buff_burn}を2倍にする。最大49スタック。持っていない場合は6スタック付与する。その後、対象の{buff_burn}をただちに1回発動する。
- Description_ja 修改稿：

### 19. 日耀灼光 (`solar_scorching_light`)

- 元数据：类型=攻击牌；稀有度=1；费用=1；标签=无；所属卡包=【日耀：烬冠】
- Name：日耀灼光
- Name 修改稿：
- Name_zh-Hant：日耀灼光
- Name_zh-Hant 修改稿：
- Name_en：Solar Scorchlight
- Name_en 修改稿：
- Name_ja：焔勢貫通
- Name_ja 修改稿：
- Description：造成8点伤害。目标每有1层{buff_burn}，额外造成X点伤害，X为自身{SunExp_sunexp_gathered_flame}层数/4，至少为1。
- Description 修改稿：
- Description_zh-Hant：造成8點傷害。目標每有1層{buff_burn}，額外造成X點傷害，X為自身{SunExp_sunexp_gathered_flame}層數/4，至少為1。
- Description_zh-Hant 修改稿：
- Description_en：Deal 8 damage. For each stack of {buff_burn} on the target, deal X extra damage, where X is your {SunExp_sunexp_gathered_flame} stacks divided by 4, minimum 1.
- Description_en 修改稿：
- Description_ja：8ダメージを与える。対象の{buff_burn}1スタックごとに追加でXダメージ。Xは自身の{SunExp_sunexp_gathered_flame}スタック/4、最低1。
- Description_ja 修改稿：

### 20. 燃灾 (`burning_calamity`)

- 元数据：类型=技能牌；稀有度=2；费用=1；标签=无；所属卡包=【日耀：天幕】
- Name：燃灾
- Name 修改稿：
- Name_zh-Hant：燃災
- Name_zh-Hant 修改稿：
- Name_en：Burning Calamity
- Name_en 修改稿：
- Name_ja：残焔伝導
- Name_ja 修改稿：
- Description：选择一个敌人，将其{buff_burn}层数的一半施加给其他所有敌人。随后该目标的{buff_burn}立刻生效一次。
- Description 修改稿：
- Description_zh-Hant：選擇一個敵人，將其{buff_burn}層數的一半施加給其他所有敵人。隨後該目標的{buff_burn}立刻生效一次。
- Description_zh-Hant 修改稿：
- Description_en：Choose an enemy. Apply half of its {buff_burn} stacks to all other enemies. Then trigger that target's {buff_burn} once immediately.
- Description_en 修改稿：
- Description_ja：敵1体を選ぶ。その{buff_burn}スタックの半分を他のすべての敵に付与する。その後、対象の{buff_burn}をただちに1回発動する。
- Description_ja 修改稿：

### 21. 燃冠誓言 (`burning_crown_oath`)

- 元数据：类型=技能牌；稀有度=2；费用=1；标签=无；所属卡包=【日耀：烬冠】
- Name：燃冠誓言
- Name 修改稿：
- Name_zh-Hant：燃冠誓言
- Name_zh-Hant 修改稿：
- Name_en：Burning Crown Oath
- Name_en 修改稿：
- Name_ja：回火輪転
- Name_ja 修改稿：
- Description：消耗至多12层{SunExp_sunexp_gathered_flame}。敌方全体获得等同于消耗层数一半的{buff_burn}。若消耗不少于8层，抽1张牌并获得1点魔能。
- Description 修改稿：
- Description_zh-Hant：消耗至多12層{SunExp_sunexp_gathered_flame}。敵方全體獲得等同於消耗層數一半的{buff_burn}。若消耗不少於8層，抽1張牌並獲得1點魔能。
- Description_zh-Hant 修改稿：
- Description_en：Consume up to 12 stacks of {SunExp_sunexp_gathered_flame}. All enemies gain {buff_burn} equal to half the consumed stacks. If at least 8 stacks are consumed, draw 1 card and gain 1 mana.
- Description_en 修改稿：
- Description_ja：最大12スタックの{SunExp_sunexp_gathered_flame}を消費する。すべての敵は消費数の半分に等しい{buff_burn}を得る。8以上消費したなら1枚引き、魔能を1得る。
- Description_ja 修改稿：

### 22. 晨线护持 (`morning_light_bulwark`)

- 元数据：类型=技能牌；稀有度=1；费用=1；标签=无；所属卡包=【日耀：星火】
- Name：晨线护持
- Name 修改稿：
- Name_zh-Hant：晨線護持
- Name_zh-Hant 修改稿：
- Name_en：Dawnline Guard
- Name_en 修改稿：
- Name_ja：晨線護持
- Name_ja 修改稿：
- Description：获得2层{SunExp_sunexp_solar_radiance}。获得4+当前日耀层数的护盾。
- Description 修改稿：
- Description_zh-Hant：獲得2層{SunExp_sunexp_solar_radiance}。獲得4+當前日耀層數的護盾。
- Description_zh-Hant 修改稿：
- Description_en：Gain 2 stacks of {SunExp_sunexp_solar_radiance}. Gain Block equal to 4 plus your current Radiance stacks.
- Description_en 修改稿：
- Description_ja：{SunExp_sunexp_solar_radiance}を2スタック得る。現在の日耀スタック+4の護盾を得る。
- Description_ja 修改稿：

### 23. 光谱折返 (`solar_return`)

- 元数据：类型=技能牌；稀有度=1；费用=0；标签=无；所属卡包=【日耀：星火】
- Name：光谱折返
- Name 修改稿：
- Name_zh-Hant：光譜折返
- Name_zh-Hant 修改稿：
- Name_en：Spectrum Return
- Name_en 修改稿：
- Name_ja：光譜折返
- Name_ja 修改稿：
- Description：若你拥有{buff_burn}，移除1层并获得1层{SunExp_sunexp_solar_radiance}；否则抽1张牌。
- Description 修改稿：
- Description_zh-Hant：若你擁有{buff_burn}，移除1層並獲得1層{SunExp_sunexp_solar_radiance}；否則抽1張牌。
- Description_zh-Hant 修改稿：
- Description_en：If you have {buff_burn}, remove 1 stack and gain 1 stack of {SunExp_sunexp_solar_radiance}; otherwise draw 1 card.
- Description_en 修改稿：
- Description_ja：自身が{buff_burn}を持つなら1スタック取り除き、{SunExp_sunexp_solar_radiance}を1スタック得る。そうでなければ1枚引く。
- Description_ja 修改稿：

### 24. 源核：日耀 (`solar_origin_core`)

- 元数据：类型=能力牌；稀有度=2；费用=1；标签=Ability；所属卡包=【日耀：星火】
- Name：源核：日耀
- Name 修改稿：
- Name_zh-Hant：源核：日耀
- Name_zh-Hant 修改稿：
- Name_en：Origin Core: Radiance
- Name_en 修改稿：
- Name_ja：小型日冕
- Name_ja 修改稿：
- Description：获得{SunExp_sunexp_origin_core_radiance}。
- Description 修改稿：
- Description_zh-Hant：獲得{SunExp_sunexp_origin_core_radiance}。
- Description_zh-Hant 修改稿：
- Description_en：Gain {SunExp_sunexp_origin_core_radiance}.
- Description_en 修改稿：
- Description_ja：{SunExp_sunexp_origin_core_radiance}を得る。
- Description_ja 修改稿：

### 25. 凝烬成塔 (`ember_tower`)

- 元数据：类型=技能牌；稀有度=1；费用=1；标签=无；所属卡包=【日耀：烬冠】
- Name：凝烬成塔
- Name 修改稿：
- Name_zh-Hant：凝燼成塔
- Name_zh-Hant 修改稿：
- Name_en：Ember Tower
- Name_en 修改稿：
- Name_ja：残り火圧縮
- Name_ja 修改稿：
- Description：将自身至多5层{buff_burn}转化为等量{SunExp_sunexp_gathered_flame}。若转化了5层，抽1张牌。
- Description 修改稿：
- Description_zh-Hant：將自身至多5層{buff_burn}轉化為等量{SunExp_sunexp_gathered_flame}。若轉化了5層，抽1張牌。
- Description_zh-Hant 修改稿：
- Description_en：Convert up to 5 of your {buff_burn} stacks into the same amount of {SunExp_sunexp_gathered_flame}. If 5 stacks were converted, draw 1 card.
- Description_en 修改稿：
- Description_ja：自身の{buff_burn}を最大5スタック、同量の{SunExp_sunexp_gathered_flame}へ変換する。5スタック変換したなら1枚引く。
- Description_ja 修改稿：

### 26. 聚炎护盾 (`gathered_flame_shield`)

- 元数据：类型=技能牌；稀有度=1；费用=1；标签=无；所属卡包=【日耀：烬冠】
- Name：聚炎护盾
- Name 修改稿：
- Name_zh-Hant：聚炎護盾
- Name_zh-Hant 修改稿：
- Name_en：Gathered Flame Shield
- Name_en 修改稿：
- Name_ja：集炎殻
- Name_ja 修改稿：
- Description：获得{SunExp_sunexp_gathered_flame}层数×2的护盾，随后消耗至多4层{SunExp_sunexp_gathered_flame}。
- Description 修改稿：
- Description_zh-Hant：獲得{SunExp_sunexp_gathered_flame}層數×2的護盾，隨後消耗至多4層{SunExp_sunexp_gathered_flame}。
- Description_zh-Hant 修改稿：
- Description_en：Gain Block equal to twice your {SunExp_sunexp_gathered_flame} stacks, then consume up to 4 stacks of {SunExp_sunexp_gathered_flame}.
- Description_en 修改稿：
- Description_ja：{SunExp_sunexp_gathered_flame}スタック×2の護盾を得る。その後{SunExp_sunexp_gathered_flame}を最大4スタック消費する。
- Description_ja 修改稿：

### 27. 轮转：聚炎 (`gathered_flame_cycle`)

- 元数据：类型=能力牌；稀有度=2；费用=2；标签=Ability；所属卡包=【日耀：烬冠】
- Name：轮转：聚炎
- Name 修改稿：
- Name_zh-Hant：輪轉：聚炎
- Name_zh-Hant 修改稿：
- Name_en：Cycle: Gathered Flame
- Name_en 修改稿：
- Name_ja：熔輪蓄圧
- Name_ja 修改稿：
- Description：获得{SunExp_sunexp_cycle_gathered_flame}。
- Description 修改稿：
- Description_zh-Hant：獲得{SunExp_sunexp_cycle_gathered_flame}。
- Description_zh-Hant 修改稿：
- Description_en：Gain {SunExp_sunexp_cycle_gathered_flame}.
- Description_en 修改稿：
- Description_ja：{SunExp_sunexp_cycle_gathered_flame}を得る。
- Description_ja 修改稿：

### 28. 日蚀 (`solar_eclipse`)

- 元数据：类型=技能牌；稀有度=1；费用=1；标签=无；所属卡包=【日耀：天幕】
- Name：日蚀
- Name 修改稿：
- Name_zh-Hant：日蝕
- Name_zh-Hant 修改稿：
- Name_en：Solar Eclipse
- Name_en 修改稿：
- Name_ja：低圧天幕
- Name_ja 修改稿：
- Description：敌方全体获得2层{buff_burn}和1层{buff_weak}。
- Description 修改稿：
- Description_zh-Hant：敵方全體獲得2層{buff_burn}和1層{buff_weak}。
- Description_zh-Hant 修改稿：
- Description_en：All enemies gain 2 stacks of {buff_burn} and 1 stack of {buff_weak}.
- Description_en 修改稿：
- Description_ja：敵全体に{buff_burn}を2スタック、{buff_weak}を1スタック付与する。
- Description_ja 修改稿：

### 29. 烟蚀 (`smoke_erosion`)

- 元数据：类型=攻击牌；稀有度=1；费用=1；标签=无；所属卡包=【日耀：天幕】
- Name：烟蚀
- Name 修改稿：
- Name_zh-Hant：煙蝕
- Name_zh-Hant 修改稿：
- Name_en：Smoke Erosion
- Name_en 修改稿：
- Name_ja：煙蝕
- Name_ja 修改稿：
- Description：造成7点伤害。若目标拥有负面 Buff，给予3层{buff_burn}。
- Description 修改稿：
- Description_zh-Hant：造成7點傷害。若目標擁有負面 Buff，給予3層{buff_burn}。
- Description_zh-Hant 修改稿：
- Description_en：Deal 7 damage. If the target has a negative buff, apply 3 stacks of {buff_burn}.
- Description_en 修改稿：
- Description_ja：7ダメージを与える。対象がデバフを持つなら{buff_burn}を3スタック付与する。
- Description_ja 修改稿：

### 30. 残光病兆 (`afterglow_omen_card`)

- 元数据：类型=能力牌；稀有度=2；费用=2；标签=Ability；所属卡包=【日耀：天幕】
- Name：残光病兆
- Name 修改稿：
- Name_zh-Hant：殘光病兆
- Name_zh-Hant 修改稿：
- Name_en：Afterglow Omen
- Name_en 修改稿：
- Name_ja：残光病兆
- Name_ja 修改稿：
- Description：获得{SunExp_sunexp_afterglow_omen}。
- Description 修改稿：
- Description_zh-Hant：獲得{SunExp_sunexp_afterglow_omen}。
- Description_zh-Hant 修改稿：
- Description_en：Gain {SunExp_sunexp_afterglow_omen}.
- Description_en 修改稿：
- Description_ja：{SunExp_sunexp_afterglow_omen}を得る。
- Description_ja 修改稿：

---

## 遗物文案

### 1. 晨辉碎片 (`morning_shard`)

- 元数据：系列=日耀遗物；标签=日耀；稀有度=1；所属卡包=【日耀：星火】
- Name：晨辉碎片
- Name 修改稿：
- Name_zh-Hant：晨輝碎片
- Name_zh-Hant 修改稿：
- Name_en：Morning Shard
- Name_en 修改稿：
- Name_ja：晨輝の欠片
- Name_ja 修改稿：
- Tips：清晨第一缕光凝成的晶片，握在掌心时仍有温度。
- Tips 修改稿：
- Tips_zh-Hant：清晨第一縷光凝成的晶片，握在掌心時仍有溫度。
- Tips_zh-Hant 修改稿：
- Tips_en：A shard condensed from the first light of dawn, still warm in the palm.
- Tips_en 修改稿：
- Tips_ja：夜明けの最初の光が結晶した欠片。掌の中でまだ温かい。
- Tips_ja 修改稿：
- Description：战斗开始时，获得2层{SunExp_sunexp_solar_radiance}。每场战斗第一次回合开始时，若你拥有{SunExp_sunexp_solar_radiance}，获得5+日耀层数的护盾。
- Description 修改稿：
- Description_zh-Hant：戰鬥開始時，獲得2層{SunExp_sunexp_solar_radiance}。每場戰鬥第一次回合開始時，若你擁有{SunExp_sunexp_solar_radiance}，獲得5+日耀層數的護盾。
- Description_zh-Hant 修改稿：
- Description_en：At combat start, gain 2 stacks of {SunExp_sunexp_solar_radiance}. The first time a round starts each combat, if you have {SunExp_sunexp_solar_radiance}, gain Block equal to 5 plus its stacks.
- Description_en 修改稿：
- Description_ja：戦闘開始時、{SunExp_sunexp_solar_radiance}を2スタック得る。各戦闘で最初のターン開始時、{SunExp_sunexp_solar_radiance}を持つなら5+日耀スタック分の護盾を得る。
- Description_ja 修改稿：

### 2. 烬衣衬布 (`ember_cloak_lining`)

- 元数据：系列=日耀遗物；标签=防火；稀有度=1；所属卡包=【日耀：烬冠】
- Name：烬衣衬布
- Name 修改稿：
- Name_zh-Hant：烬衣襯布
- Name_zh-Hant 修改稿：
- Name_en：Ember Cloak Lining
- Name_en 修改稿：
- Name_ja：断熱裏布
- Name_ja 修改稿：
- Tips：薄得像一层晨雾，却能把火焰折回光里。
- Tips 修改稿：
- Tips_zh-Hant：薄得像一層晨霧，卻能把火焰折回光裡。
- Tips_zh-Hant 修改稿：
- Tips_en：A lining as thin as morning mist, folding flame back into light.
- Tips_en 修改稿：
- Tips_ja：朝霧ほど薄い布地だが、炎を光へ折り返す。
- Tips_ja 修改稿：
- Description：回合开始时，若你拥有{buff_burn}，移除1层{buff_burn}，并获得2层{SunExp_sunexp_gathered_flame}。每回合最多触发一次。
- Description 修改稿：
- Description_zh-Hant：回合開始時，若你擁有{buff_burn}，移除1層{buff_burn}，並獲得2層{SunExp_sunexp_gathered_flame}。每回合最多觸發一次。
- Description_zh-Hant 修改稿：
- Description_en：At round start, if you have {buff_burn}, remove 1 stack of it and gain 2 stacks of {SunExp_sunexp_gathered_flame}. Triggers at most once per round.
- Description_en 修改稿：
- Description_ja：ターン開始時、自身が{buff_burn}を持つなら1スタック取り除き、{SunExp_sunexp_gathered_flame}を2スタック得る。各ターン1回まで。
- Description_ja 修改稿：

### 3. 环日镜 (`sun_orbit_mirror`)

- 元数据：系列=日耀遗物；标签=行动；稀有度=2；所属卡包=【日耀：星火】
- Name：环日镜
- Name 修改稿：
- Name_zh-Hant：環日鏡
- Name_zh-Hant 修改稿：
- Name_en：Sun-Orbit Mirror
- Name_en 修改稿：
- Name_ja：環日鏡
- Name_ja 修改稿：
- Tips：镜面里没有倒影，只有一颗永远绕行的太阳。
- Tips 修改稿：
- Tips_zh-Hant：鏡面裡沒有倒影，只有一顆永遠繞行的太陽。
- Tips_zh-Hant 修改稿：
- Tips_en：There is no reflection in the mirror, only a sun in perpetual orbit.
- Tips_en 修改稿：
- Tips_ja：鏡面に映る影はなく、永遠に巡る太陽だけがある。
- Tips_ja 修改稿：
- Description：每行动3次，若你拥有{SunExp_sunexp_solar_radiance}，对随机敌人施加2层{buff_burn}；否则获得2层{SunExp_sunexp_solar_radiance}。
- Description 修改稿：
- Description_zh-Hant：每行動3次，若你擁有{SunExp_sunexp_solar_radiance}，對隨機敵人施加2層{buff_burn}；否則獲得2層{SunExp_sunexp_solar_radiance}。
- Description_zh-Hant 修改稿：
- Description_en：Every 3 actions, if you have {SunExp_sunexp_solar_radiance}, apply 2 stacks of {buff_burn} to a random enemy; otherwise gain 2 stacks of {SunExp_sunexp_solar_radiance}.
- Description_en 修改稿：
- Description_ja：3回行動するたび、{SunExp_sunexp_solar_radiance}を持つならランダムな敵に{buff_burn}を2スタック付与する。持たないなら{SunExp_sunexp_solar_radiance}を2スタック得る。
- Description_ja 修改稿：

### 4. 太阳瓶 (`sun_bottle`)

- 元数据：系列=日耀遗物；标签=灼烧；稀有度=2；所属卡包=【日耀：天幕】
- Name：太阳瓶
- Name 修改稿：
- Name_zh-Hant：太阳瓶
- Name_zh-Hant 修改稿：
- Name_en：Sun Bottle
- Name_en 修改稿：
- Name_ja：焔流サイフォン
- Name_ja 修改稿：
- Tips：漏斗内壁刻着倒流的火舌，专门收拢失控的热。
- Tips 修改稿：
- Tips_zh-Hant：漏斗內壁刻著倒流的火舌，專門收攏失控的熱。
- Tips_zh-Hant 修改稿：
- Tips_en：Its inner wall is etched with reverse-flowing tongues of fire, built to collect runaway heat.
- Tips_en 修改稿：
- Tips_ja：内壁には逆流する炎の舌が刻まれ、暴走した熱を集める。
- Tips_ja 修改稿：
- Description：回合开始时，随机一名带有{buff_burn}的敌人，其{buff_burn}立刻生效一次；随后移除其1层{buff_burn}，你获得2层{SunExp_sunexp_gathered_flame}。若没有敌人拥有{buff_burn}，改为对随机敌人施加2层{buff_burn}。
- Description 修改稿：
- Description_zh-Hant：回合開始時，隨機一名帶有{buff_burn}的敵人，其{buff_burn}立刻生效一次；隨後移除其1層{buff_burn}，你獲得2層{SunExp_sunexp_gathered_flame}。若沒有敵人擁有{buff_burn}，改為對隨機敵人施加2層{buff_burn}。
- Description_zh-Hant 修改稿：
- Description_en：At round start, choose a random enemy with {buff_burn} and trigger its {buff_burn} once. Then remove 1 stack from it and gain 2 stacks of {SunExp_sunexp_gathered_flame}. If no enemy has {buff_burn}, apply 2 stacks to a random enemy instead.
- Description_en 修改稿：
- Description_ja：ターン開始時、{buff_burn}を持つランダムな敵1体の{buff_burn}を1回発動する。その後、その敵から1スタック取り除き、{SunExp_sunexp_gathered_flame}を2スタック得る。該当する敵がいない場合、ランダムな敵に{buff_burn}を2スタック付与する。
- Description_ja 修改稿：

### 5. 日相刻盘 (`solar_phase_dial`)

- 元数据：系列=日耀遗物；标签=阶段；稀有度=3；所属卡包=【日耀：星火】
- Name：日相刻盘
- Name 修改稿：
- Name_zh-Hant：日相刻盤
- Name_zh-Hant 修改稿：
- Name_en：Solar Phase Dial
- Name_en 修改稿：
- Name_ja：黄道刻盤
- Name_ja 修改稿：
- Tips：刻盘的每一格都对应一次太阳角度的偏移。
- Tips 修改稿：
- Tips_zh-Hant：刻盤的每一格都對應一次太陽角度的偏移。
- Tips_zh-Hant 修改稿：
- Tips_en：Each notch on the dial marks a shift in the sun angle.
- Tips_en 修改稿：
- Tips_ja：盤の一目盛りごとに太陽角のずれが刻まれている。
- Tips_ja 修改稿：
- Description：每场战斗中，{SunExp_sunexp_solar_radiance}首次达到4/8/12层时分别触发：抽1张牌、获得1点魔能、清除自身{buff_burn}并使敌方全体{buff_burn}立刻生效一次。
- Description 修改稿：
- Description_zh-Hant：每場戰鬥中，{SunExp_sunexp_solar_radiance}首次達到4/8/12層時分別觸發：抽1張牌、獲得1點魔能、清除自身{buff_burn}並使敵方全體{buff_burn}立刻生效一次。
- Description_zh-Hant 修改稿：
- Description_en：Each combat, the first time {SunExp_sunexp_solar_radiance} reaches 4/8/12 stacks, respectively: draw 1 card, gain 1 mana, clear your {buff_burn} and trigger all enemies' {buff_burn} once.
- Description_en 修改稿：
- Description_ja：各戦闘で{SunExp_sunexp_solar_radiance}が初めて4/8/12に到達した時、それぞれ1枚引く、魔能を1得る、自身の{buff_burn}を消して全敵の{buff_burn}を1回発動する。
- Description_ja 修改稿：

### 6. 小型日轮 (`miniature_sunwheel`)

- 元数据：系列=日耀遗物；标签=防御；稀有度=3；所属卡包=【日耀：烬冠】
- Name：小型日轮
- Name 修改稿：
- Name_zh-Hant：小型日輪
- Name_zh-Hant 修改稿：
- Name_en：Miniature Sunwheel
- Name_en 修改稿：
- Name_ja：白昼の天蓋
- Name_ja 修改稿：
- Tips：罩住城邦的不是玻璃，而是被固定下来的正午。
- Tips 修改稿：
- Tips_zh-Hant：罩住城邦的不是玻璃，而是被固定下來的正午。
- Tips_zh-Hant 修改稿：
- Tips_en：What shields the city is not glass, but noon held in place.
- Tips_en 修改稿：
- Tips_ja：都市を覆うのはガラスではなく、固定された正午そのものだ。
- Tips_ja 修改稿：
- Description：回合开始时，若你拥有{SunExp_sunexp_scorching_canopy}，获得等同于其层数×3的护盾；随后若自身拥有{buff_burn}，将1层转化为{SunExp_sunexp_gathered_flame}，否则获得1层{SunExp_sunexp_solar_radiance}。
- Description 修改稿：
- Description_zh-Hant：回合開始時，若你擁有{SunExp_sunexp_scorching_canopy}，獲得等同於其層數×3的護盾；隨後若自身擁有{buff_burn}，將1層轉化為{SunExp_sunexp_gathered_flame}，否則獲得1層{SunExp_sunexp_solar_radiance}。
- Description_zh-Hant 修改稿：
- Description_en：At round start, if you have {SunExp_sunexp_scorching_canopy}, gain Block equal to its stacks ×3. Then if you have {buff_burn}, convert 1 stack into {SunExp_sunexp_gathered_flame}; otherwise gain 1 stack of {SunExp_sunexp_solar_radiance}.
- Description_en 修改稿：
- Description_ja：ターン開始時、{SunExp_sunexp_scorching_canopy}を持つならそのスタック×3の護盾を得る。その後、自身が{buff_burn}を持つなら1スタックを{SunExp_sunexp_gathered_flame}に変換し、持たないなら{SunExp_sunexp_solar_radiance}を1スタック得る。
- Description_ja 修改稿：

### 7. 炽冠圣心 (`blazing_crown_heart`)

- 元数据：系列=日耀遗物；标签=核心；稀有度=4；所属卡包=【日耀：天幕】
- Name：炽冠圣心
- Name 修改稿：
- Name_zh-Hant：熾冠聖心
- Name_zh-Hant 修改稿：
- Name_en：Blazing Crown Heart
- Name_en 修改稿：
- Name_ja：炽冠圣心
- Name_ja 修改稿：
- Tips：它不像遗物，更像一颗被迫保持安静的小型太阳。
- Tips 修改稿：
- Tips_zh-Hant：它不像遺物，更像一顆被迫保持安靜的小型太陽。
- Tips_zh-Hant 修改稿：
- Tips_en：Less a relic than a small sun forced to remain quiet.
- Tips_en 修改稿：
- Tips_ja：遺物というより、静かでいることを強いられた小さな太陽だ。
- Tips_ja 修改稿：
- Description：战斗开始时，获得1层{SunExp_sunexp_solar_crown}、4层{SunExp_sunexp_solar_radiance}、2层{SunExp_sunexp_scorching_canopy}。回合开始时，全体获得来自炽灼天幕的{buff_burn}；若你拥有{SunExp_sunexp_ember_cloak}或{SunExp_sunexp_solar_radiance}达到12层，本次自身不获得该{buff_burn}，且敌方全体额外获得1层{buff_burn}。
- Description 修改稿：
- Description_zh-Hant：戰鬥開始時，獲得1層{SunExp_sunexp_solar_crown}、4層{SunExp_sunexp_solar_radiance}、2層{SunExp_sunexp_scorching_canopy}。回合開始時，全體獲得來自炽灼天幕的{buff_burn}；若你擁有{SunExp_sunexp_ember_cloak}或{SunExp_sunexp_solar_radiance}達到12層，本次自身不獲得該{buff_burn}，且敵方全體額外獲得1層{buff_burn}。
- Description_zh-Hant 修改稿：
- Description_en：At combat start, gain 1 stack of {SunExp_sunexp_solar_crown}, 4 stacks of {SunExp_sunexp_solar_radiance}, and 2 stacks of {SunExp_sunexp_scorching_canopy}. At round start, Scorching Canopy burns all combatants. If you have {SunExp_sunexp_ember_cloak} or at least 12 Radiance, you avoid that self-burn and all enemies gain 1 extra stack of {buff_burn}.
- Description_en 修改稿：
- Description_ja：戦闘開始時、{SunExp_sunexp_solar_crown}を1、{SunExp_sunexp_solar_radiance}を4、{SunExp_sunexp_scorching_canopy}を2スタック得る。ターン開始時、炽灼天幕により全員が{buff_burn}を得る。{SunExp_sunexp_ember_cloak}を持つ、または日耀12以上なら自身はその灼焼を受けず、全敵が追加で{buff_burn}を1スタック得る。
- Description_ja 修改稿：

### 8. 日心棱镜 (`solar_prism`)

- 元数据：系列=日耀遗物；标签=日耀；稀有度=1；所属卡包=【日耀：星火】
- Name：日心棱镜
- Name 修改稿：
- Name_zh-Hant：日心稜鏡
- Name_zh-Hant 修改稿：
- Name_en：Solar Prism
- Name_en 修改稿：
- Name_ja：日心プリズム
- Name_ja 修改稿：
- Tips：棱镜中心封着一枚微型日核，转动时会折出第二道晨光。
- Tips 修改稿：
- Tips_zh-Hant：稜鏡中心封著一枚微型日核，轉動時會折出第二道晨光。
- Tips_zh-Hant 修改稿：
- Tips_en：A miniature solar core is sealed inside, refracting a second dawn when turned.
- Tips_en 修改稿：
- Tips_ja：中心に小さな太陽核が封じられ、回すと第二の夜明けを屈折する。
- Tips_ja 修改稿：
- Description：战斗开始时，获得1层{SunExp_sunexp_solar_radiance}。每回合第一次获得{SunExp_sunexp_solar_radiance}后，获得1层{buff_extraordinary}。
- Description 修改稿：
- Description_zh-Hant：戰鬥開始時，獲得1層{SunExp_sunexp_solar_radiance}。每回合第一次獲得{SunExp_sunexp_solar_radiance}後，獲得1層{buff_extraordinary}。
- Description_zh-Hant 修改稿：
- Description_en：At combat start, gain 1 stack of {SunExp_sunexp_solar_radiance}. Each round, after you first gain {SunExp_sunexp_solar_radiance}, gain 1 stack of {buff_extraordinary}.
- Description_en 修改稿：
- Description_ja：戦闘開始時、{SunExp_sunexp_solar_radiance}を1スタック得る。各ターンで初めて{SunExp_sunexp_solar_radiance}を得た後、{buff_extraordinary}を1スタック得る。
- Description_ja 修改稿：

### 9. 授冕圣座 (`coronation_throne`)

- 元数据：系列=日耀遗物；标签=圣冕；稀有度=2；所属卡包=【日耀：星火】
- Name：授冕圣座
- Name 修改稿：
- Name_zh-Hant：授冕圣座
- Name_zh-Hant 修改稿：
- Name_en：Coronation Throne
- Name_en 修改稿：
- Name_ja：授冕圣座
- Name_ja 修改稿：
- Tips：它不产生光，只负责让真正的冠冕安稳降下。
- Tips 修改稿：
- Tips_zh-Hant：它不產生光，只負責讓真正的冠冕安穩降下。
- Tips_zh-Hant 修改稿：
- Tips_en：It does not create light; it only helps the true crown descend safely.
- Tips_en 修改稿：
- Tips_ja：光を生まず、真の冠が安らかに降りる場を整えるだけだ。
- Tips_ja 修改稿：
- Description：每场战斗第一次获得{SunExp_sunexp_solar_crown}后，抽1张牌并获得2点护盾。
- Description 修改稿：
- Description_zh-Hant：每場戰鬥第一次獲得{SunExp_sunexp_solar_crown}後，抽1張牌並獲得2點護盾。
- Description_zh-Hant 修改稿：
- Description_en：Each combat, after you first gain {SunExp_sunexp_solar_crown}, draw 1 card and gain 2 Block.
- Description_en 修改稿：
- Description_ja：各戦闘で初めて{SunExp_sunexp_solar_crown}を得た後、1枚引き、2護盾を得る。
- Description_ja 修改稿：

### 10. 聚炎护符 (`gathered_flame_charm`)

- 元数据：系列=日耀遗物；标签=聚炎；稀有度=1；所属卡包=【日耀：烬冠】
- Name：聚炎护符
- Name 修改稿：
- Name_zh-Hant：聚炎護符
- Name_zh-Hant 修改稿：
- Name_en：Gathered Flame Charm
- Name_en 修改稿：
- Name_ja：聚炎護符
- Name_ja 修改稿：
- Tips：护符里的火没有出口，只能向内凝成更密的热。
- Tips 修改稿：
- Tips_zh-Hant：護符裡的火沒有出口，只能向內凝成更密的熱。
- Tips_zh-Hant 修改稿：
- Tips_en：The fire inside has no exit, so it condenses inward into denser heat.
- Tips_en 修改稿：
- Tips_ja：護符の火に出口はなく、内側へ濃い熱として凝る。
- Tips_ja 修改稿：
- Description：每回合第一次自身{buff_burn}层数增加后，获得2层{SunExp_sunexp_gathered_flame}。
- Description 修改稿：
- Description_zh-Hant：每回合第一次自身{buff_burn}層數增加後，獲得2層{SunExp_sunexp_gathered_flame}。
- Description_zh-Hant 修改稿：
- Description_en：Each round, after your {buff_burn} stacks first increase, gain 2 stacks of {SunExp_sunexp_gathered_flame}.
- Description_en 修改稿：
- Description_ja：各ターンで初めて自身の{buff_burn}が増えた後、{SunExp_sunexp_gathered_flame}を2スタック得る。
- Description_ja 修改稿：

### 11. 灰烬护符 (`ash_charm`)

- 元数据：系列=日耀遗物；标签=防火；稀有度=2；所属卡包=【日耀：烬冠】
- Name：灰烬护符
- Name 修改稿：
- Name_zh-Hant：灰燼護符
- Name_zh-Hant 修改稿：
- Name_en：Ash Charm
- Name_en 修改稿：
- Name_ja：残り火圧弁
- Name_ja 修改稿：
- Tips：阀门每次开启都像一次短促的日出。
- Tips 修改稿：
- Tips_zh-Hant：閥門每次開啟都像一次短促的日出。
- Tips_zh-Hant 修改稿：
- Tips_en：Each opening of the valve feels like a brief sunrise.
- Tips_en 修改稿：
- Tips_ja：弁が開くたび、短い日の出のように光る。
- Tips_ja 修改稿：
- Description：回合开始时，若你拥有至少4层{buff_burn}，移除2层，获得2点护盾和2层{SunExp_sunexp_gathered_flame}。
- Description 修改稿：
- Description_zh-Hant：回合開始時，若你擁有至少4層{buff_burn}，移除2層，獲得2點護盾和2層{SunExp_sunexp_gathered_flame}。
- Description_zh-Hant 修改稿：
- Description_en：At round start, if you have at least 4 stacks of {buff_burn}, remove 2 stacks, gain 2 Block, and gain 2 stacks of {SunExp_sunexp_gathered_flame}.
- Description_en 修改稿：
- Description_ja：ターン開始時、自身が{buff_burn}を4スタック以上持つなら2スタック取り除き、2護盾と{SunExp_sunexp_gathered_flame}2スタックを得る。
- Description_ja 修改稿：

### 12. 曜阳日晷 (`blazing_sundial`)

- 元数据：系列=日耀遗物；标签=天幕；稀有度=1；所属卡包=【日耀：天幕】
- Name：曜阳日晷
- Name 修改稿：
- Name_zh-Hant：曜陽日晷
- Name_zh-Hant 修改稿：
- Name_en：Blazing Sundial
- Name_en 修改稿：
- Name_ja：低圧穹幕
- Name_ja 修改稿：
- Tips：它把天空压低一点，让火焰和呼吸都变得迟缓。
- Tips 修改稿：
- Tips_zh-Hant：它把天空壓低一點，讓火焰和呼吸都變得遲緩。
- Tips_zh-Hant 修改稿：
- Tips_en：It lowers the sky just enough to slow both flame and breath.
- Tips_en 修改稿：
- Tips_ja：空を少し低く押し下げ、炎も呼吸も鈍らせる。
- Tips_ja 修改稿：
- Description：回合开始时，若敌方全体拥有{buff_burn}，敌方全体获得1层{buff_weak}。
- Description 修改稿：
- Description_zh-Hant：回合開始時，若敵方全體擁有{buff_burn}，敵方全體獲得1層{buff_weak}。
- Description_zh-Hant 修改稿：
- Description_en：At round start, if all enemies have {buff_burn}, all enemies gain 1 stack of {buff_weak}.
- Description_en 修改稿：
- Description_ja：ターン開始時、敵全体が{buff_burn}を持つなら、敵全体に{buff_weak}を1スタック付与する。
- Description_ja 修改稿：

### 13. 燃灾风带 (`burning_calamity_wind_belt`)

- 元数据：系列=日耀遗物；标签=扩散；稀有度=2；所属卡包=【日耀：天幕】
- Name：燃灾风带
- Name 修改稿：
- Name_zh-Hant：燃災風帶
- Name_zh-Hant 修改稿：
- Name_en：Burning Calamity Wind Belt
- Name_en 修改稿：
- Name_ja：赤道風帯
- Name_ja 修改稿：
- Tips：环形热风总会把一处火星带到另一处阴影里。
- Tips 修改稿：
- Tips_zh-Hant：環形熱風總會把一處火星帶到另一處陰影裡。
- Tips_zh-Hant 修改稿：
- Tips_en：Ring-shaped hot winds carry sparks from one shadow to another.
- Tips_en 修改稿：
- Tips_ja：輪状の熱風は火花を一つの影から別の影へ運ぶ。
- Tips_ja 修改稿：
- Description：回合开始时，至多4名带有{buff_burn}的敌人各使随机另一名敌人获得1层{buff_burn}。
- Description 修改稿：
- Description_zh-Hant：回合開始時，至多4名帶有{buff_burn}的敵人各使隨機另一名敵人獲得1層{buff_burn}。
- Description_zh-Hant 修改稿：
- Description_en：At round start, up to 4 enemies with {buff_burn} each cause a random other enemy to gain 1 stack of {buff_burn}.
- Description_en 修改稿：
- Description_ja：ターン開始時、{buff_burn}を持つ敵最大4体が、それぞれランダムな別の敵に{buff_burn}を1スタック付与する。
- Description_ja 修改稿：

