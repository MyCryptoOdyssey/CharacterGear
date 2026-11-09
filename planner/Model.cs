// Planner data model: loads the collector's JSON files and the game tables.
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Web.Script.Serialization;

namespace CharacterGear.Planner
{
    internal static class J
    {
        public static Dictionary<string, object> D(object o, string k)
        {
            var d = o as Dictionary<string, object>; object v;
            return d != null && d.TryGetValue(k, out v) ? v as Dictionary<string, object> : null;
        }
        public static IEnumerable<object> A(object o, string k)
        {
            var d = o as Dictionary<string, object>; object v;
            if (d != null && d.TryGetValue(k, out v) && v is IEnumerable && !(v is string)) foreach (object x in (IEnumerable)v) yield return x;
        }
        public static string S(object o, string k)
        {
            var d = o as Dictionary<string, object>; object v;
            return d != null && d.TryGetValue(k, out v) && v != null ? Convert.ToString(v, CultureInfo.InvariantCulture) : "";
        }
        public static long L(object o, string k, long def = 0)
        {
            var d = o as Dictionary<string, object>; object v;
            if (d == null || !d.TryGetValue(k, out v) || v == null) return def;
            try { return Convert.ToInt64(v, CultureInfo.InvariantCulture); } catch { return def; }
        }
        public static double F(object o, string k, double def = 0)
        {
            var d = o as Dictionary<string, object>; object v;
            if (d == null || !d.TryGetValue(k, out v) || v == null) return def;
            try { return Convert.ToDouble(v, CultureInfo.InvariantCulture); } catch { return def; }
        }
        public static bool B(object o, string k)
        {
            var d = o as Dictionary<string, object>; object v;
            return d != null && d.TryGetValue(k, out v) && v is bool && (bool)v;
        }
    }

    internal class SpellInfo
    {
        public int Id; public string Name, Category, School; public int CategoryId, Power;
        public int StatType, StatKey; public double Value;
        public double Duration;   // seconds (spells.tsv column 7)
        public bool IsItemSchool { get { return School == "ItemEnchantment"; } }
        public bool Additive { get { return (StatType & 0x8000) != 0; } }
        public bool IsSkill { get { return (StatType & 0x10) != 0; } }
        public bool IsAttribute { get { return (StatType & 0x1) != 0; } }
    }

    internal class SkillFormula { public int Id, Attr1, Attr2, Divisor, X; public string Name; }

    internal static class Tables
    {
        public static readonly Dictionary<int, SpellInfo> Spells = new Dictionary<int, SpellInfo>();
        public static readonly Dictionary<int, SkillFormula> Skills = new Dictionary<int, SkillFormula>();
        public static readonly Dictionary<int, string> SetNames = new Dictionary<int, string>();
        public static readonly Dictionary<int, string> Materials = new Dictionary<int, string>();
        public class TemplateRow
        {
            public string Profile, Kind, Want, Label, Rule = ""; public int Key;
            public bool IfTrained { get { return Rule.Contains("iftrained"); } }
            public bool Optional { get { return Rule.Contains("optional"); } }
        }
        // Tinkering and crafting skills (ACE ids): a Tinker/Crafter's targets and weights come from the ones it has trained.
        public static readonly int[] TradeSkills = { 18, 28, 29, 30, 40, 38, 39, 37, 23 };
        public static readonly List<TemplateRow> Templates = new List<TemplateRow>();
        public static readonly Dictionary<string, string> PetDamage = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        public static readonly Dictionary<string, int> PetSchool = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        public static readonly Dictionary<int, SortedDictionary<int, List<int>>> SetTiers = new Dictionary<int, SortedDictionary<int, List<int>>>();

        public static string SetName(long id) { string n; return SetNames.TryGetValue((int)id, out n) ? n : "set " + id; }
        // ACE GetSpellSet: count capped at the highest tier; a count with no tier uses the tier below.
        public static List<int> SetSpells(long setId, int count)
        {
            SortedDictionary<int, List<int>> t;
            if (!SetTiers.TryGetValue((int)setId, out t)) return new List<int>();
            List<int> best = null;
            foreach (var kv in t) if (kv.Key <= count) best = kv.Value;
            return best ?? new List<int>();
        }

        public static void Load()
        {
            foreach (string[] f in Rows("spells.tsv"))
            {
                if (f.Length < 10) continue;
                var s = new SpellInfo { Id = Int(f[0]), Name = f[1], CategoryId = Int(f[2]), Category = f[3], Power = Int(f[4]), School = f[5],
                    StatType = Int(f[7]), StatKey = Int(f[8]), Value = Dbl(f[9]), Duration = Dbl(f[6]) };
                Spells[s.Id] = s;
            }
            foreach (string[] f in Rows("set_names.tsv")) if (f.Length >= 2) SetNames[Int(f[0])] = f[1];
            foreach (string[] f in Rows("materials.tsv")) if (f.Length >= 2) Materials[Int(f[0])] = f[1];
            foreach (string[] f in Rows("pets.tsv"))
            {
                if (f.Length < 2 || f[0] == "name") continue;
                if (f[1] != "") PetDamage[f[0]] = f[1];
                if (f.Length >= 3) PetSchool[f[0]] = Int(f[2]);
            }
            foreach (string[] f in Rows("templates.tsv")) if (f.Length >= 5) Templates.Add(new TemplateRow { Profile = f[0], Kind = f[1], Key = Int(f[2]), Want = f[3], Label = f[4], Rule = f.Length > 5 ? f[5] : "" });
            foreach (string[] f in Rows("set_bonuses.tsv"))
            {
                if (f.Length < 3) continue;
                int s = Int(f[0]), p = Int(f[1]);
                if (!SetTiers.ContainsKey(s)) SetTiers[s] = new SortedDictionary<int, List<int>>();
                var l = new List<int>(); foreach (string x in f[2].Split(',')) if (x.Trim() != "") l.Add(Int(x));
                SetTiers[s][p] = l;
            }
            foreach (string[] f in Rows("skills.tsv"))
            {
                if (f.Length < 6) continue;
                var k = new SkillFormula { Id = Int(f[0]), Name = f[1], Attr1 = Int(f[2]), Attr2 = Int(f[3]), Divisor = Int(f[4]), X = Int(f[5]) };
                Skills[k.Id] = k;
            }
        }

        static IEnumerable<string[]> Rows(string name)
        {
            using (Stream st = Assembly.GetExecutingAssembly().GetManifestResourceStream("CharacterGear." + name))
            using (var r = new StreamReader(st))
            {
                string line; bool header = false;
                while ((line = r.ReadLine()) != null)
                {
                    if (line.StartsWith("#") || line.Trim() == "") continue;
                    if (!header) { header = true; continue; }
                    yield return line.Split('\t');
                }
            }
        }
        static int Int(string s) { int v; return int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out v) ? v : 0; }
        static double Dbl(string s) { double v; return double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out v) ? v : 0; }
        public static SpellInfo Spell(long id) { SpellInfo s; return Spells.TryGetValue((int)id, out s) ? s : null; }
        public static string SpellName(long id) { var s = Spell(id); return s != null ? s.Name : "spell " + id; }
    }

    internal class Item
    {
        public string Id, Name, Class, Where, Pack, Holder;
        public string SeenAt = "";   // when this copy was saved (character scan or chest snapshot)
        public bool InChest { get { return Where == "chest"; } }
        public string Display;   // what the game shows: "<material> <name>", plus a short tag if two pieces look the same
        public long ValidLocations, Coverage, WieldedLocation;
        public bool HasAnswer;
        // Armor/clothing protection per damage type as the ID showed it (ACE ArmorProfile = base + banes on it now),
        // in Elements order. null = no armor profile in the ID.
        public double[] Profile;
        public readonly Dictionary<int, long> Ints = new Dictionary<int, long>();
        public readonly Dictionary<int, long> Dids = new Dictionary<int, long>();
        public readonly Dictionary<int, double> Floats = new Dictionary<int, double>();
        public readonly Dictionary<int, long> Int64s = new Dictionary<int, long>();
        // Leveled items (cloaks, society jewelry, rare armor): ACE ExperienceSystem.ItemTotalXPToLevel.
        public int ItemLevel
        {
            get
            {
                long style = Int(320), max = Int(319), total, b;
                if (style <= 0 || !Int64s.TryGetValue(4, out total) || !Int64s.TryGetValue(5, out b) || b <= 0) return 0;
                int level = 0;
                if (style == 1) level = (int)(total / b);
                else if (style == 2) { long need = b, left = total; while (left >= need) { level++; left -= need; need *= 2; } }
                else if (style == 3) level = (total >= b && total < b * 3) ? 1 : (int)((total - b) / b);
                return (int)Math.Min(level, max > 0 ? max : level);
            }
        }
        // How much this piece counts toward its set: item level for leveled items, otherwise 1 (ACE GetSpellSet).
        public int SetWeight { get { return Int(320) > 0 ? ItemLevel : 1; } }
        public double Flt(int k, double def = 0) { double v; return Floats.TryGetValue(k, out v) ? v : def; }
        public bool HasF(int k) { return Floats.ContainsKey(k); }
        public readonly List<int> OwnSpells = new List<int>();
        public readonly List<int> CastOn = new List<int>();
        public long Int(int k, long def = 0) { long v; return Ints.TryGetValue(k, out v) ? v : def; }
        public bool Has(int k) { return Ints.ContainsKey(k); }
        public long Did(int k, long def = 0) { long v; return Dids.TryGetValue(k, out v) ? v : def; }
    }

    // A house chest snapshot (collector: data\<server>\chests\<id>.json). Each chest is one more holder.
    internal class Chest
    {
        public string Id, Label, OpenedBy, SeenAt, File;
        public bool Complete;
        public readonly List<Item> Items = new List<Item>();
    }

    // Damage types the planner models (nether left out: no bane exists, and ACE gives armor 0 vs nether
    // unless the piece has that property, which the ID doesn't show reliably).
    internal static class Elements
    {
        public const int Count = 7;
        public static readonly string[] Name = { "Slash", "Pierce", "Bludgeon", "Cold", "Fire", "Acid", "Lightning" };
        public static readonly string[] Key = { "slash", "pierce", "bludgeon", "cold", "fire", "acid", "lightning" };
        public static readonly string[] ProfileKey = { "slash", "pierce", "bludgeon", "cold", "fire", "acid", "lightning" };
        public static readonly int[] BaneKey = { 13, 14, 15, 16, 17, 18, 19 };     // ACE PropertyFloat ArmorModVs*
        public static readonly int[] ResistKey = { 64, 65, 66, 68, 67, 69, 70 };   // ACE PropertyFloat Resist* (protections, wards)
        public static int OfResistKey(int k) { return Array.IndexOf(ResistKey, k); }
    }

    internal class SkillRow { public int Id; public string Name, Training; public long Base, Buffed; }

    internal class Character
    {
        public string Name, Server, Race, ScannedAt, File;
        public long Level, Vitae;
        public int ScholarsCrystals;
        public readonly Dictionary<int, long> AttrBase = new Dictionary<int, long>();
        public readonly Dictionary<int, long> AttrBuffed = new Dictionary<int, long>();
        public readonly Dictionary<int, long> VitalBase = new Dictionary<int, long>();
        public readonly Dictionary<int, long> VitalBuffed = new Dictionary<int, long>();
        public readonly Dictionary<int, SkillRow> Skills = new Dictionary<int, SkillRow>();
        public readonly Dictionary<int, long> Props = new Dictionary<int, long>();
        public readonly List<int> GearEnchantments = new List<int>();     // on the character, from worn items (duration -1)
        public readonly List<int> CastBuffs = new List<int>();            // on the character, cast by players (timed)
        public readonly List<Item> Items = new List<Item>();
    }

    internal static class Loader
    {
        public static List<Character> LoadServer(string serverDir)
        {
            var list = new List<Character>();
            var js = new JavaScriptSerializer { MaxJsonLength = int.MaxValue };
            foreach (string path in Directory.GetFiles(serverDir, "*.json"))
            {
                try
                {
                    object root = js.DeserializeObject(File.ReadAllText(path));
                    list.Add(Parse(root, path));
                }
                catch (Exception ex) { Console.WriteLine("Skipped " + Path.GetFileName(path) + ": " + ex.Message); }
            }
            list.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
            return list;
        }

        // Two pieces that still look the same get a short tag (key stat + end of the item id) so you can tell them apart.
        public static void TagLookAlikes(IEnumerable<Item> items)
        {
            foreach (var g in items.GroupBy(i => i.Display).Where(g => g.Count() > 1).ToList())
                foreach (var it in g)
                {
                    string stat = it.Int(44) > 0 ? "Dmg " + it.Int(44) : it.Int(28) > 0 ? "AL " + it.Int(28) : it.Int(105) > 0 ? "work " + it.Int(105) : "";
                    it.Display += " (" + (stat != "" ? stat + ", " : "") + "#" + it.Id.Substring(Math.Max(0, it.Id.Length - 4)) + ")";
                }
        }

        public static List<Chest> LoadChests(string serverDir)
        {
            var list = new List<Chest>();
            string dir = Path.Combine(serverDir, "chests");
            if (!Directory.Exists(dir)) return list;
            var js = new JavaScriptSerializer { MaxJsonLength = int.MaxValue };
            foreach (string path in Directory.GetFiles(dir, "*.json"))
            {
                try
                {
                    object root = js.DeserializeObject(File.ReadAllText(path));
                    var ch = new Chest { Id = J.S(root, "chestId"), Label = J.S(root, "label"), OpenedBy = J.S(root, "openedBy"), SeenAt = J.S(root, "seenAt"), Complete = J.B(root, "complete"), File = path };
                    if (ch.Label == "") ch.Label = "House chest " + ch.Id;
                    foreach (object o in J.A(root, "items"))
                    {
                        if (!J.B(o, "gear")) continue;
                        var it = ParseItem(o, ch.Label); it.SeenAt = ch.SeenAt; it.Where = "chest";
                        ch.Items.Add(it);
                    }
                    var spells = new List<int>();
                    foreach (object e in J.A(root, "openerEnchantments")) spells.Add((int)J.L(e, "spell"));
                    StripAuras(spells, ch.Items);   // weapon IDs carry the opener's auras too
                    list.Add(ch);
                }
                catch (Exception ex) { Console.WriteLine("Skipped chest " + Path.GetFileName(path) + ": " + ex.Message); }
            }
            list.Sort((a, b) => string.Compare(a.Label, b.Label, StringComparison.OrdinalIgnoreCase));
            return list;
        }

        // The same item in two files (moved between a chest and a character): keep the newest copy.
        public static void DropOlderCopies(List<Character> chars, List<Chest> chests)
        {
            var newest = new Dictionary<string, string>();
            foreach (var it in chars.SelectMany(c => c.Items).Concat(chests.SelectMany(c => c.Items)))
            {
                string t; if (!newest.TryGetValue(it.Id, out t) || string.CompareOrdinal(it.SeenAt, t) > 0) newest[it.Id] = it.SeenAt;
            }
            Func<Item, bool> stale = it => string.CompareOrdinal(it.SeenAt, newest[it.Id]) < 0;
            foreach (var c in chars) c.Items.RemoveAll(it => stale(it));
            foreach (var c in chests) c.Items.RemoveAll(it => stale(it));
        }

        static void MapDecal(Item it, Dictionary<string, object> src, string decalKey, int aceKey, bool isInt)
        {
            object v; if (src == null || !src.TryGetValue(decalKey, out v) || v == null) return;
            if (isInt) { if (!it.Ints.ContainsKey(aceKey)) it.Ints[aceKey] = Convert.ToInt64(v, CultureInfo.InvariantCulture); }
            else if (!it.Floats.ContainsKey(aceKey)) it.Floats[aceKey] = Convert.ToDouble(v, CultureInfo.InvariantCulture);
        }

        static Character Parse(object root, string path)
        {
            var c = new Character { Name = J.S(root, "character"), Server = J.S(root, "server"), ScannedAt = J.S(root, "scannedAt"), File = path };
            var ch = J.D(root, "char");
            c.Level = J.L(ch, "level"); c.Race = J.S(ch, "race"); c.Vitae = J.L(ch, "vitae");
            var attrs = J.D(ch, "attributes");
            if (attrs != null) foreach (var kv in attrs) { int k = int.Parse(kv.Key); c.AttrBase[k] = J.L(kv.Value, "base"); c.AttrBuffed[k] = J.L(kv.Value, "buffed"); }
            var vit = J.D(ch, "vitals");
            if (vit != null) foreach (var kv in vit) { int k = int.Parse(kv.Key); c.VitalBase[k] = J.L(kv.Value, "base"); c.VitalBuffed[k] = J.L(kv.Value, "buffed"); }
            var sk = J.D(ch, "skills");
            if (sk != null) foreach (var kv in sk)
                {
                    int k = int.Parse(kv.Key);
                    c.Skills[k] = new SkillRow { Id = k, Name = J.S(kv.Value, "name"), Training = J.S(kv.Value, "training"), Base = J.L(kv.Value, "base"), Buffed = J.L(kv.Value, "buffed") };
                }
            var props = J.D(ch, "props");
            if (props != null) foreach (var kv in props) c.Props[int.Parse(kv.Key)] = Convert.ToInt64(kv.Value, CultureInfo.InvariantCulture);
            foreach (object e in J.A(ch, "enchantments"))
            {
                int spell = (int)J.L(e, "spell");
                if (J.F(e, "duration") < 0) c.GearEnchantments.Add(spell); else c.CastBuffs.Add(spell);
            }
            foreach (object o in J.A(root, "items"))
            {
                if (J.S(o, "name") == "Scholar's Crystal") c.ScholarsCrystals += Math.Max(1, (int)J.L(o, "stack", 1));
                if (!J.B(o, "gear")) continue;
                var it = ParseItem(o, c.Name); it.SeenAt = c.ScannedAt;
                c.Items.Add(it);
            }
            StripHolderAuras(c);
            return c;
        }

        static Item ParseItem(object o, string holder)
        {
            var it = new Item { Id = J.S(o, "id"), Name = J.S(o, "name"), Class = J.S(o, "class"), Where = J.S(o, "where"), Pack = J.S(o, "pack"), Holder = holder,
                ValidLocations = J.L(o, "equipableSlots"), Coverage = J.L(o, "coverage"), WieldedLocation = J.L(o, "wieldedLocation") };
            var ans = J.D(o, "idAnswer");
            if (ans != null)
            {
                it.HasAnswer = true;
                var ints = J.D(ans, "ints");
                if (ints != null) foreach (var kv in ints) it.Ints[int.Parse(kv.Key)] = Convert.ToInt64(kv.Value, CultureInfo.InvariantCulture);
                var i64 = J.D(ans, "int64s");
                if (i64 != null) foreach (var kv in i64) it.Int64s[int.Parse(kv.Key)] = Convert.ToInt64(kv.Value, CultureInfo.InvariantCulture);
                var flts = J.D(ans, "floats");
                if (flts != null) foreach (var kv in flts) if (kv.Value != null) it.Floats[int.Parse(kv.Key)] = Convert.ToDouble(kv.Value, CultureInfo.InvariantCulture);
                var dids = J.D(ans, "dids");
                if (dids != null) foreach (var kv in dids) it.Dids[int.Parse(kv.Key)] = Convert.ToInt64(kv.Value, CultureInfo.InvariantCulture);
                foreach (object s in J.A(ans, "itemSpells")) it.OwnSpells.Add(Convert.ToInt32(s, CultureInfo.InvariantCulture));
                foreach (object s in J.A(ans, "castOnSpells")) it.CastOn.Add(Convert.ToInt32(s, CultureInfo.InvariantCulture));
                var ap = J.D(ans, "armorProfile");
                if (ap != null)
                {
                    it.Profile = new double[Elements.Count];
                    for (int e = 0; e < Elements.Count; e++) { object pv; it.Profile[e] = ap.TryGetValue(Elements.ProfileKey[e], out pv) && pv != null ? Convert.ToDouble(pv, CultureInfo.InvariantCulture) : 1.0; }
                }
            }
            else
            {
                // Fallback: Decal's copy (keys below 65536 are server property numbers).
                var ints = J.D(o, "ints");
                if (ints != null) foreach (var kv in ints) { int k; if (int.TryParse(kv.Key, out k) && k < 65536) it.Ints[k] = Convert.ToInt64(kv.Value, CultureInfo.InvariantCulture); }
                foreach (object s in J.A(o, "spells")) it.OwnSpells.Add(Convert.ToInt32(s, CultureInfo.InvariantCulture));
                var flts = J.D(o, "floats");
                if (flts != null) foreach (var kv in flts) { int k; if (int.TryParse(kv.Key, out k) && k < 65536 && kv.Value != null) it.Floats[k] = Convert.ToDouble(kv.Value, CultureInfo.InvariantCulture); }
            }
            // Weapon numbers travel in the ID answer's weapon block, which Decal decodes into its own keys.
            var dints = J.D(o, "ints"); var dflt = J.D(o, "floats");
            MapDecal(it, dints, "218103842", 44, true); MapDecal(it, dints, "218103839", 49, true); MapDecal(it, dints, "218103840", 48, true);
            MapDecal(it, dflt, "167772171", 22, false); MapDecal(it, dflt, "167772172", 62, false); MapDecal(it, dflt, "167772174", 63, false);
            if (it.ValidLocations == 0) it.ValidLocations = it.Int(9);
            string mat; it.Display = it.Name;
            if (it.Int(131) > 0 && Tables.Materials.TryGetValue((int)it.Int(131), out mat) && !it.Name.StartsWith(mat, StringComparison.OrdinalIgnoreCase)) it.Display = mat + " " + it.Name;
            return it;
        }

        // A weapon's ID numbers include its holder's active weapon buffs (Blood Drinker, Heart Seeker, Defender,
        // Spirit Drinker, Swift Killer) - even in the pack (seen in testing: 40 dmg unbuffed, 64 with auras).
        // Strip them so every weapon compares at base; the planner adds the assumed level 7 buffs back.
        static void StripHolderAuras(Character c) { StripAuras(c.GearEnchantments.Concat(c.CastBuffs), c.Items); }
        static void StripAuras(IEnumerable<int> spells, List<Item> items)
        {
            var best = new Dictionary<int, SpellInfo>();
            foreach (int id in spells)
            {
                var s = Tables.Spell(id); if (s == null) continue;
                if (s.StatKey != 360 && s.StatKey != 168 && s.StatKey != 169 && s.StatKey != 170 && s.StatKey != 361) continue;
                SpellInfo b; if (!best.TryGetValue(s.CategoryId, out b) || s.Power > b.Power) best[s.CategoryId] = s;
            }
            double dmg = 0, atk = 0, def = 0, elem = 0, spd = 0;
            foreach (var s in best.Values)
            {
                if (s.StatKey == 360) dmg += s.Value; else if (s.StatKey == 168) atk += s.Value; else if (s.StatKey == 169) def += s.Value;
                else if (s.StatKey == 170) elem += s.Value; else spd += s.Value;
            }
            if (dmg == 0 && atk == 0 && def == 0 && elem == 0 && spd == 0) return;
            foreach (var it in items)
            {
                if ((it.ValidLocations & (0x100000 | 0x200000 | 0x400000 | 0x1000000 | 0x2000000)) == 0) continue;
                if (it.Has(44) && it.Int(44) > 0) it.Ints[44] = Math.Max(1, it.Int(44) - (long)dmg);
                if (it.HasF(62)) it.Floats[62] = it.Flt(62) - atk;
                if (it.HasF(29)) it.Floats[29] = it.Flt(29) - def;
                if (it.HasF(152)) it.Floats[152] = it.Flt(152) - elem;
                if (it.Has(49) && spd != 0) it.Ints[49] = it.Int(49) - (long)spd;
            }
        }
    }
}
