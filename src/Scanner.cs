// Scanner: finds every item the character carries, IDs the gear (throttled), writes the JSON file.
// READ-ONLY by design: the only game request it makes is RequestId (appraise).
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Decal.Adapter;
using Decal.Adapter.Wrappers;

namespace CharacterGear
{
    internal class Scanner
    {
        internal const int TICK_MS = 250;
        const int ID_EVERY_MS = 300;            // ~3 ID requests per second
        const int ID_WAIT_MS = 15000;           // give up waiting for answers this long after the last request
        const int DIRTY_SAVE_MS = 60000;        // save changes (picked up / moved items) at most once a minute
        const int EQUIP_SAVE_MS = 3000;         // ...but an equip / unequip is saved (and the picks refreshed) within ~3 s
        readonly HashSet<int> lastEquipped = new HashSet<int>();
        bool equipChanged;
        const int FORCE_SAVE_MS = 300000;       // and refresh the file every 5 minutes anyway

        // Decal's own keys (not server property numbers)
        const int KEY_STACK = 218103814, KEY_WIELDER = 218103818, KEY_COVERAGE = 218103821, KEY_EQUIPABLE = 218103822;
        const int KEY_WIELDED_LOCATION = 10;    // ACE PropertyInt CurrentWieldedLocation

        readonly PluginCore P;
        internal Scanner(PluginCore p) { P = p; }

        readonly Queue<int> queue = new Queue<int>();
        readonly HashSet<int> queued = new HashSet<int>();
        DateTime scanAt = DateTime.MaxValue, nextId = DateTime.MinValue, lastRequest = DateTime.MinValue;
        DateTime lastSave = DateTime.MinValue, dirtySince = DateTime.MaxValue;
        bool scanning, retried;
        DateTime scanStart = DateTime.MinValue;
        int gearTotal, gearDone;
        internal bool HasScanned;
        internal bool Scanning { get { return scanning; } }
        internal string StatusText = "Waiting for login";
        internal string SavedText = "Not saved yet";

        CoreManager C { get { return CoreManager.Current; } }
        int MyId { get { return C.CharacterFilter.Id; } }

        internal string DataPath
        {
            get { return Path.Combine(Path.Combine(Path.Combine(PluginCore.PluginDir, "data"), Util.SafeName(P.ServerName)), Util.SafeName(P.CharName) + ".json"); }
        }

        internal void ScheduleScan(int seconds) { scanAt = DateTime.Now.AddSeconds(seconds); StatusText = "Scan starts in " + seconds + " s"; }
        internal void Cancel() { scanning = false; queue.Clear(); queued.Clear(); scanAt = DateTime.MaxValue; }

        internal void Tick()
        {
            if (!P.InGame || !P.Tracked) return;
            DateTime now = DateTime.Now;
            if (now >= scanAt) { scanAt = DateTime.MaxValue; StartScan(); }

            if (queue.Count > 0 && now >= nextId)
            {
                int id = queue.Dequeue(); queued.Remove(id);
                WorldObject wo = C.WorldFilter[id];
                if (wo != null && !Done(wo)) { C.Actions.RequestId(id); lastRequest = now; }
                nextId = now.AddMilliseconds(ID_EVERY_MS);
            }

            if (scanning)
            {
                gearDone = CountIdentified();
                int left = Math.Max(0, gearTotal - gearDone);
                int filled = gearTotal > 0 ? (int)Math.Round(10.0 * gearDone / gearTotal) : 0;
                StatusText = "[" + new string('#', filled) + new string('-', 10 - filled) + "] " + gearDone + "/" + gearTotal +
                             (left > 0 ? " - about " + Math.Max(1, (queue.Count + 1) * ID_EVERY_MS / 1000) + " s left" : " - finishing");
                bool allIn = gearDone >= gearTotal;
                bool waitedLongEnough = queue.Count == 0 && (now - lastRequest).TotalMilliseconds > ID_WAIT_MS;
                if (queue.Count == 0 && !allIn && waitedLongEnough && !retried)
                {
                    retried = true;               // one retry for items whose answer never came
                    foreach (WorldObject wo in MyItems()) if (IsGear(wo) && !Done(wo)) Enqueue(wo.Id);
                    lastRequest = now;
                }
                else if (allIn || (waitedLongEnough && retried))
                {
                    scanning = false; HasScanned = true;
                    Save(true);   // finished scan = history snapshot
                    int missing = gearTotal - gearDone;
                    StatusText = "Done: " + gearDone + " gear items" + (missing > 0 ? " (" + missing + " gave no ID)" : "");
                    P.Chat("Scan done for " + P.CharName + ": " + gearDone + "/" + gearTotal + " gear items ID'd. Saved. Updating recommendations...");
                    P.Rec.RunPlanner(false);   // fresh picks for the Recommended window (runs outside the game)
                }
            }
            else if (HasScanned)
            {
                int wait = equipChanged ? EQUIP_SAVE_MS : DIRTY_SAVE_MS;
                if (queue.Count == 0 && dirtySince != DateTime.MaxValue && (now - dirtySince).TotalMilliseconds > wait) { Save(); P.Rec.RunPlanner(false, true); }   // items moved: refresh picks quietly
                else if ((now - lastSave).TotalMilliseconds > FORCE_SAVE_MS) Save();
            }
        }

        void StartScan()
        {
            queue.Clear(); queued.Clear(); retried = false;
            scanStart = DateTime.Now;   // every scan asks again, so the numbers are fresh
            gearTotal = 0;
            foreach (WorldObject wo in MyItems())
            {
                if (!IsGear(wo)) continue;
                gearTotal++;
                Enqueue(wo.Id);
            }
            scanning = true; lastRequest = DateTime.Now;
            P.Chat("Scanning " + P.CharName + ": " + gearTotal + " gear items to ID (about " + Math.Max(1, queue.Count * ID_EVERY_MS / 1000) + " s). Read-only.");
        }

        void Enqueue(int id) { if (queued.Add(id)) queue.Enqueue(id); }

        int CountIdentified()
        {
            int n = 0, total = 0;
            foreach (WorldObject wo in MyItems()) if (IsGear(wo)) { total++; if (Done(wo)) n++; }
            gearTotal = total;
            return n;
        }

        // A new item arrived or an item changed: ID new gear, and remember to refresh the file.
        internal void OnObjectSeen(WorldObject wo) { Touch(wo); }
        internal void OnObjectChanged(WorldObject wo) { Touch(wo); }
        void Touch(WorldObject wo)
        {
            if (wo == null || !P.InGame || !P.Tracked || !HasScanned) return;
            if (!IsMine(wo)) return;
            if (dirtySince == DateTime.MaxValue) dirtySince = DateTime.Now;
            // wielded / taken off since the last save? -> fast path
            bool eq = wo.Values((LongValueKey)KEY_WIELDED_LOCATION, 0) != 0 || wo.Values((LongValueKey)KEY_WIELDER, 0) == MyId;
            if (eq != lastEquipped.Contains(wo.Id)) { equipChanged = true; dirtySince = DateTime.Now; }
            if (IsGear(wo) && !P.Appraisal.Records.ContainsKey(wo.Id)) Enqueue(wo.Id);
        }

        // ID'd during this scan = the server's raw ID answer arrived after the scan started.
        bool Done(WorldObject wo)
        {
            AppraisalRecord r;
            return P.Appraisal.Records.TryGetValue(wo.Id, out r) && r.At >= scanStart;
        }

        internal void SaveIfDirty()
        {
            if (HasScanned || scanning) Save();
        }

        // ---------- what the character carries ----------
        bool IsMine(WorldObject wo)
        {
            int me = MyId;
            if (wo.Container == me) return true;
            if (wo.Values((LongValueKey)KEY_WIELDER, 0) == me) return true;
            if (wo.Container != 0)
            {
                WorldObject pack = C.WorldFilter[wo.Container];
                if (pack != null && pack.Container == me) return true;
            }
            return false;
        }

        // Summoning essences have no equip slot but carry requirements and pet ratings: ID them like gear.
        static readonly System.Text.RegularExpressions.Regex EssenceRx = new System.Text.RegularExpressions.Regex(@"Essence( \(\d+\))?$");
        internal static bool IsEssence(WorldObject wo) { return wo.ObjectClass == ObjectClass.Misc && EssenceRx.IsMatch(wo.Name ?? ""); }

        internal static bool IsGear(WorldObject wo)
        {
            if (wo.Values((LongValueKey)KEY_EQUIPABLE, 0) != 0) return true;
            if (IsEssence(wo)) return true;
            switch (wo.ObjectClass)
            {
                case ObjectClass.Armor: case ObjectClass.Clothing: case ObjectClass.Jewelry:
                case ObjectClass.MeleeWeapon: case ObjectClass.MissileWeapon: case ObjectClass.WandStaffOrb:
                    return true;
            }
            return false;
        }

        List<WorldObject> MyItems()
        {
            var list = new List<WorldObject>();
            var seen = new HashSet<int>();
            foreach (WorldObject wo in C.WorldFilter.GetInventory()) if (seen.Add(wo.Id)) list.Add(wo);
            int me = MyId;
            foreach (WorldObject wo in C.WorldFilter.GetAll())   // equipped items, in case the inventory list leaves them out
                if (!seen.Contains(wo.Id) && wo.Values((LongValueKey)KEY_WIELDER, 0) == me) { seen.Add(wo.Id); list.Add(wo); }
            return list;
        }

        // ---------- write the file ----------
        const int HISTORY_KEEP = 50;   // snapshots kept per character

        void Save() { Save(false); }
        void Save(bool snapshot)
        {
            dirtySince = DateTime.MaxValue; lastSave = DateTime.Now; equipChanged = false;
            var j = new Json();
            j.Open();
            j.Num("schema", 1);
            j.Str("tool", "CharacterGear " + typeof(Scanner).Assembly.GetName().Version);
            j.Str("server", P.ServerName);
            j.Str("character", P.CharName);
            j.Str("scannedAt", DateTime.Now.ToString("yyyy-MM-ddTHH:mm:ss"));
            j.Bool("scanComplete", HasScanned && !scanning);
            WriteCharacter(j);
            j.Key("items"); j.OpenArr();
            int me = MyId;
            lastEquipped.Clear();
            foreach (WorldObject wo in MyItems())
            {
                WriteItem(j, wo, me);
                if (wo.Values((LongValueKey)KEY_WIELDED_LOCATION, 0) != 0 || wo.Values((LongValueKey)KEY_WIELDER, 0) == me) lastEquipped.Add(wo.Id);
            }
            j.CloseArr();
            j.Close();
            string path = DataPath;
            string text = j.ToString();
            Util.WriteText(path, text);
            SavedText = "Saved " + DateTime.Now.ToString("HH:mm:ss");
            if (snapshot && P.HistoryOn) WriteHistory(text);
        }

        // One copy per finished scan (login, Rescan me): data\<server>\history\<char>\yyyyMMdd-HHmmss.json, newest 50 kept.
        internal string HistoryDir
        {
            get { return Path.Combine(Path.Combine(Path.Combine(Path.Combine(PluginCore.PluginDir, "data"), Util.SafeName(P.ServerName)), "history"), Util.SafeName(P.CharName)); }
        }
        void WriteHistory(string text)
        {
            try
            {
                string dir = HistoryDir;
                Util.WriteText(Path.Combine(dir, DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".json"), text);
                string[] files = Directory.GetFiles(dir, "*.json");
                Array.Sort(files, StringComparer.OrdinalIgnoreCase);
                for (int i = 0; i < files.Length - HISTORY_KEEP; i++) File.Delete(files[i]);
            }
            catch (Exception ex) { Util.ErrorLog("history: " + ex.Message); }
        }

        internal void WriteEnchantments(Json j, string key)
        {
            j.Key(key); j.OpenArr();
            try { foreach (EnchantmentWrapper e in C.CharacterFilter.Enchantments) { j.Open(); j.Num("spell", e.SpellId); j.Dbl("duration", e.Duration); j.Close(); } } catch { }
            j.CloseArr();
        }

        void WriteCharacter(Json j)
        {
            CharacterFilter cf = C.CharacterFilter;
            j.Key("char"); j.Open();
            j.Num("id", cf.Id);
            j.Str("account", cf.AccountName);
            j.Num("level", cf.Level);
            j.Num("vitae", cf.Vitae);   // death penalty; the server checks skills AFTER vitae
            j.Str("race", cf.Race);
            j.Str("gender", cf.Gender);
            j.Num("totalXp", cf.TotalXP);
            j.Num("skillCredits", cf.SkillPoints);

            j.Key("attributes"); j.Open();
            foreach (CharFilterAttributeType t in Enum.GetValues(typeof(CharFilterAttributeType)))
            {
                try
                {
                    AttributeInfoWrapper a = cf.Attributes[t];
                    if (a == null) continue;
                    j.Key(((int)t).ToString()); j.Open();
                    j.Str("name", t.ToString()); j.Num("base", a.Base); j.Num("buffed", a.Buffed); j.Num("creation", a.Creation);
                    j.Close();
                }
                catch { }
            }
            j.Close();

            j.Key("vitals"); j.Open();
            foreach (CharFilterVitalType t in Enum.GetValues(typeof(CharFilterVitalType)))
            {
                try
                {
                    SkillInfoWrapper v = cf.Vitals[t];
                    if (v == null) continue;
                    j.Key(((int)t).ToString()); j.Open();
                    j.Str("name", t.ToString()); j.Num("base", v.Base); j.Num("buffed", v.Buffed); j.Num("current", v.Current);
                    j.Close();
                }
                catch { }
            }
            j.Close();

            j.Key("skills"); j.Open();
            foreach (CharFilterSkillType t in Enum.GetValues(typeof(CharFilterSkillType)))
            {
                try
                {
                    SkillInfoWrapper s = cf.Skills[t];
                    if (s == null) continue;
                    j.Key(((int)t).ToString()); j.Open();
                    j.Str("name", t.ToString()); j.Str("training", s.Training.ToString()); j.Bool("known", s.Known);
                    j.Num("base", s.Base); j.Num("buffed", s.Buffed); j.Num("current", s.Current); j.Num("bonus", s.Bonus);
                    j.Close();
                }
                catch { }
            }
            j.Close();

            j.Key("augmentations"); j.OpenArr();
            try { foreach (int a in cf.Augmentations) j.Val(a); } catch { }
            j.CloseArr();

            // Character properties the server sent at login (ratings, society ranks, ...); non-zero only.
            j.Key("props"); j.Open();
            for (int k = 1; k <= 500; k++)
            {
                try { int v = cf.GetCharProperty(k); if (v != 0) j.Num(k.ToString(), v); } catch { }
            }
            j.Close();

            // Active spells at scan time, so the planner can work out unbuffed numbers.
            j.Key("enchantments"); j.OpenArr();
            try
            {
                foreach (EnchantmentWrapper e in cf.Enchantments)
                {
                    j.Open();
                    j.Num("spell", e.SpellId); j.Num("family", e.Family); j.Num("layer", e.Layer);
                    j.Num("affected", e.Affected); j.Num("affectedMask", e.AffectedMask);
                    j.Dbl("adjustment", e.Adjustment); j.Dbl("duration", e.Duration); j.Num("secondsLeft", e.TimeRemaining);
                    j.Close();
                }
            }
            catch { }
            j.CloseArr();
            j.Close();
        }

        internal void WriteItem(Json j, WorldObject wo, int me, string forcedWhere = null)
        {
            bool gear = IsGear(wo);
            int wielded = wo.Values((LongValueKey)KEY_WIELDED_LOCATION, 0);
            bool equipped = wielded != 0 || wo.Values((LongValueKey)KEY_WIELDER, 0) == me;
            string where = forcedWhere ?? (equipped ? "equipped" : wo.Container == me ? "pack" : "sidepack");

            j.Open();
            j.Str("id", wo.Id.ToString("X8"));
            j.Str("name", wo.Name);
            j.Str("class", wo.ObjectClass.ToString());
            j.Bool("gear", gear);
            j.Str("where", where);
            if (where == "sidepack" || (forcedWhere != null && wo.Container != 0))
            {
                WorldObject pack = C.WorldFilter[wo.Container];
                j.Str("pack", pack != null ? pack.Name : "");
                j.Str("packId", wo.Container.ToString("X8"));
            }
            j.Num("icon", wo.Icon);
            int stack = wo.Values((LongValueKey)KEY_STACK, 0);
            if (stack > 1) j.Num("stack", stack);
            if (gear)
            {
                j.Bool("hasId", wo.HasIdData);
                j.Num("wieldedLocation", wielded);
                j.Num("equipableSlots", wo.Values((LongValueKey)KEY_EQUIPABLE, 0));
                j.Num("coverage", wo.Values((LongValueKey)KEY_COVERAGE, 0));

                // Raw properties. Keys below 65536 are the server's own property numbers (ACE PropertyInt etc.);
                // bigger keys are Decal's own fields.
                j.Key("ints"); j.Open();
                foreach (int k in wo.LongKeys) { try { j.Num(k.ToString(), wo.Values((LongValueKey)k, 0)); } catch { } }
                j.Close();
                j.Key("floats"); j.Open();
                foreach (int k in wo.DoubleKeys) { try { j.Dbl(k.ToString(), wo.Values((DoubleValueKey)k, 0.0)); } catch { } }
                j.Close();
                j.Key("strings"); j.Open();
                foreach (int k in wo.StringKeys) { try { j.Str(k.ToString(), wo.Values((StringValueKey)k, "")); } catch { } }
                j.Close();
                j.Key("bools"); j.Open();
                foreach (int k in wo.BoolKeys) { try { j.Bool(k.ToString(), wo.Values((BoolValueKey)k, false)); } catch { } }
                j.Close();
                j.Key("spells"); j.OpenArr();
                for (int i = 0; i < wo.SpellCount; i++) { try { j.Val(wo.Spell(i)); } catch { } }
                j.CloseArr();
                j.Key("activeSpells"); j.OpenArr();
                for (int i = 0; i < wo.ActiveSpellCount; i++) { try { j.Val(wo.ActiveSpell(i)); } catch { } }
                j.CloseArr();
                AppraisalRecord rec;
                if (P.Appraisal.Records.TryGetValue(wo.Id, out rec)) WriteIdAnswer(j, rec);
            }
            j.Close();
        }
            // The server's own ID answer, decoded exactly (see Appraisal.cs). Keys = ACE property numbers.
        static void WriteIdAnswer(Json j, AppraisalRecord r)
        {
            j.Key("idAnswer"); j.Open();
            j.Str("at", r.At.ToString("yyyy-MM-ddTHH:mm:ss"));
            j.Bool("success", r.Success);
            j.Key("ints"); j.Open(); foreach (var kv in r.Ints) j.Num(kv.Key.ToString(), kv.Value); j.Close();
            j.Key("int64s"); j.Open(); foreach (var kv in r.Int64s) j.Num(kv.Key.ToString(), kv.Value); j.Close();
            j.Key("bools"); j.Open(); foreach (var kv in r.Bools) j.Bool(kv.Key.ToString(), kv.Value); j.Close();
            j.Key("floats"); j.Open(); foreach (var kv in r.Floats) j.Dbl(kv.Key.ToString(), kv.Value); j.Close();
            j.Key("strings"); j.Open(); foreach (var kv in r.Strings) j.Str(kv.Key.ToString(), kv.Value); j.Close();
            j.Key("dids"); j.Open(); foreach (var kv in r.Dids) j.Num(kv.Key.ToString(), kv.Value); j.Close();
            j.Key("itemSpells"); j.OpenArr(); foreach (uint s in r.ItemSpells) j.Val(s); j.CloseArr();
            j.Key("castOnSpells"); j.OpenArr(); foreach (uint s in r.CastOnSpells) j.Val(s); j.CloseArr();
            if (r.ArmorProfile != null)
            {
                j.Key("armorProfile"); j.Open();
                string[] names = { "slash", "pierce", "bludgeon", "cold", "fire", "acid", "nether", "lightning" };
                for (int i = 0; i < 8; i++) j.Dbl(names[i], Math.Round(r.ArmorProfile[i], 4));
                j.Close();
            }
            j.Close();
        }
    }
}
