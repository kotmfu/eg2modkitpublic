# Behaviour trees (`AXBT`)

Part of the [file format reference](../REFERENCE.md): [4.31 Behaviour trees (`AXBT`)](../REFERENCE.md#431-behaviour-trees-axbt).

How minions, agents and geniuses carry out jobs. One chunk in `misc\common.asr`, 60 KB, 95 trees, 506 steps.

A tree hash is `KeyHash` of its job type name. 12 names are exe strings: `ConstructFurniture`, `DeconstructFurniture`,
`RepairFurniture`, `SabotageFurniture`, `StealFurniture`, `JumpOffFurniture`, `SabotageTrap`, `PaintTile`, `SpawnInLair`,
`Idle`, `UseSmokeBomb` and `ZalikaBackfireRevertToWorker`.

`
u32 0, u32 1, u32 0, u32 tree count
tree:   [u32 3][u32 tree hash][u32 next free step index], then a stream of lists and steps
list:   [u32 type][u32 type][u32 child count][u32 0]
step:   [u32 8][u32 index][u16 0 or 1][name\0][pad][u16 ffff or 0][u8 0][u8 flag][u32 tree hash] settings
`

**Nesting.** A list opens a parent's child list. Its children follow, and the parent's own step record comes after them:
a record that arrives while the innermost open list is full is that list's owner. Every game tree resolves to one root
with each list exactly filled. List types:

| Type | Parent | Uses |
|---|---|---|
| 701 | Serial: runs its children in order | 121 |
| 703 | Parallel | 36 |
| 704 | Continuous | 1 |
| 801 | Timer (one child) | 6 |
| 802 | Loop (one child) | 7 |
| 804 | Always Succeed (one child) | 16 |
| 810 | Set Variable (one child) | 3 |

Parents keep their type but can carry a designer label ("Perform interrogation sequence" is a Serial). The u32 after a
Serial's or Parallel's tree hash (1-3) is not its child count.

**Actions and conditions** (steps without children) are preceded by their class id as `[u32 id][u32 id][…]`: `EG Move To
Job` 1051, `EG Play Animation` 1023, `Condition` 907, `EG Complete Job` 1000, `NavMesh MoveTo` 125.

**Settings.** After a step's tree hash come its settings: typed values, with plain fields (a bool byte or a u32) between
some of them. A typed value is 44 bytes plus its value:

`
[u32 7][u32 category][u32 4][u32 type] value [u32 3][u32 variable key][20 zero bytes]
`

| Type | Value | Category | Uses |
|---|---|---|---|
| 3 | f32 (e.g. -1 on conditions, 3 on Look At, 0.2 on NavMesh MoveTo, Timer 0.01 to 1.5) | 3 | 144 |
| 4 | bool, 1 byte | 3 | 71 |
| 13 | two u32 (both 0 in every game tree) | 0 | 49 |
| 14 | variable reference `[u32 999][u32 0x800N]`; N (1, 3 or 4) is the variable's kind | 2 | 157 |
| 17 | enum `[u32 value][u32 0x576e3cdd]` (EG Play Animation 9 and 15, EG Complete Job 0) | 3 | 46 |

The variable key is 0 on constants. On type 14 it names the blackboard variable the step reads, as `KeyHash` of the
name: 28 distinct keys. Three names are known: `IsAssignedVehicle` (`0x409bb734`), `IsEscorteeTrapped` (`0x21739ddc`,
read by "Escortee Is Trapped") and `CanDestroyTraps` (`0x83fb86fc`, read by Sabotage Trap and Destroy Trap). No game
file or exe string holds them; they hash from word combinations. The files store no setting names.
**Plain fields.** Untyped values sit between and after the typed settings, in a fixed order per step class. Every step
ends with `[u32 0]`, and some actions end `[u8 1][u32 0]`.

| Step | Layout after the tree hash |
|---|---|
| Serial | `[u32 mode]`: 1 sequence (stops at a failing child), 2 fallback (moves on to the next child only when one fails), 3 unknown. Not the child count. |
| Parallel | `[u32 mode]` 0-3; the usual root "job still valid + work" Parallel uses 2 |
| Loop | `[u32 0][u32 0][i32 count][f32 time]`: -1 = no limit |
| Condition (class 907, also custom labels such as "Is Job Valid") | `[u8 watch]` f32 (-1) · variable · `[u32 compare]` · value |
| Look At, Turn To | variable · `[u32]` (0) · f32 |
| NavMesh MoveTo, EG Move to Actor | variable · `[u8]` (0) · f32 · f32 or variable |
| EG Play Animation, EG Complete Job | enum · `[u32 animation hash][u8][u8][u8]` |
| Set Variable | `[u32 4]` · variable/value pairs |

In conditions, `watch` = 1 keeps checking while the parent runs, and every condition under a Parallel sets it.
`compare` 4 = equals and 3 = not equals; the pairs "Minion Has Required Prop" (3) and "Minion Does Not Have Required
Prop" (4), and "Prop Is On Furniture: True" (3) and "…: False" (4), show it. A type 13 value `[0, 0]` stands for
"nothing", so `variable ≠ nothing` means the variable holds something.
**Indices.** Step indices run up to the header's "next free" value with gaps for deleted steps.

**Jobs.** Jobs (`rjob`) name their tree by hash in their body (around +3,100 to +3,650), and some jobs' aux id equals it.
25 trees have a job that names them ("Repairing Item", "Constructing", "Interrogating a Prisoner", "Burning Body"). Traits
and genius entries (`fegd`) name a few more.

Example, "Repairing Item":

`
Parallel
   Condition
   Serial
      EG Move To Job
      Look At
      EG Retrieve Job Prop
      Set Started Performing Job
      EG Repair Vehicle
`

**Unnamed variables.** 25 of 28 keys stay unnamed. What each one holds, from the steps reading it:

| Key | Kind | Read by |
|---|---|---|
| `0x5a3cf04c` | 3 | "Is Job Valid", "Is job invalid?" and Conditions in 13 trees (= false or = nothing) |
| `0xf1e1909e` | 3 | "Set Started Performing Job", "Start job" (20 steps) |
| `0xc99b251a` | 3 | Complete Job, EG Complete Job |
| `0xd8de65f1` | 3 | "Enable ability cancel", "Disable ability cancel" |
| `0x7e4b2ac9` | 3 | Prop on furniture / floor / in world, actor on interrogation furniture, extinguisher on furniture |
| `0x5e699c18` | 3 | Minion has / does not have required prop (ammo), "Has Collected Money (or given up)" |
| `0x15d4e2a9` | 3 | "Can Interact with Prop Point Furniture", "Has Checked for Vault Prop" |
| `0x9aca96d9` | 3 | Look At (13), the look target |
| `0x1f987214` | 3 | Look At, EG Move Within Range |
| `0xf56bd819` | 1, 3 | EG Move to Actor, "Escortee has arrived", "Follow escorted actor", "Look at victim": the escorted or target actor |
| `0xe66c21f4` | 3 | NavMesh MoveTo, "Go to actor", "Go back outside trap" |
| `0xa5f66af4`, `0xe436077a` | 3 | EG Move to Actor, EG Move Within Range |
| `0x5f32cba1` | 3 | NavMesh MoveTo |
| `0x2b965928`, `0x8ea76976` | 1 | One NavMesh MoveTo (destination and second target) |
| `0x88285d75` | 4 | NavMesh MoveTo |
| `0xadcaa472` | 1 | NavMesh MoveTo (2 trees) |
| `0x67b105ea`, `0xf4c0a1d9` | 4 | Turn To |
| `0x3d0d8f0b` | 4 | Set Variable, set to -20 or -100 |
| `0x49a9c3e7` | 1, 4 | Set Variable, set to true |
| `0x7145b7f0` | 4 | Condition, = nothing and ≠ nothing in one tree |
| `0xe97e721b`, `0xf3ca0f30` | 4 | Condition ≠ nothing |

Search: KeyHash over every exe, data and text string, every token n-gram of those strings, and a meet-in-the-middle
search over 1 to 4 words (prefixes `Is`, `Has`, `Can`, `Should`, `Was`, `m_`, `b`, `Target`, `Current`, `Last`;
joined plain, with `_` or with spaces) from the step names and the exe's identifier words. The search recovers the three
known names; every other match is a word salad that fits no reader.
