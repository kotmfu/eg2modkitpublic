# Trails (`FXTT`)

Part of the [file format reference](../REFERENCE.md): [4.33 Effects and cutscenes](../REFERENCE.md#433-effects-and-cutscenes).

**Trails (`FXTT`, 984).** Ribbons along a path (`Wake-Line`, `Fire_Ribbon_V2`, `LightBeam_B`). After the header:

```
curve f32, u32 x3
curve f32, u32 x3
curve vec3                  path points (x, y, 0)
f32
u32 7
texture\0
u32 blend, u32 1, u32 n, n flipbook frames      as in FXPT
curve f32
u8 x4, f32 1, u32 0, u8, f32
second texture\0, alpha LUT\0, u8, u8, distortion texture\0    as in FXPT
u32 x2
curve vec4                  colour
curve f32 x5
u32 x5
curve f32
u32 x12
```

**Slots.** | Slot | Width | Values | Probably |
|---|---|---|---|
| 0 | 1 | `[0, 1]` ramp in 594; drawn with 64 or 129 samples | |
| 0 words | 3 f32 | -1 to 1 | |
| 1 | 1 | constant 0 to 8.8; ramps to 1 | |
| 1 words | 3 f32 | 0.01 to 30 | |
| 2 | 3 | path points (x, y, z), constant 0 in 897 | trail shape |
| 3 | 1 | `[0, x]` ramp, x to 1996 | |
| 4 | 4 | 0 to 1 per channel | colour and alpha |
| 5 | 1 | 0.06 to 1, usually 1 | |
| 6, 7 | 1 | `[0, 1]` ramps; constants 0.79 and 0.03 | |
| 8 | 1 | 0 to 375, median 0.5 | probably width |
| 9 | 1 | 0.2 to 1, usually 1 | |
| 10 | 1 | 0.6 to 6, usually 1 | |

The 5 words after slot 9: 1 to 100 (median 6), -10 to 80, 0 to 10, and two equal values 0.1 to 120. The 12
closing words: 0.01 to 15, 0.1 to 2.5, 0.01 to 40, 1 to 100, 0, 1, 100, 200 (974), two angles up to pi/2 and
pi/4, 0 to 3, and a varied word. The 100 and 200 pair matches `FXPT` block words 10 and 11.
