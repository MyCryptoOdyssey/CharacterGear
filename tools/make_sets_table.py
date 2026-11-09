# Builds docs/ARMOR_SETS.html from tables/spells.tsv, set_names.tsv, set_bonuses.tsv, skills.tsv.
# Run: python -I tools/make_sets_table.py   (from the project folder)
import html
sp = {}
for l in open('tables/spells.tsv', encoding='utf-8'):
    if l[0] == '#' or l.startswith('spell_id'): continue
    f = l.rstrip('\n').split('\t'); sp[int(f[0])] = f
names = {}
for l in open('tables/set_names.tsv', encoding='utf-8'):
    if l[0] == '#' or l.startswith('set_id'): continue
    a, b = l.rstrip('\n').split('\t'); names[int(a)] = b
skills = {}
for l in open('tables/skills.tsv', encoding='utf-8'):
    if l[0] == '#' or l.startswith('skill_id'): continue
    f = l.split('\t'); skills[int(f[0])] = f[1]
attrs = {1: 'Strength', 2: 'Endurance', 3: 'Quickness', 4: 'Coordination', 5: 'Focus', 6: 'Self'}
ratings = {307: 'Damage rating', 308: 'Damage resist rating', 313: 'Crit rating', 314: 'Crit damage rating', 315: 'Crit resist rating', 316: 'Crit damage resist rating'}
tiers = {}
for l in open('tables/set_bonuses.tsv', encoding='utf-8'):
    if l[0] == '#' or l.startswith('set_id'): continue
    s, p, ids = l.rstrip('\n').split('\t'); tiers.setdefault(int(s), []).append((int(p), [int(x) for x in ids.split(',') if x]))

def eff(i):
    f = sp.get(i)
    if not f: return 'spell %d' % i
    t = int(f[7] or 0); k = int(f[8] or 0)
    try: vv = float(f[9])
    except ValueError: vv = None
    what = ''
    if vv is not None:
        if t & 0x10: what = '%s +%g' % (skills.get(k, 'skill %d' % k), vv)
        elif t & 0x1 and k == 0: what = 'all attributes +%g' % vv
        elif t & 0x1: what = '%s +%g' % (attrs.get(k, 'attribute %d' % k), vv)
        elif t & 0x4 and k in ratings: what = '%s +%g' % (ratings[k], vv)
        elif t & 0x80: what = 'Armor (all parts) +%g' % vv
        elif t & 0x2: what = 'vital +%g' % vv
    return html.escape(f[1]) + (' <span class="v">(%s)</span>' % html.escape(what) if what else '')

skip = {0, 1, 2, 3, 34, 42, 43, 44, 45}
groups = [('Loot attribute sets (any loot armor; cap 5 pieces)', list(range(13, 30))),
          ('Society armor and Gauntlet jewelry', [30, 131, 132, 133, 134, 135, 136, 137]),
          ('Quest armor and clothing', [4, 5, 6, 7, 8, 9, 10, 11, 12, 31, 32, 33, 46, 47, 48, 89, 91, 92, 93, 130] + list(range(94, 130))),
          ('Rare sets', [40, 41]), ('Aetheria (counts sigil levels)', list(range(35, 40))),
          ('Cloaks (counts cloak level)', list(range(49, 89)) + [90]), ('Paragon weapons', [138, 139, 140])]
css = (':root{--bg:#fbfaf7;--fg:#1d1c1a;--mut:#6b6760;--line:#e3dfd6;--card:#fff;--acc:#6b4e16}'
       '@media (prefers-color-scheme:dark){:root{--bg:#161513;--fg:#ece9e2;--mut:#a29d93;--line:#34312c;--card:#1f1d1a;--acc:#e0c27a}}'
       'body{margin:0;background:var(--bg);color:var(--fg);font:15px/1.45 system-ui,Segoe UI,sans-serif}main{max-width:1100px;margin:0 auto;padding:16px}'
       'h2{border-bottom:2px solid var(--line);margin-top:1.6em}table{border-collapse:collapse;width:100%;background:var(--card);margin-bottom:10px}'
       'th,td{border-bottom:1px solid var(--line);padding:5px 8px;text-align:left;vertical-align:top}th{color:var(--mut);font-size:.85em}'
       '.v{color:var(--acc);font-size:.9em}.meta{color:var(--mut)}td.n{white-space:nowrap;font-weight:600}')
out = ['<!doctype html><html lang="en"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1"><title>Armor Set Bonuses</title><style>' + css + '</style></head><body><main>',
       '<h1>Armor Set Bonuses</h1><p class="meta">Every item set in the game, from the stock end-of-retail spell table (client_portal.dat) read with ACE\'s own reader. Set id = item property 265. '
       'Rules (ACE WorldObject_Set.GetSpellSet): the bonus depends on how many pieces of the set you wear (Aetheria and cloaks count item levels instead), capped at the top tier; a count between tiers uses the tier below. '
       'Set bonuses apply even if the pieces\' own magic cannot activate (ACE: "set spells are not affected by mana"). Set spells are their own spell families, so they stack with buffs and cantrips.</p>']
done = set()
def table(title, rows):
    out.append('<h2>%s</h2><table><tr><th>Set</th><th>Pieces</th><th>Bonus spells (effect)</th></tr>' % html.escape(title))
    for s in rows:
        done.add(s)
        ts = sorted(tiers[s])
        for j, (p, ids) in enumerate(ts):
            first = '<td class="n" rowspan="%d">%s <span class="meta">#%d</span></td>' % (len(ts), html.escape(names.get(s, str(s))), s) if j == 0 else ''
            out.append('<tr>%s<td>%d</td><td>%s</td></tr>' % (first, p, '<br>'.join(eff(i) for i in ids)))
    out.append('</table>')
for title, ids in groups:
    rows = [i for i in ids if i in tiers and i not in skip and i not in done]
    if rows: table(title, rows)
rest = sorted(s for s in tiers if s not in done and s not in skip)
if rest: table('Other sets', rest)
out.append('</main></body></html>')
open('docs/ARMOR_SETS.html', 'w', encoding='utf-8').write('\n'.join(out))
print('sets:', len(done))
