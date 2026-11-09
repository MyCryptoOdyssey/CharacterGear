// Paper doll: the character's gear laid out like the in-game equipment window, "Now" next to "Recommended",
// with changed slots highlighted. Text boxes (item icons would need the client's portal.dat - later).
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;

namespace CharacterGear.Planner
{
    internal static class PaperDoll
    {
        class Box { public string Key, Label; public long Cov, Slot; }

        // Armor boxes by coverage (outer layer), underclothes by coverage (under layer), jewelry/cloak/trinket by slot.
        static readonly Box[] Left = {
            new Box { Key = "neck", Label = "Neck", Slot = 0x8000 }, new Box { Key = "lwrist", Label = "Left wrist", Slot = 0x10000 },
            new Box { Key = "lring", Label = "Left ring", Slot = 0x40000 }, new Box { Key = "trinket", Label = "Trinket", Slot = 0x4000000 },
            new Box { Key = "cloak", Label = "Cloak", Slot = 0x8000000 } };
        static readonly Box[] Body = {
            new Box { Key = "head", Label = "Head", Cov = 0x4000 }, new Box { Key = "chest", Label = "Chest", Cov = 0x400 },
            new Box { Key = "uarm", Label = "Upper arms", Cov = 0x1000 }, new Box { Key = "abd", Label = "Abdomen", Cov = 0x800 },
            new Box { Key = "larm", Label = "Lower arms", Cov = 0x2000 }, new Box { Key = "hands", Label = "Hands", Cov = 0x8000 },
            new Box { Key = "uleg", Label = "Upper legs", Cov = 0x100 }, new Box { Key = "lleg", Label = "Lower legs", Cov = 0x200 },
            new Box { Key = "feet", Label = "Feet", Cov = 0x10000 } };
        static readonly Box[] Under = {
            new Box { Key = "shirt", Label = "Shirt", Cov = 0x8 | 0x10 | 0x20 | 0x40 }, new Box { Key = "pants", Label = "Pants", Cov = 0x2 | 0x4 } };
        static readonly Box[] Right = {
            new Box { Key = "rwrist", Label = "Right wrist", Slot = 0x20000 }, new Box { Key = "rring", Label = "Right ring", Slot = 0x80000 },
            new Box { Key = "weapon", Label = "Weapon / wand" } };

        static string H(string s) { return WebUtility.HtmlEncode(s ?? ""); }

        static Dictionary<string, Verdict> Fill(List<Choice> suit, Verdict weapon)
        {
            var d = new Dictionary<string, Verdict>();
            foreach (var ch in suit)
            {
                var it = ch.V.Item;
                bool cloth = it.Coverage != 0 && (it.ValidLocations & (0xF8000 | 0x4000000 | 0x8000000)) == 0;
                if (cloth) foreach (var b in Body.Concat(Under)) if ((it.Coverage & b.Cov) != 0) d[b.Key] = ch.V;
                if (!cloth)
                {
                    foreach (var b in Body) if (b.Key == "head" && (ch.Mask & 0x1) != 0 || b.Key == "hands" && (ch.Mask & 0x20) != 0 || b.Key == "feet" && (ch.Mask & 0x100) != 0) d[b.Key] = ch.V;
                }
                foreach (var b in Left.Concat(Right)) if (b.Slot != 0 && (ch.Mask & b.Slot) != 0) d[b.Key] = ch.V;
            }
            if (weapon != null) d["weapon"] = weapon;
            return d;
        }

        static void Column(StringBuilder sb, string cls, Box[] boxes, Dictionary<string, Verdict> mine, Dictionary<string, Verdict> other, bool showChange, string who)
        {
            sb.Append("<div class=\"pd-col " + cls + "\">");
            foreach (var b in boxes)
            {
                Verdict v; mine.TryGetValue(b.Key, out v);
                Verdict o; other.TryGetValue(b.Key, out o);
                bool free = showChange && v == null && o != null;   // recommended leaves it free: keep what you have
                bool changed = showChange && v != null && (o == null || v.Item.Id != o.Item.Id);
                sb.Append("<div class=\"pd-box" + (changed ? " pd-chg" : "") + (v == null && !free ? " pd-empty" : "") + "\"><div class=\"pd-lbl\">" + H(b.Label) + "</div>");
                if (v != null)
                {
                    var it = v.Item;
                    var sub = new List<string>();
                    if (v.BuffedAL > 0 && it.Coverage != 0) sub.Add("AL " + v.BuffedAL);
                    if (it.Int(265) > 0) sub.Add(Tables.SetName(it.Int(265)));
                    if (it.Has(44) && it.Int(44) > 0) sub.Add("Dmg " + it.Int(44));
                    sb.Append("<div class=\"pd-name\">" + H(it.Display) + "</div><div class=\"pd-sub\">" + H(string.Join(" | ", sub.ToArray())) + "</div>");
                    if (showChange && it.Holder != who) sb.Append("<div class=\"pd-from\">from " + H(it.Holder) + "</div>");
                    else if (showChange && it.Where != "equipped") sb.Append("<div class=\"pd-from\">from your pack</div>");
                }
                else if (free) sb.Append("<div class=\"pd-name dim\">free - keep yours</div><div class=\"pd-sub\">nothing scanned adds anything here</div>");
                else sb.Append("<div class=\"pd-name dim\">empty</div>");
                sb.Append("</div>");
            }
            sb.Append("</div>");
        }

        static void Doll(StringBuilder sb, string title, Dictionary<string, Verdict> mine, Dictionary<string, Verdict> other, bool showChange, string who)
        {
            sb.Append("<div class=\"pd\"><div class=\"pd-title\">" + H(title) + "</div><div class=\"pd-grid\">");
            Column(sb, "pd-side", Left, mine, other, showChange, who);
            sb.Append("<div class=\"pd-mid\">");
            Column(sb, "pd-body", Body, mine, other, showChange, who);
            Column(sb, "pd-under", Under, mine, other, showChange, who);
            sb.Append("</div>");
            Column(sb, "pd-side", Right, mine, other, showChange, who);
            sb.Append("</div></div>");
        }

        public static void Render(StringBuilder sb, string charName, List<Choice> now, Verdict nowWeapon, List<Choice> rec, Verdict recWeapon)
        {
            var a = Fill(now, nowWeapon); var b = Fill(rec, recWeapon);
            // Same ring/bracelet on the other hand is not a change: line the recommended pair up with today's.
            foreach (var pair in new[] { new[] { "lring", "rring" }, new[] { "lwrist", "rwrist" } })
            {
                Verdict al, ar, bl, br; a.TryGetValue(pair[0], out al); a.TryGetValue(pair[1], out ar); b.TryGetValue(pair[0], out bl); b.TryGetValue(pair[1], out br);
                if ((bl != null && ar != null && bl.Item.Id == ar.Item.Id) || (br != null && al != null && br.Item.Id == al.Item.Id))
                {
                    if (br != null) b[pair[0]] = br; else b.Remove(pair[0]);
                    if (bl != null) b[pair[1]] = bl; else b.Remove(pair[1]);
                }
            }
            int changes = 0;
            foreach (var k in a.Keys.Union(b.Keys)) { Verdict x, y; a.TryGetValue(k, out x); b.TryGetValue(k, out y); if (y != null && (x == null || x.Item.Id != y.Item.Id)) changes++; }
            sb.Append("<h3>Paper doll <span class=\"sub\">" + changes + " slot(s) change, highlighted</span></h3><div class=\"pd-wrap\">");
            Doll(sb, charName + " now", a, b, false, charName);
            Doll(sb, "Recommended", b, a, true, charName);
            sb.Append("</div>");
        }

        public const string Css = @"
.pd-wrap{display:grid;grid-template-columns:repeat(auto-fit,minmax(min(100%,520px),1fr));gap:14px;margin:6px 0 10px}
.pd{background:var(--card);border:1px solid var(--line);border-radius:8px;padding:10px}
.pd-title{font-weight:700;margin-bottom:8px;color:var(--acc)}
.pd-grid{display:grid;grid-template-columns:1fr 1.6fr 1fr;gap:8px}
.pd-mid{display:flex;flex-direction:column;gap:8px}
.pd-col{display:flex;flex-direction:column;gap:6px}
.pd-body{display:grid;grid-template-columns:1fr 1fr;gap:6px}
.pd-body .pd-box:first-child{grid-column:1 / span 2}
.pd-body .pd-box:last-child{grid-column:1 / span 2}
.pd-under{display:grid;grid-template-columns:1fr 1fr;gap:6px}
.pd-box{border:1px solid var(--line);border-radius:6px;padding:5px 7px;min-height:52px;background:var(--bg)}
.pd-chg{border:2px solid var(--mid);background:color-mix(in srgb,var(--mid) 10%,var(--bg))}
.pd-empty{opacity:.65}
.pd-lbl{font-size:.7em;text-transform:uppercase;letter-spacing:.04em;color:var(--mut)}
.pd-name{font-size:.86em;font-weight:600;line-height:1.2}
.pd-sub{font-size:.74em;color:var(--mut)}
.pd-from{font-size:.72em;color:var(--mid);font-weight:600}
@media (max-width:560px){.pd-grid{grid-template-columns:1fr}}
";
    }
}
