// Appraisal: LISTENS to the server's ID answer (game event 0xF7B0 / 0x00C9, IdentifyObjectResponse) and keeps
// the exact contents. Why: Decal merges an item's own spells with spells cast ON it into one list; the raw answer
// marks cast-on spells with the 0x80000000 bit (ACE AppraiseInfo.AddEnchantments). Listen-only: sends nothing.
// Layout (ACE GameEventIdentifyObjectResponse + AppraiseInfo.Write):
//   guid, flags, success, [int table], [int64 table], [bool table], [float table], [string table], [did table],
//   [spellbook], [armor profile 8 floats], (weapon/creature/hook profiles, armor levels: not read)
//   table = ushort count, ushort buckets, then count x (uint key, value)
using System;
using System.Collections.Generic;
using System.Text;
using Decal.Adapter;

namespace CharacterGear
{
    public class AppraisalRecord
    {
        public uint Flags; public bool Success; public DateTime At;
        public readonly SortedDictionary<uint, int> Ints = new SortedDictionary<uint, int>();
        public readonly SortedDictionary<uint, long> Int64s = new SortedDictionary<uint, long>();
        public readonly SortedDictionary<uint, bool> Bools = new SortedDictionary<uint, bool>();
        public readonly SortedDictionary<uint, double> Floats = new SortedDictionary<uint, double>();
        public readonly SortedDictionary<uint, string> Strings = new SortedDictionary<uint, string>();
        public readonly SortedDictionary<uint, uint> Dids = new SortedDictionary<uint, uint>();
        public readonly List<uint> ItemSpells = new List<uint>();     // the item's own spells (cantrips, cast-on-use, proc)
        public readonly List<uint> CastOnSpells = new List<uint>();   // spells cast ON the item right now (bit 0x80000000)
        public float[] ArmorProfile;                                  // slash, pierce, bludgeon, cold, fire, acid, nether, lightning (with active banes)
    }

    public class Appraisal
    {
        const int EVT_ID_RESPONSE = 0x00C9;
        const uint ENCHANTMENT_BIT = 0x80000000;
        readonly PluginCore P;
        internal readonly Dictionary<int, AppraisalRecord> Records = new Dictionary<int, AppraisalRecord>();
        internal int Parsed, Failed;

        internal Appraisal(PluginCore p) { P = p; }

        internal void Start() { CoreManager.Current.EchoFilter.ServerDispatch += OnServer; }
        internal void Stop() { try { CoreManager.Current.EchoFilter.ServerDispatch -= OnServer; } catch { } }

        void OnServer(object sender, NetworkMessageEventArgs e)
        {
            try
            {
                if (e.Message == null || e.Message.Type != unchecked((int)0xF7B0)) return;
                int guid;
                AppraisalRecord rec = Parse(e.Message.RawData, out guid);
                if (rec == null) return;
                Records[guid] = rec;
                Parsed++;
            }
            catch (Exception ex)
            {
                Failed++;
                if (Failed <= 3) Util.ErrorLog("appraisal parse: " + ex.Message);
            }
        }

        // Decodes one 0xF7B0 message; null if it is not an ID answer. Public for the offline test.
        public static AppraisalRecord Parse(byte[] raw, out int guid)
        {
            guid = 0;
            {
                if (raw == null || raw.Length < 24) return null;
                int off = BitConverter.ToInt32(raw, 0) == unchecked((int)0xF7B0) ? 4 : 0;   // RawData may or may not start with the type
                // [recipient guid][sequence][event type][body]
                if (BitConverter.ToInt32(raw, off + 8) != EVT_ID_RESPONSE) return null;
                var r = new Reader(raw, off + 12);
                guid = (int)r.U32();
                var rec = new AppraisalRecord { At = DateTime.Now };
                rec.Flags = r.U32();
                rec.Success = r.U32() != 0;
                if ((rec.Flags & 0x0001) != 0) { int n = r.TableCount(); for (int i = 0; i < n; i++) { uint k = r.U32(); rec.Ints[k] = (int)r.U32(); } }
                if ((rec.Flags & 0x2000) != 0) { int n = r.TableCount(); for (int i = 0; i < n; i++) { uint k = r.U32(); rec.Int64s[k] = r.I64(); } }
                if ((rec.Flags & 0x0002) != 0) { int n = r.TableCount(); for (int i = 0; i < n; i++) { uint k = r.U32(); rec.Bools[k] = r.U32() != 0; } }
                if ((rec.Flags & 0x0004) != 0) { int n = r.TableCount(); for (int i = 0; i < n; i++) { uint k = r.U32(); rec.Floats[k] = r.F64(); } }
                if ((rec.Flags & 0x0008) != 0) { int n = r.TableCount(); for (int i = 0; i < n; i++) { uint k = r.U32(); rec.Strings[k] = r.Str16(); } }
                if ((rec.Flags & 0x1000) != 0) { int n = r.TableCount(); for (int i = 0; i < n; i++) { uint k = r.U32(); rec.Dids[k] = r.U32(); } }
                if ((rec.Flags & 0x0010) != 0)
                {
                    int n = (int)r.U32();
                    for (int i = 0; i < n; i++)
                    {
                        uint s = r.U32();
                        if ((s & ENCHANTMENT_BIT) != 0) rec.CastOnSpells.Add(s & ~ENCHANTMENT_BIT); else rec.ItemSpells.Add(s);
                    }
                }
                if ((rec.Flags & 0x0080) != 0)
                {
                    rec.ArmorProfile = new float[8];
                    for (int i = 0; i < 8; i++) rec.ArmorProfile[i] = r.F32();
                }
                return rec;
            }
        }

        // Little-endian reader over the raw message.
        class Reader
        {
            readonly byte[] b; int p;
            public Reader(byte[] bytes, int pos) { b = bytes; p = pos; }
            void Need(int n) { if (p + n > b.Length) throw new IndexOutOfRangeException("ID answer shorter than expected"); }
            public uint U32() { Need(4); uint v = BitConverter.ToUInt32(b, p); p += 4; return v; }
            public long I64() { Need(8); long v = BitConverter.ToInt64(b, p); p += 8; return v; }
            public double F64() { Need(8); double v = BitConverter.ToDouble(b, p); p += 8; return v; }
            public float F32() { Need(4); float v = BitConverter.ToSingle(b, p); p += 4; return v; }
            public int TableCount() { Need(4); int n = BitConverter.ToUInt16(b, p); p += 4; return n; }   // ushort count + ushort buckets
            public string Str16()
            {
                Need(2); int len = BitConverter.ToUInt16(b, p); p += 2;
                Need(len); string s = Encoding.GetEncoding(1252).GetString(b, p, len); p += len;
                int pad = (4 - ((2 + len) % 4)) % 4; Need(pad); p += pad;
                return s;
            }
        }
    }
}
