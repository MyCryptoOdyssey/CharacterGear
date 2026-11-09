# CharacterGear

A Decal plugin and planner for Asheron's Call players with several characters and gear mules on one server.
It inventories everything your characters carry and wear (and what sits in your house chests), then works out the
best suit each character can actually wear from all of it, following the server's own wield and activation
rules, and tells you which character holds each piece.

Built and tested against ACEmulator servers.

## What you get

**In game: the "Recommended gear" window.** One button per page:

| Page | Shows |
|---|---|
| Your Suit | Each slot: what you wear now, and when something better exists, a `--> Recommended` line with the piece, who holds it and what to do |
| Weapons | Best weapon, off hand (shield or second weapon), and the best weapon for each damage type (rends count) |
| Armor / Clothing / Jewelry | The recommended pieces and a short reason for each (set, best spell, armor level, ratings) |
| Aetheria | Sigils for your Blue / Yellow / Red slots (set level, surge chance) |
| Essence | Summoning essences for your school, only when one is a clear upgrade |
| Elements | Legendary ward coverage per damage type, with the best ward you own and why it isn't used |
| Spells | Legendary cantrip coverage for every skill you have trained |

After each scan, chat prints a short summary (upgrades, ward and spell coverage). Equip changes refresh the picks
within seconds.

**Outside the game: an HTML report** (`data\<server>\CharacterGear_Report.html`) with every character, paper dolls,
the recommended suit, a hand-over list, wishlist, set bonuses and every item with its stats.

## Read-only

The collector only reads. It asks the server to ID (appraise) items and writes files on your PC. It never moves,
equips, uses, gives, drops, splits or sells anything, and sends no chat commands. `build.bat` runs
`tools/readonly_guard.ps1`, which refuses to build if the source calls any other game action.

## How it decides

- **Level 7 self buffs** are assumed for every character (+40 to every attribute and skill, level 7 Impenetrability,
  banes, protections and Armor Self), plus the cantrips in the suit being judged.
- **Wield and activation rules follow the server** (ACEmulator): wield requirements, Arcane Lore and other activation
  requirements, old weapon skills mapped to the current ones, set bonuses by piece count (or item level for
  Aetheria and cloaks), enchantment stacking, armor per body part with hit odds, ratings, shields, dual wield.
- **Profiles** are detected from trained skills (two-handed, heavy, light, finesse, missile, war, void, life,
  tinker, crafter, plus summoning or dual wield). Override them in `data\CharacterGear_profiles.tsv`.
- **Weights** for each profile live in `data\CharacterGear_weights.tsv` (created on first run). Edit and rerun.
- **End-game goals** per profile are in `tables\templates.tsv`.

## Install

1. Install Decal and Virindi View Service (Virindi Tank includes it).
2. Copy `CharacterGear.dll` and `CharacterGearPlanner.exe` (from a release, or build them) into one folder,
   e.g. `C:\Games\Decal Plugins\CharacterGear\`.
3. Register the plugin with Decal: edit the path in `install\register_CharacterGear.reg` if you used another
   folder, then double-click it. (`unregister_CharacterGear.reg` removes it.)
4. Log in. The CharacterGear panel appears in the Decal bar, and the first scan starts a few seconds after login.

Log in each character (mules too) once so it is scanned. Open a house chest to add its contents.

**Commands:** `/cg status`, `/cg scan`, `/cg rec`, `/cg plan`, `/cg track on|off|default`, `/cg trackall on|off`,
`/cg history on|off`, `/cg chests on|off`, `/cg chest name <label>`.

## Build

`build.bat` needs the .NET Framework 4 compiler (`csc.exe`, part of Windows), Decal 3.0 and Virindi View Service at
their default paths (edit the paths at the top of `build.bat` if yours differ). Output goes to `bin\`.

## Credits

- **ACEmulator** ([ACE](https://github.com/ACEmulator/ACE), AGPL-3.0, and its
  [world database](https://github.com/ACEmulator/ACE-World-16PY-Patches), AGPL-3.0): the reference for the game
  rules this tool reproduces, and the source of some values in `tables\` (spell stat values, essence schools and
  pet damage types). No ACE code is included; see `tables\README.md` for where each table came from.
- **Asheron's Call client data** (`client_portal.dat`): spell names, categories, set bonuses and skill formulas,
  read with ACE's DatLoader.
- **[Decal](https://sourceforge.net/projects/decaldev/)** and **Virindi View Service** (Virindi Plugins): the
  plugin framework and the in-game window system it runs on. Neither is included.
- **[Mag-SuitBuilder](https://github.com/Mag-nus/Mag-Plugins)** (LGPL-2.1): inspiration for the suit search. No code
  from it is used.

## Disclaimer

This project is for non-commercial use. It is not associated or affiliated in any way with Turbine, Inc. or WB Games
Inc. Asheron's Call was a registered trademark of Turbine, Inc. and WB Games Inc., which has since expired.

## License

MIT (see `LICENSE`).
