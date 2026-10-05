# Ragdoll (`RAGD`)

Part of the [file format reference](../REFERENCE.md): [4.36 Animation and skeletons](../REFERENCE.md#436-animation-and-skeletons).

**Ragdoll (`RAGD`, `misc\common.asr`, 1).** One rig, for `THE_FOJ_SwagBag`.

```
u32 22, u32 0
name\0 padded to 4
u32 bones                                   8
bones x { f32 3x3 rotation, f32 position x3, u32 index, u32 0x7fffffff, bone name\0 padded to 4 }
u32 shapes                                  8
shapes x 80: f32 3x3 rotation, f32 position x3, f32 half extents x3, u32 bone, u32 0, f32, f32, u32 0x00ffff00
u32 joints                                  7
joints x 197 bytes (below), then a 48-byte trailer
```

Bone names are `bn_THE_FOJ_SwagBag`, `_01`…`_03` and `bn_THE_FOJ_SwagBagZipper_01`…`_04`.

**Joints.** They follow the 8 shape records:

```
u32 joints                                  7
joints x 197:
  f32 3x3 rotation A, f32 3x3 rotation B, f32 position A x3, f32 position B x3
  u32 a, u32 b, u32 c                       body indices 0-7 (first: 0 or 7 or 2)
  f32 3x3, f32 3x3                          second pair of joint frames
  u8 1
  f32 x3                                    angle limits in radians (1 deg/60/60, 0/10/10, 1.5/40/40, 0/32.5/37.5)
  00 ff ff 00
48 bytes: u32 2, u32 1, u32 7, u32 2, u32 7, u32 0, u32 0, name\0 padded to 4 (THE_FOJ_SwagBag), 00 ff ff 00
```

Every rotation in the joints is orthonormal. The whole chunk walks to its last byte:
`0x52c + 4 + 7 x 197 + 48 = 2755`.
