# Attachment (`CTAT`)

Part of the [file format reference](../REFERENCE.md): [4.33 Effects and cutscenes](../REFERENCE.md#433-effects-and-cutscenes).

**Attachment (`CTAT`, 358).** Follows the `CTTR` of the actor it hangs on. `[u32 6][u32 0][u32 type]`, then block 7
(type 0) or 6 (types 2 and 5) around a block 6 base:

```
base:   bone\0 (hat, Light_Right, or empty), name\0, u32 KeyHash(name) (1 or 2 when the name is empty or repeated),
        u32 kind (0, 1, 8, 9), f32 x7 = 0, 0, 0, 0, 1, 0, 0
type 0 (model, 286): u32 5, 5 x [u32 model][u32 material], u32 0    (MA_Hat_CnstWrkr)
type 2 (light, 4):   name\0 of the light (Landing_Light_R)
type 5 (effect, 68): u32 FXET hash
```

**Base floats.** All 358 hold `0, 0, 0, 0, 1, 0, 0`. In the x, y, z, w order that `CTTR` uses, an offset
plus an identity quaternion would read `0, 0, 0, 0, 0, 0, 1`, so the seven are not that. The layout stays unknown.
