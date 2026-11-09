// Milestone 4: weapons and wands. Mirrors ACE's damage pipeline (DamageEvent, WorldObject_Weapon, SkillCheck):
//  - melee/missile: max damage (+ level 7 Blood Drinker 22 + Blood Thirst cantrip), variance, crit chance (weapon
//    or Critical Strike imbue: melee (base-100)/600, missile (base-60)/600, cap base 400/360) + crit rating, crit
//    multiplier (weapon or Crippling Blow imbue: melee (base-40)/60, missile base/60, max 6), damage/crit damage
//    ratings, elemental rending (base/160 melee, base/144 missile, 1..2.5), armor rending (vs an assumed monster AL);
//  - casters: wand elemental damage mod (+ level 7 Spirit Drinker 7% + Spirit Thirst), magic crit (base 5%);
//  - hit chance = ACE SkillCheck 1 - 1/(1+e^(0.03*(attack - defense))), attack = skill x weapon attack mod;
//  - evade chance the same with Melee Defense x weapon defense mod (feeds the defense score).
// Monster defense/attack are unknown, so they are set relative to the character (weights hit_margin, evade_margin).
using System;
using System.Collections.Generic;
using System.Linq;

namespace CharacterGear.Planner
{
    internal class WeaponStats
    {
        public double Expected;       // expected damage per attack before the hit roll
        public double HitChance, EvadeChance, CritChance, CritMult, AttackMod, DefenseMod, MaxDamage, Rending, ArmorRending;
        public string Notes = "";
    }

    internal partial class Optimizer
    {
        const double L7_BLOOD = 22, L7_HEART = 0.17, L7_DEFENDER = 0.17, L7_SPIRIT = 0.07;
        public Verdict Weapon;                 // weapon used when scoring (chosen by Best, or the current one)
        List<Verdict> weaponCandidates;

        bool IsCaster { get { return Main == "war" || Main == "void" || Main == "life"; } }
        bool IsMissile { get { return Main == "missile"; } }
        // Tinkers and crafters fight with nothing in particular: no weapon pick, offense/defense per their weights.
        bool IsCombat { get { return Main != "tinker" && Main != "crafter"; } }
        int ProfileSkill { get { return Profiles.MainSkill[Main]; } }

        List<Verdict> WeaponCandidates()
        {
            if (weaponCandidates != null) return weaponCandidates;
            weaponCandidates = allVerdicts.Where(v => v.Usable && FitsProfile(v.Item)).ToList();
            return weaponCandidates;
        }

        bool FitsProfile(Item it)
        {
            long l = it.ValidLocations;
            if (!IsCombat) return false;
            if (IsCaster) return (l & 0x1000000) != 0 && (it.HasF(152) || it.HasF(144));
            if (IsMissile) return (l & 0x400000) != 0;
            if (Main == "twohanded") return (l & 0x2000000) != 0;
            if ((l & 0x100000) == 0) return false;
            int ws = (int)it.Int(48);
            return ws == 0 || R.MoA(ws) == ProfileSkill || ws == ProfileSkill;
        }

        public Verdict CurrentWeapon()
        {
            return WeaponCandidates().FirstOrDefault(v => v.Item.Holder == C.Name && v.Item.Where == "equipped");
        }

        static double Chance(double skill, double difficulty) { return 1.0 - 1.0 / (1.0 + Math.Exp(0.03 * (skill - difficulty))); }
        static double ArmorThrough(double al) { return al > 0 ? ARMOR_CONST / (al + ARMOR_CONST) : 1.0; }

        // Best spell per category on the weapon for one aura key (360 damage, 168 attack, 169 defense, 170 elemental):
        // the weapon's own normal-level aura only counts above the assumed level 7 buff; cantrips ("thirst") add.
        double WeaponBoost(Verdict v, int key, double level7)
        {
            double normal = level7, extra = 0;
            Magic wm = SuitMagic(v, suitArcane);
            if (v != null && (wm == Magic.On || wm == Magic.WithCrystal))
            {
                var best = new Dictionary<int, SpellInfo>();
                foreach (int id in v.Item.OwnSpells)
                {
                    var s = Tables.Spell(id); if (s == null || s.StatKey != key) continue;
                    SpellInfo b; if (!best.TryGetValue(s.CategoryId, out b) || s.Power > b.Power) best[s.CategoryId] = s;
                }
                foreach (var s in best.Values) { if (Rules.IsNormal(s)) normal = Math.Max(normal, Math.Abs(s.Value)); else extra += Math.Abs(s.Value); }
            }
            return normal + extra;
        }

        // Current weapon numbers include the wielder's active aura buffs - strip them so every weapon compares at base.
        double AuraNow(int key)
        {
            var best = new Dictionary<int, SpellInfo>();
            foreach (int id in C.GearEnchantments.Concat(C.CastBuffs))
            {
                var s = Tables.Spell(id); if (s == null || s.StatKey != key) continue;
                SpellInfo b; if (!best.TryGetValue(s.CategoryId, out b) || s.Power > b.Power) best[s.CategoryId] = s;
            }
            return best.Values.Sum(s => s.Value);
        }

        public WeaponStats Combat(Verdict wv, double mainSkill, double meleeD, double D, double CD, double CR)
        {
            var w = new WeaponStats();
            var it = wv != null ? wv.Item : null;
            bool wieldedNow = it != null && it.Holder == C.Name && it.Where == "equipped";
            long imb = it != null ? (it.Int(179) | it.Int(303)) : 0;
            double baseSkill = Math.Min(R.SkillBase(ProfileSkill), IsMissile || IsCaster ? 360 : 400);

            w.AttackMod = (it != null && it.HasF(62) ? it.Flt(62) : 1.0) + WeaponBoost(wv, 168, L7_HEART);
            w.DefenseMod = (it != null && it.HasF(29) ? it.Flt(29) : 1.0) + WeaponBoost(wv, 169, L7_DEFENDER);

            // crit chance and multiplier
            double critFreq = it != null && it.HasF(147) ? it.Flt(147) : (IsCaster ? 0.05 : 0.10);
            if ((imb & 0x1) != 0)
            {
                double cs = IsCaster || IsMissile ? Math.Max(0, baseSkill - 60) / 600.0 : Math.Max(0, baseSkill - 100) / 600.0;
                critFreq = Math.Max(critFreq, cs); w.Notes += "Critical Strike " + Math.Round(cs * 100) + "% crits; ";
            }
            w.CritChance = Math.Min(1, critFreq + CR / 100.0);
            double critMult = it != null && it.HasF(136) ? it.Flt(136) : 1.0;
            if ((imb & 0x2) != 0)
            {
                double cb = IsCaster || IsMissile ? baseSkill / 60.0 : Math.Max(0, baseSkill - 40) / 60.0;
                cb = Math.Min(6.0, Math.Max(1.0, cb));
                critMult = Math.Max(critMult, cb); w.Notes += "Crippling Blow crit x" + Math.Round(1 + cb, 1) + "; ";
            }
            w.CritMult = critMult;

            double avg, max;
            if (IsCaster)
            {
                double elem = (it != null && it.HasF(152) ? it.Flt(152) - 1 : 0) + WeaponBoost(wv, 170, L7_SPIRIT);
                max = 1 + elem; avg = AVG_OF_MAX * max;
                w.MaxDamage = max;
            }
            else
            {
                double baseMax;
                if (IsMissile) baseMax = W.Get(Profile, "ammo_damage") * (it != null && it.HasF(63) ? it.Flt(63) : 1.0) + (it != null ? it.Int(204) : 0);
                else baseMax = it != null ? it.Int(44) : 1;   // already stripped of the holder's auras at load
                max = Math.Max(1, baseMax + (it == null ? 0 : WeaponBoost(wv, 360, IsMissile ? 0 : L7_BLOOD)));   // no weapon = no weapon buff
                double variance = it != null && it.HasF(22) ? it.Flt(22) : 0.25;
                avg = max * (1 - variance / 2);
                w.MaxDamage = max;
            }

            // elemental rending (acts like a vulnerability up to 2.5x) when it matches the weapon's damage type
            w.Rending = 1;
            if (it != null && !IsCaster)
            {
                long dt = it.Int(45);
                long[,] map = { { 1, 0x8 }, { 2, 0x10 }, { 4, 0x20 }, { 32, 0x40 }, { 8, 0x80 }, { 64, 0x100 }, { 16, 0x200 }, { 0x400, 0x4000 } };
                for (int i = 0; i < map.GetLength(0); i++)
                    if ((dt & map[i, 0]) != 0 && (imb & map[i, 1]) != 0)
                    {
                        double rm = Math.Min(2.5, Math.Max(1.0, baseSkill / (IsMissile ? 144.0 : 160.0)));
                        w.Rending = Math.Max(w.Rending, rm);
                    }
                if (w.Rending > 1) w.Notes += "rending x" + Math.Round(w.Rending, 2) + " (vs that element); ";
            }
            // armor rending vs an assumed monster armor level
            w.ArmorRending = 1;
            if ((imb & 0x4) != 0 && !IsCaster)
            {
                double rmod = 1 - (IsMissile ? Math.Max(0, baseSkill - 144) / 360.0 : Math.Max(0, baseSkill - 160) / 400.0);
                rmod = Math.Max(0.6, rmod);
                double mal = W.Get(Profile, "monster_al");
                w.ArmorRending = ArmorThrough(mal * rmod) / ArmorThrough(mal);
                w.Notes += "armor rending x" + Math.Round(w.ArmorRending, 2) + " (monster AL " + mal + "); ";
            }

            w.Expected = ((1 - w.CritChance) * avg * (1 + D / 100) + w.CritChance * max * (1 + critMult) * (1 + (D + CD) / 100)) * w.Rending * w.ArmorRending;

            // hit and evade chances vs monsters set relative to this character (average weapon = mod 1.17)
            // Anchored to the character as he is NOW (level 7 + what he wears), so the margins describe "a monster that
            // you hit ~82% of the time and evade ~50% of the time today" and every change is measured from there.
            double bare = R.Skill(ProfileSkill), bareD = R.Skill(6);
            double monsterDef = bare * (1 + L7_HEART) - W.Get(Profile, "hit_margin");
            double monsterAtk = bareD * (1 + L7_DEFENDER) - W.Get(Profile, "evade_margin");
            w.HitChance = Chance(mainSkill * (IsCaster ? 1.0 : w.AttackMod), IsCaster ? bare - W.Get(Profile, "hit_margin") : monsterDef);
            w.EvadeChance = Chance(meleeD * w.DefenseMod, monsterAtk);
            return w;
        }

        public class ElementPick { public string Element; public Verdict Best, Second; public double BestDmg, SecondDmg; public WeaponStats BestStats, SecondStats; }
        public static readonly string[] ElementNames = { "Slash", "Pierce", "Bludgeon", "Acid", "Fire", "Cold", "Lightning", "Nether" };
        static readonly long[] ElementBits = { 1, 2, 4, 32, 16, 8, 64, 0x400 };

        // Best wieldable weapon of this character's type for each damage type, scored in the given suit
        // (expected damage per attack incl. hit chance, crits, ratings and rending - a matching rend counts up to x2.5).
        public List<ElementPick> BestByElement(List<Choice> suit)
        {
            var keep = Weapon;
            var dmg = new Dictionary<Verdict, Tuple<double, WeaponStats>>();
            var total = new Dictionary<Verdict, double>();
            foreach (var v in WeaponCandidates()) { Weapon = v; var sc = Score(suit); dmg[v] = Tuple.Create(sc.Weapon.Expected * sc.Weapon.HitChance, sc.Weapon); total[v] = sc.Total; }
            Weapon = keep;
            var list = new List<ElementPick>();
            for (int i = 0; i < ElementNames.Length; i++)
            {
                var pick = new ElementPick { Element = ElementNames[i] };
                // ranked by the same total score as the main pick (damage AND the weapon's defense bonus)
                foreach (var kv in dmg.Where(k => (k.Key.Item.Int(45) & ElementBits[i]) != 0).OrderByDescending(k => total[k.Key]))
                {
                    if (pick.Best == null) { pick.Best = kv.Key; pick.BestDmg = kv.Value.Item1; pick.BestStats = kv.Value.Item2; }
                    else if (pick.Second == null) { pick.Second = kv.Key; pick.SecondDmg = kv.Value.Item1; pick.SecondStats = kv.Value.Item2; }
                }
                list.Add(pick);
            }
            return list;
        }

        public string WeaponLabel(Verdict v) { return v == null ? "none" : v.Item.Display + " (" + v.Item.Holder + ")"; }
    }
}
