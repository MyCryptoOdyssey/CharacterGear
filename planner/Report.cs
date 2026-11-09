// HTML wieldability report: one page per server, one section per character.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;

namespace CharacterGear.Planner
{
    internal static class Report
    {
        static readonly KeyValuePair<long, string>[] SlotNames = {
            P(0x1,"Head"), P(0x200,"Chest"), P(0x400,"Abdomen"), P(0x800,"Upper arms"), P(0x1000,"Lower arms"), P(0x20,"Hands"),
            P(0x2000,"Upper legs"), P(0x4000,"Lower legs"), P(0x100,"Feet"),
            P(0x2,"Chest (under)"), P(0x4,"Abdomen (under)"), P(0x8,"Upper arms (under)"), P(0x10,"Lower arms (under)"), P(0x40,"Upper legs (under)"), P(0x80,"Lower legs (under)"),
            P(0x8000,"Neck"), P(0x30000,"Wrist"), P(0xC0000,"Ring"), P(0x100000,"Melee weapon"), P(0x200000,"Shield"), P(0x400000,"Missile weapon"),
            P(0x800000,"Ammo"), P(0x1000000,"Wand / orb"), P(0x2000000,"Two-handed"), P(0x4000000,"Trinket"), P(0x8000000,"Cloak"), P(0x70000000,"Aetheria")
        };
        static KeyValuePair<long, string> P(long k, string v) { return new KeyValuePair<long, string>(k, v); }

        static string SlotText(long loc)
        {
            var parts = new List<string>();
            foreach (var kv in SlotNames) if ((loc & kv.Key) != 0 && !parts.Contains(kv.Value)) parts.Add(kv.Value);
            return parts.Count == 0 ? "-" : string.Join(", ", parts.ToArray());
        }

        static string Group(Item it)
        {
            long l = it.ValidLocations;
            if ((l & 0x800000) != 0) return "6 Ammo";
            if ((l & (0x100000 | 0x200000 | 0x400000 | 0x1000000 | 0x2000000)) != 0) return "1 Weapons, wands & shields";
            if ((l & 0x7C000000) != 0) return "4 Cloak, trinket & Aetheria";
            if ((l & 0xF8000) != 0) return "3 Jewelry";
            if ((l & (0x7E00 | 0x1 | 0x20 | 0x100)) != 0) return "2 Armor";
            if ((l & 0xDE) != 0) return "5 Underclothes";
            return "7 Other";
        }

        static string H(string s) { return WebUtility.HtmlEncode(s ?? ""); }

        // Per-character rows for the in-game "Recommended" window: tab, slot, item, holder, what to do.
        public static readonly Dictionary<string, List<string[]>> Rec = new Dictionary<string, List<string[]>>();
        public static readonly Dictionary<string, string[]> RecHead = new Dictionary<string, string[]>();
        // "#suit" line: the recommended pieces (id:mask) so a quick refresh can start from them.
        public static readonly Dictionary<string, string> RecSuit = new Dictionary<string, string>();
        public static readonly Dictionary<string, List<KeyValuePair<string, long>>> Warm = new Dictionary<string, List<KeyValuePair<string, long>>>();
        static void AddRec(string who, string tab, string slot, string item, string holder, string todo, string why = "")
        {
            if (!Rec.ContainsKey(who)) Rec[who] = new List<string[]>();
            Rec[who].Add(new[] { tab, slot, item, holder, todo, why });
        }

        // Short spell names like the game community writes them: "Legendary War Magic Aptitude" -> "Leg War".
        static readonly HashSet<string> Filler = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Aptitude", "Prowess", "Mastery", "Expertise", "Self", "Magic", "Combat", "Weapon", "Weapons", "of", "the", "Handed" };
        public static string Short(string spell)
        {
            var w = spell.Split(' ').ToList();
            string tier = "";
            if (w.Count > 1 && (w[0] == "Minor" || w[0] == "Major" || w[0] == "Epic" || w[0] == "Legendary")) { tier = w[0] == "Legendary" ? "Leg" : w[0] == "Minor" ? "Min" : w[0] == "Major" ? "Maj" : "Epic"; w.RemoveAt(0); }
            var keep = w.Where(x => !Filler.Contains(x)).Select(x => x.Length > 4 ? x.Substring(0, 3) : x).Take(2).ToList();
            if (keep.Count == 0 && w.Count > 0) keep.Add(w[0].Length > 4 ? w[0].Substring(0, 3) : w[0]);
            return (tier != "" ? tier + " " : "") + string.Join(" ", keep.ToArray());
        }

        // Why this piece is in the suit: its set, its most valuable spell (that works), armor level, gear ratings.
        static string Why(Optimizer opt, SuitScore s, Verdict v)
        {
            var it = v.Item; var parts = new List<string>();
            if (it.Int(265) > 0)
            {
                int n = 0; foreach (var kv in s.MagicOf) if (kv.Key.Item.Int(265) == it.Int(265)) n += kv.Key.Item.SetWeight;
                if (n > 0) parts.Add(Tables.SetName(it.Int(265)) + " (" + n + ")");   // a leveled rare at level 0 adds nothing to its set
            }
            Magic m; if (!s.MagicOf.TryGetValue(v, out m)) m = v.Magic;
            if (m == Magic.On || m == Magic.WithCrystal)
            {
                SpellInfo bestSp = null; double bestVal = -1;
                foreach (int id in it.OwnSpells)
                {
                    var sp = Tables.Spell(id); if (sp == null || TierOf(sp.Name) == 0) continue;
                    bool modeled = opt.CombatProfile && sp.IsSkill && (sp.StatKey == opt.MainSkillId || sp.StatKey == 6);
                    double val = TierOf(sp.Name) * 10 + (modeled ? 50 : 0) + opt.SpellPoints(sp);
                    if ((sp.StatType & 0x80) != 0) val += 30;   // Armor cantrip
                    if (val > bestVal) { bestVal = val; bestSp = sp; }
                }
                if (bestSp != null) parts.Add(Short(bestSp.Name));
            }
            if (v.BuffedAL > 0 && it.Coverage != 0 && parts.Count < 2) parts.Add("AL " + v.BuffedAL);
            string[] rn = { "Dmg", "DmgRes", "Crit", "CritRes", "CritDmg", "CritDmgRes", "Heal", "Neth", "", "Vit" };
            for (int k = 370; k <= 379 && parts.Count < 3; k++) if (rn[k - 370] != "" && it.Int(k) != 0) parts.Add(rn[k - 370] + " " + it.Int(k));
            return string.Join(", ", parts.ToArray());
        }
        static string TabOf(long mask)
        {
            if ((mask & Optimizer.SigilSlots) != 0) return "Aetheria";
            if ((mask & (0x1 | 0x20 | 0x100 | 0x7E00)) != 0) return "Armor";
            if ((mask & (0xDE | 0x8000000)) != 0) return "Clothing";
            return "Jewelry";
        }

        // only = plan just this character (quick refresh after an equip change); the HTML is then not needed.
        public static string Build(string server, List<Character> chars, List<Chest> chests, Weights weights, string only = null)
        {
            var all = chars.SelectMany(c => c.Items).Concat(chests.SelectMany(c => c.Items)).ToList();
            var crystal = chars.Where(c => c.ScholarsCrystals > 0).Select(c => c.Name + " (" + c.ScholarsCrystals + ")").ToList();
            bool crystalOwned = crystal.Count > 0;

            var sb = new StringBuilder();
            sb.Append("<!doctype html><html lang=\"en\"><head><meta charset=\"utf-8\"><meta name=\"viewport\" content=\"width=device-width,initial-scale=1\">");
            sb.Append("<title>Character Gear - " + H(server) + "</title><style>").Append(Css).Append(PaperDoll.Css).Append("</style></head><body><main>");
            sb.Append("<h1>Character Gear <span class=\"sub\">" + H(server) + "</span></h1>");
            sb.Append("<p class=\"meta\">Generated " + DateTime.Now.ToString("yyyy-MM-dd HH:mm") + " from " + chars.Count + " scanned character(s)" + (chests.Count > 0 ? " and " + chests.Count + " house chest(s)" : "") + ", " + all.Count + " gear pieces.</p>");
            sb.Append("<div class=\"note\"><b>Based on level 7 self spells</b> (+40 to every attribute and skill) plus the cantrips each character is wearing right now. ");
            sb.Append("Item magic switches on only if every activation requirement passes <i>when you wield the piece</i>; it then stays on until you take it off or its mana runs out. ");
            sb.Append(crystalOwned ? "Scholar's Crystal (+250 Arcane Lore, stacks with buffs and cantrips) owned by: <b>" + H(string.Join(", ", crystal.ToArray())) + "</b>." : "No Scholar's Crystal found on any scanned character.");
            sb.Append(" Armor shows <b>base AL</b> (spells removed) and <b>buffed AL</b> (base + level 7 Impenetrability 220, or the piece's own stronger spell, + its own Impenetrability cantrip).</div>");

            sb.Append("<h2>Characters</h2><table class=\"chars\"><tr><th>Character</th><th>Level</th><th>Gear held</th><th>Scanned</th></tr>");
            foreach (var c in chars) sb.Append("<tr><td><a href=\"#c-" + Anchor(c.Name) + "\">" + H(c.Name) + "</a></td><td>" + c.Level + "</td><td>" + c.Items.Count + "</td><td>" + H(c.ScannedAt.Replace('T', ' ')) + "</td></tr>");
            sb.Append("</table>");
            if (chests.Count > 0)
            {
                sb.Append("<h2>House chests</h2><p class=\"meta\">Saved when the chest was last opened - open it again to refresh. Gear seen in two places counts only where it was seen last.</p>");
                sb.Append("<table class=\"chars\"><tr><th>Chest</th><th>Gear held</th><th>Last opened</th><th>Opened by</th></tr>");
                foreach (var ch in chests) sb.Append("<tr><td>" + H(ch.Label) + (ch.Complete ? "" : " <span class=\"tag\">IDs incomplete</span>") + "</td><td>" + ch.Items.Count + "</td><td>" + H(ch.SeenAt.Replace('T', ' ')) + "</td><td>" + H(ch.OpenedBy) + "</td></tr>");
                sb.Append("</table>");
            }
            sb.Append("<label class=\"filter\"><input type=\"checkbox\" id=\"onlyOk\" onchange=\"document.body.classList.toggle('only',this.checked)\"> Show only pieces the character can wield</label>");

            foreach (var c in chars)
            {
                if (only != null && c.Name != only) continue;
                var rules = new Rules(c, crystalOwned);
                sb.Append("<section id=\"c-" + Anchor(c.Name) + "\"><h2>" + H(c.Name) + " <span class=\"sub\">level " + c.Level + (string.IsNullOrEmpty(c.Race) ? "" : " " + H(c.Race)) + "</span></h2>");
                if (c.Vitae > 0) sb.Append("<p class=\"warn\">Vitae " + c.Vitae + " - the server checks skills after the death penalty; numbers below ignore it.</p>");
                sb.Append("<details><summary>" + H(c.Name) + "'s skills used for the checks</summary><table class=\"skills\"><tr><th>Skill</th><th>Training</th><th>Base</th><th>Now (at scan)</th><th>Level 7 + worn cantrips (used below)</th></tr>");
                foreach (var s in c.Skills.Values.Where(s => rules.TrainingRank(s.Id) >= 2).OrderBy(s => s.Name))
                    sb.Append("<tr><td>" + H(rules.SkillName(s.Id)) + "</td><td>" + H(s.Training) + "</td><td>" + s.Base + "</td><td>" + s.Buffed + "</td><td><b>" + rules.Skill(s.Id) + "</b></td></tr>");
                sb.Append("<tr><td>Arcane Lore with Scholar's Crystal</td><td></td><td></td><td></td><td><b>" + rules.Skill(14, true) + "</b></td></tr>");
                sb.Append("</table></details>");

                var verdicts = all.Select(i => rules.Judge(i)).ToList();
                int usable = verdicts.Count(v => v.Usable);
                sb.Append("<p class=\"meta\">" + usable + " of " + verdicts.Count + " pieces wieldable.</p>");
                SuitSection(sb, c, rules, weights, verdicts);
                SetsSection(sb, c, verdicts);
                foreach (var g in verdicts.GroupBy(v => Group(v.Item)).OrderBy(g => g.Key))
                {
                    sb.Append("<h3>" + H(g.Key.Substring(2)) + "</h3><div class=\"scroll\"><table><tr><th>Item</th><th>Held by</th><th>Slots</th><th>AL base / buffed</th><th>Set</th><th>Stats</th><th>Can wield?</th><th>Magic</th><th>Spells</th></tr>");
                    foreach (var v in g.OrderByDescending(v => v.Usable).ThenByDescending(v => v.BuffedAL).ThenBy(v => v.Item.Display))
                    {
                        var it = v.Item;
                        string held = it.Holder == c.Name ? "<b>" + H(it.Holder) + "</b>" : H(it.Holder);
                        held += " <span class=\"where\">" + H(it.Where == "equipped" ? "worn" : it.Where == "sidepack" ? "pack: " + it.Pack : "main pack") + "</span>";
                        if (it.Int(114) >= 1) held += " <span class=\"tag\">attuned: only " + H(it.Holder) + " can use it</span>";
                        string wield = v.Wield.Count == 0 ? "<span class=\"ok\">Yes</span>" :
                            v.WieldOk ? "<span class=\"ok\" title=\"" + H(Join(v.Wield)) + "\">Yes</span>" :
                            "<span class=\"no\">No</span><br><small>" + H(string.Join("; ", v.Wield.Where(x => !x.Ok).Select(x => x.Text).ToArray())) + "</small>";
                        string magic;
                        switch (v.Magic)
                        {
                            case Magic.On: magic = "<span class=\"ok\" title=\"" + H(Join(v.MagicChecks)) + "\">Works</span>"; break;
                            case Magic.WithCrystal: magic = "<span class=\"mid\">Use Scholar's Crystal, then wield</span><br><small>" + H(string.Join("; ", v.MagicChecks.Where(x => !x.Ok).Select(x => x.Text).ToArray())) + "</small>"; break;
                            case Magic.Off: magic = "<span class=\"no\">Won't work</span><br><small>" + H(string.Join("; ", v.MagicChecks.Where(x => !x.Ok).Select(x => x.Text).ToArray())) + "</small>"; break;
                            default: magic = "<span class=\"dim\">-</span>"; break;
                        }
                        if (v.Magic == Magic.Off && it.Int(265) > 0) magic += "<br><small class=\"setnote\">Set bonus still counts (set spells ignore Arcane Lore and mana)</small>";
                        if (v.ActiveNow == false && v.Magic != Magic.Off) magic += "<br><span class=\"mid\">Off right now</span> <small>(was put on before you met the requirement; take it off and put it back on while buffed)</small>";
                        else if (v.ActiveNow == true) magic += " <small>(on now)</small>";
                        string al = v.BaseAL >= 0 ? v.BaseAL + " / <b>" + v.BuffedAL + "</b>" : "";
                        string spells = string.Join(", ", it.OwnSpells.Select(id => Tables.SpellName(id)).ToArray());
                        sb.Append("<tr class=\"" + (v.Usable ? "y" : "n") + "\"><td>" + H(it.Display) + "</td><td>" + held + "</td><td>" + H(SlotText(it.ValidLocations)) + "</td><td class=\"num\">" + al + "</td><td>" + (it.Int(265) > 0 ? H(Tables.SetName(it.Int(265))) : "") + "</td><td class=\"stats\">" + H(Stats(it)) + "</td><td>" + wield + "</td><td>" + magic + "</td><td class=\"spells\">" + H(spells) + "</td></tr>");
                    }
                    sb.Append("</table></div>");
                }
                sb.Append("</section>");
            }
            sb.Append("</main></body></html>");
            return sb.ToString();
        }

        // Milestone 3: recommended suit vs current suit.
        static void SuitSection(StringBuilder sb, Character c, Rules rules, Weights weights, List<Verdict> verdicts)
        {
            var opt = new Optimizer(c, rules, weights, verdicts);
            List<KeyValuePair<string, long>> warm; if (Warm.TryGetValue(c.Name, out warm)) opt.Warm = warm;
            var cur = opt.Current(); var curWeapon = opt.CurrentWeapon(); var curOff = opt.CurrentOffHand(); opt.Weapon = curWeapon; opt.OffHand = curOff; var cs = opt.Score(cur);
            SuitScore bs; var best = opt.Best(out bs); var bestWeapon = opt.Weapon; var bestOff = bs.OffHand;
            RecSuit[c.Name] = string.Join(",", best.Select(b => b.V.Item.Id + ":" + b.Mask).ToArray());
            sb.Append("<h3>Recommended suit <span class=\"sub\">profile: " + H(opt.ProfileSkillName) + (opt.ProfileOverridden ? " (set by you)" : " (detected from trained skills)") + "</span></h3>");
            sb.Append("<p class=\"meta\">Change it in data/CharacterGear_profiles.tsv (e.g. <i>war+summoning</i>, <i>void+summoning</i>, <i>heavy+dualwield</i>, <i>tinker</i>) and run the planner again.</p>");
            sb.Append("<p class=\"meta\">Weapon or wand, armor, underclothes, jewelry, cloak and trinket. Score now <b>" + Math.Round(cs.Total) + "</b> &rarr; recommended <b>" + Math.Round(bs.Total) + "</b> (" + (bs.Total >= cs.Total ? "+" : "") + Math.Round(bs.Total - cs.Total) + "). Weights: data/CharacterGear_weights.tsv (editable).</p>");
            PaperDoll.Render(sb, c.Name, cur, curWeapon, best, bestWeapon);
            sb.Append("<div class=\"scroll\"><table class=\"suit\"><tr><th>Slot</th><th>Recommended</th><th>Held by</th><th>AL buffed</th><th>Set</th><th>Magic</th><th>What to do</th></tr>");
            var handover = new List<string>();
            if (bestWeapon != null)
            {
                var wi = bestWeapon.Item; var wt = new List<string>();
                if (wi.Holder != c.Name) { wt.Add("get it from " + wi.Holder); handover.Add(wi.Display + " from " + wi.Holder); }
                else if (wi.Where != "equipped") wt.Add("wield it (in your " + (wi.Where == "sidepack" ? "pack " + wi.Pack : "main pack") + ")");
                if (bestWeapon.Magic == Magic.WithCrystal) wt.Add("use Scholar's Crystal first, then wield");
                if (bestWeapon.Magic == Magic.Off) wt.Add("its own magic won't work");
                AddRec(c.Name, "Weapons", "Best overall", wi.Display, wi.Holder, wt.Count == 0 ? "already wielded" : string.Join("; ", wt.ToArray()), "dmg " + Math.Round(bs.Weapon.Expected * bs.Weapon.HitChance, 1) + ", def " + Math.Round(bs.Weapon.DefenseMod, 2));
                sb.Append("<tr><td>Weapon</td><td><b>" + H(wi.Display) + "</b></td><td>" + H(wi.Holder) + "</td><td></td><td>" + (wi.Int(265) > 0 ? H(Tables.SetName(wi.Int(265))) : "") + "</td><td>" + (bestWeapon.Magic == Magic.On ? "Works" : bestWeapon.Magic == Magic.WithCrystal ? "With crystal" : bestWeapon.Magic == Magic.Off ? "Won't work" : "-") + "</td><td>" + (wt.Count == 0 ? "<span class=\"dim\">already wielded</span>" : H(string.Join("; ", wt.ToArray()))) + "</td></tr>");
            }
            else sb.Append("<tr><td>Weapon</td><td colspan=\"6\" class=\"dim\">No wieldable " + H(opt.MainSkillName) + " weapon found among scanned characters</td></tr>");
            // Off hand: shield or second weapon (only where the server allows one beside the main weapon)
            if (bestOff != null)
            {
                var oi = bestOff.Item; var ot = new List<string>();
                if (oi.Holder != c.Name) { ot.Add("get it from " + oi.Holder); handover.Add(oi.Display + " from " + oi.Holder); }
                else if (oi.Where != "equipped") ot.Add("wield it in the off hand (in your " + (oi.Where == "sidepack" ? "pack " + oi.Pack : oi.InChest ? "chest" : "main pack") + ")");
                Magic om; if (!bs.MagicOf.TryGetValue(bestOff, out om)) om = bestOff.Magic;
                if (om == Magic.WithCrystal) ot.Add("use Scholar's Crystal first, then wield");
                if (om == Magic.Off) ot.Add("its own magic won't work");
                string kind = (oi.ValidLocations & 0x100000) != 0 ? "Off hand (dual wield)" : "Off hand (shield)";
                AddRec(c.Name, "Weapons", kind, oi.Display, oi.Holder, ot.Count == 0 ? "already wielded" : string.Join("; ", ot.ToArray()), Why(opt, bs, bestOff));
                sb.Append("<tr><td>" + kind + "</td><td><b>" + H(oi.Display) + "</b></td><td>" + H(oi.Holder) + "</td><td class=\"num\">" + (bestOff.BuffedAL > 0 ? bestOff.BuffedAL.ToString() : "") + "</td><td>" + (oi.Int(265) > 0 ? H(Tables.SetName(oi.Int(265))) : "") + "</td><td>" + (om == Magic.On ? "Works" : om == Magic.WithCrystal ? "With crystal" : om == Magic.Off ? "Won't work" : "-") + "</td><td>" + (ot.Count == 0 ? "<span class=\"dim\">already wielded</span>" : H(string.Join("; ", ot.ToArray()))) + "</td></tr>");
            }
            else
            {
                string why = opt.OffHandReason();
                AddRec(c.Name, "Weapons", "Off hand", "(none)", "", why, "");
                sb.Append("<tr><td>Off hand</td><td colspan=\"6\" class=\"dim\">" + H(why) + "</td></tr>");
            }
            foreach (var ch in best.OrderBy(x => Optimizer.SlotOrder(x.Mask)))
            {
                var v = ch.V; var it = v.Item;
                var todo = new List<string>();
                bool mine = it.Holder == c.Name;
                bool wornHere = mine && it.Where == "equipped";
                if (!mine) { todo.Add("get it from " + it.Holder + (it.Where == "equipped" ? " (they wear it now)" : "")); handover.Add(it.Display + " from " + it.Holder + (it.Where == "equipped" ? " (worn by " + it.Holder + ")" : "")); }
                else if (!wornHere) todo.Add("wear it (in your " + (it.Where == "sidepack" ? "pack " + it.Pack : "main pack") + ")");
                Magic sm; if (!bs.MagicOf.TryGetValue(v, out sm)) sm = v.Magic;
                if (sm == Magic.WithCrystal) todo.Add("use Scholar's Crystal first, then wield");
                if (v.ActiveNow == false && sm == Magic.On) todo.Add("take off and put back on while buffed" + (v.Magic != Magic.On ? ", after the pieces that raise your Arcane Lore" : "") + " (magic is off now)");
                else if (sm == Magic.On && v.Magic == Magic.Off && it.Holder == c.Name) todo.Add("put it on after the pieces that raise your Arcane Lore");
                if (sm == Magic.Off || sm == Magic.None)
                {
                    var why = new List<string>();
                    if (it.Int(265) > 0) why.Add("set bonus");
                    if (v.BuffedAL > 0) why.Add("armor level");
                    string[] rn = { "Dmg", "Dmg resist", "Crit", "Crit resist", "Crit dmg", "Crit dmg resist", "Heal boost", "Nether resist", "", "Vitality" };
                    for (int k = 370; k <= 379; k++) if (rn[k - 370] != "" && it.Int(k) != 0) why.Add(rn[k - 370] + " rating " + it.Int(k));
                    if (why.Count > 0) todo.Add("worn for its " + string.Join(" + ", why.ToArray()) + (sm == Magic.Off ? "; its own magic won't work" : ""));
                    else if (sm == Magic.Off) todo.Add("its own magic won't work");
                }
                string magic = sm == Magic.On ? "Works" : sm == Magic.WithCrystal ? "With crystal" : sm == Magic.Off ? "Won't work" : "-";
                if ((ch.Mask & Optimizer.SigilSlots) != 0 && !opt.SigilFlagKnown && it.Where != "equipped") todo.Add("needs the " + Optimizer.SlotOf(ch.Mask).ToLower() + " slot unlocked (quest)");
                AddRec(c.Name, TabOf(ch.Mask), Optimizer.SlotOf(ch.Mask), it.Display, it.Holder, todo.Count == 0 ? "already worn" : string.Join("; ", todo.ToArray()), Why(opt, bs, v));
                sb.Append("<tr><td>" + H(Optimizer.SlotOf(ch.Mask)) + "</td><td><b>" + H(it.Display) + "</b></td><td>" + H(it.Holder) + "</td><td class=\"num\">" + (v.BuffedAL > 0 ? v.BuffedAL.ToString() : "") + "</td><td>" + (it.Int(265) > 0 ? H(Tables.SetName(it.Int(265))) : "") + "</td><td>" + magic + "</td><td>" + (todo.Count == 0 ? "<span class=\"dim\">already worn</span>" : H(string.Join("; ", todo.ToArray()))) + "</td></tr>");
            }
            sb.Append("</table></div>");
            var freeSlots = new List<string>();
            long used = best.Aggregate(0L, (m, x) => m | x.Mask);
            foreach (var fs in new[] { new { M = 0x8000L, N = "Neck" }, new { M = 0x10000L, N = "Left wrist" }, new { M = 0x20000L, N = "Right wrist" }, new { M = 0x40000L, N = "Left ring" }, new { M = 0x80000L, N = "Right ring" }, new { M = 0x4000000L, N = "Trinket" }, new { M = 0x8000000L, N = "Cloak" }, new { M = 0x1L, N = "Head" }, new { M = 0x20L, N = "Hands" }, new { M = 0x100L, N = "Feet" } })
                if ((used & fs.M) == 0) freeSlots.Add(fs.N);
            CurrentRows(c, opt, cs, bs, cur, curWeapon, best, bestWeapon);
            SuitRows(c, opt, cs, bs, cur, curWeapon, best, bestWeapon);
            RecHead[c.Name] = new[] { opt.ProfileSkillName, Math.Round(cs.Total).ToString(), Math.Round(bs.Total).ToString(), Math.Round(bs.DamageTakenPct, 2) + "%", Math.Round(bs.DamageDealtPct * 0.00875, 1).ToString() };
            foreach (var f in freeSlots) AddRec(c.Name, (f == "Head" || f == "Hands" || f == "Feet") ? "Armor" : f == "Cloak" ? "Clothing" : "Jewelry", f, "(free - keep yours)", "", "nothing scanned helps here");
            // Aetheria: a row per slot that has no sigil in the recommended suit, saying why.
            for (int si = 0; si < 3; si++)
            {
                long bit = 0x10000000L << si;
                if ((used & bit) != 0) continue;
                var probe = new Item { ValidLocations = bit };
                string why = !opt.SigilSlotOpen(probe) ? (opt.SigilFlagKnown ? "slot not unlocked yet (quest)" : "slot opens at level " + new[] { 75, 150, 225 }[si] + " (quest)")
                    : verdicts.Any(v => (v.Item.ValidLocations & bit) != 0) ? "no " + Optimizer.SigilColor[si].ToLower() + " sigil scanned that helps" : "no " + Optimizer.SigilColor[si].ToLower() + " Aetheria collected yet";
                AddRec(c.Name, "Aetheria", Optimizer.SigilColor[si] + " sigil", "(none)", "", why, "");
            }
            if (freeSlots.Count > 0) sb.Append("<p class=\"meta\"><b>Free slots:</b> " + H(string.Join(", ", freeSlots.ToArray())) + " - nothing scanned helps " + H(c.Name) + " there (pieces whose magic won't switch on and that are in no set add nothing). Wear anything you like.</p>");
            if (handover.Count > 0) sb.Append("<p><b>Hand-over list for " + H(c.Name) + ":</b> " + H(string.Join(" | ", handover.ToArray())) + "</p>");

            Wishlist(sb, c, opt, bs, verdicts, best);
            EssenceSection(sb, c, rules, verdicts);

            // Best weapon per damage type (switch by what you fight); rending marked.
            var picks = opt.BestByElement(best);
            sb.Append("<h3>Best " + H(opt.MainSkillName) + " weapon for each damage type</h3><p class=\"meta\">Scored in the recommended suit. A rend that matches the weapon's element works like a vulnerability (up to x2.5 at high base skill), so rended weapons usually win. Ranked like the main pick: damage AND the weapon's defense bonus (it raises your evade chance). Damage = expected damage per attack incl. hit chance, crits and ratings, against a monster with no special weakness except the rend.</p>");
            sb.Append("<p class=\"meta\">Stars: <b>*</b> low &nbsp; <b>**</b> medium &nbsp; <b>***</b> max (ward strength for that type; blank = none).</p>");
            sb.Append("<div class=\"scroll\"><table><tr><th>Damage type</th><th>Best</th><th>Held by</th><th>Rend</th><th>Damage / attack</th><th>Defense mod</th><th>Runner-up</th></tr>");
            foreach (var pk in picks)
            {
                if (pk.Best == null) AddRec(c.Name, "Weapons", pk.Element, "(none collected yet)", "", "");
                else AddRec(c.Name, "Weapons", pk.Element, pk.Best.Item.Display, pk.Best.Item.Holder, pk.Best.Item.Holder == c.Name ? (pk.Best.Item.Where == "equipped" ? "wielded now" : "in your pack") : "get it from " + pk.Best.Item.Holder, (pk.BestStats.Rending > 1 ? "REND x" + Math.Round(pk.BestStats.Rending, 2) + ", " : "") + "dmg " + Math.Round(pk.BestDmg, 1));
                if (pk.Best == null) { sb.Append("<tr><td>" + pk.Element + "</td><td colspan=\"6\" class=\"dim\">none collected yet</td></tr>"); continue; }
                string rend = pk.BestStats.Rending > 1 ? "<span class=\"ok\">Yes x" + Math.Round(pk.BestStats.Rending, 2) + "</span>" : "<span class=\"dim\">no</span>";
                sb.Append("<tr><td>" + pk.Element + "</td><td><b>" + H(pk.Best.Item.Display) + "</b></td><td>" + H(pk.Best.Item.Holder) + "</td><td>" + rend + "</td><td class=\"num\">" + Math.Round(pk.BestDmg, 1) + "</td><td class=\"num\">" + Math.Round(pk.BestStats.DefenseMod, 2) + "</td><td>" + (pk.Second != null ? H(pk.Second.Item.Display) + " <span class=\"dim\">(" + H(pk.Second.Item.Holder) + ", dmg " + Math.Round(pk.SecondDmg, 1) + ", def " + Math.Round(pk.SecondStats.DefenseMod, 2) + ")</span>" : "<span class=\"dim\">-</span>") + "</td></tr>");
            }
            sb.Append("</table></div>");

            ElementSection(sb, c, opt, cs, bs, best, verdicts);

            sb.Append("<div class=\"scroll\"><table class=\"cmp\"><tr><th></th><th>Now</th><th>Recommended</th></tr>");
            Row(sb, "Score (defense + offense + skills/stats + sets + other)", Parts(cs), Parts(bs));
            Row(sb, "Damage you take, scored mix (% of unarmored, unbuffed, unrated)", Math.Round(cs.DamageTakenPct, 2) + "%", Math.Round(bs.DamageTakenPct, 2) + "%");
            Row(sb, "Expected damage per attack (incl. hit chance, ratings, crits)", Math.Round(cs.DamageDealtPct * 0.00875, 1).ToString(), Math.Round(bs.DamageDealtPct * 0.00875, 1).ToString());
            Row(sb, "Weapon", H(opt.WeaponLabel(curWeapon)), H(opt.WeaponLabel(bestWeapon)));
            Row(sb, "Off hand", H(cs.OffHand != null ? opt.WeaponLabel(cs.OffHand) : "none") + (cs.ShieldNote != "" ? "<br><small>" + H(cs.ShieldNote) + "</small>" : ""), H(bs.OffHand != null ? opt.WeaponLabel(bs.OffHand) : "none") + (bs.ShieldNote != "" ? "<br><small>" + H(bs.ShieldNote) + "</small>" : ""));
            Row(sb, opt.MainSkillName + " / Melee Defense (level 7 + this suit)", Math.Round(cs.MainSkill) + " / " + Math.Round(cs.MeleeD), Math.Round(bs.MainSkill) + " / " + Math.Round(bs.MeleeD));
            Row(sb, "Hit chance / evade chance (vs. assumed monster)", Math.Round(cs.Weapon.HitChance * 100) + "% / " + Math.Round(cs.Weapon.EvadeChance * 100) + "%", Math.Round(bs.Weapon.HitChance * 100) + "% / " + Math.Round(bs.Weapon.EvadeChance * 100) + "%");
            Row(sb, "Weapon: max dmg, crit chance, crit x, attack / defense mod", WStats(cs.Weapon), WStats(bs.Weapon));
            Row(sb, "Arcane Lore (level 7 + this suit) - decides which pieces' magic works", Math.Round(cs.Arcane).ToString(), Math.Round(bs.Arcane).ToString());
            Row(sb, "Armor Self on every part (level 7 + Armor cantrips)", "+" + cs.BodyArmor, "+" + bs.BodyArmor);
            Row(sb, "Piece AL, hit-weighted average", Math.Round(cs.AvgAL).ToString(), Math.Round(bs.AvgAL).ToString());
            for (int i = 0; i < 9; i++) Row(sb, "&nbsp;&nbsp;" + Optimizer.PartNames[i] + " <span class=\"dim\">(hit " + Math.Round(Optimizer.PartHit[i] * 100) + "%)</span>", cs.PartAL[i].ToString(), bs.PartAL[i].ToString(), true);
            Row(sb, "Set bonuses", H(string.Join("; ", cs.SetLines.ToArray())), H(string.Join("; ", bs.SetLines.ToArray())), true);
            Row(sb, "Spells counted (points)", H(string.Join(", ", cs.SpellLines.ToArray())), H(string.Join(", ", bs.SpellLines.ToArray())), true);
            Row(sb, "Ratings", H(string.Join(", ", cs.RatingLines.ToArray())), H(string.Join(", ", bs.RatingLines.ToArray())), true);
            sb.Append("</table></div>");
        }
        // Damage taken per damage type, and the suit that is best against each one (only the swaps are shown).
        // Ward strength vs Legendary (damage cut Minor 10 / Major 15 / Epic 20 / Legendary 25% = 40/60/80/100%):
        // blank none, * low (<=50%: Minor), ** medium (50-90%: Major, Epic), *** max (Legendary).
        static string SlotFor(Verdict v, List<Choice> best, Optimizer opt, SuitScore bs)
        {
            if (v == null) return "";
            if (opt.Weapon != null && v.Item.Id == opt.Weapon.Item.Id) return "Weapon";
            if (bs.OffHand != null && v.Item.Id == bs.OffHand.Item.Id) return "Off hand";
            var ch = best.FirstOrDefault(b => b.V.Item.Id == v.Item.Id);
            return ch != null ? Optimizer.SlotOf(ch.Mask).Split(',')[0] : "";
        }
        static string WhyNotUsed(Verdict v, Optimizer opt)
        {
            if (!v.Movable) return "attuned to " + v.Item.Holder;
            if (!v.WieldOk) return "can't wield: " + string.Join(", ", v.Wield.Where(x => !x.Ok).Select(x => x.Text).ToArray());
            if (v.Magic == Magic.Off) return "needs " + string.Join(", ", v.MagicChecks.Where(x => !x.Ok).Select(x => x.Text).ToArray());
            if (v.Magic == Magic.WithCrystal) return "needs Scholar's Crystal";
            if ((v.Item.ValidLocations & 0x200000) != 0 && (v.Item.ValidLocations & 0x100000) == 0 && !opt.OffHandAllowed(opt.Weapon, v)) return "shield - no free hand";
            if ((v.Item.ValidLocations & 0x03F00000) != 0) return "on a weapon - your main weapon wins";
            return "costs more than it gains";
        }
        // header + legend block for the coverage tabs (blank rows around the legend so it stands out)
        static void CoverageHead(string who, string tab, string legendDetail, string covLabel, string covText, string covDetail, string first)
        {
            AddRec(who, tab, "", "", "", "", "");
            AddRec(who, tab, "Legend", "*  low      **  medium      ***  max", "", legendDetail, " ");
            AddRec(who, tab, "", "", "", "", "");
            AddRec(who, tab, covLabel, covText, "", covDetail, " ");
            AddRec(who, tab, "", "", "", "", "");
            AddRec(who, tab, "[" + first + "]", "[In your suit]", "[Better one you own]", "Columns: " + first + " | in your suit | better one you own | why it isn't used", "[Why it isn't used]");
        }

        static string Stars(int tier) { return tier >= 4 ? "***" : tier >= 2 ? "**" : tier == 1 ? "*" : ""; }

        static void ElementSection(StringBuilder sb, Character c, Optimizer opt, SuitScore cs, SuitScore bs, List<Choice> best, List<Verdict> verdicts)
        {
            sb.Append("<h3>Damage you take by type</h3><p class=\"meta\">% of a hit or spell that LANDS, compared with an unarmored, unbuffed character (evade not counted here). Melee/missile hits: each piece's AL x its protection vs that type " +
                "(level 7 bane +1.7 and the piece's own bane cantrip, capped at 2.0 like the server), Armor Self, level 7 protection (x0.35) and ward cantrips (Minor x0.9 ... Legendary x0.75). " +
                "Spells: armor does NOT help (ACE: only protections, wards and ratings), so the planner counts half of each element's damage as spells (weight spell_share). " +
                "Level 7 banes push most armor to the cap, so the differences come mostly from <b>ward cantrips</b> and from clothing with low protection. The suit itself is scored with every damage type counting the same (end goal: Legendary ward coverage for all of them; weights dmg_slash ... dmg_lightning). " +
                "<i>Suit for this type</i> starts from the recommended suit and swaps only what helps against that type. Nether is left out (no bane exists).</p>");
            var missing = new List<string>(); int legend = 0;
            for (int e = 0; e < Elements.Count; e++) { if (bs.Ward[e] <= 0.75 + 1e-9) legend++; else missing.Add(Elements.Name[e] + (bs.Ward[e] < 1 ? " (" + (bs.Ward[e] <= 0.8 + 1e-9 ? "Epic" : bs.Ward[e] <= 0.85 + 1e-9 ? "Major" : "Minor") + ")" : " (none)")); }
            sb.Append("<p><b>Legendary ward coverage in the recommended suit: " + legend + " of " + Elements.Count + "</b>" + (missing.Count > 0 ? " - missing: " + H(string.Join(", ", missing.ToArray())) + ". The Wishlist shows where your best copy of each sits." : " - complete.") + "</p>");
            CoverageHead(c.Name, "Elements", "Stars = ward strength for that damage type: blank none, * low, ** medium, *** max.",
                "Coverage", legend + " of " + Elements.Count + " damage types have a Legendary ward", missing.Count > 0 ? "missing: " + string.Join(", ", missing.ToArray()) : "complete", "Damage type");
            sb.Append("<div class=\"scroll\"><table><tr><th>Damage type</th><th>Now</th><th>Recommended</th><th>Suit for this type</th><th>Swaps for that suit</th></tr>");
            for (int e = 0; e < Elements.Count; e++)
            {
                opt.SetMix(e);
                SuitScore es; var suit = opt.Adjust(best, out es);
                var added = suit.Where(x => !best.Any(b => b.V.Item.Id == x.V.Item.Id)).OrderBy(x => Optimizer.SlotOrder(x.Mask)).ToList();
                var removed = best.Where(b => !suit.Any(x => x.V.Item.Id == b.V.Item.Id)).ToList();
                bool better = es.TakenBy[e] < bs.TakenBy[e] * 0.98 && (added.Count > 0 || removed.Count > 0);   // show swaps only for 2%+ less damage
                var swaps = new List<string>();
                if (better)
                {
                    foreach (var a in added)
                    {
                        var out1 = removed.Where(b => (b.Mask & a.Mask) != 0 || Optimizer.ClashItems(a.V.Item, a.Mask, b.V.Item, b.Mask)).Select(b => b.V.Item.Display).ToList();
                        swaps.Add(Optimizer.SlotOf(a.Mask) + ": " + a.V.Item.Display + " (" + a.V.Item.Holder + ")" + (out1.Count > 0 ? " instead of " + string.Join(" + ", out1.ToArray()) : ""));
                    }
                    foreach (var b in removed.Where(b => !added.Any(a => (b.Mask & a.Mask) != 0 || Optimizer.ClashItems(a.V.Item, a.Mask, b.V.Item, b.Mask))))
                        swaps.Add(Optimizer.SlotOf(b.Mask) + ": take off " + b.V.Item.Display);
                }
                string ward = bs.Ward[e] < 1 ? " <span class=\"dim\">(ward x" + bs.Ward[e] + ")</span>" : "";
                int sT = bs.Ward[e] <= 0.75 + 1e-9 ? 4 : bs.Ward[e] <= 0.8 + 1e-9 ? 3 : bs.Ward[e] <= 0.85 + 1e-9 ? 2 : bs.Ward[e] < 1 ? 1 : 0;
                sb.Append("<tr><td>" + Elements.Name[e] + " <b>" + Stars(sT) + "</b></td><td class=\"num\">" + Math.Round(cs.TakenBy[e], 2) + "%</td><td class=\"num\">" + Math.Round(bs.TakenBy[e], 2) + "%" + ward + "</td><td class=\"num\">" +
                    (better ? "<b>" + Math.Round(es.TakenBy[e], 2) + "%</b>" : "<span class=\"dim\">same</span>") + "</td><td>" + (better ? H(string.Join("; ", swaps.ToArray())) : "<span class=\"dim\">recommended suit is already best</span>") + "</td></tr>");
                // Window row = ward coverage, plainly: what the recommended suit has for this type (and from which piece),
                // and the best ward of this type you own anywhere. Damage numbers and swaps go to the click line.
                int key = Elements.ResistKey[e];
                Func<int, bool> isWard = id => { var sp = Tables.Spell(id); return sp != null && sp.StatKey == key && sp.Category.Contains("Ward") && TierOf(sp.Name) > 0; };
                var suitPieces = best.Select(b => b.V).ToList();
                if (opt.Weapon != null) suitPieces.Add(opt.Weapon);
                if (bs.OffHand != null) suitPieces.Add(bs.OffHand);
                Verdict fromV = null; int haveT = 0;
                foreach (var v in suitPieces)
                {
                    Magic mg; if (!bs.MagicOf.TryGetValue(v, out mg)) mg = v.Magic;
                    if (mg == Magic.Off) continue;
                    foreach (int id in v.Item.OwnSpells.Where(isWard)) { int tt = TierOf(Tables.Spell(id).Name); if (tt > haveT) { haveT = tt; fromV = v; } }
                }
                Verdict ownV = null; int ownT = 0;
                foreach (var v in verdicts)
                    foreach (int id in v.Item.OwnSpells.Where(isWard)) { int tt = TierOf(Tables.Spell(id).Name); if (tt > ownT || (tt == ownT && ownV != null && !ownV.Usable && v.Usable)) { ownT = tt; ownV = v; } }
                string now = haveT > 0 ? Tiers[haveT - 1] + " - " + SlotFor(fromV, best, opt, bs) + ": " + fromV.Item.Display : "none";
                string bestTxt, betterCol, whyCol;
                if (haveT >= 4) { bestTxt = "done (Legendary)"; betterCol = "-"; whyCol = "done"; }
                else if (ownV != null && ownT > haveT)
                {
                    whyCol = WhyNotUsed(ownV, opt);
                    betterCol = Tiers[ownT - 1] + ": " + ownV.Item.Display + " (" + ownV.Item.Holder + ")";
                    bestTxt = "you own " + Tiers[ownT - 1] + ": " + ownV.Item.Display + " (" + whyCol + ")";
                }
                else { betterCol = "none owned"; whyCol = "collect a Legendary one"; bestTxt = haveT > 0 ? "no better one owned - collect Legendary" : "none owned - collect one"; }
                string detail = Elements.Name[e] + ": suit has " + (haveT > 0 ? Tiers[haveT - 1] + " ward on " + fromV.Item.Display : "no ward") + "; " + bestTxt +
                    ". Damage of a landed " + Elements.Name[e].ToLower() + " hit/spell: " + Math.Round(bs.TakenBy[e], 2) + "% of unarmored" +
                    (better ? "; a suit just for " + Elements.Name[e].ToLower() + " fights gets " + Math.Round(es.TakenBy[e], 2) + "% with: " + string.Join("; ", swaps.ToArray()) : "");
                AddRec(c.Name, "Elements", Elements.Name[e] + " " + Stars(haveT), now, betterCol, detail, whyCol);
            }
            opt.SetMix(-1);
            sb.Append("</table></div>");
        }

        static void EssenceSection(StringBuilder sb, Character c, Rules rules, List<Verdict> verdicts)
        {
            bool trained = rules.TrainingRank(54) >= 2;
            long mastery; c.Props.TryGetValue(362, out mastery);
            int known = verdicts.Count(v => Essences.Is(v.Item));
            sb.Append("<h3>Summoning essences</h3>");
            if (!trained)
            {
                sb.Append("<p class=\"meta\">Summoning isn't trained, so no essence is suggested.</p>");
                AddRec(c.Name, "Essence", "Summoning", "(not trained)", "", "Summoning isn't trained - no essences suggested", "");
                return;
            }
            string school = mastery >= 1 && mastery <= 3 ? Essences.School[mastery] : "none chosen";
            sb.Append("<p class=\"meta\">Summoning " + rules.Skill(54) + " (level 7). School: <b>" + H(school) + "</b> - a character chooses one school and can then summon only that school's essences (36 stock essences have none). " +
                "A suggestion is made only when it is an obvious upgrade: no lower summoning tier and no fewer pet ratings than the best one you hold of that damage type. " + known + " essences scanned (the collector IDs essences from this version on - rescan mules that hold them).</p>");
            AddRec(c.Name, "Essence", "School", school, "", mastery >= 1 ? "only " + school + " essences (and ones with no school)" : "no school chosen - only essences without a school requirement", "Summoning " + rules.Skill(54));
            var picks = Essences.Picks(c, verdicts, mastery);
            // Essences of your school that only your Summoning skill keeps from you (the next tier to train for).
            var blocked = verdicts.Where(v => Essences.Is(v.Item) && v.Movable && (Essences.SchoolOf(v.Item) == 0 || Essences.SchoolOf(v.Item) == mastery)
                && v.MagicChecks.Any(x => !x.Ok) && v.MagicChecks.Where(x => !x.Ok).All(x => x.Text.StartsWith("Summoning"))).ToList();
            if (blocked.Count > 0)
            {
                long next = blocked.Min(v => Essences.Tier(v.Item));
                int n = blocked.Count(v => Essences.Tier(v.Item) == next);
                sb.Append("<p class=\"meta\"><b>Next tier:</b> " + n + " essence(s) of your school need Summoning " + next + " (you: " + rules.Skill(54) + " at level 7).</p>");
                AddRec(c.Name, "Essence", "Next tier", n + " essence(s) at Summoning " + next, "", "raise Summoning to " + next + " (you: " + rules.Skill(54) + " at level 7)", "");
            }
            if (picks.Count == 0)
            {
                sb.Append("<p class=\"dim\">No essence that " + H(c.Name) + " can summon was found.</p>");
                AddRec(c.Name, "Essence", "-", "(none usable)", "", known == 0 ? "no ID'd essences yet - rescan characters that hold them" : "none of the scanned essences passes the requirements", "");
                return;
            }
            sb.Append("<div class=\"scroll\"><table><tr><th>Pet damage</th><th>Best for you</th><th>Held by</th><th>Pet</th><th>Yours now</th><th>Verdict</th></tr>");
            foreach (var pk in picks)
            {
                var b = pk.Best.Item;
                string todo = !pk.Upgrade ? pk.Reason : b.Holder == c.Name ? "in your pack" : "get it from " + b.Holder;
                sb.Append("<tr><td>" + H(pk.Damage) + "</td><td><b>" + H(b.Display) + "</b></td><td>" + H(b.Holder) + "</td><td>" + H(Essences.RatingText(b)) + "</td><td>" + (pk.Held != null ? H(pk.Held.Item.Display + " (" + Essences.RatingText(pk.Held.Item) + ")") : "<span class=\"dim\">none</span>") + "</td><td>" + (pk.Upgrade ? "<span class=\"ok\">upgrade</span> - " : "") + H(pk.Reason) + "</td></tr>");
                AddRec(c.Name, "Essence", pk.Damage, b.Display, b.Holder, (pk.Upgrade ? "* " : "") + todo, Essences.RatingText(b));
            }
            sb.Append("</table></div>");
        }

        static string WStats(WeaponStats w) { return Math.Round(w.MaxDamage, 2) + ", " + Math.Round(w.CritChance * 100) + "%, x" + Math.Round(1 + w.CritMult, 1) + ", " + Math.Round(w.AttackMod, 2) + " / " + Math.Round(w.DefenseMod, 2) + (w.Notes != "" ? "<br><small>" + H(w.Notes) + "</small>" : ""); }
        static string Parts(SuitScore s) { return Math.Round(s.Total) + " (" + Math.Round(s.Defense) + " + " + Math.Round(s.Offense) + " + " + Math.Round(s.Spells) + " + " + Math.Round(s.Sets) + " + " + Math.Round(s.Ratings) + ")"; }
        static void Row(StringBuilder sb, string label, string a, string b, bool raw = false)
        {
            sb.Append("<tr><td>" + label + "</td><td class=\"spells\">" + a + "</td><td class=\"spells\">" + b + "</td></tr>");
        }

        static readonly string[] Tiers = { "Minor", "Major", "Epic", "Legendary" };
        static int TierOf(string spellName) { for (int i = 0; i < Tiers.Length; i++) if (spellName.StartsWith(Tiers[i] + " ")) return i + 1; return 0; }
        static bool Matches(SpellInfo s, Tables.TemplateRow t)
        {
            if (s == null || TierOf(s.Name) == 0) return false;
            if (t.Kind == "ward") return !s.Additive && s.StatKey == t.Key && s.Category.Contains("Ward");
            if (!s.Additive) return false;
            if (t.Kind == "skill") return s.IsSkill && s.StatKey == t.Key;
            if (t.Kind == "attr") return s.IsAttribute && s.StatKey == t.Key;
            if (t.Kind == "armor") return (s.StatType & 0x80) != 0;
            return false;
        }

        // Template vs recommended suit: which end-game cantrips the suit has, and where a missing one already sits.
        static void Wishlist(StringBuilder sb, Character c, Optimizer opt, SuitScore bs, List<Verdict> verdicts, List<Choice> best)
        {
            var parts = opt.Profile.Split('+');
            var rules = opt.Rules;
            var rows = Tables.Templates.Where(t => (parts.Contains(t.Profile) || t.Profile == "all") && (!t.IfTrained || rules.TrainingRank(t.Key) >= 2))
                .GroupBy(t => t.Kind + ":" + t.Key).Select(g => g.First()).ToList();
            // Tinker / Crafter: Legendary for each trained tinkering/crafting skill + the attributes that feed them.
            if (parts.Contains("tinker") || parts.Contains("crafter"))
            {
                var attrs = new HashSet<int>();
                foreach (int k in Tables.TradeSkills.Where(k => rules.TrainingRank(k) >= 2))
                {
                    rows.Add(new Tables.TemplateRow { Profile = "trade", Kind = "skill", Key = k, Want = "Legendary", Label = rules.SkillName(k) + (rules.TrainingRank(k) >= 3 ? " (specialized)" : " (trained)") });
                    SkillFormula f; if (Tables.Skills.TryGetValue(k, out f)) { if (f.Attr1 > 0) attrs.Add(f.Attr1); if (f.Attr2 > 0) attrs.Add(f.Attr2); }
                }
                string[] an = { "", "Strength", "Endurance", "Quickness", "Coordination", "Focus", "Willpower (Self)" };
                foreach (int a in attrs.OrderBy(a => a)) if (!rows.Any(t => t.Kind == "attr" && t.Key == a)) rows.Add(new Tables.TemplateRow { Profile = "trade", Kind = "attr", Key = a, Want = "Legendary", Label = an[a] + " (feeds your trade skills)" });
            }
            // Every other trained / specialized skill that has cantrips ("the true picture").
            var cantripSkills = new HashSet<int>(Tables.Spells.Values.Where(s => s.Additive && s.IsSkill && TierOf(s.Name) > 0).Select(s => s.StatKey));
            foreach (var sk in c.Skills.Values.Where(s => rules.TrainingRank(s.Id) >= 2 && cantripSkills.Contains(s.Id)).OrderByDescending(s => rules.TrainingRank(s.Id)).ThenBy(s => rules.SkillName(s.Id)))
                if (!rows.Any(t => t.Kind == "skill" && t.Key == sk.Id))
                    rows.Add(new Tables.TemplateRow { Profile = "trained", Kind = "skill", Key = sk.Id, Want = "Legendary", Label = rules.SkillName(sk.Id) + (rules.TrainingRank(sk.Id) >= 3 ? " (spec)" : "") });
            if (rows.Count == 0) return;
            int done = 0, counted = rows.Count(t => !t.Optional);
            var html = new StringBuilder();
            // In-game "Spells" tab (creature cantrips; wards live on Elements): stars + the piece that gives it + best owned.
            var suitPieces = best.Select(b => b.V).ToList();
            if (opt.Weapon != null) suitPieces.Add(opt.Weapon);
            if (bs.OffHand != null) suitPieces.Add(bs.OffHand);
            var spellRows = rows.Where(t => t.Kind != "ward").ToList();
            int sDone = 0, sCount = spellRows.Count(t => !t.Optional);
            var recRows = new List<string[]>();
            foreach (var t in rows)
            {
                int want = Array.IndexOf(Tiers, t.Want) + 1;
                var inSuit = bs.Active.Where(s => Matches(s, t)).OrderByDescending(s => TierOf(s.Name)).FirstOrDefault();
                int have = inSuit != null ? TierOf(inSuit.Name) : 0;
                if (have >= want && !t.Optional) done++;
                // best copy anywhere in the collection
                Verdict bestV = null; int bestT = 0;
                foreach (var v in verdicts)
                    foreach (int id in v.Item.OwnSpells)
                    {
                        var s = Tables.Spell(id);
                        if (!Matches(s, t)) continue;
                        int tt = TierOf(s.Name);
                        bool better = tt > bestT || (tt == bestT && bestV != null && !(bestV.Usable && bestV.Magic != Magic.Off) && v.Usable && v.Magic != Magic.Off);
                        if (better) { bestT = tt; bestV = v; }
                    }
                string status;
                if (have >= want) status = "<span class=\"ok\">done</span>";
                else if (bestV != null && bestT > have)
                {
                    string why = !bestV.Movable ? "attuned to " + bestV.Item.Holder
                        : !bestV.WieldOk ? "can't wield: " + string.Join("; ", bestV.Wield.Where(x => !x.Ok).Select(x => x.Text).ToArray())
                        : bestV.Magic == Magic.Off ? "its magic won't switch on: " + string.Join("; ", bestV.MagicChecks.Where(x => !x.Ok).Select(x => x.Text).ToArray())
                        : bestV.Magic == Magic.WithCrystal ? "needs Scholar's Crystal"
                        : (bestV.Item.ValidLocations & 0x200000) != 0 && (bestV.Item.ValidLocations & 0x100000) == 0 && !opt.OffHandAllowed(opt.Weapon, bestV) ? "it is a shield, and " + opt.OffHandReason(true)
                        : (bestV.Item.ValidLocations & 0x73F00000) != 0 ? "it is a weapon - the weapon pick weighs it against damage"
                        : "its slot goes to a better piece overall";
                    status = "<span class=\"mid\">you own " + Tiers[bestT - 1] + "</span> on " + H(bestV.Item.Display) + " (" + H(bestV.Item.Holder) + ") - " + H(why);
                }
                else status = "<span class=\"no\">to collect</span> - look for " + H(t.Want + " " + t.Label);
                if (t.Kind != "ward")
                {
                    if (have >= 4 && !t.Optional) sDone++;
                    string piece = "";
                    if (inSuit != null)
                    {
                        var from = suitPieces.FirstOrDefault(v => v.Item.OwnSpells.Contains(inSuit.Id));
                        piece = from != null ? from.Item.Display : "set bonus";
                    }
                    var fromPiece = inSuit != null ? suitPieces.FirstOrDefault(v => v.Item.OwnSpells.Contains(inSuit.Id)) : null;
                    string now = inSuit != null ? Tiers[have - 1] + " - " + (fromPiece != null ? SlotFor(fromPiece, best, opt, bs) + ": " + fromPiece.Item.Display : "set bonus") : "none";
                    string bestTxt, betterCol, whyCol;
                    if (have >= 4) { bestTxt = "done (Legendary)"; betterCol = "-"; whyCol = "done"; }
                    else if (bestV != null && bestT > have)
                    {
                        whyCol = WhyNotUsed(bestV, opt);
                        betterCol = Tiers[bestT - 1] + ": " + bestV.Item.Display + " (" + bestV.Item.Holder + ")";
                        bestTxt = "you own " + Tiers[bestT - 1] + ": " + bestV.Item.Display + " (" + whyCol + ")";
                    }
                    else { betterCol = "none owned"; whyCol = "collect a Legendary one"; bestTxt = have > 0 ? "no better one owned - collect Legendary" : "none owned - collect one"; }
                    string label = t.Label.Replace("Melee Defense (Invulnerability)", "Invulnerability").Replace("Magic Defense (Magic Resistance)", "Magic Resist").Replace("Willpower (Self)", "Willpower").Replace(" (feeds your trade skills)", "");
                    recRows.Add(new[] { label + (t.Optional ? " (nice)" : "") + " " + Stars(have), now, betterCol,
                        label + ": suit has " + (inSuit != null ? inSuit.Name + " (" + piece + ")" : "none") + "; " + bestTxt + (t.Optional ? " - nice to have" : ""), whyCol });
                }
                html.Append("<tr><td>" + H(t.Label) + (t.Optional ? " <span class=\"dim\">(nice to have)</span>" : "") + "</td><td>" + H(t.Want) + "</td><td>" + (inSuit != null ? H(inSuit.Name) : "<span class=\"dim\">none</span>") + "</td><td>" + status + "</td></tr>");
            }
            if (recRows.Count > 0)
            {
                CoverageHead(c.Name, "Spells", "Stars = cantrip tier in your recommended suit: blank none, * Minor, ** Major/Epic, *** Legendary.",
                    "Coverage", sDone + " of " + sCount + " of your skills have a Legendary cantrip", "Creature cantrips for your trained skills (wards are on the Elements tab)", "Spell");
                foreach (var x in recRows.Where(x => x[1] != "none").Concat(recRows.Where(x => x[1] == "none"))) AddRec(c.Name, "Spells", x[0], x[1], x[2], x[3], x[4]);   // what your suit has first
            }
            sb.Append("<h3>Wishlist <span class=\"sub\">" + done + " of " + counted + " end-game cantrips in the recommended suit</span></h3>");
            sb.Append("<p class=\"meta\">Template for " + H(opt.ProfileSkillName) + " (tables/templates.tsv - a draft to mark up) plus a Legendary ward for every damage type (end goal: full coverage). Cantrips only; buffs are assumed at level 7.</p>");
            sb.Append("<div class=\"scroll\"><table><tr><th>Target</th><th>Want</th><th>Recommended suit has</th><th>Status / where it is</th></tr>").Append(html).Append("</table></div>");
        }

        // "Your Suit" tab: per slot, what you wear now; if the recommendation differs, a second line
        // "--> Recommended" with the piece, its holder and what to do. A slot with nothing better = one line.
        static void SuitRows(Character c, Optimizer opt, SuitScore cs, SuitScore bs, List<Choice> cur, Verdict curWeapon, List<Choice> best, Verdict bestWeapon)
        {
            Func<Verdict, string> act = v =>
            {
                var it = v.Item;
                string a = it.Holder == c.Name ? (it.Where == "equipped" ? "wearing" : "in your pack" + (it.Where == "sidepack" && it.Pack != "" ? " (" + it.Pack + ")" : ""))
                    : it.InChest ? "take from " + it.Holder : "get from " + it.Holder;
                Magic m; if (!bs.MagicOf.TryGetValue(v, out m)) m = v.Magic;
                if (m == Magic.WithCrystal) a += " - Scholar's Crystal first";
                if ((it.ValidLocations & Optimizer.SigilSlots) != 0 && !opt.SigilFlagKnown && it.Where != "equipped") a += " - slot must be unlocked";
                return a;
            };
            int n = 0, changes = 0;
            var lines = new List<string[]>();
            Action<string, Verdict, Verdict> slot = (name, now, rec) =>
            {
                n++;
                bool same = now != null && rec != null && now.Item.Id == rec.Item.Id;
                if (now == null && rec == null) return;
                lines.Add(new[] { n + ". " + name, now != null ? now.Item.Display : "(empty)", now != null ? c.Name : "", now != null ? (same ? "wearing" : (rec != null ? "wearing - replace it" : "wearing")) : "-", now != null ? Why(opt, cs, now) : "" });
                if (!same && rec != null)
                {
                    changes++;
                    string w = Why(opt, bs, rec);
                    lines.Add(new[] { "   --> Recommended", rec.Item.Display, rec.Item.Holder, act(rec) + (w != "" ? " (" + w + ")" : ""), w });
                }
                else if (!same && now != null && rec == null) { changes++; lines.Add(new[] { "   --> Recommended", "take it off", "", "adds nothing - wear anything you like", "" }); }
            };
            slot("Weapon", curWeapon, bestWeapon);
            if (cs.OffHand != null || bs.OffHand != null) slot("Off hand", cs.OffHand, bs.OffHand);
            var used = new HashSet<Choice>();
            foreach (var b in best.OrderBy(x => Optimizer.SlotOrder(x.Mask)))
            {
                var nowPieces = cur.Where(ch => Optimizer.ClashItems(b.V.Item, b.Mask, ch.V.Item, ch.Mask)).ToList();
                foreach (var ch in nowPieces) used.Add(ch);
                var nowV = nowPieces.FirstOrDefault(ch => ch.V.Item.Id == b.V.Item.Id);
                var shown = nowV != null ? nowV.V : nowPieces.Select(x => x.V).FirstOrDefault();
                string name = Optimizer.SlotOf(b.Mask).Split(',')[0];
                slot(name, shown, b.V);
            }
            foreach (var ch in cur.Where(x => !used.Contains(x)).OrderBy(x => Optimizer.SlotOrder(x.Mask)))
                slot(Optimizer.SlotOf(ch.Mask).Split(',')[0], ch.V, null);
            AddRec(c.Name, "Suit", "Your suit", n + " slots", "", changes == 0 ? "you are wearing the best you own" : changes + " to change", " ");
            AddRec(c.Name, "Suit", "", "", "", "", " ");
            foreach (var l in lines) AddRec(c.Name, "Suit", l[0], l[1], l[2], l[1] + " (" + (l[2] != "" ? l[2] + ", " : "") + l[3] + ")" + (l[4] != "" ? " [" + l[4] + "]" : ""), l[3]);
        }

        // "Current" tab: what is worn now by slot; "*" marks a slot where the recommendation is different.
        static void CurrentRows(Character c, Optimizer opt, SuitScore cs, SuitScore bs, List<Choice> cur, Verdict curWeapon, List<Choice> best, Verdict bestWeapon)
        {
            Func<Item, Item, bool> same = (a, b) => a != null && b != null && a.Id == b.Id;
            // weapon
            {
                string up = bestWeapon != null && (curWeapon == null || !same(curWeapon.Item, bestWeapon.Item)) ? "* upgrade: " + bestWeapon.Item.Display + " (" + bestWeapon.Item.Holder + ")" : "keep";
                AddRec(c.Name, "Current", (up == "keep" ? "" : "* ") + "Weapon", curWeapon != null ? curWeapon.Item.Display : "(none for this profile)", c.Name, up, curWeapon != null ? Why(opt, cs, curWeapon) : "");
            }
            {
                var co = cs.OffHand; var bo = bs.OffHand;
                bool keepO = (co == null && bo == null) || (co != null && bo != null && same(co.Item, bo.Item));
                string upO = keepO ? "keep" : bo != null ? "* upgrade: " + bo.Item.Display + " (" + bo.Item.Holder + ")" : "* take off - adds nothing";
                AddRec(c.Name, "Current", (keepO ? "" : "* ") + "Off hand", co != null ? co.Item.Display : "(empty)", co != null ? c.Name : "", upO, co != null ? Why(opt, cs, co) : "");
            }
            var covered = new HashSet<Choice>();
            foreach (var ch in cur.OrderBy(x => Optimizer.SlotOrder(x.Mask)))
            {
                var repl = best.Where(b => Optimizer.ClashItems(b.V.Item, b.Mask, ch.V.Item, ch.Mask)).ToList();   // same rule as the server: coverage for armor/clothing
                foreach (var b in repl) covered.Add(b);
                bool keep = repl.Any(b => same(b.V.Item, ch.V.Item));
                string up;
                if (keep) up = "keep";
                else if (repl.Count == 0) up = "* take off - adds nothing (slot free)";
                else up = "* upgrade: " + string.Join(" + ", repl.Select(b => b.V.Item.Display + " (" + b.V.Item.Holder + ")").ToArray());
                AddRec(c.Name, "Current", (keep ? "" : "* ") + Optimizer.SlotOf(ch.Mask), ch.V.Item.Display, c.Name, up, Why(opt, cs, ch.V));
            }
            foreach (var b in best.Where(b => !covered.Contains(b)).OrderBy(x => Optimizer.SlotOrder(x.Mask)))
                AddRec(c.Name, "Current", "* " + Optimizer.SlotOf(b.Mask), "(empty)", "", "* add: " + b.V.Item.Display + " (" + b.V.Item.Holder + ")", Why(opt, bs, b.V));
        }

        static string Pct(double mult) { return (mult >= 1 ? "+" : "") + Math.Round((mult - 1) * 100) + "%"; }
        static readonly string[] ImbueNames = { "Critical Strike", "Crippling Blow", "Armor Rending", "Slash Rending", "Pierce Rending", "Bludgeon Rending", "Acid Rending", "Cold Rending", "Lightning Rending", "Fire Rending", "Melee Defense", "Missile Defense", "Magic Defense", "Spellbook", "Nether Rending" };

        // Key modifiers: weapon damage/attack/defense, caster damage, ratings, imbues.
        static string Stats(Item it)
        {
            var s = new List<string>();
            if (it.Has(44) && it.Int(44) > 0) s.Add("Dmg " + it.Int(44) + (it.HasF(22) ? " (var " + Math.Round(it.Flt(22), 2) + ")" : ""));
            if (it.HasF(63) && it.Flt(63) != 1) s.Add("Dmg mod " + Pct(it.Flt(63)));
            if (it.Has(204) && it.Int(204) > 0) s.Add("Elem +" + it.Int(204));
            if (it.HasF(152) && it.Flt(152) != 1) s.Add("Elem dmg " + Pct(it.Flt(152)));
            if (it.HasF(62) && it.Flt(62) != 1) s.Add("Attack " + Pct(it.Flt(62)));
            if (it.HasF(29) && it.Flt(29) != 1) s.Add("Melee D " + Pct(it.Flt(29)));
            if (it.HasF(149) && it.Flt(149) != 1) s.Add("Missile D " + Pct(it.Flt(149)));
            if (it.HasF(150) && it.Flt(150) != 1) s.Add("Magic D " + Pct(it.Flt(150)));
            if (it.HasF(144) && it.Flt(144) > 0) s.Add("Mana conv +" + Math.Round(it.Flt(144) * 100) + "%");
            if (it.Has(49) && it.Has(44)) s.Add("Speed " + it.Int(49));
            if (it.Has(166) && it.Int(166) > 0) s.Add("Slayer (type " + it.Int(166) + ")" + (it.HasF(138) ? " x" + Math.Round(it.Flt(138), 2) : ""));
            long imb = it.Int(179) | it.Int(303);
            for (int i = 0; i < ImbueNames.Length; i++) if ((imb & (1L << i)) != 0) s.Add(ImbueNames[i]);
            string[] rn = { "Dmg", "Dmg resist", "Crit", "Crit resist", "Crit dmg", "Crit dmg resist", "Heal boost", "Nether resist", "", "Vitality" };
            for (int k = 370; k <= 379; k++) if (rn[k - 370] != "" && it.Int(k) != 0) s.Add(rn[k - 370] + " rating " + it.Int(k));
            return string.Join(", ", s.ToArray());
        }

        // Sets: what each set this character could wear gives at each piece count (stock set tables).
        static void SetsSection(StringBuilder sb, Character c, List<Verdict> verdicts)
        {
            var bySet = verdicts.Where(v => v.Usable && v.Item.Int(265) > 0).GroupBy(v => v.Item.Int(265)).OrderByDescending(g => g.Count()).ToList();
            if (bySet.Count == 0) return;
            sb.Append("<h3>Armor sets this character can wear</h3><p class=\"meta\">Set bonuses count every set piece you wear, even pieces whose own magic won't work (ACE: set spells are not affected by mana or Arcane Lore). Bonuses come from the stock set tables; the next milestone picks the best combination.</p><div class=\"scroll\"><table><tr><th>Set</th><th>Wieldable pieces owned</th><th>Worn now</th><th>Bonus by pieces worn</th></tr>");
            foreach (var g in bySet)
            {
                long id = g.Key;
                int worn = c.Items.Count(i => i.Where == "equipped" && i.Int(265) == id);
                var tiers = new List<string>();
                SortedDictionary<int, List<int>> t;
                if (Tables.SetTiers.TryGetValue((int)id, out t))
                    foreach (var kv in t) tiers.Add("<b>" + kv.Key + ":</b> " + H(string.Join(", ", kv.Value.Select(x => Tables.SpellName(x)).ToArray())));
                string pieces = string.Join(", ", g.Select(v => v.Item.Display + " (" + v.Item.Holder + ")").ToArray());
                sb.Append("<tr><td>" + H(Tables.SetName(id)) + "</td><td>" + g.Count() + "<br><small>" + H(pieces) + "</small></td><td>" + worn + "</td><td class=\"spells\">" + string.Join("<br>", tiers.ToArray()) + "</td></tr>");
            }
            sb.Append("</table></div>");
        }

        static string Join(List<Check> l) { return string.Join("; ", l.Select(x => (x.Ok ? "OK " : "NO ") + x.Text).ToArray()); }
        static string Anchor(string n) { var sb = new StringBuilder(); foreach (char ch in n) sb.Append(char.IsLetterOrDigit(ch) ? ch : '-'); return sb.ToString(); }

        const string Css = @"
:root{--bg:#fbfaf7;--fg:#1d1c1a;--mut:#6b6760;--line:#e3dfd6;--card:#fff;--ok:#1f7a3a;--no:#b3261e;--mid:#9a6400;--acc:#6b4e16}
@media (prefers-color-scheme:dark){:root{--bg:#161513;--fg:#ece9e2;--mut:#a29d93;--line:#34312c;--card:#1f1d1a;--ok:#5cc47d;--no:#ff8a80;--mid:#f0b54a;--acc:#e0c27a}}
*{box-sizing:border-box}body{margin:0;background:var(--bg);color:var(--fg);font:15px/1.45 system-ui,Segoe UI,sans-serif}
main{max-width:1200px;margin:0 auto;padding:16px}h1{margin:.2em 0}h1 .sub,h2 .sub{color:var(--mut);font-weight:400;font-size:.7em}
h2{margin-top:1.6em;border-bottom:2px solid var(--line);padding-bottom:4px}h3{margin:1.2em 0 .4em;color:var(--acc)}
.meta{color:var(--mut)}.note{background:var(--card);border:1px solid var(--line);border-left:4px solid var(--acc);padding:10px 12px;border-radius:6px}
.warn{color:var(--mid)}table{border-collapse:collapse;width:100%;background:var(--card)}th,td{border-bottom:1px solid var(--line);padding:6px 8px;text-align:left;vertical-align:top}
th{font-size:.85em;color:var(--mut);font-weight:600}.scroll{overflow-x:auto}.num{white-space:nowrap}.spells{font-size:.8em;color:var(--mut);max-width:340px}.stats{font-size:.82em;max-width:220px}.setnote{color:var(--acc)}.suit td b{color:var(--fg)}.cmp td:first-child{white-space:nowrap}
.ok{color:var(--ok);font-weight:600}.no{color:var(--no);font-weight:600}.mid{color:var(--mid);font-weight:600}.dim{color:var(--mut)}
.where{color:var(--mut);font-size:.85em}.tag{font-size:.75em;border:1px solid var(--line);border-radius:4px;padding:0 4px;color:var(--mut)}
small{color:var(--mut)}.filter{display:block;margin:14px 0}.chars{max-width:640px}.skills{max-width:640px}
body.only tr.n{display:none}details{margin:.4em 0}summary{cursor:pointer;color:var(--acc)}a{color:var(--acc)}
";
    }
}
