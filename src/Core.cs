// CharacterGear collector - Decal plugin (2026-11-08)
// READ-ONLY: it asks the server to ID (appraise) the items a character carries and writes one JSON
// file per character on this PC. It never moves, equips, uses, gives, drops, splits or sells anything
// and sends no chat commands. build.bat refuses to build if the source calls any other game action.
// Data:     <plugin folder>\data\<server>\<character>.json   (read by the planner)
// Settings: <plugin folder>\settings.txt  (TrackAll=1|0, Track.<server>__<char>=1|0 per-character override)
// Commands: /cg status | /cg scan | /cg rec | /cg plan | /cg track on|off|default | /cg trackall on|off
//           /cg chests on|off | /cg chest [name <label>]   (house chest snapshots, see ChestScanner.cs)
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using Decal.Adapter;
using Decal.Adapter.Wrappers;

namespace CharacterGear
{
    [FriendlyName("CharacterGear")]
    public class PluginCore : PluginBase
    {
        internal static PluginCore Instance;
        internal VirindiViewService.HudView View;
        VirindiViewService.Controls.HudCheckBox chkTrackAll, chkTrackMe;
        VirindiViewService.Controls.HudStaticText lblState, lblSaved, lblWhy;
        internal VirindiViewService.Controls.HudButton BtnRec;
        System.Drawing.Color normalText = System.Drawing.Color.Empty;
        System.Windows.Forms.Timer timer;
        internal Scanner Scanner;
        internal ChestScanner Chests;
        internal Appraisal Appraisal;
        internal RecommendedView Rec;
        internal bool InGame;
        internal string CharName = "", ServerName = "";
        bool updatingUi;

        protected override void Startup()
        {
            Instance = this;
            Scanner = new Scanner(this);
            Chests = new ChestScanner(this);
            Appraisal = new Appraisal(this);
            Rec = new RecommendedView(this);
            try
            {
                VirindiViewService.ViewProperties properties;
                VirindiViewService.ControlGroup controls;
                var parser = new VirindiViewService.XMLParsers.Decal3XMLParser();
                parser.ParseFromResource("CharacterGear.ViewXML.xml", out properties, out controls);
                View = new VirindiViewService.HudView(properties, controls);
                chkTrackAll = (VirindiViewService.Controls.HudCheckBox)View["chkTrackAll"];
                chkTrackMe = (VirindiViewService.Controls.HudCheckBox)View["chkTrackMe"];
                lblState = (VirindiViewService.Controls.HudStaticText)View["lblState"];
                lblSaved = (VirindiViewService.Controls.HudStaticText)View["lblSaved"];
                lblWhy = (VirindiViewService.Controls.HudStaticText)View["lblWhy"];
                var btn = (VirindiViewService.Controls.HudButton)View["btnScan"];
                btn.Hit += (s, e) => Safe(() => ScanNow());
                BtnRec = (VirindiViewService.Controls.HudButton)View["btnRec"];
                BtnRec.Hit += (s, e) => Safe(() => { if (InGame) Rec.Toggle(); });
                chkTrackAll.Change += (s, e) => Safe(() => { if (!updatingUi) SetTrackAll(chkTrackAll.Checked); });
                chkTrackMe.Change += (s, e) => Safe(() => { if (!updatingUi) SetTrackMe(chkTrackMe.Checked ? "1" : "0"); });
            }
            catch (Exception ex) { Debug.WriteLine(ex); }

            try
            {
                Core.CharacterFilter.LoginComplete += OnLoginComplete;
                Core.CharacterFilter.Logoff += OnLogoff;
                Core.CommandLineText += OnCommand;
                Appraisal.Start();   // listen-only: decodes the server's ID answers
                Core.WorldFilter.CreateObject += (s, e) => Safe(() => Scanner.OnObjectSeen(e.New));
                Core.WorldFilter.ChangeObject += (s, e) => Safe(() => Scanner.OnObjectChanged(e.Changed));
            }
            catch (Exception ex) { Debug.WriteLine(ex); }

            timer = new System.Windows.Forms.Timer();
            timer.Interval = Scanner.TICK_MS;
            timer.Tick += (s, e) => Safe(() => { Scanner.Tick(); Chests.Tick(); Rec.Tick(); UpdateUi(); });
            timer.Start();
        }

        protected override void Shutdown()
        {
            try
            {
                if (timer != null) { timer.Stop(); timer.Dispose(); timer = null; }
                Core.CharacterFilter.LoginComplete -= OnLoginComplete;
                Core.CharacterFilter.Logoff -= OnLogoff;
                Core.CommandLineText -= OnCommand;
                Appraisal.Stop();
            }
            catch (Exception ex) { Debug.WriteLine(ex); }
            try { if (Rec != null) Rec.Dispose(); } catch { }
            try { if (View != null) { View.Dispose(); View = null; } } catch { }
            Instance = null;
        }

        void OnLoginComplete(object sender, EventArgs e)
        {
            Safe(() =>
            {
                CharName = Core.CharacterFilter.Name ?? "";
                ServerName = Core.CharacterFilter.Server ?? "";
                InGame = true;
                if (Tracked) Scanner.ScheduleScan(8);   // give the server a few seconds to send the inventory
            });
        }

        void OnLogoff(object sender, LogoffEventArgs e)
        {
            // Last chance to write: Decal unloads plugins right after logout.
            Safe(() => { if (Tracked) Scanner.SaveIfDirty(); });
            InGame = false;
        }

        // ---------- tracking settings (one file for all characters) ----------
        internal static string PluginDir { get { return System.IO.Path.GetDirectoryName(typeof(PluginCore).Assembly.Location); } }
        string SettingsPath { get { return System.IO.Path.Combine(PluginDir, "settings.txt"); } }
        internal string SettingsFile { get { return SettingsPath; } }

        // House chest snapshots. Default ON; /cg chests on|off
        internal bool ChestsOn { get { return Util.Get(Util.ReadKv(SettingsPath), "Chests") != "0"; } }
        void SetChests(bool on)
        {
            var d = Util.ReadKv(SettingsPath); d["Chests"] = on ? "1" : "0"; Util.WriteKv(SettingsPath, d);
            Chat("House chest snapshots: " + (on ? "ON (open a house chest and its gear is ID'd and saved)" : "OFF") + ".");
        }
        string MyKey { get { return "Track." + Util.SafeName(ServerName) + "__" + Util.SafeName(CharName); } }

        // Scan history (one snapshot per finished scan). Default ON; /cg history on|off
        internal bool HistoryOn { get { return Util.Get(Util.ReadKv(SettingsPath), "History") != "0"; } }
        void SetHistory(bool on)
        {
            var d = Util.ReadKv(SettingsPath); d["History"] = on ? "1" : "0"; Util.WriteKv(SettingsPath, d);
            Chat("Scan history: " + (on ? "ON (one copy per finished scan, newest 50 kept per character)" : "OFF") + ".");
        }

        internal bool TrackAll { get { return Util.Get(Util.ReadKv(SettingsPath), "TrackAll") != "0"; } }
        // "" = follow Track all, "1" = always, "0" = never
        internal string TrackOverride { get { return Util.Get(Util.ReadKv(SettingsPath), MyKey); } }
        internal bool Tracked
        {
            get
            {
                if (CharName == "") return false;
                string o = TrackOverride;
                return o == "" ? TrackAll : o == "1";
            }
        }

        void SetTrackAll(bool on)
        {
            var d = Util.ReadKv(SettingsPath); d["TrackAll"] = on ? "1" : "0"; Util.WriteKv(SettingsPath, d);
            Chat("Track all characters: " + (on ? "ON" : "OFF") + ". Characters you set one by one keep their own setting.");
            AfterTrackChange();
        }
        void SetTrackMe(string v)
        {
            if (CharName == "") return;
            var d = Util.ReadKv(SettingsPath);
            if (v == "") d.Remove(MyKey); else d[MyKey] = v;
            Util.WriteKv(SettingsPath, d);
            Chat(CharName + ": " + (v == "" ? "follows Track all" : v == "1" ? "tracked" : "NOT tracked (no scan, no file update)") + ".");
            AfterTrackChange();
        }
        void AfterTrackChange()
        {
            if (Tracked && InGame && !Scanner.HasScanned) Scanner.ScheduleScan(1);
            if (!Tracked) Scanner.Cancel();
        }

        void ScanNow()
        {
            if (!InGame) return;
            if (!Tracked) { Chat(CharName + " is not tracked. Tick 'Track this character' first."); return; }
            Scanner.ScheduleScan(0);
        }

        void UpdateUi()
        {
            if (View == null || chkTrackAll == null) return;
            updatingUi = true;
            try
            {
                bool all = TrackAll, me = Tracked;
                if (chkTrackAll.Checked != all) chkTrackAll.Checked = all;
                if (chkTrackMe.Checked != me) chkTrackMe.Checked = me;
                string o = TrackOverride;
                string why = CharName == "" ? "" : o == "" ? "(follows Track all)" : "(set for this character)";
                if (lblWhy.Text != why) lblWhy.Text = why;
                string st = !InGame ? "Not in game" : !me ? "Not tracked" : Chests.StatusText != "" && !Scanner.Scanning ? Chests.StatusText : Scanner.StatusText;
                if (lblState.Text != st) lblState.Text = st;
                if (normalText == System.Drawing.Color.Empty) normalText = lblState.TextColor;
                var want = Scanner.Scanning ? System.Drawing.Color.Gold : normalText;   // bright while the scan runs
                if (lblState.TextColor != want) lblState.TextColor = want;
                string sv = Scanner.SavedText;
                if (lblSaved.Text != sv) lblSaved.Text = sv;
            }
            finally { updatingUi = false; }
        }

        void OnCommand(object sender, ChatParserInterceptEventArgs e)
        {
            Safe(() =>
            {
                string t = (e.Text ?? "").Trim();
                string lower = t.ToLowerInvariant();
                if (!lower.StartsWith("/cg")) return;
                if (t.Length > 3 && t[3] != ' ') return;
                e.Eat = true;
                string[] a = lower.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                string cmd = a.Length > 1 ? a[1] : "help";
                string arg = a.Length > 2 ? a[2] : "";
                if (cmd == "status")
                {
                    Chat((Tracked ? "Tracked" : "NOT tracked") + " " + (TrackOverride == "" ? "(follows Track all)" : "(set for this character)") +
                         " | Track all " + (TrackAll ? "ON" : "OFF") + " | History " + (HistoryOn ? "ON" : "OFF") + " | Chests " + (ChestsOn ? "ON" : "OFF") + " | " + Scanner.StatusText + " | " + Scanner.SavedText);
                    Chat("File: " + Scanner.DataPath + " | ID answers decoded: " + Appraisal.Parsed + (Appraisal.Failed > 0 ? ", failed: " + Appraisal.Failed : ""));
                }
                else if (cmd == "scan") ScanNow();
                else if (cmd == "rec" || cmd == "recommended") { if (InGame) Rec.Toggle(); }
                else if (cmd == "plan") Rec.RunPlanner(true);
                else if (cmd == "track" && (arg == "on" || arg == "off" || arg == "default")) SetTrackMe(arg == "on" ? "1" : arg == "off" ? "0" : "");
                else if (cmd == "trackall" && (arg == "on" || arg == "off")) SetTrackAll(arg == "on");
                else if (cmd == "history" && (arg == "on" || arg == "off")) SetHistory(arg == "on");
                else if (cmd == "chests" && (arg == "on" || arg == "off")) SetChests(arg == "on");
                else if (cmd == "chest" && arg == "name")
                {
                    int at = lower.IndexOf(" name", StringComparison.Ordinal);
                    Chests.Rename(at >= 0 ? t.Substring(at + 5) : "");   // original capitals kept
                }
                else if (cmd == "chest") Chat(Chests.StatusText != "" ? Chests.StatusText : "No house chest open." + (Chests.LastChest != 0 ? " Last: " + Chests.LabelOf(Chests.LastChest) : "") + " Rename while open: /cg chest name <label>");
                else Chat("/cg status | /cg scan | /cg track on|off|default | /cg trackall on|off | /cg history on|off | /cg chests on|off | /cg chest name <label>   (read-only: IDs items, never moves or uses them)");
            });
        }

        internal void Chat(string msg)
        {
            try { Host.Actions.AddChatText("[CharacterGear] " + msg, 5); } catch { }
        }

        internal void Safe(Action a)
        {
            try { a(); }
            catch (Exception ex)
            {
                Debug.WriteLine(ex);
                Util.ErrorLog(CharName + " | " + ServerName + " | " + ex);
            }
        }
    }
}
