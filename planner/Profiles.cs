// Character archetypes. A profile is a main archetype plus optional extras, e.g. "war+summoning",
// "heavy+dualwield", "twohanded". Detected from trained skills; override per character in
// data\CharacterGear_profiles.tsv (character <TAB> profile, or "auto").
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace CharacterGear.Planner
{
    internal static class Profiles
    {
        public static readonly string[] Mains = { "twohanded", "heavy", "light", "finesse", "missile", "war", "void", "life", "tinker", "crafter" };
        public static readonly string[] Extras = { "summoning", "dualwield" };
        public static readonly Dictionary<string, int> MainSkill = new Dictionary<string, int> {
            { "twohanded", 41 }, { "heavy", 44 }, { "light", 45 }, { "finesse", 46 }, { "missile", 47 }, { "war", 34 }, { "void", 43 }, { "life", 33 },
            { "tinker", 18 }, { "crafter", 38 } };
        public static readonly Dictionary<string, string> Label = new Dictionary<string, string> {
            { "twohanded", "Two Handed Combat" }, { "heavy", "Heavy Weapons" }, { "light", "Light Weapons" }, { "finesse", "Finesse Weapons" },
            { "missile", "Missile Weapons" }, { "war", "War Magic" }, { "void", "Void Magic" }, { "life", "Life Magic" },
            { "tinker", "Tinkerer" }, { "crafter", "Crafter" }, { "summoning", "Summoning" }, { "dualwield", "Dual Wield" } };

        static readonly Dictionary<string, string> overrides = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        public static string FilePath;

        public static string Pretty(string profile)
        {
            return string.Join(" + ", profile.Split('+').Select(p => { string l; return Label.TryGetValue(p, out l) ? l : p; }).ToArray());
        }

        // Read overrides; add any new character with "auto" so the file always lists everyone.
        public static void Load(string dataDir, IEnumerable<string> names)
        {
            FilePath = Path.Combine(dataDir, "CharacterGear_profiles.tsv");
            var lines = new List<string>();
            if (File.Exists(FilePath)) lines.AddRange(File.ReadAllLines(FilePath));
            else
            {
                lines.Add("# Character profiles. 'auto' = detected from trained skills. To choose, write a main archetype, optionally");
                lines.Add("# + extras, e.g.  war+summoning   void+summoning   twohanded   heavy+dualwield   missile+summoning   tinker   crafter");
                lines.Add("# Mains: " + string.Join(", ", Mains) + ".  Extras: " + string.Join(", ", Extras) + ".");
                lines.Add("# Run CharacterGearPlanner.exe again after editing.");
            }
            var known = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string l in lines)
            {
                if (l.StartsWith("#") || l.Trim() == "") continue;
                string[] f = l.Split('\t');
                if (f.Length < 2) continue;
                known.Add(f[0].Trim());
                string p = f[1].Trim().ToLowerInvariant().Replace(" ", "");
                if (p != "auto" && p != "" && Valid(p)) overrides[f[0].Trim()] = p;
            }
            bool added = false;
            foreach (string n in names) if (!known.Contains(n)) { lines.Add(n + "\tauto"); added = true; }
            if (added || !File.Exists(FilePath)) File.WriteAllLines(FilePath, lines.ToArray(), new UTF8Encoding(false));
        }

        static bool Valid(string p)
        {
            var parts = p.Split('+');
            return Mains.Contains(parts[0]) && parts.Skip(1).All(x => Extras.Contains(x));
        }

        public static bool IsOverridden(string name) { return overrides.ContainsKey(name); }

        public static string For(Character c, Rules r)
        {
            string o;
            if (overrides.TryGetValue(c.Name, out o)) return o;
            return Detect(r);
        }

        // Detection: the highest trained combat/magic skill is the main archetype, unless the character's
        // tinkering or crafting skills are higher (a dedicated tinker/crafter). Summoning and Dual Wield,
        // when trained, are added as extras.
        public static string Detect(Rules r)
        {
            string main = null; long best = -1;
            foreach (var kv in MainSkill)
            {
                if (kv.Key == "tinker" || kv.Key == "crafter") continue;
                if (r.TrainingRank(kv.Value) >= 2 && r.SkillBase(kv.Value) > best) { best = r.SkillBase(kv.Value); main = kv.Key; }
            }
            long tink = new[] { 18, 28, 29, 30 }.Where(s => r.TrainingRank(s) >= 2).Select(s => r.SkillBase(s)).DefaultIfEmpty(-1).Max();
            long craft = new[] { 37, 38, 39 }.Where(s => r.TrainingRank(s) >= 2).Select(s => r.SkillBase(s)).DefaultIfEmpty(-1).Max();
            if (tink > best && tink >= craft) main = "tinker";
            else if (craft > best) main = "crafter";
            if (main == null) main = "heavy";
            string p = main;
            if (r.TrainingRank(54) >= 2) p += "+summoning";
            if (r.TrainingRank(49) >= 2 && (main == "heavy" || main == "light" || main == "finesse")) p += "+dualwield";
            return p;
        }
    }
}
