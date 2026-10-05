# Sound emitters (`SDPH`)

Part of the [file format reference](../REFERENCE.md): [4.32 Sound and dialogue](../REFERENCE.md#432-sound-and-dialogue).

**Sound emitters (`SDPH`, `envs\*.pc`; 4 chunks, 527 emitters).** The island's placed ambience sources: 106, 193,
115 and 113 per island.

```
[u32 10][u32 0][u32 n]
n x emitter:
  [u32 id][u32 event id][u32 flags][f32 x][f32 y][f32 z][f32 qx][f32 qy][f32 qz][f32 qw]
  [f32 dist_min][f32 dist_max][f32 5][f32 5][f32 5][f32 10][f32 10][f32 10]
  [u8 0] volume [u32 0] [6 f32 bounds] [16 bytes] [f32 volume]
volume = [u8 1][u16 0][u8 0x80][u8 0][u32 size][u32 0][u32 shapes] + shapes      size = 8 + shape bytes
shape  = [u8 type][u32 0][u32 0x800000][u32 len] + len bytes
         type 1, len 40: [f32 min_x][f32 max_x][f32 min_y][f32 max_y][f32 min_z][f32 max_z][12 bytes 0][f32 1]
         type 2, len 32: [f32 x][f32 y][f32 z][f32 radius][12 bytes 0][f32 1]
```

- An emitter is 138 bytes without shapes. Ids count up from 100000 (`0x186a0`) or 100096 (`0x18700`) with gaps.
- Every event id resolves to an `SDEV` in the same island file (`ENV_Waves_Crash_Medium_Loop`, `ENV_Ice_Crack_Long`).
- `flags` is `0x42` (381), `0x40` (130), `0x2`, `0x52`, `0x4e` or `0xe`. One emitter carries two shapes; the rest
  carry none.
- `dist_min`/`dist_max` runs from 5/60 to 30/120 and differs from the event's own distances in 496 emitters.
- The final float is 0.1 to 1, probably a volume.
- The bounds come in equal pairs (`min_x = max_x`), probably a cached point.
