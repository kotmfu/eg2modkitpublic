# Actor (`CTAC`)

Part of the [file format reference](../REFERENCE.md): [4.33 Effects and cutscenes](../REFERENCE.md#433-effects-and-cutscenes).

**Actor (`CTAC`, 456).** `[u32 38][u32 0][u32 type]`, then block 38 (camera, type 0, 133) or 40 (actor, type 2,
323) around a block 38 base:

```
base:  u32 1, name\0, u32 id, u32 999, u32 0, u32 group, empty string
camera: u32 0, f32 fov (radians; 25 to 52 degrees), u32 2, u32 0/1, f32 x7 depth of field (4, 0.1, 0.05, 0.1, 1, 0.2, 0),
        f32 0 x4, f32 1, f32 near (0.1/0.5/0.001), f32 far (2000), u32 0, u32 0, u8 1, u8 1
actor:  u32 5, 5 x [u32 model][u32 material], u32 0 x3, u32 kind (0-3), u32 flags (0/8/16/18/26), u32 0, u32 hash/0, u32 0
```

Ids start at the `CUTS` id + 1 and mostly count up in chunk order (strictly in 11 of 30 cutscenes). `group` repeats for actors edited together (all workers share one). Model slots
hold `KeyHash` of model names (`minion_transport_helicopter`, `MA_Head_Asian01`, `Maximilian_BodyNone`) and of
material overrides (`Max_Head_Dark`, `Black`, `White`); 0 marks an empty slot.
