# GUI layouts (`GUAT`)

Part of the [file format reference](../REFERENCE.md): [4.21 GUI layouts (`GUAT`)](../REFERENCE.md#421-gui-layouts-guat) · [4.37 GUI, fonts, input and stats](../REFERENCE.md#437-gui-fonts-input-and-stats).

One `GUAT` chunk per GUI archive (`gui\main.asr` 7.4 MB, 347 components; `gui\gui.asr` 816 KB, 76). It uses the
data object property encoding ([4.3](../REFERENCE.md#43-data-objects-and-properties)) and round-trips byte for byte.

```
u32 0, u32 0, u32 component count
component count x prop 0x8 (component)
```

**Component** (prop `0x8`): a raw header, then widgets (prop `0x9`) in tree order, parameters (prop `0x2`, bindings
named `Internal_*`, `External_*` and the like), and flowgraphs named `F_*`. The header holds the widget tree:

```
u32 name hash, u32 1, u32 widget count
widget count x [u8 0][u32 child count][u32 parent index (0xffffffff = root)]
```

Entries follow the widget order, which is pre-order: each widget comes after its parent, and its children follow it
before the next sibling. Widgets refer to each other by name hash, not by index. The first widget (the root) carries
the component's logic flowgraph.

**Widget** (prop `0x9`):

| Part | Layout |
|---|---|
| Transform | prop `0x7`: raw `[u32 name hash][u32 2][…]`, f32 x and y in its last 12 bytes (e.g. 270, -70); then prop `0x3f` and an f32 opacity |
| Image | raw `[u32 1][u32 0x7fa2ad4a][u32 5][u32 RGBA tint][texture path\0 padded]…` |
| Text | raw `[u32 1][u32 0x5f6527ef][u32 9]…` |
| Container | raw `[u32 1 or 2][u32 0x7933751b][u32 3]…` |
| Size | after prop `0x0` kind 63: raw with f32 width, f32 height, e.g. `[11 zero bytes][f32 40][f32 112]` |
| Flags | raw 13 bytes `00 01 00 00 00 …` |

A widget without the type block is a group with its own size raw: `[5 bytes][f32 width][f32 height]…`. Widget names
are hashed. Texture paths are stored as full `Data\Graphics\GUI\…\name.tga` strings.

**Genius and henchman bar** (`gui\main.asr`, component `0x85b32cbb`, flowgraph `F_characterSelect`):

| Widget | Index | Role |
|---|---|---|
| `0xcee990c5` | 1 | Positioner, 826 x 112, at (270, -70) |
| `0xdfa5aacd` | 2 | Shadow plate, 750 x 112, opacity 0.35: end, end, mid (746) |
| `0x5d3c049f` | 6 | Plate, 750 x 112: end, end, mid (770), indent (`t_HUDbar_indent_mid`, 730 x 72) |
| `0x18db4456` | 11 | Portrait row, 1060 x 100, gap 20, centred in the indent and scaled 0.65, so its left end sits at (indent width - 0.65 x row width) / 2. Widening the plate by d keeps the first portrait in place only when the row grows by d / 0.65. The game adds the genius and henchman portraits here. |
| `0x02105993` | 12 | Slot row, 730 x 72, gap 90: a 25 x 25 genius placeholder and five 25 x 25 `t_hudBar_button` squares |

Widgets 1, 2 and 6 sit by their centre (x at +28 of the transform), so a wider plate grows both ways. The plate always shows 6 spaces at a 115-unit pitch. Portraits past the sixth run on past the plate's end.

**Components.** A component is:

```
u32 name hash, u32 root count, u32 entry count
entry count x [u8 kind][u32 child count][u32 parent index]      tree, pre-order
entry count x entry                                              kind 0: prop 9 widget; kind 1: prop 2 component instance
u32 n, n x parameter record                                      the component's parameters (GUAP record layout)
prop 1 { u32 n, n x [u32 parameter id][u32 KeyHash(parameter name)] }   same n and order as the parameter list
u32 m, m x state machine
```

The root count equals the number of entries whose parent is `0xffffffff` (1 in 409 components, 2 in 13, 3 in 2, 5 in 1).

A component instance (prop 2, 930 entries) is a prop 7 that starts with the instance's name hash, then
`[u32 component name][u32 n]` + n parameter records (the GUAP layout) that override the template's defaults. Every
instance names an existing component: 523 in the same archive, 407 in the other one (`gui\main.asr` instances many
`gui\gui.asr` components such as buttons and popups). Every override's parameter id is one of the template's own
parameter ids (19,090 of 19,090).

A widget's or instance's prop 7 starts with the 52- or 76-byte transform, and its graphs follow inside the same prop
(a front-end page's prop 7 is 2,146 bytes and holds go-to-state nodes). The `F_*` flowgraphs therefore sit in the
entries.

Parameter ids are unique per declaration: 1,536 names map to 3,570 ids, and a name used in several components gets a
different id in each. The ids do not derive from the name (h31, FNV-1/1a, djb2, sdbm, CRC32, MD5 and Jenkins fail on
full, short, case and separator forms). `GUAP` ids behave the same. Code and flowgraphs find a parameter by id; the
name table gives `KeyHash` of each name (3,572 of 3,572 match).

**Component state machines.** 275 of the 425 components have one or more (150 have none; up to 12). They drive the
component's look: each state sets widget properties, and each transition animates them.

```
state machine:  u32 3, u32 name hash, u32 entry count, entry count x entry, u32 initial state
entry:          u32 from state (0 = a state definition), u32 state, u32 5, f32 duration, u8 flag, u32 track count, tracks
track:          u32 track kind, u32 widget name hash, u32 property hash, u32 1, u32 1, u32 key count,
                key count x [f32 time][value]
```

1,819 state definitions and 3,234 transitions; transitions carry durations such as 0.25 and 0.425. Every track's widget
hash names a widget of the same component (33,175 checked). In a state definition a track has 0 or 1 key; transitions
have up to 27.

| Track kind | Value | Common properties |
|---|---|---|
| `0x3c8b9591` | u8 bool | `0xb78ead2a` (14,238 tracks; 0 or 1, probably visibility), `0x781cbc8f` |
| `0x551e1c95` | f32 | `0x33fe8e67` (0 to 1, probably opacity); pairs `0xa1647365`/`0xa1647366` (values such as 1500 and -2000, probably position x/y) and `0xf16c797e`/`0xf16c797f` (0 to 1, probably scale x/y); `0x71f8097d`, `0x362784d3`, `0x374a64e6`, `0xb577c1ad` |
| `0x154ff737` | 4 f32 RGBA | `0x81bd945a` (1,266), `0x8b2b4999`, `0xa3e0af9c` |

A flowgraph changes state with node type `0x61e0d5dc` (80 bytes), whose body ends
`[u32 component name][u32 machine name][u32 state]`.

**Mods screen.** The front end's hidden Mods panel, the button edit that reaches it and the panel's text are in [Mods screen](mods-screen.md).
