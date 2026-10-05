# Research (`rtrp`, `rttr`) and engineering (`rctr`, `rctt`)

Part of the [file format reference](../REFERENCE.md): [4.15 Research (`rtrp`, `rttr`) and engineering (`rctr`, `rctt`)](../REFERENCE.md#415-research-rtrp-rttr-and-engineering-rctr-rctt).

- `rtrp` starts with prop `0xf`: name text ref @+33, description @+49, f32 research time @+77, node icon key. Scripts
  give the rewards ([4.20](../REFERENCE.md#420-scripts-flowgraphs)).
- Requirement lists sit inside the first prop: minion entries prop4 `[type][count][3][u8 1]`, furniture prop1
  `[fnas][count]`, costs prop1 `[rcns][amount]`, unlocks `[n][fnas…]`. A project with an empty furniture list never
  progresses.
- `rttr` (research tree), prop `0xa` → `0x4`: links `n + (from, to, 0)`, the same links reversed, nodes `n +
  (index, out links, in links, rtrp id, u32 column (1-based), u8 available at start, u32 row)`, `u32 cols, u32
  rows`, grid (node index per cell, row-major, -1 empty), colours. Game trees use at most 5 tiers and 7 rows, links
  run forward within a row, and a node has at most one prerequisite. A tree outside those limits crashes the game.
- `rctt` (engineering tree): fixed header to +130 (+45 name, +49 selector icon key, +53 background key), then links,
  nodes with free placement (`[item or 0 = junction][u8 root][u32 col][u32 row][f32 dx][f32 dy]`), and a grid.
