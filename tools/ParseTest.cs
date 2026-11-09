using System; using System.IO; using System.Text;
class T {
  static void Table(BinaryWriter w, int n) { w.Write((ushort)n); w.Write((ushort)16); }
  static void Str(BinaryWriter w, string s) { w.Write((ushort)s.Length); w.Write(Encoding.GetEncoding(1252).GetBytes(s)); int pad=(4-((2+s.Length)%4))%4; for(int i=0;i<pad;i++) w.Write((byte)0); }
  static int Main() {
    var ms = new MemoryStream(); var w = new BinaryWriter(ms);
    w.Write(unchecked((int)0xF7B0)); w.Write(0x50000001); w.Write(7); w.Write(0x00C9);   // type, recipient, seq, event
    w.Write(unchecked((int)0x8009ABCD));                                                     // object guid
    w.Write((uint)(0x1|0x2|0x4|0x8|0x10|0x80|0x1000|0x2000)); w.Write(1u);                    // flags, success
    Table(w,3); w.Write(28u); w.Write(496); w.Write(158u); w.Write(7); w.Write(160u); w.Write(180);
    Table(w,1); w.Write(4u); w.Write(123456789012L);                                          // int64 (comes before bool)
    Table(w,1); w.Write(69u); w.Write(1u);                                                    // bool
    Table(w,1); w.Write(13u); w.Write(1.2d);                                                  // float
    Table(w,2); w.Write(1u); Str(w,"Leather Cuirass"); w.Write(16u); Str(w,"abc");           // strings (odd lengths -> padding)
    Table(w,1); w.Write(28u); w.Write(1540u);                                                 // did
    w.Write(3); w.Write(4671u); w.Write(5429u); w.Write(2108u | 0x80000000u);                 // spellbook
    float[] ap = {1.7f,1.7f,1.7f,1.7f,1.7f,1.9f,1f,2.2f}; foreach (var f in ap) w.Write(f);   // armor profile
    int guid; var r = CharacterGear.Appraisal.Parse(ms.ToArray(), out guid);
    bool ok = r != null && guid == unchecked((int)0x8009ABCD) && r.Ints[28]==496 && r.Ints[160]==180 && r.Int64s[4]==123456789012L
      && r.Bools[69] && r.Floats[13]==1.2 && r.Strings[1]=="Leather Cuirass" && r.Strings[16]=="abc" && r.Dids[28]==1540
      && r.ItemSpells.Count==2 && r.ItemSpells[0]==4671 && r.CastOnSpells.Count==1 && r.CastOnSpells[0]==2108 && Math.Abs(r.ArmorProfile[7]-2.2f)<1e-6;
    Console.WriteLine(ok ? "PARSE TEST PASSED" : "PARSE TEST FAILED");
    // also: non-ID event returns null
    var b = ms.ToArray(); BitConverter.GetBytes(0x01AD).CopyTo(b, 12);
    Console.WriteLine(CharacterGear.Appraisal.Parse(b, out guid) == null ? "non-ID event ignored: OK" : "non-ID event: FAILED");
    return ok ? 0 : 1;
  }
}
