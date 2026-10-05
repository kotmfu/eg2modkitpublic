# Ambience zones (`AUDA`)

Part of the [file format reference](../REFERENCE.md): [4.32 Sound and dialogue](../REFERENCE.md#432-sound-and-dialogue).

**Ambience zones (`AUDA`, `envs\*.pc`; 4).** Two named zones and a default per island (`Main`, `INT - Lair Arctic`,
`Default environment volume`), version 13.

```
[u32 13][u32 0][u32 n]
n + 1 zones:
  +00 [u8 1][f32 priority]                    110 Main, 150 INT, 0 default
  +05 [20 bytes 0][4 bytes, empty string]
  +1d [u8 on][u32 reverb id][f32 level]       id = RVBP preset (0x1d60f206) on the INT zone
  +26 [u8 on][u32 path hash][f32 volume]      streamed ambience loop
  +2f [u8 on][u32 bus hash][f32 0.5]          KeyHash(Env_Streams)
  +38 ... layers (below)
  [6 f32 bounds: min_x max_x min_y max_y min_z max_z]
  volume (as in SDPH)
  [name][u32 colour][u32 zone id]
trailer (below)
```

- The path hash is KeyHash of a streamed WAV path in `sounds\streamingsounds.asr.pc.streamsounds`:
  `Sounds/Environment/ENV_Beach_Generic_Base_Loop_01.wav` (Main, volume 0.4) and
  `Sounds/Environment/ENV_Mountain_Generic_Base_Loop_01.wav` (INT, volume 0.08).
- Main has one box shape spanning the island (plus or minus 30000). INT has a box and a sphere of radius 250. The
  default zone has no shapes.
- The colour is `0xffe1c9fd`-style ARGB, probably for debug drawing.
- Zone ids are `0x908d8cab` to `0x908d8cad`.

**Zone layers and trailer.**

```
zone, from +38:
  +38 [u8 0][u32 0][f32 30]            0 on the default zone and on Main in lair_arctic_01
  +41 [16 bytes]                       a fixed 16-byte value on every INT zone, 0 elsewhere
  +51 [5 bytes 0][f32 0.5][u32 4][u32 n]
  +62 n x 51-byte layer:
        [u32 3][u32 index][u32 0][u32 0][f32 1][35 bytes, a 1 at +14 (index 1) or +15 (index 0); index 1 ends [u32 1]]
  then [6 f32 bounds] and the volume block
```

`n` is 1 on Main, 2 on INT, 0 on the default zone. The fixed part is `0x62 + 51 * n` bytes in all 12 zones.

```
trailer:
  [u32 2 zones][u32 1][u32 0]
  6 x [4 f32]           min_x, min_y, min_z, max_x, max_y, max_z of each zone in file order;
                        slots 3-4 and the default zone hold +1e30 (min) and -1e30 (max)
  [u32 1][u8 1][u8 1][26 bytes 0]
  [u32 k] k x [u32 inner zone id][u32 outer zone id][f32 2][f32 1][u32 0]
```

The trailer bounds match each zone's own bounds. `k` is 1 on `lair_tropical_01` and `lair_tropical_02` (INT inside
Main), 0 on the other two. The pair probably sets a 2-second crossfade between the zones.
