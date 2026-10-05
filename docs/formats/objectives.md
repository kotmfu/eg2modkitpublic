# Objectives (`robj`)

Part of the [file format reference](../REFERENCE.md): [4.17 Objectives (`robj`)](../REFERENCE.md#417-objectives-robj).

The first prop (`0x20`) starts with a u32 type at +33: 0 main or tutorial, 1 side story (crime lords, loot, DLC and
sandbox recruits), 2 optional. Its child `0x1` holds `[u32 n]` + n task props (key `0x8000002d`). The title text
ref sits at +37 (key at +49).

No object lists the objectives. The game evaluates each loaded objective's condition script. The `sandbox`
package (managed) holds the Sandbox mode objectives.

A task holds its texts, condition blocks and `[1][room id]` (its `_Activate` step), so a task copies cleanly into
another objective. The game runs tasks one at a time and starts a task's modifier when the task becomes current.

**Task condition block** (prop `0xd`, 166 bytes): `[u32 kind][u32 amount][u8 flag]`, two prop5 type references,
then prop1 `[hash]` entries.

| Kind | Condition | Example |
|---|---|---|
| 3 | N minions of a type | "Hire 25 more minions" |
| 4 | N furniture items | "Construct Lockers in the Barracks", 25 |
| 6 | N tiles of a room type (hash in the first prop1) | `MediumTierGold` `0xf6245d9a` task 0: 64 Barracks tiles, amount at body +92 |
| 10 | N schemes completed | "Complete 10 Schemes" |
| 0x24 | Objective state | |

Reward scripts start other objectives (node type `0xd4e0f0bc` = "start objective X").

**Henchman limit.** No single value caps henchmen. The 20 `Recruit X (Sandbox_Henchmen)` objectives, "Unholy Diver",
the four "One Of Us" objectives and each crime-lord story start count henchmen in the lair (script node type `0x10e28e6e`,
`{MinionType} InLair`, which game scripts use only for henchmen) against an i32 setting, the first one after the node type:

- 5: the objective needs fewer than 5 henchmen, so recruit offers stop at 5,
- 4 (story starts): it fails at 4 henchmen while a recruit is already on the way.

`Has5Henchmen` (objectives_optional) checks the same count for an optional objective. The "Henchman Improvement
Program" research raises henchman stats, not the cap. The HUD's genius and henchman bar shows 6 fixed spaces and
fills them in order, so further henchmen draw past its end.

**One side story at a time.** The exe, not data, refuses to start or resume a side story while another one isn't paused
(objective manager message `0x80b8`, Objectives screen resume button). See HANDOFF "Several side stories at once".
