# Emitter particles (`FXPT`)

Part of the [file format reference](../REFERENCE.md): [4.33 Effects and cutscenes](../REFERENCE.md#433-effects-and-cutscenes).

**Emitter particles (`FXPT`, 4866).** After the header:

```
curve vec4                  colour (RGBA over the particle's life)
curve f32, curve f32
curve vec2
curve f32, curve f32
u32 7
texture\0                   \specialfx\pfx\...\name.tga, padded to 4
u32 blend                   1 (2849), 5 (1904), 11 (113)
u32 1
u32 n                       flipbook frames
n x [f32 u0][f32 v0][f32 u1][f32 v1][f32 start]   start = frame / n
curve f32
u8 x4                       flags
f32 1
u32 0
u8
f32
second texture\0            empty in most
alpha LUT texture\0         ..._ALUT.tga, empty in most
u8 has second texture, u8 has alpha LUT
distortion texture\0        5 bytes of zeros when absent; present in 5 (..._DIST.tga)
f32 x19                     starts 60, ...; then 1, 0.5, flags, 100, 200, ..., 360, 0.5, 0.7
u32 m
m x [u32][u32 1]
curve vec3, curve vec3, curve vec3, curve f32
u32 x9
```

**Curve slots.** | Slot | Width | Values | Probably |
|---|---|---|---|
| 0 | 4 | 0 to 1 per channel, multi-sample in 3846 of 4866 | colour and alpha over life |
| 1 | 1 | `[0, 1]` ramp in 3981 | normalised life ramp |
| 2 | 1 | `[0, 1]` ramp in 3881; constant 0.02 to 1.4 | |
| 3 | 2 | 0.03 to 162, median 0.3 | size (width, height) |
| 4 | 1 | starts 0, ends -10 to 36, usually 0 | rotation over life |
| 5 | 1 | constant 0 to 1, median 0 | |
| 6 | 1 | starts 0, ends 1 to 839; smoke 15, steam 18, sparks 0.5 | |
| 7 | 3 | `(1, 1, 1)` in 4836 | scale |
| 8 | 3 | 0 | |
| 9 | 3 | `[0, x]` ramp | |
| 10 | 1 | 0 or 1 | |

**The 19-word block.** | Word | Values | Probably |
|---|---|---|
| 0 | 60 | the same 60 opens `FXTT`'s matching block |
| 1 | 1 to 100, median 8 | |
| 2 | 0.05 to 40, median 1 | particle lifetime, seconds |
| 3 | 0 to 10, usually 0 | lifetime random range |
| 4 | 0.1 typical, up to 30 | |
| 5 | 0 to 360 | random start rotation, degrees |
| 6 | 1 (4861) | |
| 7 | 1.0 (4498), 15, 5, 10 | |
| 8 | 0.5 (4498), 2, 1 | |
| 9 | flags (0xa000, 0x28580, 0x28480, 0x8000a000, 0x20002) | |
| 10 | 100 (4834) | fade-in distance |
| 11 | 200, down to 12 | fade-out distance |
| 12 | 0 to 5, median 0.05 | probably soft-particle depth |
| 13 | 0, or a hash in 3 | |
| 14, 15 | -8 to 9, usually 0 | offsets |
| 16 | 0 or 360 | rotation range, degrees |
| 17 | 0.5 | |
| 18 | 0.7 | |

The `[u32][u32 1]` pairs that follow hold floats (0.5, 100) or runs of consecutive values (`0x8dd7b403`,
`...404`, `...405`). The 9 closing words: 0, three angles in radians (0, pi/2, -pi), 0, the same three again in
words 4 and 5, flags (0x10 in 4835), and two angles up to pi/2.
