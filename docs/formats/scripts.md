# Scripts (flowgraphs)

Part of the [file format reference](../REFERENCE.md): [4.20 Scripts (flowgraphs)](../REFERENCE.md#420-scripts-flowgraphs).

`rscm`, `robj`, `room`, `rtrp`, `rrtl`, `rant` and `rctr` hold scripts.

```
name\0 (pad 4)  "FlowGraph/<template>\0"  [u32 1][u32 node count]  nodes...
node:  [type hash][version words / size blocks][00 ff ff 00][u32 3][u32 node id] ... settings, pins
link:  [link id][from node][from pin][to node][to pin][00 ff ff 00] + 25 bytes   (49 bytes, stored under its output pin as [u32 count][u32 ?] + links)
pin:   [pin hash][u32 1][u8 0][name\0 pad 4]
value: "Value\0" + 31 bytes -> [u8 1][u32 4][u32 type][value]   type 2 int, 3 float, 4 bool (u8)
```

Node 0 is Start and node 1 is End. Nodes sit inside size-prefixed property blocks, so a structural edit splices bytes
and fixes the size of each enclosing block. Version words before the marker hold 1 or 2.

**Names.** A pin hash is `KeyHash` of the pin's internal name, which the files don't store. The name stored next to a
pin is its display name and can differ: pin `0x054328a2` is `InputAmount` and shows as "Count". Hashing the game's
strings and word pairs recovers 61 of the 102 pin hashes that links use, including:

| Hash | Name | Role |
|---|---|---|
| `0x65c663f8` | `InputFlow` | Trigger input of every action step |
| `0xb41ab1af` | `OutputFlow` | Fires when an action step finishes |
| `0x79062fbc`, `0xa6f2a4b5` | `FlowTrue`, `FlowFalse` | Branch outputs |
| `0x2969894e` | `DataConstant` | Output of a constant value |
| `0xd017f110`, `0x2c4e5c5e`, `0x080e372a` | `OutputValue`, `OutputResult`, `OutputDataObject` | Data outputs |

Node type hashes don't come from any name in the game's strings, apart from `Branch` (`0xadaf25a2`) and
`ConstantValue` (`0x5cd5544d`). The game's scripts use 114 node types in 17,128 nodes. The most used:

| Type | Uses | Inputs, outputs | Does |
|---|---|---|---|
| `0x5cd5544d` | 4,343 | `DataConstant` | Constant value |
| `0x5ff81097` | 2,311 | `OutputDataObject` | Object reference (resource, room, tag) |
| `0xe2767556` | 1,791 | `InputFlow`, `InputAmount`, `InputConsumableType` | Change a consumable (resource) by an amount |
| `0x687b2d0e`, `0xb7032762`, `0x667797cc` | 617, 591, 118 | Operand1, Operand2, Result | Compare or combine two values |
| `0x7c81ffb9` | 605 | `InputAmount`, `InputDuration`, `MoneyTransactionReason` | Gold transaction |
| `0xc53cb767` | 504 | Consumable, `OutputCount` | Read a consumable's amount |
| `0x514ad5d4` | 305 | `InputObjective`, `OutputResult` | Read an objective's state |
| `0x10e28e6e` | 70 | `InputMinionType` | Count henchmen in the lair |
| `0xd4e0f0bc` | 5 | `InputFlow` | Start an objective |

**GUI flowgraph format (FG3).** `STSM`, `STRC`, `GUIF` and the `GUAT` entries share one flowgraph format, different
from the data-object scripts of [4.20](../REFERENCE.md#420-scripts-flowgraphs):

```
u32 5, u32 node count
node count x [u32 2][u32 node type hash][u32 size] + node body
"<name>\0" padded to 4                   "Total Count - Minions Dead/Flowgraph", "SF_MessageBox_Active", …
u32 n, n x u32 node index                1 entry node in 205 graphs, up to 10
u32 0
prop 1 { u32 node count, node count x [u32 node id][u32 node index] }   the indices are a permutation of 0..count-1
trailer
```

A node body lists its pins. A pin descriptor is `[i32 -1][u32 0][u32 pin class][u32 index][u32 pin name hash]`,
preceded by two small words. Pin classes: `0x8050a76c` (50,748 pins), `0xea806047` (32,856), `0x658ba8cb` (5,440).
Pin names are `KeyHash` of the pin name: `0x00368f3a` "type", `0x0036452d` "text"; the commonest, `0x30dad90c` and
`0x7faacfff`, stay unnamed. The 36 node types of the stat graphs all recur in `GUAT` (13,433 nodes) and `GUIF` (936).
Node bodies hold no strings.

| Trailer | Layout |
|---|---|
| `STRC` (118) | prop 1 { `u32 0` }, prop 1 { `[u32 n]` + n `STSM` stat ids }: the stats whose change runs the reaction (e.g. `Refresh stats on load` `0x5541b54e` in most) |
| `STSM` (83) | prop 1 { `u32 0` }, `[u32 table][u32 key]` (the stat's display text, e.g. `menu/STATS_POWER_AVAILABLE` "Power Available:", or 0), prop 1 { `[u32 n]` + n hashes }, prop 1 { `[u32 n]` + n `[hash][u32 0][u32 0]` }, prop 1 { `[u32 n]` + n `[hash][u32 0]` }, `u8 1` |

The last two `STSM` lists hold `0xc8e624fa` in 82 of 83 stats.
