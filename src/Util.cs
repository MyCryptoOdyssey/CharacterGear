using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;

namespace CharacterGear
{
    internal static class Util
    {
        internal static string SafeName(string s)
        {
            s = s ?? "";
            foreach (char c in Path.GetInvalidFileNameChars()) s = s.Replace(c, '_');
            return s;
        }

        internal static Dictionary<string, string> ReadKv(string path)
        {
            var d = new Dictionary<string, string>();
            try
            {
                if (!File.Exists(path)) return d;
                foreach (string line in File.ReadAllLines(path))
                {
                    int i = line.IndexOf('=');
                    if (i > 0) d[line.Substring(0, i)] = line.Substring(i + 1);
                }
            }
            catch (Exception ex) { Debug.WriteLine(ex); }
            return d;
        }

        internal static void WriteKv(string path, Dictionary<string, string> d)
        {
            var sb = new StringBuilder();
            foreach (var kv in d) sb.Append(kv.Key).Append('=').Append(kv.Value).Append("\r\n");
            WriteText(path, sb.ToString());
        }

        internal static string Get(Dictionary<string, string> d, string k) { string v; return d.TryGetValue(k, out v) ? v : ""; }

        // Write via a temp file so a reader never sees half a file.
        internal static void WriteText(string path, string text)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                string tmp = path + ".tmp";
                File.WriteAllText(tmp, text, new UTF8Encoding(false));
                if (File.Exists(path)) File.Delete(path);
                File.Move(tmp, path);
            }
            catch (Exception ex) { Debug.WriteLine(ex); ErrorLog("write " + path + ": " + ex.Message); }
        }

        internal static void ErrorLog(string msg)
        {
            try
            {
                string path = Path.Combine(PluginCore.PluginDir, "errors.log");
                File.AppendAllText(path, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + " | " + msg + Environment.NewLine);
            }
            catch { }
        }
    }

    // Tiny JSON writer (the plugin targets .NET 4 without extra libraries).
    internal class Json
    {
        readonly StringBuilder sb = new StringBuilder();
        readonly Stack<bool> first = new Stack<bool>();
        bool afterKey;

        void Sep()
        {
            if (afterKey) { afterKey = false; return; }
            if (first.Count > 0)
            {
                if (!first.Peek()) sb.Append(',');
                first.Pop(); first.Push(false);
            }
        }
        public void Open() { Sep(); sb.Append('{'); first.Push(true); }
        public void Close() { first.Pop(); sb.Append('}'); }
        public void OpenArr() { Sep(); sb.Append('['); first.Push(true); }
        public void CloseArr() { first.Pop(); sb.Append(']'); }
        public void Key(string k) { Sep(); Esc(k); sb.Append(':'); afterKey = true; }
        public void Val(long v) { Sep(); sb.Append(v.ToString(CultureInfo.InvariantCulture)); }
        public void Num(string k, long v) { Key(k); Val(v); }
        public void Dbl(string k, double v)
        {
            Key(k); Sep();
            if (double.IsNaN(v) || double.IsInfinity(v)) sb.Append("null");
            else sb.Append(v.ToString("R", CultureInfo.InvariantCulture));
        }
        public void Str(string k, string v) { Key(k); Sep(); if (v == null) sb.Append("null"); else Esc(v); }
        public void Bool(string k, bool v) { Key(k); Sep(); sb.Append(v ? "true" : "false"); }

        void Esc(string s)
        {
            sb.Append('"');
            foreach (char c in s)
            {
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (c < 0x20) sb.Append("\\u").Append(((int)c).ToString("x4"));
                        else sb.Append(c);
                        break;
                }
            }
            sb.Append('"');
        }
        public override string ToString() { return sb.ToString(); }
    }
}
