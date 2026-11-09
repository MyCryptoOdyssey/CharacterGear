# Tables

Game data the planner needs, embedded in `CharacterGearPlanner.exe` at build time. Every file starts with `#` lines
that describe its columns.

| File | What | Source |
|---|---|---|
| `spells.tsv` | Every spell: name, category, power, school, duration, what it changes and by how much | Names, categories, power, school and duration from the end-of-retail `client_portal.dat` SpellTable (read with ACE's DatLoader, see `tools/SpellDump_Program.cs`). Stat values from the ACEmulator world database. |
| `set_bonuses.tsv` | Set bonus spells by piece count | `client_portal.dat` SpellTable.SpellSet |
| `set_names.tsv` | Equipment set names | ACEmulator `EquipmentSet` enum |
| `skills.tsv` | Skill formulas (attributes and divisor) | `client_portal.dat` SkillTable |
| `materials.tsv` | Material names (the prefix the client shows) | ACEmulator `MaterialType` enum |
| `pets.tsv` | Summoning essences: pet damage type and summoning school | Damage types from the essence-to-pet mapping in ACEmulator `PetDevice.cs`; schools from the ACEmulator world database |
| `weights.tsv` | Scoring weights per profile | This project (editable copy in `data\` after the first run) |
| `templates.tsv` | End-game cantrip goals per profile | This project (edit to match your goals) |

The ACEmulator code and world database are AGPL-3.0. The values taken from them are game facts (numbers and names),
not code. `tools/` has the small programs used to regenerate the tables from a client `portal.dat` and an ACE world
database, if you would rather build them yourself.
