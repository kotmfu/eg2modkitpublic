# Dynamic instances (`DYIN`)

Part of the [file format reference](../REFERENCE.md): [4.36 Animation and skeletons](../REFERENCE.md#436-animation-and-skeletons).

**Dynamic instances (`DYIN`, `misc\common.asr` and 3 packages, 4).**

```
u32 1, u32 0, u32 n
n x 64:  [u32 0][u32 piece count][u32 first piece][u32 0][u32 7149f2ca][u32 7149f2ca][f32 0.08-0.46]
         [u32 0 x4][f32 1][f32 20][u32 0 x3]
n x u32  name hash per record
u32 m
m x 64:  [u32 mesh hash][u32 vertices][u32 indices][u32 0][u32 first vertex][u32 0][u32 first index][u32 0]
         [i32 -1][u32 0][f32 min xyz][f32 max xyz]
```

- Piece counts sum to m.
- First vertex and first index step by the mesh's counts (215 and 738 per piece in `misc\common.asr`).
- `misc\common.asr` holds 14 records and 16 pieces. The package instances hold 3, 2 and 1 records.
- The name hashes come in consecutive runs (`0b66fc47`, `0b66fc48`, `0b66fc49`), like names ending in 1, 2, 3.
  They are unresolved.
