// Wieldability rules - mirrors ACE (Player_Inventory.CheckWieldRequirement, WorldObject_Use.CheckUseRequirements,
// CreatureSkill.Current, AttributeFormula.GetFormula, EnchantmentManager top-layer stacking).
// Assumption: every character is buffed with LEVEL 7 self spells (+40 to each attribute and skill),
// plus the cantrips/item spells it is wearing right now. Scholar's Crystal (+250 Arcane Lore) is an option.
using System;
using System.Collections.Generic;
using System.Linq;

namespace CharacterGear.Planner
{
    internal class Check { public bool Ok; public string Text; public Check(bool ok, string t) { Ok = ok; Text = t; } }

    internal enum Magic { None, On, WithCrystal, Off }

    internal class Verdict
    {
        public Item Item; public bool Movable = true; public bool WieldOk = true;
        public readonly List<Check> Wield = new List<Check>();
        public Magic Magic = Magic.None;
        public readonly List<Check> MagicChecks = new List<Check>();
        public long BaseAL = -1, BuffedAL = -1;
        public double[] RL;            // protection multiplier per damage type with level 7 bane + own bane cantrip (ACE clamp -2..2)
        public double[] BaseRL;        // the same without any spells
        public bool? ActiveNow;   // worn by this character: are its spells switched on right now?
        public bool Usable { get { return Movable && WieldOk; } }
    }

    internal class Rules
    {
        public const int LEVEL7 = 40;            // Level VII self spell: +40 attribute / skill (tables/spells.tsv, e.g. Heavy Weapon Mastery Self VII)
        public const int L7_IMPEN = 220;         // Brogard's Defiance (Impenetrability VII) on each armor piece
        public const double L7_BANE = 1.7;       // level VII banes (Olthoi's Bane, Inferno's Bane, ...): +1.7 protection vs that type
        public const double L7_PROT = 0.35;      // level VII protections (Caustic Blessing, Fiery Blessing, ...): damage x0.35
        public const int CRYSTAL_ARCANE = 250;   // Prodigal Arcane Enlightenment (Scholar's Crystal), own category - stacks
        static readonly int[] RetiredMelee = { 1, 4, 5, 9, 10, 11, 13 };   // Axe Dagger Mace Spear Staff Sword Unarmed
        static readonly int[] RetiredMissile = { 2, 3, 12 };               // Bow Crossbow Thrown
        static readonly string[] AttrName = { "", "Strength", "Endurance", "Quickness", "Coordination", "Focus", "Self" };
        static readonly string[] Heritage = { "", "Aluvian", "Gharu'ndim", "Sho", "Viamontian", "Shadowbound", "Gearknight", "Tumerok", "Lugian", "Empyrean", "Penumbraen", "Undead", "Olthoi", "Olthoi Acid" };

        readonly Character C;
        public readonly bool CrystalOwned;
        public Rules(Character c, bool crystalOwned) { C = c; CrystalOwned = crystalOwned; }

        // ---------- character numbers under the level-7 assumption ----------
        // Stacking (ACE): group by spell category, best power per category, add categories.
        // The assumed level-7 self spell lives in the normal "...Raising" category, so it competes only with
        // normal spells on items (e.g. a Strength Self VI on a bracelet), never with cantrips.
        double Boost(Func<SpellInfo, bool> match, bool addLevel7, double extra = 0)
        {
            var best = new Dictionary<int, SpellInfo>();
            foreach (int id in C.GearEnchantments)
            {
                var s = Tables.Spell(id);
                if (s == null || !s.Additive || !match(s)) continue;
                SpellInfo b;
                if (!best.TryGetValue(s.CategoryId, out b) || s.Power > b.Power) best[s.CategoryId] = s;
            }
            double total = 0, normal = 0;
            foreach (var s in best.Values)
            {
                if (IsNormal(s)) normal = Math.Max(normal, s.Value); else total += s.Value;
            }
            if (addLevel7) normal = Math.Max(normal, LEVEL7);
            return total + normal + extra;
        }
        // Normal player/buff-bot spell family (competes with the assumed level 7 buff). Cantrip ("Extra..."), set
        // ("Set..."), cloak ("Cloak...") and rare ("...Rare") families are separate categories and always stack.
        public static bool IsNormal(SpellInfo s)
        {
            return s.Category.EndsWith("Raising") && !s.Category.StartsWith("Extra") && !s.Category.StartsWith("Set") && !s.Category.StartsWith("Cloak");
        }

        public long AttrBase(int a) { long v; return C.AttrBase.TryGetValue(a, out v) ? v : 0; }
        public long Attr(int a) { return AttrBase(a) + (long)Boost(s => s.IsAttribute && (s.StatKey == a || (s.StatKey == 0 && (s.StatType & 0x2000) != 0)), true); }

        static long Formula(SkillFormula f, Func<int, long> attr)
        {
            if (f == null || f.X == 0) return 0;
            long t = attr(f.Attr1) + (f.Attr2 > 0 ? attr(f.Attr2) : 0);
            return f.Divisor > 1 ? (long)Math.Round((double)t / f.Divisor, MidpointRounding.AwayFromZero) : t;
        }

        public int MoA(int skill)
        {
            if (Array.IndexOf(RetiredMissile, skill) >= 0) return 47;
            if (Array.IndexOf(RetiredMelee, skill) >= 0)
            {
                int best = 45; long bv = Skill(45);
                foreach (int s in new[] { 44, 46 }) { long v = Skill(s); if (v > bv) { bv = v; best = s; } }
                return best;
            }
            return skill;
        }

        public bool Usable(int skill)
        {
            SkillRow r; if (!C.Skills.TryGetValue(skill, out r)) return false;
            return r.Training.IndexOf("Unusable", StringComparison.OrdinalIgnoreCase) < 0;
        }
        public int TrainingRank(int skill)   // ACE SkillAdvancementClass: 1 untrained, 2 trained, 3 specialized
        {
            SkillRow r; if (!C.Skills.TryGetValue(skill, out r)) return 0;
            string t = r.Training.ToLowerInvariant();
            return t.Contains("special") ? 3 : t.Contains("train") && !t.Contains("untrain") ? 2 : t.Contains("unusable") ? 0 : 1;
        }

        // Skill value under the assumption (ACE CreatureSkill.Current): formula(attributes) + ranks + additive spells.
        public long Skill(int skill, bool withCrystal = false)
        {
            SkillRow r; if (!C.Skills.TryGetValue(skill, out r) || !Usable(skill)) return 0;
            SkillFormula f; Tables.Skills.TryGetValue(skill, out f);
            long ranksPart = r.Base - Formula(f, AttrBase);
            double extra = (withCrystal && skill == 14) ? CRYSTAL_ARCANE : 0;
            return Formula(f, Attr) + ranksPart + (long)Boost(s => s.IsSkill && s.StatKey == skill, true, extra);
        }
        // Skill under level 7 buffs plus the given extra boosts (used for a candidate suit - ignores what is worn now).
        public double SkillWith(int skill, Func<int, double> attrExtra, double skillExtra)
        {
            SkillRow r; if (!C.Skills.TryGetValue(skill, out r) || !Usable(skill)) return 0;
            SkillFormula f; Tables.Skills.TryGetValue(skill, out f);
            long ranksPart = r.Base - Formula(f, AttrBase);
            double formula = 0;
            if (f != null && f.X != 0)
            {
                double t = AttrBase(f.Attr1) + LEVEL7 + attrExtra(f.Attr1);
                if (f.Attr2 > 0) t += AttrBase(f.Attr2) + LEVEL7 + attrExtra(f.Attr2);
                formula = f.Divisor > 1 ? Math.Round(t / f.Divisor, MidpointRounding.AwayFromZero) : t;
            }
            return formula + ranksPart + LEVEL7 + skillExtra;
        }

        public long SkillBase(int skill) { SkillRow r; return C.Skills.TryGetValue(skill, out r) ? r.Base : 0; }
        public string SkillName(int skill) { SkillRow r; if (C.Skills.TryGetValue(skill, out r)) return Pretty(r.Name); SkillFormula f; return Tables.Skills.TryGetValue(skill, out f) ? f.Name : "skill " + skill; }
        static string Pretty(string n) { var sb = new System.Text.StringBuilder(); foreach (char ch in n) { if (char.IsUpper(ch) && sb.Length > 0) sb.Append(' '); sb.Append(ch); } return sb.ToString(); }

        // ---------- one item for this character ----------
        public Verdict Judge(Item it)
        {
            var v = new Verdict { Item = it };

            if (it.Int(114) >= 1 && it.Holder != C.Name && !it.InChest) { v.Movable = false; v.Wield.Add(new Check(false, "Attuned to " + it.Holder + " (can't be handed over)")); }

            WieldReq(v, it.Int(158), (int)it.Int(159), it.Int(160));
            WieldReq(v, it.Int(270), (int)it.Int(271), it.Int(272));
            WieldReq(v, it.Int(273), (int)it.Int(274), it.Int(275));
            WieldReq(v, it.Int(276), (int)it.Int(277), it.Int(278));
            if (it.Has(324))
            {
                long h = it.Int(324);
                bool ok = HeritageName(h).Equals(C.Race ?? "", StringComparison.OrdinalIgnoreCase);
                v.Wield.Add(new Check(ok, HeritageName(h) + " only"));
            }
            v.WieldOk = v.Wield.All(c => c.Ok);

            MagicChecks(v, it);
            ArmorLevel(v, it);
            ActiveNow(v, it);
            return v;
        }

        void WieldReq(Verdict v, long type, int key, long diff)
        {
            if (type <= 0) return;
            switch (type)
            {
                case 1: { int s = MoA(key); long have = Skill(s); v.Wield.Add(new Check(have >= diff, SkillName(s) + " " + diff + " (you: " + have + ")" + (s != key ? " [old " + SkillName(key) + " req]" : ""))); break; }
                case 2: { int s = MoA(key); long have = SkillBase(s); v.Wield.Add(new Check(have >= diff, "Base " + SkillName(s) + " " + diff + " (you: " + have + ")")); break; }
                case 3: { long have = Attr(key); v.Wield.Add(new Check(have >= diff, A(key) + " " + diff + " (you: " + have + ")")); break; }
                case 4: { long have = AttrBase(key); v.Wield.Add(new Check(have >= diff, "Base " + A(key) + " " + diff + " (you: " + have + ")")); break; }
                case 5: case 6:
                    {
                        int vital = key == 1 ? 2 : key == 3 ? 4 : key == 5 ? 6 : key;   // max health/stamina/mana
                        long have; (type == 5 ? C.VitalBuffed : C.VitalBase).TryGetValue(vital, out have);
                        string n = vital == 2 ? "Health" : vital == 4 ? "Stamina" : "Mana";
                        v.Wield.Add(new Check(have >= diff, (type == 6 ? "Base " : "") + "max " + n + " " + diff + " (you: " + have + ")")); break;
                    }
                case 7: v.Wield.Add(new Check(C.Level >= diff, "Level " + diff + " (you: " + C.Level + ")")); break;
                case 8: { int s = MoA(key); int r = TrainingRank(s); v.Wield.Add(new Check(r >= diff, SkillName(s) + " " + (diff >= 3 ? "specialized" : "trained"))); break; }
                case 9:
                    {
                        long have; C.Props.TryGetValue(key, out have);
                        string n = key == 287 ? "Celestial Hand rank" : key == 288 ? "Eldrytch Web rank" : key == 289 ? "Radiant Blood rank" : "property " + key;
                        v.Wield.Add(new Check(have >= diff, n + " " + diff + " (you: " + have + ")")); break;
                    }
                case 12: v.Wield.Add(new Check(HeritageName(diff).Equals(C.Race ?? "", StringComparison.OrdinalIgnoreCase), HeritageName(diff) + " only")); break;
                default: v.Wield.Add(new Check(true, "requirement type " + type + " not checked")); break;
            }
        }

        // Item magic switches on only if every activation requirement passes WHEN WIELDED (then stays on).
        void MagicChecks(Verdict v, Item it)
        {
            // Gear: the checks only decide whether its magic switches on. Essences have no spells, but the server checks
            // the same use requirements (Summoning skill/level) every time you summon (ACE PetDevice.CheckUseRequirements).
            if (it.OwnSpells.Count == 0 && !Essences.Is(it)) return;
            bool okNoCrystal = true, okWithCrystal = true;
            if (it.Has(109) && it.Int(109) > 0)
            {
                long need = it.Int(109), have = Skill(14), haveC = Skill(14, true);
                bool ok = have >= need;
                okNoCrystal &= ok; okWithCrystal &= haveC >= need;
                v.MagicChecks.Add(new Check(ok, "Arcane Lore " + need + " (you: " + have + (ok ? "" : CrystalOwned ? ", with crystal " + haveC : "") + ")"));
            }
            long skillLimit = it.Did(37, it.Int(176));
            if (skillLimit > 0 && it.Has(115))
            {
                int s = MoA((int)skillLimit); long need = it.Int(115), have = Skill(s);
                bool ok = have >= need; okNoCrystal &= ok; okWithCrystal &= ok;
                v.MagicChecks.Add(new Check(ok, SkillName(s) + " " + need + " (you: " + have + ")"));
            }
            if (it.Did(41) > 0)
            {
                int s = MoA((int)it.Did(41)); bool ok = TrainingRank(s) >= 3; okNoCrystal &= ok; okWithCrystal &= ok;
                v.MagicChecks.Add(new Check(ok, SkillName(s) + " specialized"));
            }
            if (it.Has(366))
            {
                int s = MoA((int)it.Int(366)); bool ok = TrainingRank(s) >= 2 && Skill(s) >= it.Int(367);
                okNoCrystal &= ok; okWithCrystal &= ok;
                v.MagicChecks.Add(new Check(ok, SkillName(s) + " trained" + (it.Int(367) > 0 ? " " + it.Int(367) : "")));
            }
            if (it.Has(369)) { bool ok = C.Level >= it.Int(369); okNoCrystal &= ok; okWithCrystal &= ok; v.MagicChecks.Add(new Check(ok, "Level " + it.Int(369))); }
            if (it.Has(257) && it.Has(258)) { int a = (int)it.Int(257); bool ok = Attr(a) >= it.Int(258); okNoCrystal &= ok; okWithCrystal &= ok; v.MagicChecks.Add(new Check(ok, A(a) + " " + it.Int(258))); }
            if (it.Has(110)) v.MagicChecks.Add(new Check(true, "Allegiance rank " + it.Int(110) + " (not checked)"));
            v.Magic = okNoCrystal ? Magic.On : (okWithCrystal && CrystalOwned) ? Magic.WithCrystal : Magic.Off;
        }

        // Armor level: strip spells cast on it now -> base; then add level-7 Impenetrability (or the piece's own
        // better normal spell, if its magic can switch on) + its own Impenetrability cantrip.
        void ArmorLevel(Verdict v, Item it)
        {
            if (!it.Has(28)) return;   // armor AND clothing: buff bots cast Impenetrability on both (clothing base 0 -> 220)
            int n; long castOn = (long)BestPerCategory(it.CastOn, s => s.IsItemSchool && s.StatKey == 28, out n);
            v.BaseAL = it.Int(28) - castOn;
            bool magic = v.Magic == Magic.On || v.Magic == Magic.WithCrystal || v.Magic == Magic.None;
            double normal = L7_IMPEN, cantrip = 0;
            if (magic)
            {
                foreach (int id in it.OwnSpells)
                {
                    var s = Tables.Spell(id);
                    if (s == null || !s.IsItemSchool || s.StatKey != 28) continue;
                    if (IsNormal(s)) normal = Math.Max(normal, s.Value); else cantrip = Math.Max(cantrip, s.Value);
                }
            }
            v.BuffedAL = v.BaseAL + (long)(normal + cantrip);

            // Protection per damage type (ACE Monster_Melee.GetArmorMod): effective AL = (AL + impen) x clamp(RL + banes, -2, 2).
            // The ID's armor profile includes banes cast on it now: take those off, then add the level 7 bane
            // (or the piece's own stronger normal bane) and its own bane cantrip.
            v.RL = new double[Elements.Count]; v.BaseRL = new double[Elements.Count];
            for (int e = 0; e < Elements.Count; e++)
            {
                int key = Elements.BaneKey[e]; int n2;
                double prof = it.Profile != null ? it.Profile[e] : 1.0;
                double castBane = BestPerCategory(it.CastOn, s => s.IsItemSchool && s.StatKey == key, out n2);
                double baseRl = prof - castBane;
                double bane = L7_BANE, extra = 0;
                if (magic)
                    foreach (int id in it.OwnSpells)
                    {
                        var s = Tables.Spell(id);
                        if (s == null || !s.IsItemSchool || s.StatKey != key) continue;
                        if (IsNormal(s)) bane = Math.Max(bane, s.Value); else extra = Math.Max(extra, s.Value);
                    }
                v.BaseRL[e] = baseRl;
                v.RL[e] = Math.Max(-2.0, Math.Min(2.0, baseRl + bane + extra));
            }
        }

        // Worn pieces only: item-school spells sit on the item (cast-on list), all others on the character.
        // Cloak and trinket spells are triggered effects, never "on", so they are not judged.
        void ActiveNow(Verdict v, Item it)
        {
            if (it.Holder != C.Name || it.Where != "equipped" || it.OwnSpells.Count == 0) return;
            if ((it.ValidLocations & (0x8000000 | 0x4000000)) != 0) return;
            bool all = true;
            foreach (int id in it.OwnSpells)
            {
                var s = Tables.Spell(id); if (s == null) continue;
                bool on = s.IsItemSchool ? it.CastOn.Contains(id) : C.GearEnchantments.Contains(id);
                if (!on) { all = false; break; }
            }
            v.ActiveNow = all;
        }

        static double BestPerCategory(IEnumerable<int> ids, Func<SpellInfo, bool> match, out int count)
        {
            var best = new Dictionary<int, SpellInfo>();
            foreach (int id in ids)
            {
                var s = Tables.Spell(id); if (s == null || !match(s)) continue;
                SpellInfo b; if (!best.TryGetValue(s.CategoryId, out b) || s.Power > b.Power) best[s.CategoryId] = s;
            }
            count = best.Count;
            return best.Values.Sum(s => s.Value);
        }

        static string A(int a) { return a >= 1 && a <= 6 ? AttrName[a] : "attribute " + a; }
        static string HeritageName(long h) { return h >= 1 && h < Heritage.Length ? Heritage[h] : "heritage " + h; }
    }
}
