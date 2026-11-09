// CharacterGear Planner - Milestone 2: wieldability report.
// Usage: CharacterGearPlanner.exe [data folder] [--no-open]
// Default data folder: C:\Games\Decal Plugins\CharacterGear\data (the collector's output).
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;

namespace CharacterGear.Planner
{
    internal static class Program
    {
        const string DefaultData = @"C:\Games\Decal Plugins\CharacterGear\data";

        static int Main(string[] args)
        {
            string data = DefaultData; bool open = true;
            string only = null;   // --char "<name>": plan only that character, keep the HTML report as it is (quick refresh)
            for (int i = 0; i < args.Length; i++)
            {
                if (args[i] == "--no-open") open = false;
                else if (args[i] == "--char" && i + 1 < args.Length) { only = args[++i]; open = false; }
                else data = args[i];
            }
            if (!Directory.Exists(data)) { Console.WriteLine("No data folder: " + data); Pause(open); return 1; }
            bool got;
            var mutex = new System.Threading.Mutex(false, "CharacterGearPlanner");
            try { got = mutex.WaitOne(60000); } catch (System.Threading.AbandonedMutexException) { got = true; }
            if (!got) { Console.WriteLine("Another planner is still running."); return 2; }
            Tables.Load();
            var weights = Weights.Load(data);
            int made = 0;
            foreach (string serverDir in Directory.GetDirectories(data))
            {
                if (Directory.GetFiles(serverDir, "*.json").Length == 0) continue;
                var chars = Loader.LoadServer(serverDir);
                if (chars.Count == 0) continue;
                var chests = Loader.LoadChests(serverDir);          // house chest snapshots = extra holders
                Loader.DropOlderCopies(chars, chests);
                Loader.TagLookAlikes(chars.SelectMany(c => c.Items).Concat(chests.SelectMany(c => c.Items)));
                Profiles.Load(data, chars.Select(c => c.Name));
                string server = chars[0].Server != "" ? chars[0].Server : Path.GetFileName(serverDir);
                if (only != null && !chars.Any(c => c.Name == only)) continue;   // not on this server
                if (only != null)
                {
                    // quick refresh: start the suit search from the last full recommendation
                    string prev = Path.Combine(serverDir, SafeName(only) + ".recommended.tsv");
                    try
                    {
                        if (File.Exists(prev))
                            foreach (string line in File.ReadAllLines(prev))
                                if (line.StartsWith("#suit\t"))
                                {
                                    var list = new List<KeyValuePair<string, long>>();
                                    foreach (string part in line.Substring(6).Split(','))
                                    {
                                        int c2 = part.LastIndexOf(':'); long m;
                                        if (c2 > 0 && long.TryParse(part.Substring(c2 + 1), out m)) list.Add(new KeyValuePair<string, long>(part.Substring(0, c2), m));
                                    }
                                    Report.Warm[only] = list;
                                }
                    }
                    catch { }
                }
                string html = Report.Build(server, chars, chests, weights, only);
                string outPath = Path.Combine(serverDir, "CharacterGear_Report.html");
                if (only == null) WriteSafe(outPath, html);
                Console.WriteLine("Report: " + outPath + "  (" + chars.Count + " characters, " + chests.Count + " house chests)");
                // one small file per character for the in-game Recommended window
                foreach (var kv in Report.Rec)
                {
                    var sbr = new StringBuilder();
                    string[] head; Report.RecHead.TryGetValue(kv.Key, out head);
                    sbr.Append("#generated\t" + DateTime.Now.ToString("yyyy-MM-dd HH:mm") + "\n");
                    if (head != null) sbr.Append("#profile\t" + head[0] + "\n#scoreNow\t" + head[1] + "\n#scoreRec\t" + head[2] + "\n#taken\t" + head[3] + "\n#dealt\t" + head[4] + "\n");
                    // Short summary the plugin prints in game chat after a scan or Refresh.
                    var rows = kv.Value;
                    var ups = new List<string>();
                    for (int i = 1; i < rows.Count; i++) if (rows[i][0] == "Suit" && rows[i][1].StartsWith("   -->") && rows[i - 1][0] == "Suit") { string sl = rows[i - 1][1]; int dot = sl.IndexOf(". "); ups.Add(dot >= 0 ? sl.Substring(dot + 2) : sl); }
                    Func<string, string> cov = tab => { var r = rows.FirstOrDefault(x => x[0] == tab && x[1] == "Coverage"); var m = r != null ? System.Text.RegularExpressions.Regex.Match(r[2], @"^(\d+) of (\d+)") : null; return m != null && m.Success ? m.Groups[1].Value + "/" + m.Groups[2].Value : "-"; };
                    var ess = rows.Where(x => x[0] == "Essence").ToList();
                    string essTxt = ess.Any(x => x[1] == "Summoning") ? "Summoning not trained"
                        : ess.Count(x => x[4].StartsWith("* ")) > 0 ? ess.Count(x => x[4].StartsWith("* ")) + " essence upgrade(s)" : "no essence upgrade";
                    sbr.Append("#chat\t" + kv.Key + (head != null ? " (" + head[0] + ")" : "") + ": " + (ups.Count == 0 ? "suit is already the best you own" : ups.Count + " upgrade(s) - " + string.Join(", ", ups.ToArray())) + "\n");
                    sbr.Append("#chat\tWards " + cov("Elements") + " Legendary | Spells " + cov("Spells") + " Legendary | Essence: " + essTxt + "\n");
                    sbr.Append("#chat\tFull report: " + outPath + "\n");
                    string suitIds; if (Report.RecSuit.TryGetValue(kv.Key, out suitIds)) sbr.Append("#suit\t" + suitIds + "\n");
                    foreach (var row in kv.Value) sbr.Append(string.Join("\t", row.Select(x => (x ?? "").Replace("\t", " ").Replace("\n", " ")).ToArray()) + "\n");
                    WriteSafe(Path.Combine(serverDir, SafeName(kv.Key) + ".recommended.tsv"), sbr.ToString());
                }
                Report.Rec.Clear(); Report.RecHead.Clear(); Report.RecSuit.Clear(); Report.Warm.Clear();
                made++;
                if (open) try { Process.Start(outPath); } catch { }
            }
            if (made == 0) Console.WriteLine("No scanned characters found in " + data);
            return 0;
        }
        static string SafeName(string s) { foreach (char ch in Path.GetInvalidFileNameChars()) s = s.Replace(ch, '_'); return s; }
        static void WriteSafe(string path, string text)
        {
            string tmp = path + ".tmp";
            File.WriteAllText(tmp, text, new UTF8Encoding(false));
            if (File.Exists(path)) File.Delete(path);
            File.Move(tmp, path);
        }
        static void Pause(bool interactive) { if (interactive) { Console.WriteLine("Press Enter."); Console.ReadLine(); } }
    }
}
