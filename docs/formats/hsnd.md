# Sound sets (`HSND`)

Part of the [file format reference](../REFERENCE.md): [4.32 Sound and dialogue](../REFERENCE.md#432-sound-and-dialogue).

**Sound sets (`HSND`, `misc\common.asr` 401, packages 1361, islands 7; 1769).** One per model or character body, named
after it (`Island_Satellite_Dish_Medium`, `MA_BodyNone_Hunter`, `Gun_*`, `InterrogationChair_*`). The game triggers
them from animations; they are not placed in the world.

```
[u32 9][u32 0/1][u32 n][name]
n x 64 bytes: [u32 slot][f32 1][f32 1][f32 20][f32 1][u32 event id][u32 event 2][7 x u32 0][u32 KeyHash(bone)][u32 0]
```

- `slot` runs 0 to 111 and is not sequential.
- 6012 event ids point at an `SDEV` in the same file, 1881 at one in `misc\common.asr`, and 672 are 0.
- The bone hash is set on 2296 records: `Hair` 976, `Weapon_R` 357, `L_toe` 273, `R_toe` 73, `Weapon_L` 55,
  `Hand_L`, `IRIS_Head`.
- Event 2 is set on 52 records. It names a damaged variant (`PRP_Punching_Bag_Hit_Damaged`).
