# Mods screen

Part of the [file format reference](../REFERENCE.md): [4.21 GUI layouts (`GUAT`)](../REFERENCE.md#421-gui-layouts-guat). Layout, tree, state machine and flowgraph formats are in [GUI layouts](gui-layouts.md) and [scripts](scripts.md).

## Where it is

All structures below sit in the `GUAT` chunk of `gui\main.asr`. Offsets into an entry count from its prop 9 (widget) or prop 2 (instance) header.

**Front end.** Component `0x8f3f8b29` (`F_Frontend_*`). Its main menu is layout widget `0xdae655e3` (entry 17), holding five groups. Each group holds a button instance of `0x617fcb8c` and a highlight group of 4 images:

| Entry | Group | Button |
|---|---|---|
| 18 | `0x66772c9c` | Load Game |
| 25 | `0x21ff9aa2` | New Game |
| 34 | `0x44a8f327` | Options (instance `0x0a3ece9e`) |
| 41 | `0x2a9e7fee` | Credits |
| 53 | `0x33192a02` | Quit |

No button carries `FE_MODS`.

**Mods panel.** Component `0xdc290d93`, instanced in the front end as entry 90 (`0x45039c4f`) under widget `0x752ad799`, beside the Load Game (`0x5c1bb0`), Options (`0x7db10e6a`) and two other side panels. It holds:

| Entry | Widget | Content |
|---|---|---|
| 2 | `0xda82747f` | Title group: bar `0x7d94263e`, TextBlock `0xc3b789bc` (`menu/FE_MODS`, "Mods") |
| 8 | `0x0874bf50` | Bar holding TextBlock `0x6725b0c6` (`menu/FE_MOD_CONFLICT`, "Selected mods have conflicts") |
| 6 | `0x9621b436` | Scrollbar, instance of `0x7d524e8c` |
| 7 | `0x35ae1f8b` | Back button (`FE_BACK`), instance of `0x7821c3db` |

The parameters are `Internal_MaxScroll`, `Internal_MinScroll`, `Internal_ScrollStep`, `Internal_ScrollOffset`, `Internal_ScrollValue`, `External_Flow_Back_(Incoming/Outgoing)` and `Internal_NavSolver_Left_toSet`. The list has no data provider parameter, and the panel has no state machine.

**Showing the panel.** Front-end machine `0xabc03abf` has 12 states. State `0xad82ddf4` shows the panel (track `0xb78ead2a` = 1 on `0x45039c4f`). The machine holds the transitions `0xf1e39e3d` (main menu) → `0xad82ddf4` (0.46 s), `0xad82ddf4` → `0xf1e39e3d`, `0xad82ddf4` → `0x7e2a6539` and `0xad82ddf4` → `0x213c16f8`.

The Load Game (`0x4ed1ec7e`), Options (`0x94f54ffc`) and two other panel states each have a go-to-state node in the front end's flowgraphs. No node in any `GUAT` or `GUIF` graph targets `0xad82ddf4`, so the shipped GUI cannot reach the panel. The missing piece is the button and its go-to-state node; no condition hides the panel.

**Back.** Entry 90 overrides `External_Flow_Back_(Outgoing)` with link `[0][0][0x1526cad2]`, the front end's `Internal_Flow_Back_(Incoming)`. The front end's root graph handles it, as it does for the other panels.

**Second copy.** The new-game component `0x2743c37c` holds another `FE_MOD_CONFLICT` TextBlock (`0x651f8804`) on the difficulty page (widget `0xacda53a5`). No graph or state track targets it or its two parents.

## Adding the button

1. Copy the entry data of entries 34 to 40, the Options group (5,359 bytes).
2. In the copy's button graph `F_Frontend_MainMenu_ButtonOptions`, set the state word of go-to-state node 0 from `0x94f54ffc` to `0xad82ddf4` (button entry +142). The node's body ends `[0x8f3f8b29][0xabc03abf][state]`.
3. Change the copy's Text override from `[1][0][0x0033155f menu][0x16590b1e FE_OPTIONS]` to key `0xc5830531` (KeyHash `FE_MODS`).
4. Rename the graph to `F_Frontend_MainMenu_ButtonMods`. The name is null terminated and padded to 4 bytes, so the entry shrinks by 4: lower the button entry's prop 2 length (+5) and prop 7 length (+14) by 4.
5. Give new ids to the 7 widget names, the 20 graph node ids (from the graph's prop 1 map), highlight machine `0x7b6c4361` and its 2 states. Replace every occurrence in the copy. Keep event words, parameter ids, override template ids, component ids, and texture and text hashes.
6. Copy machine `0x7b6c4361` (1,076 bytes) with the same id map. Its tracks then drive the copied highlight images.
7. Rebuild the tree table:
   - insert 7 rows in front of the Quit group (index 53);
   - parent the group row to 17 and the other rows to old parent − 34 + 53;
   - add 7 to every existing parent of 53 or more;
   - raise entry 17's child count from 5 to 6 and the entry count (component +8) from 144 to 151.
8. Insert the copied entry data in front of old entry 53's data.
9. Raise the machine count from 11 to 12 and append the copied machine at the end of the component.
10. Set the component's prop 8 length to the new size (+6,494 bytes).

The machine and the panel's Back handling need no change. The copied button is missing from graph `F_Frontend_Gamepad_Focus`, so gamepad focus skips it.

## Panel text

The panel's `FE_MOD_CONFLICT` TextBlock and its bar stay hidden in game, even with new text and with the bar's binding record `{1, 0, 0, 0x12, 0x040e1a0e}` removed. No graph, track or other data names either widget, so the hiding comes from the exe.

A copy of the title group shows. To add a text block under the title:

1. Copy entries 2 to 4 (group, bar, TextBlock) and insert them at index 5 under root 0. Add 3 to every parent of 5 or more, raise the root's child count by 1, and raise the entry count by 3.
2. Give the 3 widget names new ids.
3. For a block of height h, edit the copy's f32 fields:

| Entry | Offset | Field | Title value |
|---|---|---|---|
| Group | +50 | y (centre) | -910; set -860 + h/2 |
| Group | +78 | Height | 80 |
| Bar | +141 | Height | 80 |
| TextBlock | +86 | Text scale | 1.5; set 1 |
| TextBlock | +102 | Text key (u32) | `FE_MODS`; set the new key |
| TextBlock | +310 | Height | 80 |

4. Fix the component's prop 8 length.

At text scale 1, a line takes about 45 units of height; h = 20 + 45 × lines fits the text. Text uses `\n` for line breaks.

## Buttons and pages in the panel

A component's graphs can send its own state machine to a state: every one of the 1,811 go-to-state nodes in this `GUAT` names the component holding the graph and one of that component's machines. Component `0x8585883e` switches its sub-pages this way, with instances of button `0x7821c3db` (the panel's Back button component) whose graph is press, then go-to-state. The same pattern gives the panel one button and one text page per entry.

**Press graph.** Entry 61 of `0x8585883e` (instance `0xe24997ff`, an instance of button `0x7821c3db`). Its graph at +46 (281 bytes) has 3 nodes (`0xd6660e58`, press event `0xdefe9eb1`, go-to-state) and entry node list [1]. The go-to-state words `[0x8585883e][0x7ce900d6][state]` sit at +254 and the 3 graph map node ids at +303, +311 and +319.

**Row template.** Button `0x4e9304f5` (`gui\gui.asr`) is a list row: 522 x 74, with a 495 x 60 label at text scale 1.15. Entry 19 of component `0x097fcbd5` (instance `0xe8492c83`, `menu/GAMEOVER_LOAD_LAST_SAVE`) instances it. No button component has a size parameter, and a label longer than its box is squashed to fit: the 221-wide `0x7821c3db` fits about 9 characters, the row about 22.

To build a row:

1. Take entry 19's first 46 bytes, entry 61's press graph, then entry 19's bytes from the end of its own graph to the end of prop 7 plus the component word (prop 1 `{0}`, the transform, `0x4e9304f5`).
2. Append the override list without the two records that link to `0x097fcbd5`'s own parameters (`ParamIDToToggleOnPress`, `isDisabled`): type `0xed974765` with a final u32 other than 0 and `0xe4f7a6ff`.
3. Set prop 7 length (+14) to 346 and prop 2 length (+5) to the entry size - 9. The row is 2,641 bytes.

| Offset | Field |
|---|---|
| +18 | Instance name |
| +254 | Go-to-state words |
| +303, +311, +319 | Graph map node ids |
| +340, +344, +348, +352 | f32 x, y, scale x, scale y (the last 24 bytes of prop 7 are `[x][y][sx][sy][rot][alpha]`) |
| +1597 | Text override key in `[1][0][0x0033155f menu][key]` |

A uniform scale shrinks label and box together; raising sx alone stretches the label.

**Page template.** The panel title (Panel text, above), with the group's f32 x at +46 and width at +74, the bar's width at +137, and the TextBlock's width at +306.

**Steps, for N rows:**

1. Build row i as above, with a new name, new map ids, `[0xdc290d93][machine][state i]` at +254, its label key and its position and scale.
2. For each page i, copy the title group, bar and TextBlock with new names, the page's text key at +102, text scale 1 and its size and position.
3. Insert the rows after the last entry of root 0's subtree (index 13, in front of the second root `0x8030c437`): per row `[1][0][0]`, then the page group `[0][2][0]`, bar and TextBlock `[0][0][group]`. Add 4N to the parents at or above the insert point and to the entry count, and 2N to root 0's child count.
4. The panel ships with no machine (its machine count is the last u32 of the component). Set it to 1 and append:
   - `[3][machine][(N+1)^2]`;
   - N+1 state definitions, the hidden state first: `[0][state][5][f32 0][u8 0][N]`, then per page group the track `[0x3c8b9591][group][0xb78ead2a][1][1][1]` with one key `[f32 0][u8 shown]`;
   - (N+1)N transitions, one per ordered pair of states: `[from][to][5][f32 0][u8 0][N]`, then the same tracks with 0 keys;
   - the initial state word, the hidden state.
5. Fix the component's prop 8 length.

**Layout.** The panel background is 1140 x 1740 (y -870 to 870) and the scrollbar covers x 530 to 570.

- Rows at scale 0.75 (391.5 x 55.5): x -354.25 (spanning -550 to -158.5), y = -832.25 + 63.5i.
- Pages: x -130 to 510 (centre 190, width 640, text width 600), top at y -860, height 20 + 45 x lines.
- At text scale 1 a character takes about 19.5 units, so a 600-wide text holds about 30. The TextBlock breaks lines only at `\n`.

The row's `IsSelected` parameter can only be set by a graph inside the row, so the panel's machine cannot mark the open row. The new rows are not in a focus chain, so a gamepad cannot reach them.
