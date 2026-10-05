# Track (`CTTR`)

Part of the [file format reference](../REFERENCE.md): [4.33 Effects and cutscenes](../REFERENCE.md#433-effects-and-cutscenes).

**Track (`CTTR`, 454).** `[u32 8][u32 0]` then block 1:

```
name\0           the CTAC it moves (the chunk follows that CTAC)
u32 flags        0x02 (282), 0x1c2 (120), 0x102 (24), 0x5c2 (20), 0x12 (8)
f32 length       path length
u32 n            keys
n keys:
  f32 x3 position, f32 x3 in tangent, f32 x3 out tangent, f32 x4 rotation quaternion
  f32 time
  f32 ease, u32 flags (0, 4, 5, 8, 12, 13), f32 speed     (below)
  f32 segment length, f32 distance at this key
  u32 m; m x f32    arc-length table, 0 or 19 entries
f32, f32         0 and 1, or a time range when flag 0x400 is set
```

**Key fields.** After `f32 time`:

```
f32 ease          0 in most keys; 0.24 and 0.52 on actor walks (probably an ease-in or blend time)
u32 flags         0, 4, 5, 8, 12, 13 (bits 0x1, 0x4, 0x8)
f32 speed         0 or the speed at the key; close to segment length / time to the next key
                  (helicopter 120.7 against 125.1, 141.1 against 139.1, 10.1 against 9.7)
```

Flag 4 appears only on middle and last keys; 0x8 keys always carry the 19-entry arc-length table. The tangents do
not follow the flags (all four zero/non-zero combinations occur under flags 0 and 12). The track trailer reads
`[0][1]` except on the 20 tracks with header flag 0x400, which read `[10][10.26]`.
