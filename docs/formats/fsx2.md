# Full-screen effects (`FSX2`)

Part of the [file format reference](../REFERENCE.md): [4.33 Effects and cutscenes](../REFERENCE.md#433-effects-and-cutscenes).

**Full-screen effects (`FSX2`, `misc\common.asr` 16, each island 1).**

```
u32 5, u32 0, u32 KeyHash(name with / separators), name\0
u32 5 or 10 (10 on the PhotoMode filters), u32 0/1, u32 0, u8 0, f32 1, f32 1
u32 n layers
layer: u32 type, u16 version (3 for type 16, else 2), u16 1, u32 length, (length + 4) bytes
```

| Type | Body |
|---|---|
| 16 Overlay | `[u32 0xc001][texture\0][f32 opacity][f32 0][f32 0][f32 1][f32 1][f32 0][u32 0][u32 0][u8 x3]` |
| 17 Colour table | `[u32 0x8c001][texture\0][f32 strength]`; textures are `\specialfx\fsfx\rgbtables\PhotoMode\RGBTable16x1_*.png` |
| 18 Blur | `[u32 0xc001][f32 x7][u32 1-4][u32 0][u8 0][u8 1][u8 1][f32 0][f32 0][f32 1][f32 1][mask texture\0]`; the f32 hold strengths and distances (0.1, 0.6, 0, 0, 50, 60, 200) |

The effects: `Island_Vignette_FullView`, `Island_Vignette_Normal`, `ScreenFlash`, `TransitionWipe`, `DepthOfField`,
`DoomsdayDeviceVignette_Max`, `DoomsdayDeviceFlash_Max` and 10 `PhotoMode\FSFX_PhotoMode_*` filters (`Chill`, `BW`,
`Violet`, `8mm`, `16mm`, `Sepia`, `Noir`, `Heatwave`, `SoftVintage`, `DoF`).
