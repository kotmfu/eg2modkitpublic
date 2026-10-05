# Decals (`SDDC`)

Part of the [file format reference](../REFERENCE.md): [4.32 Sound and dialogue](../REFERENCE.md#432-sound-and-dialogue).

**Decals (`SDDC`, `envs\lair_tropical_01_default.pc`; 1).** Probably not sound data: 35 oriented boxes.

```
[u32 2][u32 0][u32 3][u32 0xd425f1d8][u32 0x5770bd2a][f32 0.274]
[u32 35] 35 x [u32 0][f32 1][f32 1][f32 -1][f32 1][f32 1][f32 1][u32 id][u32 0]        id 100000, 100001 or 100002
[u32 35] 35 x [u32 1][f32 x][f32 y][f32 z][9 f32 rotation matrix][f32 sx][f32 sy][f32 sz][f32 1][u8 1][u32 index]
```

The sizes are 2 to 4.6 square and 0.3 to 1.1 deep. The indices are a permutation of 0 to 34.
