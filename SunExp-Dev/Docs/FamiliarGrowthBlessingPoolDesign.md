# Familiar Growth Blessing Pool Design

Status: Draft for review
Scope: SunExp familiar growth system

## Goals

The familiar growth blessing system is an out-of-run progression layer for
registered Partner species and their individual familiar instances.

This draft focuses on blessing-pool content. It does not define final C#
implementation details, CSV rows, or shipped balance.

Core goals:

- Keep the common familiar blessing pool based on base-game resources, buffs,
  blessings, and combat concepts.
- Keep SunExp-specific mechanics such as Embers, Solar Radiance, Starlight, and
  Star-Clay Body out of the common pool.
- Put SunExp-specific mechanics only in species-specific or series-specific
  familiar pools.
- Prevent all familiar-growth blessings from entering the base-game random
  blessing pool.
- Reuse existing game icon resources where possible.

## Random Pool Isolation

Every familiar-growth blessing that is also represented in `Data/Blessing`
should use both safeguards:

- `Id` starts with `*`
- `Weight` is `0`

Recommended `Data/Blessing` row conventions:

```csv
Id,Weight,OwnScript,FightScript,Icon,Type,Source,Rarity
*familiar_guard_paw,0,,,Icon/Blessing/守卫,使魔,使魔成长,1
```

Actual familiar growth selection should be driven by
`SunExp/familiar.blessing.registry.json`, not by the base-game random blessing
table.

Recommended registry fields:

```json
{
  "id": "*familiar_guard_paw",
  "name": "护主小爪",
  "tier": 1,
  "weight": 100,
  "species": ["*"],
  "pool": "common",
  "tags": ["base-game", "defense"],
  "exclusiveGroup": "",
  "effects": [
    { "kind": "RunStartShield", "value": "3" }
  ]
}
```

## Aptitude Rules

Familiar aptitude is stored as an integer from `0` to `100`.

Display stages:

| Range | Display |
|---:|---|
| 0-29 | 普通 |
| 30-49 | 良好 |
| 50-69 | 优秀 |
| 70-89 | 了不起的天分 |
| 90-100 | 完美 |

Body instances, such as `species-000`, default to aptitude `70`.

Tier roll weights by aptitude:

| Aptitude Stage | Tier 1 | Tier 2 | Tier 3 | Tier 4 | Tier 5 |
|---|---:|---:|---:|---:|---:|
| 普通 | 100% | 0% | 0% | 0% | 0% |
| 良好 | 75% | 25% | 0% | 0% | 0% |
| 优秀 | 40% | 30% | 30% | 0% | 0% |
| 了不起的天分 | 20% | 20% | 30% | 30% | 0% |
| 完美 | 10% | 20% | 20% | 30% | 20% |

## Pool Boundaries

Common pool:

- Base-game combat resources: HP, max HP, mana, shield, card draw, gold,
  reward choices, dice/checks.
- Base-game positive buffs: `{buff_evergreen}`, `{buff_resilient}`,
  `{buff_fast}`, `{buff_cycle}`, `{buff_keenedge}`, `{buff_elements}`,
  `{buff_impregnable}`, `{buff_contagion}`, `{buff_chrysalis}`,
  `{buff_rebirth}`, `{buff_extraordinary}`.
- Base-game negative/offensive buffs: `{buff_vulnerability}`,
  `{buff_bleeding}`, `{buff_burn}`.

Species-specific pool:

- Partner identity.
- Native Partner blessing enhancement.
- Species-specific run behavior.
- SunExp-specific mechanics when the species belongs to SunExp.

Series-specific pool:

- SunExp mechanics shared by multiple SunExp species, such as Embers, Solar
  Radiance, Starlight, Star Score, Projection, or Manifest.
- Not part of the first common pool.

## Common Blessing Pool

The first common pool contains 20 blessings. All effects should be conservative
because these blessings are intended to be reusable by any familiar species.

| Tier | ID | Name | Icon | Tags | Effect Draft |
|---:|---|---|---|---|---|
| 1 | `*familiar_guard_paw` | 护主小爪 | `Icon/Blessing/守卫` | defense, shield | At combat start, gain 3 Shield. |
| 1 | `*familiar_first_aid` | 轻柔照护 | `Icon/Blessing/牧师` | survival, heal | At combat start, restore 2 HP. |
| 1 | `*familiar_coin_nose` | 钱袋嗅觉 | `Icon/Blessing/国王` | economy, gold | After combat victory, gain 5 extra Gold. |
| 1 | `*familiar_quick_peek` | 探头探脑 | `Icon/Blessing/工匠` | draw, opening | At combat start, draw 1 extra card. |
| 2 | `*familiar_evergreen_down` | 自愈绒羽 | `Icon/Blessing/天使` | survival, regeneration | At combat start, gain 1 stack of `{buff_evergreen}`. |
| 2 | `*familiar_resilient_shell` | 强韧外壳 | `Icon/Blessing/盾卫` | defense, mitigation | At combat start, gain 1 stack of `{buff_resilient}`. |
| 2 | `*familiar_keen_claw` | 磨亮爪尖 | `Icon/Blessing/士官` | offense, damage | At combat start, gain 1 stack of `{buff_keenedge}`. |
| 2 | `*familiar_weak_spot` | 破绽嗅探 | `Icon/Blessing/萨满` | debuff, vulnerability | At combat start, a random enemy gains 1 stack of `{buff_vulnerability}`. |
| 3 | `*familiar_fast_shadow` | 敏锐影步 | `Icon/Blessing/游侠` | draw, tempo | At combat start, gain 1 stack of `{buff_fast}`. |
| 3 | `*familiar_cycle_habit` | 循环习惯 | `Icon/Blessing/命运之轮` | mana, shuffle | At combat start, gain 1 stack of `{buff_cycle}`. |
| 3 | `*familiar_bleeding_mark` | 伤口标记 | `Icon/Blessing/血鬼` | debuff, bleed | The first time you deal damage each combat, the target gains 3 stacks of `{buff_bleeding}`. |
| 3 | `*familiar_burn_mark` | 火星标记 | `Icon/Blessing/审判` | debuff, burn | The first time you deal damage each combat, the target gains 3 stacks of `{buff_burn}`. |
| 4 | `*familiar_elemental_breath` | 元素吐息 | `Icon/Blessing/主教` | offense, elements | At combat start, gain 2 stacks of `{buff_elements}`. |
| 4 | `*familiar_impregnable_guard` | 坚毅护卫 | `Icon/Blessing/统合城邦` | defense, percent-mitigation | At combat start, gain 1 stack of `{buff_impregnable}`. |
| 4 | `*familiar_combo_signal` | 连携信号 | `Icon/Blessing/月亮` | offense, draw-trigger | At combat start, gain 1 stack of `{buff_contagion}`. |
| 4 | `*familiar_chrysalis_cover` | 庇护轮廓 | `Icon/Blessing/太阳` | survival, damage-cap | At combat start, gain 1 stack of `{buff_chrysalis}`. |
| 5 | `*familiar_rebirth_oath` | 再起约定 | `Icon/Blessing/重生保险` | survival, rebirth | At combat start, gain 30 stacks of `{buff_rebirth}`. Intended to be once per run or heavily gated. |
| 5 | `*familiar_law_of_luck` | 偏移之骰 | `Icon/Blessing/规训律法` | dice, check | During the run, value dice and check dice receive a small bonus. Exact value TBD. |
| 5 | `*familiar_reward_omen` | 奖赏预感 | `Icon/Blessing/统治手谕` | reward, economy | Combat rewards gain 1 extra choice. Needs a per-run cap. |
| 5 | `*familiar_extraordinary_bond` | 超凡羁绊 | `Icon/Blessing/虚无之心` | offense, extraordinary | At combat start, gain 50 stacks of `{buff_extraordinary}`. |

## Common Pool Balance Notes

- Tier 1 should feel useful but small.
- Tier 2 introduces stable base-game buffs.
- Tier 3 introduces rhythm or first-hit effects.
- Tier 4 introduces strong base-game buff packages.
- Tier 5 should be powerful, but should require run caps, exclusivity, or high
  aptitude to avoid turning the system into pure stat inflation.

Recommended common-pool exclusive groups:

| Exclusive Group | Members | Reason |
|---|---|---|
| `common_t5_core` | all tier 5 common blessings | Avoid stacking multiple top-end passive engines on one familiar. |
| `opening_defense` | shield, resilient, impregnable, chrysalis | Optional if opening defense becomes too dense. |
| `first_hit_debuff` | bleeding mark, burn mark | Optional if first-hit debuffs become too strong. |

## Dusk Species Pool

Dusk can use SunExp mechanics because this pool is species-specific.

Current Partner identity:

- Species ID: `dusk`
- Native blessing: `SunExp_sunexp_dusk_afterheat_recovery`
- Theme: afterheat, Burn, Embers, ash-gold recovery, sunset storage.

| Tier | ID | Name | Icon | Tags | Effect Draft |
|---:|---|---|---|---|---|
| 1 | `*familiar_dusk_warm_fur` | 暖绒余火 | `Mods/SunExp/ModResource/Images/Buff/SunExp/huanghun_1` | dusk, burn | At combat start, a random enemy gains 2 stacks of `{buff_burn}`. |
| 2 | `*familiar_dusk_ash_nose` | 灰金嗅觉 | `Icon/Blessing/萨满` | dusk, ember | Once per turn, after an enemy's Burn triggers, gain a small amount of Embers. |
| 3 | `*familiar_dusk_afterheat_store` | 熄前回收 | `Icon/Blessing/审判` | dusk, afterheat | When enemy Burn triggers, convert part of the triggered stacks into Embers or Gathered Flame. |
| 4 | `*familiar_dusk_sunset_seal` | 残阳封存 | `Icon/Blessing/太阳` | dusk, burn-transfer | When Embers offset Burn, transfer part of the offset stacks to a random enemy as Burn. |
| 5 | `*familiar_dusk_manifest` | 黄昏现形 | `Mods/SunExp/ModResource/Images/Partner/SunExp/dusk_choice` | dusk, manifest | Unlock Dusk Manifest. Manifested Dusk periodically triggers enemy Burn and recovers Embers. |

Recommended exclusive group:

- `species_dusk_manifest`: `*familiar_dusk_manifest`
- `species_dusk_afterheat_engine`: tier 3 and tier 4 may be mutually exclusive
  if their conversion loops become too strong.

## Star-Clay Doll Species Pool

Star-Clay Doll can use SunExp star mechanics because this pool is
species-specific.

Current Partner identity:

- Species ID: `star_clay_doll`
- Native blessing: `SunExp_sunexp_star_clay_doll_placeholder`
- Theme: Starlight, Star Blessing, Star Score, clay shell, unfinished road.

| Tier | ID | Name | Icon | Tags | Effect Draft |
|---:|---|---|---|---|---|
| 1 | `*familiar_star_clay_memory_step` | 星泥记路 | `Mods/SunExp/ModResource/Images/Buff/Loneer/renkui_1` | star-clay, starlight | At combat start, gain a small amount of Starlight. |
| 2 | `*familiar_star_clay_handed_light` | 笨拙递光 | `Icon/Blessing/星星` | star-clay, starlight | Once per turn after your first action, gain a small amount of Starlight. |
| 3 | `*familiar_star_clay_half_phrase` | 半句星谱 | `Icon/Blessing/月亮` | star-clay, star-score | The first time each run you form a Star Score phrase, gain an extra Star Blessing. |
| 4 | `*familiar_star_clay_shell` | 替身泥壳 | `Icon/Blessing/重生保险` | star-clay, survival | Once per run, before lethal damage, if you do not have Star-Clay Body, gain 1 stack of Star-Clay Body. |
| 5 | `*familiar_star_clay_manifest` | 星泥现形 | `Mods/SunExp/ModResource/Images/Partner/SunExp/RenKui_choice` | star-clay, manifest | Unlock Star-Clay Doll Manifest. Manifested Star-Clay Doll helps accumulate Starlight and provides survival pacing. |

Recommended exclusive group:

- `species_star_clay_manifest`: `*familiar_star_clay_manifest`
- `species_star_clay_survival`: `*familiar_star_clay_shell` and any future
  Star-Clay resurrection blessing.

## Registry Category Plan

Recommended registry `pool` values:

| Pool | Meaning |
|---|---|
| `common` | Base-game, reusable by all familiar species. |
| `species.dusk` | Dusk-only blessings. |
| `species.star_clay_doll` | Star-Clay Doll-only blessings. |
| `series.sunexp.solar` | Future SunExp Solar/Ember shared pool. |
| `series.sunexp.star` | Future SunExp Star/Starlight shared pool. |
| `module.projection` | Future Projection/Manifest integration pool. |

Recommended effect-kind naming:

| Effect Kind | Purpose |
|---|---|
| `RunStartShield` | Grant shield at combat start. |
| `RunStartHeal` | Heal at combat start. |
| `RunStartDraw` | Draw cards at combat start. |
| `RunStartBuff` | Grant a base-game or mod buff at combat start. |
| `BattleWinGold` | Grant extra gold after battle win. |
| `RewardChoiceBonus` | Add reward choices with caps. |
| `FirstDamageApplyBuff` | Apply a buff to the first damaged target in combat. |
| `DiceBonus` | Apply dice/check adjustments. |
| `ManifestEnable` | Enable species Manifest. |
| `CompanionIntentPoolPatch` | Patch manifested unit decision pool. |

## Open Questions

1. Should tier 5 common blessings be mutually exclusive per familiar?
2. Should common tier 5 blessings be limited to one active blessing per run,
   even if the familiar owns several?
3. Should `*familiar_reward_omen` apply to all battle rewards or only ordinary
   combat nodes?
4. Should species-specific tier 5 Manifest blessings require the common
   `ManifestEnable` effect kind, or use one species-specific effect kind each?
5. Should body instances receive a fixed first blessing, or should they roll
   from the same pool as other instances?

