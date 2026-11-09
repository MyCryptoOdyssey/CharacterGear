// House chest snapshots: when you open a house storage chest ("Storage"), the collector IDs the gear inside
// and saves it to data\<server>\chests\<chest id>.json. The planner treats each chest as one more holder.
// READ-ONLY: opening the chest is the player's own click; this only reads what the server sends and asks
// for IDs (RequestId). Nothing is taken out or put in. A snapshot is as fresh as the last time the chest was open.
using System;
using System.Collections.Generic;
using System.IO;
using Decal.Adapter;
using Decal.Adapter.Wrappers;

namespace CharacterGear
{
    internal class ChestScanner
    {
        const int ID_EVERY_MS = 300;
        const int NO_ANSWER_MS = 10000;     // ask once more if an ID answer hasn't come after this long
        const int FIRST_SAVE_MS = 3000;     // let the chest's contents arrive before the first save
        const int SAVE_EVERY_MS = 2000;

        readonly PluginCore P;
        internal ChestScanner(PluginCore p) { P = p; }
        CoreManager C { get { return CoreManager.Current; } }

        int open;                                   // chest being snapshotted (0 = none)
        internal int LastChest;
        DateTime openedAt, nextId = DateTime.MinValue, lastSave = DateTime.MinValue;
        readonly Queue<int> queue = new Queue<int>();
        readonly Dictionary<int, DateTime> asked = new Dictionary<int, DateTime>();
        readonly HashSet<int> retried = new HashSet<int>();
        string lastSig = "", savedIds = "";
        bool announced;
        int leftChest; DateTime leftAt;            // the chest we just stepped away from (pack inside it, short close)
        const int REOPEN_GRACE_MS = 120000;
        internal string StatusText = "";

        string Dir { get { return Path.Combine(Path.Combine(Path.Combine(PluginCore.PluginDir, "data"), Util.SafeName(P.ServerName)), "chests"); } }
        internal string FileOf(int chest) { return Path.Combine(Dir, chest.ToString("X8") + ".json"); }
        internal string SettingKey(int chest) { return "ChestName." + Util.SafeName(P.ServerName) + "." + chest.ToString("X8"); }
        internal string LabelOf(int chest)
        {
            string n = Util.Get(Util.ReadKv(P.SettingsFile), SettingKey(chest));
            return n != "" ? n : "House chest " + chest.ToString("X8").Substring(4);
        }

        // ACE's house storage chest is called "Storage" (weenie 9687); dungeon chests and corpses are left alone.
        bool IsHouseChest(int id)
        {
            WorldObject wo = C.WorldFilter[id];
            return wo != null && wo.Name == "Storage" && wo.Container != C.CharacterFilter.Id;
        }

        List<WorldObject> Contents(int chest)
        {
            var list = new List<WorldObject>();
            var packs = new HashSet<int> { chest };
            foreach (WorldObject wo in C.WorldFilter.GetByContainer(chest)) { list.Add(wo); if (wo.ObjectClass == ObjectClass.Container) packs.Add(wo.Id); }
            if (packs.Count > 1)   // packs inside the chest: only what the server has shown (open them to include them)
                foreach (int p in packs) if (p != chest) foreach (WorldObject wo in C.WorldFilter.GetByContainer(p)) list.Add(wo);
            return list;
        }

        DateTime reopenedAt = DateTime.MinValue;
        const int EMPTY_TRUST_MS = 10000;

        bool InsideChest(int id, int chest)
        {
            WorldObject wo = C.WorldFilter[id];
            return wo != null && wo.Container == chest;
        }

        bool FileHasItems(int chest)
        {
            try { string f = FileOf(chest); return File.Exists(f) && File.ReadAllText(f).Contains("\"gear\":true"); } catch { return false; }
        }

        bool Done(WorldObject wo)
        {
            AppraisalRecord r;
            return P.Appraisal.Records.TryGetValue(wo.Id, out r) && r.At >= openedAt;
        }

        internal void Tick()
        {
            if (!P.InGame || !P.Tracked || !P.ChestsOn) { open = 0; return; }
            DateTime now = DateTime.Now;
            int oc = C.Actions.OpenedContainer;   // read-only: which container the player has open
            if (open != 0 && oc != open && oc != 0 && InsideChest(oc, open)) oc = open;   // a pack inside the chest: still this chest
            if (oc != open)
            {
                // Closing: nothing is saved here (the client may already have dropped the contents);
                // the snapshot was kept up to date while the chest was open.
                if (open != 0) { leftChest = open; leftAt = now; }
                open = 0; StatusText = "";
                if (oc != 0 && IsHouseChest(oc))
                {
                    bool back = oc == leftChest && (now - leftAt).TotalMilliseconds < REOPEN_GRACE_MS;
                    open = oc; LastChest = oc;
                    if (!back)
                    {
                        openedAt = now; queue.Clear(); asked.Clear(); retried.Clear(); lastSig = ""; savedIds = ""; announced = false;
                        P.Chat(LabelOf(oc) + " opened: IDing the gear inside (read-only, nothing is moved). Rename it: /cg chest name <label>");
                    }
                    else { lastSig = ""; reopenedAt = now; }   // same chest again within 2 min: keep going, no new chat line
                }
            }
            if (open == 0) return;

            var items = Contents(open);
            int gear = 0, done = 0;
            foreach (WorldObject wo in items)
            {
                if (!Scanner.IsGear(wo)) continue;
                gear++;
                if (Done(wo)) { done++; continue; }
                DateTime t;
                if (!asked.TryGetValue(wo.Id, out t)) { asked[wo.Id] = DateTime.MinValue; queue.Enqueue(wo.Id); }
                else if (t != DateTime.MinValue && (now - t).TotalMilliseconds > NO_ANSWER_MS && retried.Add(wo.Id)) { asked[wo.Id] = DateTime.MinValue; queue.Enqueue(wo.Id); }
            }
            // one ID at a time, and never while the character's own scan is running
            if (queue.Count > 0 && now >= nextId && !P.Scanner.Scanning)
            {
                int id = queue.Dequeue();
                WorldObject wo = C.WorldFilter[id];
                if (wo != null && !Done(wo)) { C.Actions.RequestId(id); asked[id] = now; }
                nextId = now.AddMilliseconds(ID_EVERY_MS);
            }
            bool waiting = false;
            foreach (var kv in asked) if (kv.Value != DateTime.MinValue && !retried.Contains(kv.Key)) { WorldObject wo = C.WorldFilter[kv.Key]; if (wo != null && !Done(wo)) waiting = true; }
            bool complete = done >= gear || (queue.Count == 0 && !waiting);
            StatusText = LabelOf(open) + ": " + done + "/" + gear + " gear ID'd" + (complete ? " - saved" : "");

            if ((now - openedAt).TotalMilliseconds < FIRST_SAVE_MS || (now - reopenedAt).TotalMilliseconds < FIRST_SAVE_MS) return;
            // Contents may arrive late: never let an empty view wipe a chest that had gear, unless it stays empty 10 s.
            if (items.Count == 0 && (now - (reopenedAt > openedAt ? reopenedAt : openedAt)).TotalMilliseconds < EMPTY_TRUST_MS && FileHasItems(open)) return;
            string sig = items.Count + ":" + gear + ":" + done;
            foreach (WorldObject wo in items) sig += "," + wo.Id;
            if (sig != lastSig && (now - lastSave).TotalMilliseconds >= SAVE_EVERY_MS)
            {
                lastSig = sig; lastSave = now;
                Save(open, items, complete);
                string ids = ""; foreach (WorldObject wo in items) ids += "," + wo.Id;
                if (announced && complete && ids != savedIds) P.Rec.RunPlanner(false, true);   // something taken out / put in: refresh picks quietly
                savedIds = ids;
            }
            if (complete && !announced && sig == lastSig)
            {
                announced = true;
                P.Chat(LabelOf(open) + " saved: " + done + "/" + gear + " gear pieces. Updating recommendations...");
                P.Rec.RunPlanner(false);
            }
        }

        void Save(int chest, List<WorldObject> items, bool complete)
        {
            WorldObject cw = C.WorldFilter[chest];
            var j = new Json();
            j.Open();
            j.Num("schema", 1);
            j.Str("tool", "CharacterGear " + typeof(ChestScanner).Assembly.GetName().Version);
            j.Str("kind", "chest");
            j.Str("server", P.ServerName);
            j.Str("chestId", chest.ToString("X8"));
            j.Str("chestName", cw != null ? cw.Name : "");
            j.Str("label", LabelOf(chest));
            j.Str("openedBy", P.CharName);
            j.Str("seenAt", DateTime.Now.ToString("yyyy-MM-ddTHH:mm:ss"));
            j.Bool("complete", complete);
            // The opener's spells: weapon ID numbers include the opener's weapon auras, the planner strips them.
            P.Scanner.WriteEnchantments(j, "openerEnchantments");
            j.Key("items"); j.OpenArr();
            int me = C.CharacterFilter.Id;
            foreach (WorldObject wo in items) P.Scanner.WriteItem(j, wo, me, "chest");
            j.CloseArr();
            j.Close();
            Util.WriteText(FileOf(chest), j.ToString());
        }

        internal void Rename(string label)
        {
            int chest = open != 0 ? open : LastChest;
            if (chest == 0) { P.Chat("Open a house chest first, then: /cg chest name <label>"); return; }
            var d = Util.ReadKv(P.SettingsFile);
            label = (label ?? "").Trim();
            if (label == "") d.Remove(SettingKey(chest)); else d[SettingKey(chest)] = label;
            Util.WriteKv(P.SettingsFile, d);
            P.Chat("Chest " + chest.ToString("X8") + " is now called '" + LabelOf(chest) + "'." + (open == chest ? "" : " Open it once more to update its file."));
            if (open == chest) lastSig = "";   // re-save with the new label
        }
    }
}
