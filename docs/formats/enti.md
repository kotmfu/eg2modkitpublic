# Entities (`ENTI`)

Part of the [file format reference](../REFERENCE.md): [4.34 Island level chunks and entities](../REFERENCE.md#434-island-level-chunks-and-entities).

**Entities (`ENTI`, islands, scenarios, maps and saves, 2,969 in the install).**

Header, in every instance:

```
u32 1
u32 0
u32 entity id     islands 0x19c73-0x1e8605; the lair grid 0x989680; scenario characters from 0x989680 up
u32 class id      table below
```

The rest is the class's data. Each class level writes its serializer version as a `u32`. When bit 31 is set, `[u8 0][u32 size]`
follows and the size covers that level's data, so these blocks have the shape of a [4.3](../REFERENCE.md#43-data-objects-and-properties) property with the version as its
key. The block keys that [4.22](../REFERENCE.md#422-lair-maps-envsbasedefinitionslairbase) lists for the grid (`1d/44/6`, `1e/45/7-8`) are such versions. Bit 26 (`0x04000000`) is
set on some blocks (GS2 nodes, parts of character entities); its meaning is unknown. Effects, lights and animated models end their
outermost block with a version-0 block of 5 bytes, `[u8 0-1][u8 0-3][u8 0][u16 0]`.

Base block (version 13, `0d 00 00 80 00 [u32 size]`). Every entity except the grid (`0x8005`) and `0x8009` holds one, nested
inside its class blocks:

```
+0   u32 type id      0 for placed island entities. Characters and vehicles: their actor or vehicle type id
                      (a data-object id; KeyHash("Tourist"), KeyHash("Worker") and KeyHash("GENIUS_MAX") also occur)
+4   u32 group        0, KeyHash("Actor") 0x0585a9f5 (0x8003, 0x8004), KeyHash("Vehicle") 0x14638f2c (0x800b),
                      0x0037b008 (0x800c)
+8   u8 x3            01 01 01; the first byte is 0 on 271 sound emitters and 48 lights
+11  u32 0
+15  u32 n            byte size of the component list
+19  component list   [u32 0][u32 count]  count x component
     [u8 flag][u32 m] + m bytes      flag 0 and m 0 except on class 0x8014
     class 0x8014 continues inside this block (below)
```

A component is `[16-byte GUID][u32 2][u32 type hash]`, and when the type hash is not 0, `[u32 size]` + size bytes. Every
entity has exactly one component. Its type hash is 0 except on these classes: `0xc5c69f77` (0x74), `0x90b362f9` and
`0xdb3f7f2d` (0x50), `0x7bee9c55` (0x51), `0xb0de9422` (0x66), `0xa94b8069` (28 of 48 vehicles) and `0x04a9cc31`
(0x8014). The 40 to 48-byte payloads are `[u32 0 x1-3][u32 1][u32 1]` + a nested component with no type
(`[GUID][u32 2][u32 0]`) + `[u32 0]`; the nested GUID is the owner's GUID one tick later. The 0x8014 payloads
(3.9-19.8 KB) nest further typed components (`0x4641c78e`).

The GUID is a version-1 UUID: 6 bytes node id, `u16` clock sequence, `u64` timestamp with the version nibble 1 in its top
4 bits. The timestamps run from July 2019 to January 2022, and 18 node ids occur.

Classes (offsets marked E count from the end of the base block):

| Class | Count | What | Layout after the header |
|---|---|---|---|
| `0x0e` | 774 | Sound emitter | `[block v4 {base}][u32 1][u32 emitter id][f32 x, y, z]`, 110 bytes. The emitter id (100000 + n) is a record id in the island's `SDPH`, and the position repeats that record's |
| `0x21` | 899 | Particle effect | `[block v1000 {[block v12 {base, fields}][block v0 {5 bytes}]}]`. E+4 is the effect: KeyHash of `<folder>/<name>`, e.g. `Island/EG_Surf_Circular_Waves_Tropical_Large_01`, `DLC_Arctic_PFX/PFX_Arctic_Furniture_Ice_3x3` |
| `0x47` | 443 | Dynamic light | `[block v1000 {u32 21, base, fields, [block v0 {5 bytes}]}]`, 538-702 bytes |
| `0x07` | 156 | Animated model (doors, doomsday silo, satellite dishes, buoys) | 335 bytes, below |
| `0x50` | 52 | GS2 node (name `GS2 Node`) | `[block 0x84000000/1 {u32 10, u32 1, u32 3, base, u32 0x2001 or 0x2011, ...}]` |
| `0x51` | 26 | GS2 spline (name `GS2 Spline`) | `[u32 6][u32 8][u32 3][base][u32 0x2001][f32 x3 position][i32 -1][u32 start node id][u32 end node id][u32 1][u32 2][u32 2, 3 or 7]` + more. Both ids name `0x50` entities in the same file |
| `0x66` | 37 | Object driven by a `COMA` binding | `[u32 20][base][u32 key][u32 1][u32 0][u32 1]`, then floats that include one position twice and a quaternion. The key equals the `+12` key of one `COMA` chunk in the same island (23 of 23 island instances), and each of those `COMA` chunks belongs to one entity. The audio flowgraphs (`0x8014`) refer to 16 of them |
| `0x03` | 29 | Cutscene controller | `[u32 8][base][u8 1][name\0 padded to 4 from its start][u32 KeyHash(name)]` + 14 bytes. Names: `IntroCutscene_<Genius>_Island01`-`03`, `IntroCutscene_<Genius>_OceansLair` |
| `0x8014` | 35 | Flowgraph (`In Floor 1`-`4`, `In Basement`, `In Floor Island`: the gameplay audio per floor) | Base block only. Its component payload and its extra data (2.1-15.6 KB) hold the graph, which names its sound emitters (`0x0e`) by entity id. After that: `[u32 1][8 bytes][block v2 {name\0 padded to 4, ...}][block v0 {u32 0}]` |
| `0x8003`, `0x8004`, `0x8007`, `0x800b` | 290, 99, 57, 48 | Minions, other characters, companions, vehicles ([4.22](../REFERENCE.md#422-lair-maps-envsbasedefinitionslairbase)) | Characters and companions are in `envs\basedefinitions\*.scenario`; vehicles are on islands and in scenarios |
| `0x8005` | 7 | Lair grid ([4.22](../REFERENCE.md#422-lair-maps-envsbasedefinitionslairbase)) | `.base` files and scenarios |
| `0x800c` | 6 | Probably the wind affector (`EG_ENTITYCLASS_AFFECTOR_WIND`) | `[block v0 { base, u32 0, u32 0, f32 1 }]`. Its base-block group is KeyHash("wind") `0x0037b008` |
| `0x6a`, `0x74`, `0x42` | 4, 4, 1 | Unidentified small entities | `0x74` is `[u32 1][base]`; `0x6a` is `[block v4 {base}][u32 3][u32 0 x3][u32 7][u32 0][u8 x4 00 ff ff 00 x3][u32 0]` |
| `0x8009` | 2 | Scenario only, no base block | Nested blocks, starting `[block v16 {u32, block v28 {u32 count, records [u32 hash][block v58]...` |

The exe registers classes by name (`AsuraEntityClass_PFX_Effect`, `_DynamicLight`, `_GS2Node`, `_GS2Spline`,
`_CutsceneController`, `_FlowGraphController`, `_SoundController`, `EG_ENTITYCLASS_MINION`, `_VEHICLE`, `_MAP` and others);
its tables of class ids are encrypted, so the names above come from the data.

Effect (`0x21`), 185-byte form (862 of 899; the longer ones, 295-1,105 bytes, are spline effects with a point list):

```
E+0   u32 1
E+4   u32 effect hash
E+8   f32 x, y, z         position
E+20  u32                 0x7fc787fe-0x7fffe7fc, probably a packed orientation
E+24  f32 x, y, z         a second point, 1 unit above the position (x and z equal in 838)
E+36  f32 0, -1, 0
E+48  u32 0 x5
E+68  f32                 0, or 0.3-0.9
E+72  block v0 { u8 0-1, u8 0-3, u8 0, u16 0 }
```

Light (`0x47`):

```
E+0   u32 kind     0 (61), 1 (178), 2 (204)
E+4   u32 flags    0x1108, 0x1908, 0x21108, 0x21908, 0x2190c
E+8   f32 x, y, z  position
E+20  f32 x9       rotation, rows orthonormal
E+56  f32          50-500, probably the range
E+60  f32          10-50
```

Later fields include an RGB colour as three floats and, on some kind-1 lights, a projected texture path
(`\specialfx\projectors\proj_water_caustics.tga`, `\specialfx\pfx\Test_jb\Caustic1.tga`).

Animated model (`0x07`), all 335 bytes:

```
@16   block v1 (310 bytes) {
@25     u32 5, u32 0, u32 0, f32 1, 0.5, 1, 0.001, 1, 0.01     the same in all 156
@61     block v2 {
@70       u32 37, base block
@139      f32 x, y, z position
@151      f32 x, y, z, w rotation quaternion
@195      block v63 { u32 model, u8 0 }      @204 model: KeyHash of the model name (Door_Standard, Doomsday_Cavern_01...)
@244      u32 idle animation                 KeyHash of the name (WindSock_Idle_01, Doomsday_Silo_Fan_Slow) or 0
        }
        block v0 { 5 bytes }
      }
```

Companion (`0x8007`): `[u32 10][base]` (8 in one), then at E `f32 x, y, z`, `f32 x9` rotation, `u32 row, floor, column`
(row = int x, column = int z, floor 2 in all 57) and two entity ids of the scenario.

**Base block in saves.** In a save every base block is 24 bytes: `[u32 type id][u32 group][u8 x3][u32 0][u32 0]
[u8 flag][u32 m]` + m bytes. The component-list size is 0 and the list is absent, so E sits 32 bytes earlier than in the
level files. This is why [4.22](../REFERENCE.md#422-lair-maps-envsbasedefinitionslairbase) finds a character's position at @80 in a save: in scenarios it is at @112. Only the
`0x8014` flowgraphs carry extra data in a save (flag 1, 3.2 KB of graph state).

**Physical object block.** Animated models (`0x07`), minions (`0x8003`), other characters (`0x8004`) and vehicles
(`0x800b`) share one class level: `block v2 { u32 37, base block, f32 x, y, z position, f32 x, y, z, w rotation quaternion,
... }`. The classes wrap it in their own versioned blocks:

| Class | Levels file | Save |
|---|---|---|
| `0x07` | `v1 { 36 bytes, v2 {...}, v0 {5 bytes} }` | same |
| `0x8003` | `v40 { v102 { v2 ... } }` or `v41 { v107 { v2 ... } }` | `v36 { v97 { v2 ... } }` |
| `0x8004` | `v19 { v102 or v107 { v2 ... } }` | `v19 { v97 { v2 ... } }` |
| `0x800b` | `v23 or v24 { v2 ... }` | `v19 { v2 ... }` |

A save writes older version numbers than the shipped scenarios. Character data after the quaternion runs to 2.6-5.7 KB and
stays unread.

**Component type hashes.** `0x6a`, `0x74` and `0x42` remain unidentified, and none of them occurs in a save. No component type hash (`0xc5c69f77`,
`0x90b362f9`, `0xdb3f7f2d`, `0x7bee9c55`, `0xb0de9422`, `0xa94b8069`, `0x04a9cc31`, `0x4641c78e`) matches KeyHash,
case-sensitive h31, FNV-1/1a, CRC32, djb2 or sdbm of any exe string, of any `AsuraEntityClass_`/`EG_ENTITYCLASS_`/`Asura_`
name with or without its prefix, or of any word in the data files. None of them appears in any other chunk or as a
plain constant in the exe.

**Sound emitters.** The emitter id and position match a record in the island's `SDPH` for every
island emitter: Caine Key 184 of 184, tropical 02 113 of 113, tropical 03 109 of 109. The arctic island has no `0x0e`
entities, although its `SDPH` holds 106 records. The other 368 are in `benchmark.scenario`.

**Light tail.** The fields after E+64 have fixed places up to E+300 in all 443 lights:

```
E+64   u32 0
E+128  24 bytes: six f32 (projector bounds, -33 to 83 in the samples), or 0xCD fill bytes (198 lights)
E+152  f32 x4 quaternion, f32 0, -1, 0, f32 x4 (all zero when E+128 is 0xCD)
E+196  u32 projected texture: KeyHash of the texture path without extension, the PLUT key (0 when none)
E+200  f32, f32 (4, 12 or 15, the pair equal), f32 (0-60), f32 (6-100)
E+216  f32, f32  angles in radians (2.618, 1.396, 0.785; then 0.393, 0.698, 0.175), probably the outer and inner cone
E+224  f32 (1 or 200), f32 (1 or 1.2)
E+232  u32 999, u32 0 x4, u32 999, f32 1, u32 0 x3
E+272  f32 1 or 1.5, f32 2-8
E+280  f32 x3 RGB, probably the colour
E+292  f32 10, u32 0
```

The 0xCD bytes are the debug-heap fill pattern that the editor never overwrote. After E+300:

```
f32 x4 (1)
f32 x4 projector parameters (0 or 0.4, 0.4, 0.1, 0.1)
3 texture slots: path\0 padded to 4 with zeros, or the 4 bytes 00 ff ff 00 for an empty slot
u32 10007, u32 1, u32 1, f32 x4 (1), u32 10007
u32 1, u32 1, u32 0, u32 3, u32 0, f32 1, u32 0, f32 0.001
u8 flag, u32 texture hash       flag 1: the hash of the slot-1 texture
f32 x3 (0.2, -0.3, 0.2 and similar), u32 0
u8 animated; 1 adds 84 bytes: u32 0, f32 1, u32 0, f32 -744.2, u32 0, u32 0, 3 x [f32 1, f32 r, f32 g, f32 5-8, f32 10]
block v0 { 5 bytes }
```

All 443 lights parse to the end with this layout. Slot 0 holds `\specialfx\pfx\Test_jb\Blob_test.tga` on 148 lights, and on
another 30 it holds `Caustic1.tga` with flag 1. Slot 1 holds the texture that E+196 names (`Caustic1.tga` 48 times,
`\specialfx\projectors\proj_water_caustics.tga` 100 times). The animated block occurs only in scenario files.

**Spline effects.** In the 37 instances longer than 185 bytes, E+64 is `u32 16` instead of 0 and a
spline follows in the `vhcl` format: `[u32 2][u32 n][u32 1][u32 n]` + n 45-byte points. Its first point is the effect's
position. Two f32 follow (0.0002-0.117, then 0 or 0.24-0.87), then the version-0 block. In the 185-byte form, E+64 is 0
and only the second f32 is present (at E+68).

**GS2 nodes.**

```
E+0   u32 flags        0x2001, or 0x2011 when a spline id follows
E+4   f32 x, y, z      position
      u32 spline id    only with 0x2011: the 0x51 entity the node belongs to (26 of 52)
      i32 -1
      u32              0x7fc1b7fc-0x7ff447fe, the packed form seen in effects
      u32 0x2001 or 0x3000, u32 0, u32 3
      u32 hash         repeated later in the record
      ...              then name "GS2 Node"
```

**Floor flowgraphs.** The graph is component-based, not the [4.20](../REFERENCE.md#420-scripts-flowgraphs) format (no `00 ff ff 00` markers).
The component payload is `[u32 0][u32 1][u32 1][GUID][u32 2][u32 0x4641c78e][u32 size]` + graph + 57 or 61 bytes. The
graph:

```
u32 1, u32 1, u32 1, GUID, u32 n
n x node: [u32 2][u32 node type hash][u32 size] + size bytes
          size bytes: [u32 0-2][u32 3][u32 0][u32 1][GUID][f32 x, y canvas position] + type-specific fields
u32 links
links x [u32 2][u32 0x16915888][u32 88]: u32 3, u32 2, u32 1, GUID link, GUID from node, GUID to node,
          u32 0, u32 1, u32 from-pin hash, u32 0, u32 1, u32 to-pin hash, u32 0
508-1,704 bytes: comment boxes and their text ("The start node caled 'Update' runs every frame")
```

All 35 parse (1,758 nodes, 609 links). There are 17 node types. The most common, `0xefd7625d` (988 nodes), ends with an
entity or emitter id. The pins in the samples are `0xea806047` to `0x30dad90c`. No node type or pin hash matches a known
string. The 57/61-byte tail after the graph begins `[u32 1][u32 2][u32 0xc15f2e92][u32 0x29 or 0x2d]`.
