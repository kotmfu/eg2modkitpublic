# Event (`CTEV`)

Part of the [file format reference](../REFERENCE.md): [4.33 Effects and cutscenes](../REFERENCE.md#433-effects-and-cutscenes).

**Event (`CTEV`, 1313).** `[u32 28][u32 0][u32 type]`, then an outer block. The base block (version 28) holds:

```
f32 time         seconds from the start
target\0         CTAC or CTAT name, or empty
i32 -1, u32 0, u32 1, u32 0, u32 0/1, u32 0, u32 0
```

| Type | Count | Outer block | After the base |
|---|---|---|---|
| 0 Animation | 446 | 28 | `[u32 flags][f32 blend][u32 KeyHash of the animation][f32 0][f32 rate][f32 0][f32 1][f32 0][f32 1]` (`HelicoptorLand`, `Worker01_Entry_edited`) |
| 4 | 1 | 28 | block 2 `{12, 0, 0, 999, 999}` |
| 6 Effect on | 52 | 28 | `[u32 KeyHash of a CTAT name]` |
| 7 Effect off | 62 | 28 | the same |
| 10 Fade | 80 | 28 | `[f32 r,g,b][f32 duration][u32 0 in / 1 out / 3]` |
| 17 Camera cut | 103 | 28 is the base | none; target is a camera |
| 20 Hide | 236 | 28 is the base | none |
| 21 Show | 168 | 28 is the base | none |
| 22 Lens | 60 | 0 | `[u32 2][u32 0/1][f32 x7]`, the camera depth-of-field block |
| 33 Screen effect | 85 | 2 | see below |
| 35 Music | 20 | 28 | `[u32 KeyHash of a music track][u8 1]` (`DMT_MUS_CUT_Intro_Island_01_Max`) |

Every `CTEV` target names a `CTAC` or `CTAT` of the same cutscene. Hides at time 0 keep later actors out of view
until their show event.

**Type 33.** After the base:

```
f32 duration      3.3 to 40 s
f32 1
u32 5, u32 1, u8 0
state
f32 1, u32 0, u32 0, u32 0
state
[u32 0, state]    only in the 4 three-state events

state:
  name\0          always empty
  u32 5, u32 1, u32 0, u8 0, f32 1, f32 1
  u32 n; n FSX2 layers
```

81 events have two states: a layer set (a type 18 blur with `Cutscene_Intro_Vignette_01.tga`, or a type 16
overlay with `Cutscene_Intro_SkyGradient_01.tga` and similar) and an empty one, so the effect probably fades out
over the duration. The 4 three-state events (one per island, at 8.3 s, lasting 5.2 s) step a blur from focus
distances 5, 25, 50 to 2, 10, 20 and back.
