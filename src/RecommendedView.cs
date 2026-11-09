// In-game "Recommended" window: shows the planner's picks for the logged-in character, one page per button
// (Your Suit / Weapons / Armor / Clothing / Jewelry / Aetheria / Essence / Elements / Spells). Reads data\<server>\<char>.recommended.tsv,
// which CharacterGearPlanner.exe writes. READ-ONLY: it only reads files and starts the planner.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;

namespace CharacterGear
{
    internal class RecommendedView
    {
        readonly PluginCore P;
        VirindiViewService.HudView view;
        VirindiViewService.Controls.HudStaticText info;
        readonly Dictionary<string, VirindiViewService.Controls.HudList> lists = new Dictionary<string, VirindiViewService.Controls.HudList>();
        readonly Dictionary<string, List<string[]>> rows = new Dictionary<string, List<string[]>>();
        string header = "";
        Process planner;
        DateTime fileTime = DateTime.MinValue;
        internal string LastPlannerResult = "";

        static readonly string[] Tabs = { "Suit", "Weapons", "Armor", "Clothing", "Jewelry", "Aetheria", "Essence", "Elements", "Spells" };
        static readonly string[] TabLabel = { "Your Suit", "Weapons", "Armor", "Clothing", "Jewelry", "Aetheria", "Essence", "Elements", "Spells" };
        // What each button shows - in the top line when you pick it (replaces any row detail shown before).
        static readonly string[] TabInfo = {
            "Your Suit - what you wear now, and what to change (--> Recommended). Click a row for details.",
            "Weapons - best weapon, off hand, and the best weapon for each damage type.",
            "Armor - the recommended armor pieces and why each one is picked.",
            "Clothing - the recommended underclothes and cloak, and why.",
            "Jewelry - the recommended neck, wrists, rings and trinket, and why.",
            "Aetheria - sigils for your Blue / Yellow / Red slots.",
            "Essence - summoning essences for your school (only clear upgrades).",
            "Elements - Legendary ward coverage for each damage type.",
            "Spells - Legendary cantrip coverage for your trained skills." };
        string currentTab = "Suit";
        readonly Dictionary<string, VirindiViewService.Controls.HudButton> tabButtons = new Dictionary<string, VirindiViewService.Controls.HudButton>();
        const string BTN_NORMAL = "Recommended gear", BTN_READY = "* Recommended gear - UPDATED *";
        bool announce;   // planner was started by a finished scan: tell the player when the picks are ready

        internal RecommendedView(PluginCore p) { P = p; }

        string PlannerExe { get { return Path.Combine(PluginCore.PluginDir, "CharacterGearPlanner.exe"); } }
        string DataDir { get { return Path.Combine(PluginCore.PluginDir, "data"); } }
        internal string RecFile { get { return Path.Combine(Path.Combine(DataDir, Util.SafeName(P.ServerName)), Util.SafeName(P.CharName) + ".recommended.tsv"); } }

        internal void Toggle()
        {
            if (view == null) Create();
            if (view == null) return;
            view.Visible = !view.Visible;
            if (view.Visible) { Load(); if (P.BtnRec != null) P.BtnRec.Text = BTN_NORMAL; }
        }

        void Create()
        {
            try
            {
                VirindiViewService.ViewProperties props; VirindiViewService.ControlGroup controls;
                new VirindiViewService.XMLParsers.Decal3XMLParser().ParseFromResource("CharacterGear.RecXML.xml", out props, out controls);
                view = new VirindiViewService.HudView(props, controls);
                info = (VirindiViewService.Controls.HudStaticText)view["lblRecInfo"];
                var btn = (VirindiViewService.Controls.HudButton)view["btnRecRefresh"];
                btn.Hit += (s, e) => P.Safe(() => RunPlanner(true));
                // Resizable: drag a corner; the tabs, lists and columns follow (VVS HudView.UserResizeable + Resize event).
                ReadColumnWidths();
                view.UserResizeable = true;
                view.MinimumClientArea = new System.Drawing.Size(MIN_W, MIN_H);
                view.MaximumClientArea = new System.Drawing.Size(1600, 1100);
                view.Resize += (s, e) => P.Safe(Relayout);
                foreach (string t in Tabs)
                {
                    var l = (VirindiViewService.Controls.HudList)view["lst" + t];
                    lists[t] = l;
                    string tab = t;
                    l.Click += (sender, row, col) => P.Safe(() => ShowRow(tab, row));
                    var b = (VirindiViewService.Controls.HudButton)view["btnTab" + t];
                    tabButtons[t] = b;
                    b.Hit += (s, e) => P.Safe(() => SelectTab(tab, true));
                }
                SelectTab(currentTab, false);
            }
            catch (Exception ex) { Util.ErrorLog("rec view: " + ex); view = null; }
        }

        // Column widths as designed in rec.xml (per list), scaled when the window is resized.
        readonly Dictionary<string, int[]> designCols = new Dictionary<string, int[]>();
        const int DESIGN_W = 800, DESIGN_H = 400, MIN_W = 780, MIN_H = 300;   // smaller squeezes the text

        // Show one list; the picked button reads "> Name"; the top line explains the page.
        void SelectTab(string tab, bool clicked)
        {
            currentTab = tab;
            for (int i = 0; i < Tabs.Length; i++)
            {
                VirindiViewService.Controls.HudList l; if (lists.TryGetValue(Tabs[i], out l)) l.Visible = Tabs[i] == tab;
                VirindiViewService.Controls.HudButton b; if (tabButtons.TryGetValue(Tabs[i], out b)) b.Text = Tabs[i] == tab ? "> " + TabLabel[i] : TabLabel[i];
            }
            if (info != null) info.Text = (header != "" ? header + "  -  " : "") + TabInfo[Array.IndexOf(Tabs, tab)];
        }
        void ReadColumnWidths()
        {
            try
            {
                var doc = new System.Xml.XmlDocument();
                using (var st = typeof(RecommendedView).Assembly.GetManifestResourceStream("CharacterGear.RecXML.xml")) doc.Load(st);
                foreach (System.Xml.XmlNode n in doc.SelectNodes("//control[@progid='DecalControls.List']"))
                {
                    var cols = new List<int>();
                    foreach (System.Xml.XmlNode c in n.SelectNodes("column")) cols.Add(int.Parse(c.Attributes["fixedwidth"].Value));
                    designCols[n.Attributes["name"].Value] = cols.ToArray();
                }
            }
            catch (Exception ex) { Util.ErrorLog("rec columns: " + ex.Message); }
        }

        void Relayout()
        {
            if (view == null) return;
            var size = view.ClientArea;
            int w = Math.Max(MIN_W, size.Width), h = Math.Max(MIN_H, size.Height);
            var rootL = view["layRoot"] as VirindiViewService.Controls.HudFixedLayout;
            if (rootL == null) return;
            rootL.SetControlRect("lblRecInfo", new System.Drawing.Rectangle(6, 4, w - 120, 34));
            rootL.SetControlRect("btnRecRefresh", new System.Drawing.Rectangle(w - 108, 6, 100, 22));
            // buttons: same size, spread over the full width
            int bw = (w - 8) / Tabs.Length;
            for (int i = 0; i < Tabs.Length; i++) rootL.SetControlRect("btnTab" + Tabs[i], new System.Drawing.Rectangle(4 + i * bw, 42, bw - 4, 22));
            foreach (string t in Tabs)
            {
                int lw = w - 4, lh = h - 74;
                rootL.SetControlRect("lst" + t, new System.Drawing.Rectangle(2, 70, lw, lh));
                int[] cols; VirindiViewService.Controls.HudList l;
                if (!designCols.TryGetValue("lst" + t, out cols) || !lists.TryGetValue(t, out l)) continue;
                // Columns sized to their longest text (~7 px a character); the last column gets everything left.
                // If the text doesn't fit, every column shrinks by the same factor.
                if (cols.Length == 0) continue;
                int avail = lw - 22;
                int[] ml; maxLen.TryGetValue(t, out ml);
                var want = new int[cols.Length]; int sum = 0;
                for (int i = 0; i < cols.Length; i++) { want[i] = ml != null && i < ml.Length ? (ml[i] > 0 ? ml[i] * 7 + 16 : 12) : cols[i]; sum += want[i]; }
                if (sum > avail) for (int i = 0; i < cols.Length; i++) want[i] = Math.Max(want[i] <= 12 ? 12 : 40, want[i] * avail / sum);
                int usedW = 0;
                for (int i = 0; i < cols.Length - 1 && i < l.ColumnCount; i++) { l.SetColumnWidth(i, want[i]); usedW += want[i]; }
                if (cols.Length - 1 < l.ColumnCount) l.SetColumnWidth(cols.Length - 1, Math.Max(40, avail - usedW));
            }
        }

        internal void Dispose() { try { if (view != null) { view.Dispose(); view = null; } } catch { } }

        // Click a row: the full text (columns are narrow) goes to the info line at the top.
        void ShowRow(string tab, int row)
        {
            List<string[]> r;
            if (!rows.TryGetValue(tab, out r) || row < 0 || row >= r.Count) return;
            var x = r[row];
            string why = x.Length > 5 ? x[5] : "";
            info.Text = (tab == "Elements" || tab == "Spells") ? x[4]
                : x[1] + ": " + x[2] + (x[3] != "" ? " (" + x[3] + ")" : "") + (x[4] != "" ? " - " + x[4] : "") + (why != "" ? " [" + why + "]" : "");
        }

        internal void Load()
        {
            rows.Clear(); header = "";
            string path = RecFile;
            if (!File.Exists(path))
            {
                if (info != null) info.Text = "No recommendations yet for " + P.CharName + ". Click Refresh (runs the planner).";
                Fill(); return;
            }
            fileTime = File.GetLastWriteTime(path);
            string profile = "", now = "", rec = "", gen = "";
            foreach (string line in File.ReadAllLines(path, Encoding.UTF8))
            {
                if (line.Trim() == "") continue;
                string[] f = line.Split('\t');
                if (line.StartsWith("#"))
                {
                    if (f[0] == "#profile") profile = f.Length > 1 ? f[1] : "";
                    else if (f[0] == "#scoreNow") now = f.Length > 1 ? f[1] : "";
                    else if (f[0] == "#scoreRec") rec = f.Length > 1 ? f[1] : "";
                    else if (f[0] == "#generated") gen = f.Length > 1 ? f[1] : "";
                    continue;
                }
                if (f.Length < 5) continue;
                if (!rows.ContainsKey(f[0])) rows[f[0]] = new List<string[]>();
                rows[f[0]].Add(f);
            }
            header = P.CharName + " (" + profile + ") | " + gen;
            Fill();
            SelectTab(currentTab, false);
        }

        void Fill()
        {
            maxLen.Clear();
            foreach (var kv in lists)
            {
                var l = kv.Value; l.ClearRows();
                List<string[]> r;
                if (!rows.TryGetValue(kv.Key, out r)) continue;
                foreach (var x in r)
                {
                    var row = l.AddRow();
                    string why = x.Length > 5 ? x[5] : "";
                    if (kv.Key == "Current") { Set(row, 0, x[1]); Set(row, 1, x[2]); Set(row, 2, x[4] == "keep" ? "keep" : x[4].Replace("* ", "")); Set(row, 3, why); }
                    else if (kv.Key == "Suit" || kv.Key == "Elements" || kv.Key == "Spells") { Set(row, 0, x[1]); Set(row, 1, x[2]); Set(row, 2, x[3]); Set(row, 3, why); }
                    else { Set(row, 0, x[1]); Set(row, 1, x[2]); Set(row, 2, x[3]); Set(row, 3, why != "" ? why : x[4]); }
                    // longest text per column, for content-fit column widths
                    int[] ml; if (!maxLen.TryGetValue(kv.Key, out ml)) maxLen[kv.Key] = ml = new int[4];
                    string[] shown = { x[1], x[2], x[3], (kv.Key == "Suit" || kv.Key == "Elements" || kv.Key == "Spells") ? why : (why != "" ? why : x[4]) };
                    for (int i = 0; i < 4; i++) ml[i] = Math.Max(ml[i], (shown[i] ?? "").Length);
                }
            }
            Relayout();
        }
        readonly Dictionary<string, int[]> maxLen = new Dictionary<string, int[]>();

        static void Set(VirindiViewService.Controls.HudList.HudListRowAccessor row, int col, string text)
        {
            var t = row[col] as VirindiViewService.Controls.HudStaticText;
            if (t != null) t.Text = text ?? "";
        }

        // Start the planner (outside the game, hidden). It reads every character file and writes the reports.
        // quiet = a background refresh after items moved: no chat line, the button still shows UPDATED.
        bool rerun, rerunQuiet = true, quietRun, summary;
        internal void RunPlanner(bool fromButton, bool quiet = false)
        {
            if (planner != null && !planner.HasExited && !fromButton) { rerun = true; rerunQuiet &= quiet; return; }   // files changed mid-run: go again after
            if (!fromButton) { if (quiet) quietRun = !announce; else { announce = true; quietRun = false; } }
            else summary = true;   // Refresh: print the summary too
            if (!File.Exists(PlannerExe)) { if (fromButton) P.Chat("CharacterGearPlanner.exe is not in " + PluginCore.PluginDir + "."); return; }
            if (planner != null && !planner.HasExited) { if (fromButton) P.Chat("The planner is already running."); return; }
            // A quiet refresh (equip change, items moved) plans only this character and skips the HTML report: ~5 s
            // instead of ~16 s. Scans and Refresh run the full planner (every character + the report).
            bool quick = quiet && !fromButton && !announce;
            string args = "\"" + DataDir + "\" --no-open" + (quick ? " --char \"" + P.CharName + "\"" : "");
            var psi = new ProcessStartInfo(PlannerExe, args) { UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden };
            planner = Process.Start(psi);
            if (info != null && fromButton) info.Text = "Planner running...";
        }

        // The planner's "#chat" lines: upgrades, ward/spell coverage, essence, report path.
        void PrintSummary()
        {
            try
            {
                foreach (string line in File.ReadAllLines(RecFile, Encoding.UTF8))
                    if (line.StartsWith("#chat\t")) P.Chat(line.Substring(6));
            }
            catch (Exception ex) { Util.ErrorLog("summary: " + ex.Message); }
        }

        // Called every tick: when the planner has finished, reload the window if it is open.
        internal void Tick()
        {
            if (planner != null && planner.HasExited)
            {
                LastPlannerResult = "planner finished " + DateTime.Now.ToString("HH:mm:ss") + " (exit " + planner.ExitCode + ")";
                planner.Dispose(); planner = null;
                if (view != null && view.Visible) Load();
                else if ((announce || quietRun) && File.Exists(RecFile))
                {
                    if (P.BtnRec != null) P.BtnRec.Text = BTN_READY;
                    if (announce) P.Chat("Recommendations ready for " + P.CharName + " - click Recommended gear (or /cg rec).");
                }
                if ((announce || summary) && File.Exists(RecFile)) PrintSummary();
                announce = false; quietRun = false; summary = false;
                if (rerun) { bool q = rerunQuiet; rerun = false; rerunQuiet = true; RunPlanner(false, q); }
            }
        }
    }
}
