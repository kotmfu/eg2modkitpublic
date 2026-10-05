# Navigation mesh (`ARNM`)

Part of the [file format reference](../REFERENCE.md): [4.35 Map and save state, more data objects](../REFERENCE.md#435-map-and-save-state-more-data-objects).

**Navigation mesh (`ARNM`, `.base`, `.scenario` and saves, 27).** A Recast/Detour navigation mesh. `[u32 1][u32 0]`, then one prop (key `0x12` or `0x14`, the version) running to the end:

```
[u32 1000][u32 n]  n x 57-byte off-mesh links:
    [u32 0x3ee][u32 id][f32 x,y,z start][f32 x,y,z end][u32 0x0585a9f5 = KeyHash "actor"]
    [u32 flags 0x10 or 0x400][f32 radius 0.28][u32 area 3 or 0][u32 1][u8 1][u32 user id]
[u32 next id]
[u32 1000][u32 n]  n x prop 0x3ed (228 bytes): [u32 8] two 8-corner boxes (f32 x,y,z each)
    [u32 area 7, 8 or 9][u32 id][f32 x,y,z centre][u32 1][u32 0][u32 0]
build block (178 bytes, below)
tile slots: grid x count * grid z count entries [u32 size] + size bytes of Detour tile data; size 0 = empty
9 bytes: 00 c1 45 00 00 01 00 00 00 (the same in every file)
```

The links are Detour off-mesh connections: the per-tile `offMeshConCount` totals equal the link count in every file. The tile slot for tile (x, z) is `x * grid z count + z`. Each tile is a Detour tile with a `VAND` header, version 7.

Build block, from the start of the block:

| Offset | Field |
|---|---|
| 0 | u32 next id (one past the last box id) |
| 4 | u32 `0x0585a9f5` ("actor") |
| 8 | u32 hash, differs per map and save |
| 12 | u32, differs per map and save |
| 16 | u32 `0x5c13d641` ("Default") |
| 45 | u32 4 |
| 49 | Recast settings, f32 unless noted: cell size 0.14, cell height 0.1, agent height 1.8, agent radius 0.28, max climb 0.5, max slope 45, 0, 20, 6, 1.3, u32 6 (verts per poly), 8, 1.5, u32 28 (tile size in cells) |
| 112 | u32 grid cell count (= x count * z count) |
| 117 | u32 max tiles (1024 or 512), u32 max polys per tile (4096 or 8192) |
| 125 | f32 x,y,z origin, f32 tile width 3.92, f32 tile depth 3.92 |
| 145 | u32 grid x count, u32 grid z count (62 x 66 Crown Gold, 63 x 107 Montañas Gemelas, 57 x 103 Caine Key, 93 x 67 Arctic) |
| 153 | f32 x,y,z minimum, f32 x,y,z maximum |
| 177 | u8 0 |
