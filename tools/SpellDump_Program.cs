using System; using System.IO; using System.Linq; using System.Text; using ACE.DatLoader;
System.Text.Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance);
var dir = args[0]; var outDir = args[1];
var pd = new PortalDatDatabase(System.IO.Path.Combine(dir, "client_portal.dat"), false);
var t = pd.SpellTable;
var sb = new StringBuilder("spell_id\tname\tcategory_id\tcategory\tpower\tschool\tduration\n");
foreach (var kv in t.Spells.OrderBy(k => k.Key))
{
    var s = kv.Value;
    sb.Append($"{kv.Key}\t{s.Name}\t{(uint)s.Category}\t{s.Category}\t{s.Power}\t{s.School}\t{s.Duration}\n");
}
File.WriteAllText(Path.Combine(outDir, "spell_categories.tsv"), sb.ToString());
var ss = new StringBuilder("set_id\tpieces\tspell_ids\n");
foreach (var kv in t.SpellSet.OrderBy(k => k.Key))
    foreach (var tier in kv.Value.SpellSetTiers.OrderBy(k => k.Key))
        ss.Append($"{kv.Key}\t{tier.Key}\t{string.Join(",", tier.Value.Spells)}\n");
File.WriteAllText(Path.Combine(outDir, "set_bonuses.tsv"), ss.ToString());
Console.WriteLine($"spells={t.Spells.Count} sets={t.SpellSet.Count}");
var sk = new StringBuilder("skill_id\tname\tattr1\tattr2\tdivisor\tx\n");
foreach (var kv in pd.SkillTable.SkillBaseHash.OrderBy(k => k.Key))
    sk.Append($"{kv.Key}\t{kv.Value.Name}\t{kv.Value.Formula.Attr1}\t{kv.Value.Formula.Attr2}\t{kv.Value.Formula.Z}\t{kv.Value.Formula.X}\n");
File.WriteAllText(Path.Combine(outDir, "skills.tsv"), sk.ToString());
Console.WriteLine("skills=" + pd.SkillTable.SkillBaseHash.Count);
