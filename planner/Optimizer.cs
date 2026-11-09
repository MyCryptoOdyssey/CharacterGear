// Milestone 3: pick the best armor / underclothes / jewelry / cloak / trinket suit for one character.
// Score = armor (average buffed AL over 9 body parts) + spells (best per category, ACE stacking; normal-level
// spells only above the assumed level 7 buff) + set bonuses (count every worn set piece, magic or not) + ratings.
// Search: local search from several seeds (empty, current suit, one per set), always taking the best single swap,
// until nothing improves. Weapons are Milestone 4.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

namespace CharacterGear.Planner
{
    internal class Weights
    {
        readonly Dictionary<string, Dictionary<string, double>> byProfile = new Dictionary<string, Dictionary<string, double>>(StringComparer.OrdinalIgnoreCase);
        public string Source;
        public static Weights Load(string dataDir)
        {
            var w = new Weights();
            string path = Path.Combine(dataDir, "CharacterGear_weights.tsv");
            if (!File.Exists(path))
            {
                using (var st = System.Reflection.Assembly.GetExecutingAssembly().GetManifestResourceStream("CharacterGear.weights.tsv"))
                using (var fs = File.Create(path)) st.CopyTo(fs);
            }
            w.Source = path;
            bool header = false;
            foreach (string line in File.ReadAllLines(path))
            {
                if (line.StartsWith("#") || line.Trim() == "") continue;
                if (!header) { header = true; continue; }
                string[] f = line.Split('\t'); double v;
                if (f.Length < 3 || !double.TryParse(f[2], NumberStyles.Float, CultureInfo.InvariantCulture, out v)) continue;
                if (!w.byProfile.ContainsKey(f[0])) w.byProfile[f[0]] = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
                w.byProfile[f[0]][f[1]] = v;
            }
            return w;
        }
        // Value if the weights file has the key (for the profile, or "all"), else the given default.
        public double GetOr(string profile, string key, double def)
        {
            Dictionary<string, double> d; double v;
            foreach (string part in profile.Split('+')) if (byProfile.TryGetValue(part, out d) && d.ContainsKey(key)) return Get(profile, key);
            if (byProfile.TryGetValue("all", out d) && d.TryGetValue(key, out v)) return v;
            return def;
        }
        public double Get(string profile, string key)
        {
            Dictionary<string, double> d; double v; bool found = false; double best = double.MinValue;
            foreach (string part in profile.Split('+'))
                if (byProfile.TryGetValue(part, out d) && d.TryGetValue(key, out v)) { found = true; best = Math.Max(best, v); }
            if (found) return best;
            if (byProfile.TryGetValue("all", out d) && d.TryGetValue(key, out v)) return v;
            return 0;
        }
    }

    internal class Choice { public Verdict V; public long Mask; }

    internal class SuitScore
    {
        public double Total, Defense, Offense, Spells, Sets, Ratings;
        public double DamageTakenPct, DamageDealtPct, BodyArmor;
        public WeaponStats Weapon; public double MainSkill, MeleeD, Arcane;
        public Dictionary<Verdict, Magic> MagicOf = new Dictionary<Verdict, Magic>();
        public List<SpellInfo> Active = new List<SpellInfo>();
        public long[] PartAL = new long[9];
        public double AvgAL;
        public double[] TakenBy = new double[Elements.Count];   // % damage taken from each type alone (vs unarmored, unbuffed)
        public double[] Ward = new double[Elements.Count];      // ward cantrip multiplier per type (1 = none)
        public Verdict OffHand; public string ShieldNote = "";
        public List<string> SpellLines = new List<string>();
        public List<string> SetLines = new List<string>();
        public List<string> RatingLines = new List<string>();
    }

    internal partial class Optimizer
    {
        // Body parts for armor level, with the slot bits that cover each (armor layer + underclothes layer).
        public static readonly string[] PartNames = { "Head", "Chest", "Abdomen", "Upper arms", "Lower arms", "Hands", "Upper legs", "Lower legs", "Feet" };
        static readonly long[] PartBits = { 0x1, 0x200 | 0x2, 0x400 | 0x4, 0x800 | 0x8, 0x1000 | 0x10, 0x20, 0x2000 | 0x40, 0x4000 | 0x80, 0x100 };
        // What a piece PROTECTS = its coverage (ACE ClothingPriority / Decal Coverage), outer + under layers.
        static readonly long[] CoverBits = { 0x4000, 0x400 | 0x8, 0x800 | 0x10, 0x1000 | 0x20, 0x2000 | 0x40, 0x8000, 0x100 | 0x2, 0x200 | 0x4, 0x10000 };
        static bool Covers(Item it, int part)
        {
            return it.Coverage != 0 ? (it.Coverage & CoverBits[part]) != 0 : (it.ValidLocations & PartBits[part]) != 0;
        }
        public const long SuitSlots = 0x7E00 | 0x1 | 0x20 | 0x100 | 0xDE | 0xF8000 | 0x4000000 | 0x8000000 | SigilSlots;
        public const long SigilSlots = 0x10000000 | 0x20000000 | 0x40000000;   // Aetheria: Blue, Yellow, Red (ACE EquipMask.SigilOne/Two/Three)
        static readonly string[] Tier = { "Minor ", "Major ", "Epic ", "Legendary " };

        readonly Character C; readonly Rules R; readonly Weights W; public readonly string Profile, Main;
        readonly List<Verdict> candidates;
        readonly List<Verdict> allVerdicts;

        public Optimizer(Character c, Rules r, Weights w, List<Verdict> verdicts)
        {
            C = c; R = r; W = w; allVerdicts = verdicts;
            Profile = Profiles.For(c, r);
            Main = Profile.Split('+')[0];
            candidates = verdicts.Where(v => v.Usable && (v.Item.ValidLocations & SuitSlots) != 0 && (v.Item.ValidLocations & 0x03F00000) == 0 && SigilSlotOpen(v.Item)).ToList();
        }

        // Aetheria slots open by quest (ACE PropertyInt.AetheriaBitfield 322: 1 Blue, 2 Yellow, 4 Red; the server
        // refuses a sigil in a locked slot). Decal doesn't pass 322 on, so when it's missing the slot is taken as open
        // from the level its quest needs (75 / 150 / 225) and the pick says "needs the slot unlocked".
        static readonly long[] SigilBit = { 0x10000000, 0x20000000, 0x40000000 };
        static readonly int[] SigilLevel = { 75, 150, 225 };
        public static readonly string[] SigilColor = { "Blue", "Yellow", "Red" };
        public bool SigilFlagKnown { get { return C.Props.ContainsKey(322); } }
        public bool SigilSlotOpen(Item it)
        {
            for (int i = 0; i < 3; i++)
                if ((it.ValidLocations & SigilBit[i]) != 0)
                {
                    long f; if (C.Props.TryGetValue(322, out f)) return (f & (1L << i)) != 0;
                    return C.Level >= SigilLevel[i];
                }
            return true;
        }
        public static int SigilIndex(Item it) { for (int i = 0; i < 3; i++) if ((it.ValidLocations & SigilBit[i]) != 0) return i; return -1; }

        public Rules Rules { get { return R; } }
        public string ProfileSkillName { get { return Profiles.Pretty(Profile); } }
        public int MainSkillId { get { return ProfileSkill; } }
        public bool CombatProfile { get { return IsCombat; } }
        public string MainSkillName { get { return R.SkillName(ProfileSkill); } }
        public bool ProfileOverridden { get { return Profiles.IsOverridden(C.Name); } }

        // Ways a piece can be worn: wrists and rings go left OR right; everything else takes all its slots.
        // Everything else takes all its slots; armor/clothing clashes are decided by coverage (see Clash).
        static List<long> Masks(Item it)
        {
            long l = it.ValidLocations & SuitSlots;
            var r = new List<long>();
            if ((l & 0x30000) == 0x30000 && (l & ~0x30000L) == 0) { r.Add(0x10000); r.Add(0x20000); return r; }
            if ((l & 0xC0000) == 0xC0000 && (l & ~0xC0000L) == 0) { r.Add(0x40000); r.Add(0x80000); return r; }
            r.Add(l); return r;
        }

        // Monster melee hit odds per body part (ACE: attack height High/Medium/Low 1/3 each, then a random part
        // inside that height - High: head, chest, upper arm; Medium: chest, abdomen, upper arm, lower arm, hand,
        // upper leg; Low: lower leg, foot). Missile attacks pick a height the same way.
        public static readonly double[] PartHit = { 1 / 9.0, 1 / 6.0, 1 / 18.0, 1 / 6.0, 1 / 18.0, 1 / 18.0, 1 / 18.0, 1 / 6.0, 1 / 6.0 };
        public const double ARMOR_CONST = 200.0 / 3.0;   // ACE SkillFormula.ArmorMod: damage x 66.67 / (AL + 66.67)
        public const int L7_ARMOR_SELF = 225;            // Executor's Blessing (Armor Self VII), whole body
        const double BASE_CRIT = 0.10;                   // ACE defaultPhysicalCritFrequency (attacker and monsters)
        const double CRIT_X = 2.0;                       // crit = max damage x 2 (default CriticalMultiplier 1.0)
        const double AVG_OF_MAX = 0.75;                  // a normal hit averages ~75% of max damage
        static readonly int[] RatingKeys = { 307, 308, 313, 314, 315, 316 };   // dmg, dmg resist, crit, crit dmg, crit resist, crit dmg resist
        static readonly int[] GearRatingKeys = { 370, 371, 372, 374, 373, 375 };

        // Damage-type mix the defense is scored against (weights dmg_slash ... dmg_lightning; default = physical only).
        double[] mix;
        double[] Mix
        {
            get
            {
                if (mix != null) return mix;
                // Default: every damage type counts the same (end goal: Legendary ward coverage for all types).
                var m = new double[Elements.Count]; double t = 0;
                for (int e = 0; e < Elements.Count; e++) { m[e] = Math.Max(0, W.GetOr(Profile, "dmg_" + Elements.Key[e], 1)); t += m[e]; }
                if (t <= 0) { for (int e = 0; e < Elements.Count; e++) m[e] = 1; t = Elements.Count; }
                for (int e = 0; e < Elements.Count; e++) m[e] /= t;
                return mix = m;
            }
        }
        public void SetMix(int element) { if (element < 0) { mix = null; return; } mix = new double[Elements.Count]; mix[element] = 1; }

        // Start from a suit and improve it under the current mix (fast: no new seeds, weapon kept).
        public List<Choice> Adjust(List<Choice> start, out SuitScore score) { var s = Climb(start); score = Score(s); return s; }

        // The search scores the same suit many times (every seed and round): remember results. Key = pieces + slots,
        // weapon, off hand and the damage-type mix (the only inputs besides the fixed character data).
        readonly Dictionary<string, SuitScore> scoreCache = new Dictionary<string, SuitScore>();
        public SuitScore Score(List<Choice> suit)
        {
            var key = new System.Text.StringBuilder();
            foreach (var ch in suit.OrderBy(x => x.V.Item.Id)) key.Append(ch.V.Item.Id).Append(':').Append(ch.Mask).Append(',');
            key.Append('|').Append(Weapon != null ? Weapon.Item.Id : "-").Append('|').Append(OffHand != null ? OffHand.Item.Id : "-").Append('|');
            foreach (double m in Mix) key.Append(m.ToString("R")).Append(',');
            string k = key.ToString();
            SuitScore hit;
            if (scoreCache.TryGetValue(k, out hit)) { suitArcane = hit.Arcane; return hit; }
            var res = ScoreUncached(suit);
            if (scoreCache.Count > 200000) scoreCache.Clear();
            scoreCache[k] = res;
            return res;
        }

        SuitScore ScoreUncached(List<Choice> suit)
        {
            var s = new SuitScore();
            // Wielded weapon and off-hand (shield or second weapon) count like suit pieces for spells, sets and gear
            // ratings (ACE: equipped items' spells apply to the wielder). They have no body coverage, so no armor parts.
            var gear = new List<Choice>(suit);
            if (Weapon != null) gear.Add(new Choice { V = Weapon, Mask = 0 });
            var off = OffHandAllowed(Weapon, OffHand) ? OffHand : null;
            if (off != null) gear.Add(new Choice { V = off, Mask = 0x200000 });
            s.OffHand = off;

            // Spells that apply: item spells (only if the piece's magic works) + set bonuses; best per category.
            var spells = new List<int>();
            var setCounts = new Dictionary<long, int>();
            // Item magic depends on the Arcane Lore THIS suit gives (its Arcane/Focus cantrips and set spells), so
            // settle it in a few rounds: start from level 7 alone, switch on what passes, recompute, repeat.
            double arcane = R.SkillWith(14, a0 => 0, 0);
            for (int round = 0; round < 4; round++)
            {
                var ids = new List<int>();
                foreach (var ch in gear) { var m = SuitMagic(ch.V, arcane); s.MagicOf[ch.V] = m; if (m == Magic.On || m == Magic.WithCrystal) ids.AddRange(ch.V.Item.OwnSpells); }
                ids.AddRange(SetSpellsOf(gear));
                double next = ArcaneFrom(ids);
                if (Math.Abs(next - arcane) < 0.5) break;
                arcane = next;
            }
            s.Arcane = arcane; suitArcane = arcane;
            foreach (var ch in gear)
            {
                var v = ch.V;
                Magic mg; s.MagicOf.TryGetValue(v, out mg);
                if (mg == Magic.On || mg == Magic.WithCrystal) spells.AddRange(v.Item.OwnSpells);
                long set = v.Item.Int(265);
                if (set > 0) { int n; setCounts.TryGetValue(set, out n); setCounts[set] = n + v.Item.SetWeight; }
            }
            var setSpellIds = new HashSet<int>();
            foreach (var kv in setCounts)
            {
                var ss = Tables.SetSpells(kv.Key, kv.Value);
                s.SetLines.Add(Tables.SetName(kv.Key) + " x" + kv.Value + (ss.Count > 0 ? ": " + string.Join(", ", ss.Select(x => Tables.SpellName(x)).ToArray()) : ": no bonus yet"));
                foreach (int id in ss) { setSpellIds.Add(id); spells.Add(id); }
            }
            var best = new Dictionary<int, SpellInfo>();
            foreach (int id in spells)
            {
                var sp = Tables.Spell(id); if (sp == null || sp.IsItemSchool) continue;   // armor/bane item spells are in the AL math
                SpellInfo b; if (!best.TryGetValue(sp.CategoryId, out b) || sp.Power > b.Power) best[sp.CategoryId] = sp;
            }

            s.Active = best.Values.ToList();
            // Whole-body armor (Armor Self buff vs level 7, + Armor cantrips) and ratings from spells.
            double bodyNormal = L7_ARMOR_SELF, bodyExtra = 0;
            var rating = new Dictionary<int, double>();
            foreach (int k in RatingKeys) { long b0; C.Props.TryGetValue(k, out b0); rating[k] = b0; }
            var prot = new double[Elements.Count]; var ward = new double[Elements.Count];
            for (int e = 0; e < Elements.Count; e++) { prot[e] = Rules.L7_PROT; ward[e] = 1; }
            foreach (var sp in best.Values)
            {
                int el = Elements.OfResistKey(sp.StatKey);
                if (el >= 0 && !sp.Additive && sp.Value < 1)   // protections (compete with level 7) and wards (stack): in the damage math
                {
                    if (sp.Category.Contains("Ward")) ward[el] = Math.Min(ward[el], sp.Value); else prot[el] = Math.Min(prot[el], sp.Value);
                    s.SpellLines.Add(sp.Name + " (" + Elements.Name[el].ToLower() + " damage x" + sp.Value + ")");
                    continue;
                }
                if (sp.Additive && (sp.StatType & 0x80) != 0)
                {
                    if (Rules.IsNormal(sp)) bodyNormal = Math.Max(bodyNormal, sp.Value); else bodyExtra += sp.Value;
                    s.SpellLines.Add(sp.Name + " (armor +" + sp.Value + ")");
                    continue;
                }
                if ((sp.StatType & 0x4) != 0 && Array.IndexOf(RatingKeys, sp.StatKey) >= 0)
                {
                    rating[sp.StatKey] += sp.Value;
                    s.SpellLines.Add(sp.Name + " (rating)");
                    continue;
                }
                double pts = SpellPoints(sp);
                if (pts <= 0)
                {
                    if (IsCombat && sp.IsSkill && (sp.StatKey == ProfileSkill || sp.StatKey == 6)) s.SpellLines.Add(sp.Name + " (in offense/defense)");
                    continue;
                }
                if (setSpellIds.Contains(sp.Id)) s.Sets += pts; else s.Spells += pts;
                s.SpellLines.Add(sp.Name + (setSpellIds.Contains(sp.Id) ? " (set)" : "") + " +" + Math.Round(pts, 1));
            }
            s.SpellLines.Sort();
            for (int i = 0; i < 6; i++) { int gk = GearRatingKeys[i]; rating[RatingKeys[i]] += gear.Sum(ch => ch.V.Item.Int(gk)); }
            // Aetheria surges (ACE Aetheria.CalcProcRate): chance per attack = sigil level x 1% (x2 in magic, x1.5 missile).
            // Destruction (+20 damage rating, 8 s) and Protection (+20 damage resist, 12 s) count by expected uptime with
            // one attack every attack_seconds (default 2); the other surges (heal over time, damage over time, heal
            // debuff) get surge_other points per sigil level.
            foreach (var ch in gear)
            {
                long proc = ch.V.Item.Did(55); if (proc == 0 || SigilIndex(ch.V.Item) < 0) continue;
                var sp = Tables.Spell(proc); if (sp == null) continue;
                double rate = ch.V.Item.ItemLevel * 0.01 * (IsCaster ? 2.0 : IsMissile ? 1.5 : 1.0);
                double secs = Math.Max(0.5, W.GetOr(Profile, "attack_seconds", 2.0));
                double up = 1 - Math.Pow(1 - Math.Min(1, rate), Math.Max(1, sp.Duration / secs));
                if (sp.StatKey == 307 || sp.StatKey == 308)
                {
                    rating[sp.StatKey] += sp.Value * up;
                    s.SpellLines.Add(sp.Name + " (" + Math.Round(rate * 100) + "% per attack, ~" + Math.Round(up * 100) + "% uptime)");
                }
                else
                {
                    double pts = ch.V.Item.ItemLevel * W.GetOr(Profile, "surge_other", 2.0);
                    s.Ratings += pts;
                    s.SpellLines.Add(sp.Name + " (" + Math.Round(rate * 100) + "% per attack) +" + Math.Round(pts, 1));
                }
            }
            double D = rating[307], DR = rating[308], CR = rating[313], CD = rating[314], CRR = rating[315], CDR = rating[316];

            // Skill and attribute boosts from this suit (cantrips, set and cloak spells; normal spells only above level 7).
            var skillExtra = new Dictionary<int, double>(); var attrExtra = new Dictionary<int, double>();
            foreach (var sp in best.Values)
            {
                if (!sp.Additive) continue;
                double val = Math.Max(0, sp.Value - (Rules.IsNormal(sp) ? Rules.LEVEL7 : 0));
                if (sp.IsSkill) { double x; skillExtra.TryGetValue(sp.StatKey, out x); skillExtra[sp.StatKey] = x + val; }
                else if (sp.IsAttribute)
                {
                    if (sp.StatKey == 0 && (sp.StatType & 0x2000) != 0) { for (int a2 = 1; a2 <= 6; a2++) { double x; attrExtra.TryGetValue(a2, out x); attrExtra[a2] = x + val; } }
                    else { double x; attrExtra.TryGetValue(sp.StatKey, out x); attrExtra[sp.StatKey] = x + val; }
                }
            }
            double se; skillExtra.TryGetValue(ProfileSkill, out se); double sd; skillExtra.TryGetValue(6, out sd);
            Func<int, double> ax = k => { double x; return attrExtra.TryGetValue(k, out x) ? x : 0; };
            s.MainSkill = R.SkillWith(ProfileSkill, ax, se);
            s.MeleeD = R.SkillWith(6, ax, sd);
            var ws = Combat(Weapon, s.MainSkill, s.MeleeD, D, CD, CR);
            if (off != null && !IsShield(off.Item))
            {
                // Dual wield (ACE): swings alternate main / off hand; off-hand swings use the LOWER of Dual Wield and the
                // weapon skill; the swing animation and power bar are 20% faster. Defense mod follows the swinging weapon.
                double sdw; skillExtra.TryGetValue(49, out sdw);
                double dw = Math.Min(R.SkillWith(49, ax, sdw), s.MainSkill);
                var w2 = Combat(off, dw, s.MeleeD, D, CD, CR);
                double h = (ws.HitChance + w2.HitChance) / 2;
                double dealtPer = (ws.Expected * ws.HitChance + w2.Expected * w2.HitChance) / 2 * DUAL_SPEED;
                ws.Expected = h > 0 ? dealtPer / h : 0; ws.HitChance = h;
                ws.DefenseMod = (ws.DefenseMod + w2.DefenseMod) / 2;
                ws.EvadeChance = (ws.EvadeChance + w2.EvadeChance) / 2;
                ws.Notes += "dual wield: off hand " + off.Item.Display + " (skill " + Math.Round(dw) + "), +20% attack speed; ";
            }
            s.ShieldNote = "";
            s.Weapon = ws;

            // DEFENSE = expected physical damage taken vs an unarmored, unrated character (ACE DamageEvent):
            // per part armor 66.67/(AL+66.67), weighted by hit odds; damage resist 100/(100+DR); crits (10% x
            // crit-resist) do max x2 and are cut by DR + crit damage resist together.
            // Per damage type: part AL = sum of (piece AL x its protection vs that type) + Armor Self; then
            // life protection (level 7 x0.35) x ward cantrip. Mixed by the profile's damage-type weights.
            double weighted = 0;
            var armorBy = new double[Elements.Count];
            for (int p = 0; p < 9; p++)
            {
                long al = 0;
                var alE = new double[Elements.Count];
                foreach (var ch in suit)
                {
                    if (!Covers(ch.V.Item, p) || ch.V.BuffedAL <= 0) continue;
                    al += ch.V.BuffedAL;
                    for (int e = 0; e < Elements.Count; e++) alE[e] += ch.V.BuffedAL * (ch.V.RL != null ? ch.V.RL[e] : 1.0);
                }
                s.PartAL[p] = al;
                weighted += PartHit[p] * al;
                for (int e = 0; e < Elements.Count; e++)
                {
                    double total = Math.Max(0, alE[e] + bodyNormal + bodyExtra);
                    armorBy[e] += PartHit[p] * ARMOR_CONST / (total + ARMOR_CONST);
                }
            }
            // Elemental damage comes partly from spells (share spell_share, default 0.5): ACE SpellProjectile uses only
            // protections/wards (+ ratings) against players - no armor, and melee evade doesn't apply.
            // Shield (ACE Creature_Combat.GetShieldMod): hits from the front half only; shield level = (AL + impen) x
            // clamp(RL + banes, -2, 2), capped by Shield skill (specialized: full skill, otherwise half); damage x
            // 66.67/(SL+66.67). Magic: a shield with Absorb Magic cuts spells from the front by
            // cap x (0.003 x base Shield skill - 0.3) (spec x1.0, trained x0.8; base 100..433) - in melee stance.
            var shieldBy = new double[Elements.Count]; double absorb = 0;
            for (int e = 0; e < Elements.Count; e++) shieldBy[e] = 1;
            if (off != null && IsShield(off.Item))
            {
                double front = Math.Max(0, Math.Min(1, W.GetOr(Profile, "shield_front", 1.0)));
                int rank = R.TrainingRank(48);
                double sk = R.SkillWith(48, ax, 0), cap = rank >= 3 ? sk : Math.Round(sk / 2);
                for (int e = 0; e < Elements.Count; e++)
                {
                    double sl = Math.Max(0, off.BuffedAL) * (off.RL != null ? off.RL[e] : 1.0);
                    sl = Math.Min(sl, cap);
                    shieldBy[e] = front * ARMOR_CONST / (sl + ARMOR_CONST) + (1 - front);
                }
                double maxAbs = off.Item.Flt(159);
                long baseSk = R.SkillBase(48);
                if (maxAbs > 0 && rank >= 2 && baseSk >= 100)
                    absorb = front * Math.Max(0, Math.Min(1, maxAbs * (rank >= 3 ? 1.0 : 0.8) * (Math.Min(baseSk, 433) * 0.003 - 0.3)));
                s.ShieldNote = "Shield level cap " + cap + " (Shield skill " + Math.Round(sk) + (rank >= 3 ? ", specialized" : rank >= 2 ? ", trained: half" : ", untrained: half") + ")" + (absorb > 0 ? ", absorbs " + Math.Round(absorb * 100) + "% of spells from the front" : "");
            }
            var mixNow = Mix; double spellShare = Math.Max(0, Math.Min(1, W.GetOr(Profile, "spell_share", 0.5)));
            var hitBy = new double[Elements.Count]; var spellBy = new double[Elements.Count];
            for (int e = 0; e < Elements.Count; e++)
            {
                s.Ward[e] = ward[e];
                double sh = e < 3 ? 0 : spellShare;
                hitBy[e] = (1 - sh) * armorBy[e] * prot[e] * ward[e] * shieldBy[e];
                spellBy[e] = sh * prot[e] * ward[e] * (1 - absorb);
            }
            s.AvgAL = weighted;   // hit-weighted average (piece AL, before Armor Self)
            s.BodyArmor = bodyNormal + bodyExtra;
            double q = BASE_CRIT * 100 / (100 + CRR), critHit = CRIT_X / AVG_OF_MAX;
            double hitFactor = (1 - q) * 100 / (100 + DR) + q * critHit * 100 / (100 + DR + CDR);
            double taken = 0, taken0 = (1 - BASE_CRIT) + BASE_CRIT * critHit;
            for (int e = 0; e < Elements.Count; e++) taken += mixNow[e] * (hitBy[e] * (1 - ws.EvadeChance) + spellBy[e]) * hitFactor;
            s.DamageTakenPct = 100 * taken / taken0;
            // Score: each damage type on its own relative improvement, added up by the mix (so the spell part of
            // elemental damage, which armor can't touch, doesn't drown out armor and evade).
            double lnSum = 0;
            for (int e = 0; e < Elements.Count; e++)
            {
                if (mixNow[e] <= 0) continue;
                double te = (hitBy[e] * (1 - ws.EvadeChance) + spellBy[e]) * hitFactor;
                lnSum += mixNow[e] * Math.Log(taken0 / Math.Max(1e-12, te));
            }
            // per type, for the table: share of a LANDED hit / spell that gets through (no evade)
            for (int e = 0; e < Elements.Count; e++) s.TakenBy[e] = 100 * (hitBy[e] + spellBy[e]) * hitFactor / taken0;
            s.Defense = W.Get(Profile, "defense") * 100 * lnSum;

            // OFFENSE = expected damage per swing vs no ratings: crit chance 10% + crit rating; normal hits x(1+D);
            // crits x2 at max damage x(1+D+CD) (ACE AdditiveCombine).
            double dealt = ws.Expected * ws.HitChance;
            double dealt0 = (1 - BASE_CRIT) * AVG_OF_MAX + BASE_CRIT * CRIT_X;
            s.DamageDealtPct = 100 * dealt / dealt0;
            s.Offense = W.Get(Profile, "offense") * 100 * Math.Log(dealt / dealt0);

            // Other gear ratings (linear): heal boost, vitality.
            foreach (int k in new[] { 376, 379 })
            {
                int kk = k;
                long t = gear.Sum(ch => ch.V.Item.Int(kk));
                if (t != 0) s.Ratings += t * W.Get(Profile, "rating_" + kk);
            }
            string[] rn = { "Dmg", "Dmg resist", "Crit", "Crit dmg", "Crit resist", "Crit dmg resist" };
            for (int i = 0; i < 6; i++) if (rating[RatingKeys[i]] != 0) s.RatingLines.Add(rn[i] + " " + rating[RatingKeys[i]]);
            long hb = gear.Sum(ch => ch.V.Item.Int(376)), vit = gear.Sum(ch => ch.V.Item.Int(379));
            if (hb != 0) s.RatingLines.Add("Heal boost " + hb);
            if (vit != 0) s.RatingLines.Add("Vitality " + vit);

            s.Total = s.Defense + s.Offense + s.Spells + s.Sets + s.Ratings;
            return s;
        }

        double suitArcane;

        // Does this piece's magic switch on with the given Arcane Lore? Other activation checks come from the verdict.
        public Magic SuitMagic(Verdict v, double arcane)
        {
            if (v == null || v.Item.OwnSpells.Count == 0) return Magic.None;
            if (v.MagicChecks.Any(c => !c.Ok && !c.Text.StartsWith("Arcane Lore"))) return Magic.Off;
            long need = v.Item.Int(109);
            if (need <= 0 || arcane >= need) return Magic.On;
            if (R.CrystalOwned && arcane + Rules.CRYSTAL_ARCANE >= need) return Magic.WithCrystal;
            return Magic.Off;
        }

        static List<int> SetSpellsOf(List<Choice> suit)
        {
            var counts = new Dictionary<long, int>();
            foreach (var ch in suit) { long set = ch.V.Item.Int(265); if (set > 0) { int n; counts.TryGetValue(set, out n); counts[set] = n + ch.V.Item.SetWeight; } }
            var r = new List<int>();
            foreach (var kv in counts) r.AddRange(Tables.SetSpells(kv.Key, kv.Value));
            return r;
        }

        // Arcane Lore under level 7 + the given spells (best per category; normal spells only above level 7).
        double ArcaneFrom(List<int> ids)
        {
            var best = new Dictionary<int, SpellInfo>();
            foreach (int id in ids)
            {
                var sp = Tables.Spell(id); if (sp == null || sp.IsItemSchool || !sp.Additive) continue;
                if (!(sp.IsSkill && sp.StatKey == 14) && !(sp.IsAttribute && (sp.StatKey == 5 || (sp.StatKey == 0 && (sp.StatType & 0x2000) != 0)))) continue;
                SpellInfo b; if (!best.TryGetValue(sp.CategoryId, out b) || sp.Power > b.Power) best[sp.CategoryId] = sp;
            }
            double sk = 0, focus = 0;
            foreach (var sp in best.Values)
            {
                double val = Math.Max(0, sp.Value - (Rules.IsNormal(sp) ? Rules.LEVEL7 : 0));
                if (sp.IsSkill) sk += val; else focus += val;
            }
            return R.SkillWith(14, a => a == 5 ? focus : 0, sk);
        }

        bool IsTrade { get { return Main == "tinker" || Main == "crafter"; } }

        // A skill cantrip is worth nothing on an untrained skill. Tinkers/crafters: their trained tinkering/crafting
        // skills weigh 1 (specialized) or 0.6 (trained), whatever the weights file says (match the character).
        double SkillWeight(int k)
        {
            if (k != 14 && R.TrainingRank(k) < 2) return 0;
            if (IsTrade && Tables.TradeSkills.Contains(k)) return R.TrainingRank(k) >= 3 ? 1.0 : 0.6;
            return W.GetOr(Profile, "skill_" + k, k == 21 ? 0.3 : 0);
        }
        // Tinkers/crafters: an attribute point is worth the skill points it gives their trained trade skills
        // (ACE skill formula (attr1 + attr2) / divisor) x that skill's weight; other profiles use the weights file.
        double AttrWeight(int a)
        {
            double w = W.Get(Profile, "attr_" + a);
            if (!IsTrade) return w;
            double t = 0;
            foreach (int k in Tables.TradeSkills)
            {
                SkillFormula f; if (!Tables.Skills.TryGetValue(k, out f) || f.Divisor <= 0) continue;
                double sw = SkillWeight(k); if (sw <= 0) continue;
                int n = (f.Attr1 == a ? 1 : 0) + (f.Attr2 == a ? 1 : 0);
                t += sw * n / f.Divisor;
            }
            return Math.Max(w, t);
        }

        public double SpellPoints(SpellInfo sp)
        {
            bool normal = Rules.IsNormal(sp);
            double v = Math.Max(0, sp.Value - (normal ? Rules.LEVEL7 : 0));
            if (sp.Additive && sp.IsSkill) return (IsCombat && (sp.StatKey == ProfileSkill || sp.StatKey == 6)) ? 0 : v * SkillWeight(sp.StatKey);   // weapon skill + Melee D are in offense/defense
            if (sp.Additive && sp.IsAttribute)
            {
                if (sp.StatKey == 0 && (sp.StatType & 0x2000) != 0) { double t = 0; for (int a = 1; a <= 6; a++) t += AttrWeight(a); return v * t; }   // all attributes (Society armor)
                return v * AttrWeight(sp.StatKey);
            }
            for (int t = 0; t < Tier.Length; t++) if (sp.Name.StartsWith(Tier[t])) return (t + 1) * W.Get(Profile, "other");
            return 0;
        }

        // ---------- search ----------
        // Previous recommendation (item id -> slot mask), for a quick refresh: start from it instead of every seed.
        public List<KeyValuePair<string, long>> Warm;

        public List<Choice> Best(out SuitScore score)
        {
            var seeds = new List<List<Choice>>();
            var worn = Current();
            var warm = new List<Choice>();
            if (Warm != null)
                foreach (var kv in Warm)
                {
                    var v = candidates.FirstOrDefault(x => x.Item.Id == kv.Key);
                    if (v != null && !warm.Any(x => Clash(x, v.Item, kv.Value))) warm.Add(new Choice { V = v, Mask = kv.Value });
                }
            if (warm.Count > 0) { seeds.Add(warm); if (worn.Count > 0) seeds.Add(worn); }
            else
            {
                seeds.Add(new List<Choice>());
                if (worn.Count > 0) seeds.Add(worn);
                foreach (var g in candidates.Where(v => v.Item.Int(265) > 0).GroupBy(v => v.Item.Int(265)).Where(g => g.Count() >= 2))
                {
                    var seed = new List<Choice>();
                    foreach (var v in g.OrderByDescending(v => v.BuffedAL)) TryPlace(seed, v, false);
                    seeds.Add(seed);
                }
            }
            List<Choice> bestSuit = null; SuitScore bestScore = null;
            Weapon = PickWeapon(worn); OffHand = PickOffHand(worn);
            for (int round = 0; round < 3; round++)
            {
                foreach (var seed in seeds)
                {
                    var suit = Climb(seed);
                    var sc = Score(suit);
                    if (bestScore == null || sc.Total > bestScore.Total + 1e-9) { bestScore = sc; bestSuit = suit; }
                }
                var w2 = PickWeapon(bestSuit); var o2 = PickOffHand(bestSuit);
                if (w2 == Weapon && o2 == OffHand) break;   // nothing changed: the suit search is settled
                Weapon = w2; OffHand = o2; seeds.Insert(0, bestSuit); bestScore = Score(bestSuit);
            }
            score = Score(bestSuit);
            return bestSuit;
        }

        // ---------- off hand: shield or second weapon ----------
        const double DUAL_SPEED = 1.2;
        public Verdict OffHand;
        List<Verdict> offCandidates;
        static bool IsShield(Item it) { return (it.ValidLocations & 0x200000) != 0 && (it.ValidLocations & 0x100000) == 0; }
        static bool IsOneHandMelee(Item it) { return it != null && it.ValidLocations == 0x100000; }

        // ACE Player_Inventory.CheckWeaponCollision: a shield only beside a one-handed melee weapon, a thrown weapon or
        // nothing (no two-handed, caster or bow/atlatl). A second weapon (dual wield) only beside a one-handed melee one.
        public bool OffHandAllowed(Verdict weapon, Verdict off)
        {
            if (off == null) return false;
            if (weapon != null && weapon.Item.Id == off.Item.Id) return false;
            var w = weapon != null ? weapon.Item : null;
            if (IsShield(off.Item))
            {
                if (w == null) return true;
                if ((w.ValidLocations & 0x2000000) != 0 || (w.ValidLocations & 0x1000000) != 0) return false;   // two-handed, caster
                if ((w.ValidLocations & 0x400000) != 0) return false;   // bows/atlatls can't; thrown weapons could, but missile profiles aren't given shields yet
                return (w.ValidLocations & 0x100000) != 0;
            }
            return IsOneHandMelee(off.Item) && IsOneHandMelee(w) && R.TrainingRank(49) >= 2;
        }

        List<Verdict> OffHandCandidates()
        {
            if (offCandidates != null) return offCandidates;
            offCandidates = allVerdicts.Where(v => v.Usable && (IsShield(v.Item) || (IsOneHandMelee(v.Item) && FitsProfile(v.Item)))).ToList();
            return offCandidates;
        }

        public Verdict CurrentOffHand()
        {
            return OffHandCandidates().FirstOrDefault(v => v.Item.Holder == C.Name && v.Item.Where == "equipped" && (v.Item.WieldedLocation & 0x200000) != 0);
        }

        // Why there is no off-hand piece (for the report).
        public string OffHandReason(bool shieldOnly = false)
        {
            var w = Weapon != null ? Weapon.Item : null;
            if (w != null && (w.ValidLocations & 0x2000000) != 0) return "a two-handed weapon leaves no hand free (ACE: no shield or second weapon)";
            if (w != null && (w.ValidLocations & 0x1000000) != 0) return "a wand/staff/orb can't share with a shield (ACE)";
            if (w != null && (w.ValidLocations & 0x400000) != 0) return "bows and atlatls need both hands (ACE)";
            if (shieldOnly) return "it scores lower than the alternatives here";
            bool dual = R.TrainingRank(49) >= 2;
            return OffHandCandidates().Any(x => OffHandAllowed(Weapon, x)) ? "nothing scanned helps in the off hand" : "no shield" + (dual ? " or second weapon" : "") + " collected yet" + (dual ? "" : " (Dual Wield isn't trained, so no second weapon)");
        }

        Verdict PickOffHand(List<Choice> suit)
        {
            Verdict bestO = null; double bv = double.MinValue;
            foreach (var v in new Verdict[] { null }.Concat(OffHandCandidates().Where(x => OffHandAllowed(Weapon, x))))
            {
                OffHand = v;
                double t = Score(suit).Total;
                if (t > bv + 1e-9) { bv = t; bestO = v; }
            }
            OffHand = bestO;
            return bestO;
        }

        Verdict PickWeapon(List<Choice> suit)
        {
            Verdict bestW = null; double bv = double.MinValue;
            foreach (var v in new Verdict[] { null }.Concat(WeaponCandidates()))
            {
                Weapon = v;
                double t = Score(suit).Total;
                if (t > bv + 1e-9) { bv = t; bestW = v; }
            }
            Weapon = bestW;
            return bestW;
        }

        public List<Choice> Current()
        {
            var suit = new List<Choice>();
            foreach (var v in candidates.Where(v => v.Item.Holder == C.Name && v.Item.Where == "equipped"))
            {
                long m = v.Item.WieldedLocation & SuitSlots;
                if (m == 0) continue;
                if (suit.Any(x => Clash(x, v.Item, m))) continue;
                suit.Add(new Choice { V = v, Mask = m });
            }
            return suit;
        }

        // Put a piece in (only into free slots when force = false).
        static bool TryPlace(List<Choice> suit, Verdict v, bool force)
        {
            if (suit.Any(x => x.V.Item.Id == v.Item.Id)) return false;
            foreach (long m in Masks(v.Item))
            {
                bool clash = suit.Any(x => Clash(x, v.Item, m));
                if (clash && !force) continue;
                suit.RemoveAll(x => Clash(x, v.Item, m));
                suit.Add(new Choice { V = v, Mask = m });
                return true;
            }
            return false;
        }

        List<Choice> Climb(List<Choice> start)
        {
            var suit = new List<Choice>(start);
            double cur = Score(suit).Total;
            for (int iter = 0; iter < 300; iter++)
            {
                List<Choice> bestNext = null; double bestVal = cur;
                foreach (var v in candidates)
                {
                    if (suit.Any(x => x.V.Item.Id == v.Item.Id)) continue;
                    foreach (long m in Masks(v.Item))
                    {
                        var next = suit.Where(x => !Clash(x, v.Item, m)).ToList();
                        next.Add(new Choice { V = v, Mask = m });
                        double val = Score(next).Total;
                        if (val > bestVal + 1e-9) { bestVal = val; bestNext = next; }
                    }
                }
                if (bestNext == null) bestNext = PairMove(suit, ref bestVal);   // Arcane enabler + the piece it unlocks
                if (bestNext == null) break;
                suit = bestNext; cur = bestVal;
            }
            // Drop pieces that add nothing (e.g. a ring whose magic won't switch on and that is in no set):
            // the slot is then shown as free instead of recommending a useless piece.
            bool dropped = true;
            while (dropped)
            {
                dropped = false;
                foreach (var ch in suit.ToList())
                {
                    var without = suit.Where(x => x != ch).ToList();
                    if (Score(without).Total >= cur - 1e-9) { suit = without; cur = Score(suit).Total; dropped = true; break; }
                }
            }
            return suit;
        }

        // Pieces that raise Arcane Lore (Arcane or Focus spells) can switch on other pieces' magic. Adding one alone may
        // gain nothing until the piece it unlocks is added too, so try the pair as one move.
        List<Choice> PairMove(List<Choice> suit, ref double bestVal)
        {
            var enablers = candidates.Where(v => v.Item.OwnSpells.Any(id => { var s = Tables.Spell(id); return s != null && s.Additive && ((s.IsSkill && s.StatKey == 14) || (s.IsAttribute && s.StatKey == 5)); })).ToList();
            List<Choice> bestNext = null;
            foreach (var a in enablers)
            {
                if (suit.Any(x => x.V.Item.Id == a.Item.Id)) continue;
                foreach (long ma in Masks(a.Item))
                {
                    var withA = suit.Where(x => !Clash(x, a.Item, ma)).ToList();
                    withA.Add(new Choice { V = a, Mask = ma });
                    foreach (var b in candidates)
                    {
                        if (b.Item.Id == a.Item.Id || withA.Any(x => x.V.Item.Id == b.Item.Id) || !b.Item.Has(109)) continue;
                        foreach (long mb in Masks(b.Item))
                        {
                            if (ClashItems(a.Item, ma, b.Item, mb)) continue;
                            var next = withA.Where(x => !Clash(x, b.Item, mb)).ToList();
                            next.Add(new Choice { V = b, Mask = mb });
                            double val = Score(next).Total;
                            if (val > bestVal + 1e-9) { bestVal = val; bestNext = next; }
                        }
                    }
                }
            }
            return bestNext;
        }

        // What can be worn together (ACE Creature_Equipment.GetEquippedItems): armor and clothing clash only if their
        // COVERAGE overlaps (GetEquippedClothingArmor); jewelry, cloaks and trinkets clash by slot.
        static bool IsCloth(Item it) { return it.Coverage != 0 && (it.ValidLocations & (0xF8000 | 0x4000000 | 0x8000000)) == 0; }
        internal static bool ClashItems(Item a, long ma, Item b, long mb)
        {
            if (IsCloth(a) && IsCloth(b)) return (a.Coverage & b.Coverage) != 0;
            return (ma & mb) != 0;
        }
        static bool Clash(Choice x, Item it, long m) { return ClashItems(x.V.Item, x.Mask, it, m); }

        public static string SlotOf(long mask)
        {
            var names = new List<string>();
            long[] bits = { 0x1, 0x200, 0x400, 0x800, 0x1000, 0x20, 0x2000, 0x4000, 0x100, 0x2, 0x4, 0x8, 0x10, 0x40, 0x80, 0x8000, 0x10000, 0x20000, 0x40000, 0x80000, 0x4000000, 0x8000000, 0x10000000, 0x20000000, 0x40000000 };
            string[] n = { "Head", "Chest", "Abdomen", "Upper arms", "Lower arms", "Hands", "Upper legs", "Lower legs", "Feet", "Chest (under)", "Abdomen (under)", "Upper arms (under)", "Lower arms (under)", "Upper legs (under)", "Lower legs (under)", "Neck", "Left wrist", "Right wrist", "Left ring", "Right ring", "Trinket", "Cloak", "Blue sigil", "Yellow sigil", "Red sigil" };
            for (int i = 0; i < bits.Length; i++) if ((mask & bits[i]) != 0) names.Add(n[i]);
            return string.Join(", ", names.ToArray());
        }
        public static int SlotOrder(long mask)
        {
            long[] bits = { 0x1, 0x200, 0x400, 0x800, 0x1000, 0x20, 0x2000, 0x4000, 0x100, 0x2, 0x4, 0x8, 0x10, 0x40, 0x80, 0x8000, 0x10000, 0x20000, 0x40000, 0x80000, 0x4000000, 0x8000000, 0x10000000, 0x20000000, 0x40000000 };
            for (int i = 0; i < bits.Length; i++) if ((mask & bits[i]) != 0) return i;
            return 99;
        }
    }
}
