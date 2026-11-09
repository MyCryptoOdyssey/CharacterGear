# Research: armor by body part, and the value of each rating (2026-11-08)

Source of truth: ACEmulator source (the server software), checked line by line. Wiki pages used only to
cross-check. Every number below is what the server actually computes.

## 1. Where monsters hit you (ACE `BodyPart.cs`, `Monster_Melee.cs`, `Monster_Combat.cs`)

A monster attack first picks a height (High / Medium / Low, equal odds for monsters that have all three), then a
random body part inside that height:

| Height | Body parts it can hit |
|---|---|
| High | Head, Chest, Upper arms |
| Medium | Chest, Abdomen, Upper arms, Lower arms, Hands, Upper legs |
| Low | Lower legs, Feet |

So **all 9 parts can be hit**, but **not equally often**:

| Part | Chance per hit | | Part | Chance per hit |
|---|---|---|---|---|
| Chest | 16.7% | | Head | 11.1% |
| Upper arms | 16.7% | | Abdomen | 5.6% |
| Lower legs | 16.7% | | Lower arms | 5.6% |
| Feet | 16.7% | | Hands | 5.6% |
| | | | Upper legs | 5.6% |

Chest, upper arms, lower legs and feet take **3x** the hits of abdomen, lower arms, hands and upper legs.
(Monsters without a High attack never hit the head; spells ignore armor entirely.)

## 2. What armor level does (ACE `SkillFormula.CalcArmorMod`, `Monster_Melee.GetArmorMod`)

Armor on a body part = every piece covering it (all layers add) x its damage-type modifier + banes, **plus**
whole-body Armor Self spells. Damage that gets through = **66.67 / (AL + 66.67)**.

| Total AL on the part | Damage that gets through |
|---|---|
| 300 | 18.2% |
| 500 | 11.8% |
| 600 | 10.0% |
| 747 (522 piece + 225 Armor Self VII) | 8.2% |
| 900 | 6.9% |

Diminishing returns: each extra AL point matters less the more you have. The planner now scores armor as
**expected damage taken** = sum over parts of (hit chance x 66.67 / (AL + 66.67)).

## 3. How the ratings work (ACE `Creature_Rating.cs`, `DamageEvent.cs`, `WorldObject_Weapon.cs`)

| Rating | Formula | When it applies |
|---|---|---|
| Damage (D) | x (100 + D) / 100 | every hit |
| Crit damage (CD) | added to D **on crits only**: x (100 + D + CD) / 100 | crits |
| Crit (CR) | crit chance + CR% (base 10%) | chance to crit |
| Damage resist (DR) | x 100 / (100 + DR) | every hit you take |
| Crit damage resist (CDR) | added to DR **on crits only**: x 100 / (100 + DR + CDR) | crits you take |
| Crit resist (CRR) | enemy crit chance x 100 / (100 + CRR) | chance you get crit |

A crit does maximum damage x 2 (default weapon), a normal hit about 75% of maximum.

## 4. What one rating point is worth (base 10% crit chance, starting from 0)

| Offense | Damage per point | | Defense | Less damage taken per point |
|---|---|---|---|---|
| **Crit** | +1.4% | | **Damage resist** | -1.0% (falls slowly as DR grows) |
| **Damage** | +1.0% | | Crit damage resist | -0.23% |
| Crit damage | +0.23% | | Crit resist | -0.14% |

Conclusions:
- **Damage rating beats crit damage rating point for point, always** - damage rating also boosts crits, while crit
  damage only works on the ~10% of hits that crit. At a 30% crit chance (Critical Strike weapon) crit damage is
  still only about half as good. The player consensus is right, and the math backs it.
- **Crit rating is the strongest offensive rating per point** (more crits = more max-damage x2 hits), but it is rare
  on gear.
- **Damage resist is by far the best defensive rating.** Crit damage resist and crit resist only touch the crit
  slice of incoming damage (~23% of physical damage at a 10% monster crit rate).
- Damage resist vs armor: at a typical end-game armor level (~747 per part with Armor Self), **+1 damage resist is worth about
  the same as +8 AL on every body part**.

## 5. Armor sets

Full table of every set, piece counts and bonuses: `docs/ARMOR_SETS.html` (generated from the game's own spell
table by `tools/make_sets_table.py`). Rules (ACE `WorldObject_Set.GetSpellSet`, `Player_Spells.EquipDequipItemFromSet`):
bonus depends on pieces worn (loot sets cap at 5, Society Armor goes to 9), set spells ignore mana and the
piece's own activation requirements, and they stack with buffs and cantrips. The wiki's Item Sets page agrees
(loot sets cap at 5; you don't need to activate the armor to get the set bonus).

## 6. How the planner uses this (Optimizer.cs)
- **defense** points = weight x 100 x ln(damage taken unarmored / damage taken in this suit) - armor per part by hit
  odds + level 7 Armor Self + DR/CRR/CDR, all with the formulas above.
- **offense** points = weight x 100 x ln(damage dealt / damage dealt with no ratings) - D, CR, CD.
- The log scale means "10% less damage taken" is worth the same points whatever your starting armor.
- Not yet modelled: damage-type mix (slash vs fire etc. and banes), monster crit rates other than 10%, weapon crit
  imbues (Milestone 4), luminance/augmentation ratings (not in the scan yet).
