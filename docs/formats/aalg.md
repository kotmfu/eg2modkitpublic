# Animation logic (`AALG`)

Part of the [file format reference](../REFERENCE.md): [4.36 Animation and skeletons](../REFERENCE.md#436-animation-and-skeletons).

**Animation logic (`AALG`, `misc\common.asr`, 1, 68665 bytes).** A list of 27 state graphs: 7 compound graphs and
20 sequences. The body has no offsets, no padding and no checksums. Every reference is an index into a list in the same
graph, a graph index, or a `KeyHash`. Strings are `name\0` padded to 4 bytes from their own start.

```
u32 1, u32 0
u32 3                         probably the version
u32 7                         compound graphs
u32 20                        sequences
27 x graph                    the 7 compound graphs first, then the 20 sequences
```

**Graph.** Every graph starts with the same header, node list and transition list. The tail depends on the type.

```
u32 type                      3 = compound graph, 6 = sequence
u32 0
u32 KeyHash(name)
name\0 padded to 4
u32 0
u32 nodes,       nodes x node (64 + 2n bytes)
u32 transitions, transitions x 32 bytes
type 3: compound tail
type 6: sequence tail
```

| # | Graph | Type | Nodes | Transitions | Offset |
|---|---|---|---|---|---|
| 0 | Compound Anim | 3 | 1 | 0 | 0x14 |
| 1 | Standard_Character | 3 | 39 | 112 | 0xa2 |
| 2 | Character_Gallery | 3 | 3 | 4 | 0x1a6a |
| 3 | Reflexes | 3 | 2 | 3 | 0x1c28 |
| 4 | Face | 3 | 2 | 2 | 0x1d68 |
| 5 | Triggerable_Flinch | 3 | 3 | 2 | 0x1e84 |
| 6 | LairBuilderAngle | 3 | 2 | 3 | 0x1fd6 |
| 7-26 | `Sequence_UseFurniture`, `_JubeiFlow`, `_ScrambleEvidenceReaction`, `_Emma_Doomsday_Execute`, `_StopMoving`, `_InTrap`, `_SabotageTrap`, `_WatchMagicTrick`, `_SpecialAbilityMountLoopDismount`, `_AnimOverride` (153 nodes, 376 transitions), `_CabalLeaderCloneSummoned`, `_StowEquipment`, `_UseSmokeBomb`, `_SpecialAbilityTargetSelection`, `_UnstowEquipment`, `_RemoteDetonationReaction`, `_Combat`, `_ZalikaBackfireRevertToWorker`, `_Wounded`, `_DisembarkVehicle` | 6 | 3-153 | 2-376 | 0x2148-0xfea4 |

388 nodes and 807 transitions in all.

**Node (64 + 2n bytes).**

```
u32 9
u32 hash                      what the node plays; meaning depends on kind (below)
u32 flags                     0x10000 on looping states (WoundedLoop, HarassedLoop, IdleCombat); also 0, 0x1000,
                              0x410000 (locomotion groups), 0x1c00000, 0x1c01000, 0x1401000 (stop and mount clips)
u32 p0                        0; acbfae87 on LairBuilderAngleDelta
u32 0, u32 0, u32 p3          p3 is 0 or KeyHash("AnimSet") on 2 nodes
u32 source                    the anim set category for kind 6 (below), else 0, 4 or a reflex category
u16 id                        the node's index; a variant copy repeats its group head's id
u8  sub                       0 = plain, 1 = group head, 2 = variant copy of a group head
u8  kind                      below
u16 first, u16 last           range of this node's transitions (0xffff, 0xffff when none)
u16 layer                     0 in Standard_Character and Character_Gallery, 0xffff elsewhere; probably the layer
u16 action                    kind 8: index into the sequence's action list; 0xffff = no payload. Else 0xffff
u32 key                       unique node id; a variant copy uses (variant << 24) | group head key
12 bytes 0
u32 n, n x [u8 parameter index][u8 value]   the parameter values that select this node (compound graphs)
```

| Kind | Count | Hash | Plays |
|---|---|---|---|
| 0 | 117 | KeyHash of an `HCAN` clip name (10), or 0 (107) | The clip directly (`Emma_SpiderBot_Spawn_01`, `Ability_Jubei_Flow_Loop_01`). Hash 0 is an empty pass-through, entry or exit state |
| 2 | 22 | KeyHash of a `Sequence_*` graph name | Runs that sequence |
| 6 | 183 | KeyHash of an anim set slot name (`IdleCombat`, `Run`, `WoundedLoop`, `Mount`, `Loop`), or 0 on 8 variant copies | The clip or `CPAN` the character's anim set maps to that slot; `source` names the set |
| 8 | 59 | Action type (below) | One action of the sequence |
| 9 | 2 | `Idle` (00313fd4), d012aa52 | Face states (graph Face) |
| 11 | 2 | 0 | Reflex player; source 3b5f2c07 (Standard_Character) or d0d5cfd4 (Reflexes) |
| 4 | 1 | 0 | Root of Triggerable_Flinch |
| 5 | 1 | 0 | Empty state of LairBuilderAngle |

Anim set categories in `source` (kind 6): `AnimSet` (ccec74b1, 147 nodes), `AnimSet_Combat` (3d480e42), `SpecialAbility`
(58f9e251), `AnimSetHarassed` (ad554398), `AnimSetUseFurniture` (b7c3cd7c), `AnimSetTrap` (e387275e). All 117 slot
hashes occur in the character `BLUE` objects, which pair each slot hash with the clip to play (`IdleCombat` with
`Rifle_B_Idle_01` in one class). One slot hash equals a `CPAN` id (`Sidesteps`, 92ecb430).

**Groups.** A group head (sub 1) and its variant copies (sub 2) follow the plain nodes. In Standard_Character the heads
are nodes 2, 20 and 29 (`Run`, `Jog`, `Walk`), and the copies are the flat, up-stairs and down-stairs versions
(`RunUpStairs`, `WalkDownStairs`). The head's first/last covers its links to its copies. The copies' first/last cover the
group's shared outgoing transitions, which are stored under the head's index.

**Transition (32 bytes).**

```
u32 2
u16 from, u16 to              node indices
f32 cost                      1; 0.1 on Idle -> RunStart -> Run; 100 on self loops; 0 on head -> variant links
f32 blend                     seconds: 0.2 (404), 0 (395), 0.75, 0.15, 0.1, 100
u32 0
u32 flags                     1 (769), 113, 0, 513, 512, 33, 112, 65, 4209
u32 sync                      0, 1 or 3; 3 on run/jog/walk changes; probably a phase sync mode
u32 exit                      0, or KeyHash of a named exit of the sequence the from-node runs
```

Transitions are stored grouped by from-node, in node order, with the head -> variant links last. The exit field names
the `Exit` action the sub-sequence ended on: Standard_Character leaves `Sequence_Combat` by `IsAlive` (7c3535e3) to
`Idle` or `IdleCombat` and by `IsDead` (b9b0648e) to node 11, and leaves `Sequence_AnimOverride` by `EnteringVehicle`
(b2ae0de2).

**Compound tail (type 3).**

```
u32 n, n x [u32 0][u32 graph index][u32 0]    layers: Standard_Character lists 4, 3, 6 (Face, Reflexes,
                                              LairBuilderAngle); Character_Gallery lists 4
u32 n, n x [u32 0][u32 1][u32 0xffffffff]     1 entry in the two root graphs, else none
u32 2
u32 parameters, parameters x [u32 0][u32 KeyHash][u32 0][u32 max]   values run 0 to max
u32 0, u8 0
u32 cells, cells x u8                         node index for each combination of parameter values
u32 n, n x [u32 0][u32 parameter hash][u32 slot][u32 0]   parameter slots of the whole character
u32 n, n x u32                                anim set categories the graph uses
u32 0, u8 triggerable                         1 on Triggerable_Flinch
```

- The cell table has (max + 1) multiplied over all parameters entries. The first parameter varies fastest. Each cell
  holds the node to be in. The `n` pairs in a node repeat the values that select it; the table decides.
- Standard_Character: `IsInCombat` (d2651f83, 0-1), aa1aee34 (0-1, selects the reflex node 28), `Movement Speed`
  (3ad89596, 0 idle, 1 walk, 2 jog, 3 run), 6dfcaebb (0-1, selects `Sidesteps`), fbb7dc3f (0 flat, 1 up stairs, 2 down
  stairs). 96 cells. Its slot map holds these five plus 966c8d2c (Face), cc543051 (Reflexes) and `AnimOverride`
  (576e3cdd, LairBuilderAngle), slots 0-7. Its categories are the six above.
- Reflexes, Face: one bool each, two cells. LairBuilderAngle: `AnimOverride` 0-45; value 3 plays `LairBuilderAngleDelta`.

**Sequence tail (type 6).**

```
u32 0, u32 0
u32 n                         equals the transition count
n x condition block           one per transition, same order
u32 actions, actions x [u32 0][u32 size][size bytes]
u32 A                         0, 1 or 2; unknown
u32 B                         0 or 2; unknown
u32 1
u16 entry                     index of the start node
```

Condition block:

```
u32 1                         always 1 (681 blocks)
leaves: [u8 compare][u8 1][u32 2][u32 parameter hash][u32 value]   repeated while the second byte is 1
[u8 negate][u8 0][u16 required]
```

- `required` is the number of leaves that must hold: 1 makes a list of leaves an OR (`AnimOverride` == 4 or 5 or 7 or 12
  or 19 or 35 or 41), 2 and 3 make an AND. An empty block has `required` 0 and always passes. The head -> variant links
  of `Sequence_SabotageTrap`, `_AnimOverride` and `_DisembarkVehicle` carry one leaf with `required` 0 (368 blocks).
- `negate` 1 probably inverts the block. In `Sequence_UseFurniture` node 11 goes to node 0 on not (x == 0 or x == 1)
  and node 0 then branches on x == 2 and x == 3.
- `compare` is 0 or 1. Both forms test equality in complementary pairs; the difference is not known. Most parameters
  use one form only. Parameter cf7dbf22 stores two u16 values (1000, 450) and is probably a random roll out of 1000.
- Named parameters: `InTrap` (b97165d2), `IsDying` (7c656443), `AnimOverride` (576e3cdd), `IsInCombat`, `Movement Speed`.
  e1895076 (`== 1`) ends many states and is probably an interrupt flag.

**Actions.** A kind 8 node runs action number `action`. Some types carry no payload (`action` 0xffff).

| Type hash | Nodes | Payload |
|---|---|---|
| `Exit` (002fb91e) | 3 | `[u32 0][u32 KeyHash(name)][name\0]`: a named exit (`IsAlive`, `IsDead`, `EnteringVehicle`) that the parent's transition exit field matches |
| `AttachPFX` (e37360dd) | 8 | `[u32 5][u32 0][u32 KeyHash(group)][u32 KeyHash(set)][u32 mode][u32 effect hash][name\0][name\0][28 bytes 0][f32 1]`; group `Lairbuilder`, set `LairBuilderPFX`, names `PFX_Paint`, `PFX_Dig`, `Effect_TrainingCompleted`; mode 1, 0, or 3 with empty names (probably detach) |
| 267d5762 | 10 | `[u32 1][u32 slot hash]` or `[u32 0x80000001][u8 0][u32 4][u32 slot hash]`; slot hashes of `AnimSetTrap` (`Mount`, `Loop`) |
| c00777ed | 7 | `[u32 0x80000001][u8 0][u32 12][u32 0/1][u32 0][f32 0.1-0.5]` |
| 8828d901 | 7 | 19 bytes `[u32 3][01 02 01][u32 1][8 bytes 0]`, once 15 bytes |
| 000c9b15 | 6 | `[u32 1][u32 0-4]` |
| f89a73e7 | 4 | `[u32 0][u8 0/1][u32 KeyHash("IK_Template_Default")][u32 1][u8 0]`: probably IK off and on |
| af2e0f14 | 2 | `[u32 0][f32 1.0 or 0.2]` |
| 385fab0e | 2 | `[u32 0][u16 2 or 0][u16 1]` |
| 36804fae | 1 | `[u32 0x80000001][u8 0][u32 5][u8 0][f32 0.2]` |
| c6c9140e, 7e13c013, ac6d199e, 9e7c1f87, 66f52f05, 976fc81a | 9 | none |

Payload values with the 0x80000000 bit use the property header of [4.3](../REFERENCE.md#43-data-objects-and-properties).

**How a clip is chosen.**

1. A character runs the root compound graph of its class: Standard_Character in the lair, Character_Gallery in the
   gallery. The graph's layer list adds Face, Reflexes and LairBuilderAngle on top. Triggerable_Flinch is not listed and
   is probably started by events.
2. The game writes the parameters (`IsInCombat`, `Movement Speed`, stairs). The cell table gives the target node. The
   graph moves along transitions, probably by lowest total cost (no direct `Idle` -> `Run` transition exists; the route
   is `Idle` -> `RunStart` -> `Run` at cost 0.1 + 0.1), blending each step by the transition's blend time.
3. Game code requests other states by node: kind 2 nodes run a sequence (`Sequence_UseFurniture` is node 23 of Standard_Character).
4. The node plays: kind 0 a fixed `HCAN` clip, kind 6 the clip or `CPAN` the character's `BLUE` anim set maps to
   (category, slot), kind 2 a sequence.
5. A sequence starts at its entry node. At each node it takes the first outgoing transition whose condition block
   passes (blocks with no leaves fire when the state ends). Kind 8 nodes run an action and move on. It ends on a node
   with no transitions or on an `Exit` action, and the parent takes the transition whose exit field matches.

**Editing.** All links are counts, indices and hashes, so a change never moves anything outside the edited graph except
by size (the chunk size in the container).

- Retarget a node: replace `hash` in place. Kind 0 takes `KeyHash` of any `HCAN` name in `misc\common.asr`; kind 6
  takes a slot hash the character's anim set defines (`source` picks the category); a kind 6 node becomes a fixed clip
  by setting kind 0, source 0 and an `HCAN` hash, as the Jubei and Emma sequences do. Keep flag 0x10000 for loops.
- Add a node: append it before the group heads or after the last node, set `id` to its index, `first`/`last` to its
  transition range and a new unique `key`. Raise the node count. Indices of nodes after an insertion point shift, so
  every transition `from`/`to`, cell table entry, `entry` and variant `id` that refers to them must shift too.
  Appending at the end avoids that.
- Add a transition: insert it into its from-node's run, raise the count, and add 1 to `first`/`last` of every later
  node (and of variant copies that share a later range). In a sequence also insert one condition block at the same
  index and raise the block count. A compound graph reaches the new node only through the cell table or a game request.
- Add a sequence: append a type 6 graph after the last sequence and raise the header's sequence count. Add a kind 2 node
  with `KeyHash` of its name to Standard_Character, transitions to and from it, and give any `Exit` action names that the
  parent's exit fields test.

**References found.** `AALG` names 8 `HCAN` clips (10 nodes), all present in `misc\common.asr`. It uses one `CPAN` id
(`Sidesteps`, as a slot name) and no `ASET` id. The 117 kind 6 slot hashes all occur in `BLUE`. The 22 kind 2 hashes
all name sequences in this chunk.
