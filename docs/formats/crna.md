# Coronas (`CRNA`)

Part of the [file format reference](../REFERENCE.md): [4.33 Effects and cutscenes](../REFERENCE.md#433-effects-and-cutscenes).

**Coronas (`CRNA`, one per island).** `[u32 5][u32 0][u32 n][u32 1]`, then n 76-byte records:

```
f32 2, u32 999, u32 0
f32 r, g, b
f32 size          3 to 80
u32 texture       index into the TEXT chunk just before
f32 x3 position
f32 x3 direction
u32 flags         18, 50, 82, 86, 114, 150, 178...
u32 0, i32 -1
f32 0.25 or 0.5
f32 -0.251
```

Counts: Arctic 126, Tropical 1 89, Tropical 2 76, Tropical 3 83.

**Flags.** Values are 16, 18, 22, 50, 54, 82, 83, 86, 114-118, 146-150, 178, 182, 338, 370 and 402. Bits 0x2
and 0x10 are set on all but 4 records (those 4 hold 16 alone). The bits do not follow the texture index, the 0.25 or
0.5 word, or the direction. The meaning stays unknown.
