# Effect (`FXET`)

Part of the [file format reference](../REFERENCE.md): [4.33 Effects and cutscenes](../REFERENCE.md#433-effects-and-cutscenes).

**Effect (`FXET`, 1166).** After the header:

```
curve vec4                  probably bounds
curve f32
f32 x7                      usually 0, 1, 0, 100, 10, 0, 0
u32 n                       parts
n parts:
  u32 hash                  an FXST/FXPT hash or an FXTT hash
  f32 x3                    position
  f32 x9                    rotation matrix
  f32 delay                 0 in 5432 of 5888 parts; 0.01 to 27 s, median 0.1 (probably start delay)
  u32 volume                spawn volume, see below
  u32 flags                 0-3
  f32 x k                   volume size
  u32 0
  u8 trail                  index of an FXTT part in the same effect that the emitter follows; 0xff for none (54 set)
  u8 is_trail               1 exactly when the part is an FXTT (984)
  u8 0
f32 duration                1 to 8 seconds is typical
u32 flags                   usually 2
u32 0
u32 sound                   KeyHash of a sound event (ENV_Cavern_Waterfall_Loop_A), or 0
f32 0.5
```

Every part belongs to the same effect name.

| Volume | Size floats | Use |
|---|---|---|
| 0 | none | point |
| 1 | min x, max x, min y, max y, min z, max z | box |
| 2 | radius | sphere (`embers` 1.0, `Fire_flash` 0.02) |
| 3 | x, y, z | ellipsoid or flat disc (`Waves` 0.5, 0, 0.5) |
| 4 | radius, thickness | probably a cylinder or disc edge (`Ice-Shards` 0.2, 0.05; `BeamSpikeA` 8, 0.1) |
| 5 | radius x, radius z, thickness | ring (`WindRing-A` 0.4, 0.4, 0.05; `shieldSpikes` [4.9](../REFERENCE.md#49-texture-streaming-ts--txst-pc_textures), [4.9](../REFERENCE.md#49-texture-streaming-ts--txst-pc_textures), 0.01) |

The two leading `FXET` curves: the vec4 is usually `(1, 1, 1, 1)` with the fourth value up to 100 (probably a
tint and an intensity); the scalar curve is a `[10, 0]` ramp in most multi-sample cases and 0.7 to 2.3 as a constant.
Both stay unnamed. The trailer's `u32 flags` holds bits 0x1, 0x2, 0x8, 0x40, 0x100, 0x4000, 0x8000 and 0x10000;
0x2 alone is the most common (402).
