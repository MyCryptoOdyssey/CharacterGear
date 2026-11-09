// Summoning essences (pet devices). Rule: only if Summoning is trained, only essences of the character's
// own school (or with no school requirement), and only suggest one that is an OBVIOUS upgrade over what the
// character already holds for that damage type.
// School: the character chooses ONE (int 362, sent at login) and can then only summon essences of that school;
// 36 of the 288 stock essences have no school. The essence's own 362 isn't in its ID, so it comes from tables/pets.tsv.
// ACE: PetDevice.CheckUseRequirements = base use requirements (UseRequiresSkill 366 / level 367, UseRequiresLevel 369 -
// checked in Rules) + SummoningMastery (int 362) must equal the player's 362 (1 Primalist, 2 Necromancer, 3 Naturalist).
// CombatPet.Init: the pet takes the essence's gear ratings 370-375 (damage, damage resist, crit, crit resist,
// crit damage, crit damage resist). Pet strength grows with the essence's Summoning requirement (367).
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace CharacterGear.Planner
{
    internal class EssencePick
    {
        public string Damage; public Verdict Best, Held; public bool Upgrade; public string Reason = "";
    }

    internal static class Essences
    {
        static readonly Regex NameRx = new Regex(@"Essence( \(\d+\))?$");
        public static bool Is(Item it) { return it != null && NameRx.IsMatch(it.Name ?? ""); }
        public static readonly string[] School = { "none", "Primalist", "Necromancer", "Naturalist" };

        public static string DamageOf(Item it)
        {
            string d; if (Tables.PetDamage.TryGetValue(it.Name, out d)) return d == "Electric" ? "Lightning" : d;
            string n = it.Name;
            if (n.StartsWith("Frost")) return "Cold";
            if (n.StartsWith("Lightning")) return "Lightning";
            if (n.StartsWith("Fire") || n.StartsWith("Volcanic")) return "Fire";
            if (n.StartsWith("Acid")) return "Acid";
            return "Unknown";
        }

        // The essence's school: not in the ID answer (ACE: 362 is not an AssessmentProperty), so from tables/pets.tsv by name.
        public static long SchoolOf(Item it)
        {
            if (it.Int(362) != 0) return it.Int(362);
            int s; return Tables.PetSchool.TryGetValue(it.Name, out s) ? s : 0;
        }

        public static long Tier(Item it) { return it.Int(367); }
        public static long Ratings(Item it) { long t = 0; for (int k = 370; k <= 375; k++) t += it.Int(k); return t; }
        public static string RatingText(Item it)
        {
            string[] n = { "D", "DR", "C", "CR", "CD", "CDR" };
            var p = new List<string>();
            for (int k = 370; k <= 375; k++) if (it.Int(k) != 0) p.Add(n[k - 370] + " " + it.Int(k));
            long sc = SchoolOf(it);
            return (sc >= 1 && sc <= 3 ? School[sc] + ", " : "") + "skill " + Tier(it) + (p.Count > 0 ? ", " + string.Join(" ", p.ToArray()) : ", no ratings");
        }

        // Can THIS character summon it? (use requirements from Rules + school)
        public static bool Usable(Verdict v, long mastery, out string why)
        {
            why = "";
            if (!v.Movable) { why = "attuned to " + v.Item.Holder; return false; }
            var fail = v.MagicChecks.Where(c => !c.Ok).Select(c => c.Text).ToList();
            if (fail.Count > 0) { why = "needs " + string.Join(", ", fail.ToArray()); return false; }
            long need = SchoolOf(v.Item);
            if (need != 0 && need != mastery) { why = "needs the " + School[Math.Min(3, (int)need)] + " school"; return false; }
            return true;
        }

        // a better than b beyond doubt: no lower Summoning requirement, no lower rating total, and better in one of them
        public static bool Obvious(Item a, Item b)
        {
            if (b == null) return true;
            return Tier(a) >= Tier(b) && Ratings(a) >= Ratings(b) && (Tier(a) > Tier(b) || Ratings(a) > Ratings(b));
        }

        public static List<EssencePick> Picks(Character c, List<Verdict> verdicts, long mastery)
        {
            var usable = new List<Verdict>();
            foreach (var v in verdicts.Where(v => Is(v.Item))) { string w; if (Usable(v, mastery, out w)) usable.Add(v); }
            var list = new List<EssencePick>();
            foreach (var g in usable.GroupBy(v => DamageOf(v.Item)).OrderBy(g => g.Key))
            {
                Func<Verdict, double> key = v => Tier(v.Item) * 1000 + Ratings(v.Item);
                var best = g.OrderByDescending(key).First();
                var held = g.Where(v => v.Item.Holder == c.Name).OrderByDescending(key).FirstOrDefault();
                var pick = new EssencePick { Damage = g.Key, Best = best, Held = held };
                if (held != null && best.Item.Id == held.Item.Id) pick.Reason = "you already hold the best";
                else if (Obvious(best.Item, held != null ? held.Item : null)) { pick.Upgrade = true; pick.Reason = held == null ? "you hold none of this type" : "higher summoning tier and/or more ratings than yours, never worse"; }
                else { pick.Best = held; pick.Reason = "no clear upgrade (better in one way, worse in another) - keep yours"; }
                list.Add(pick);
            }
            return list;
        }
    }
}
