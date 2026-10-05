# IK template (`IKTM`)

Part of the [file format reference](../REFERENCE.md): [4.36 Animation and skeletons](../REFERENCE.md#436-animation-and-skeletons).

**IK template (`IKTM`, `misc\common.asr`, 1).**

```
u32 0, u32 0
name\0 padded to 4            IK_Template_Default
u32 0
u32 model hash                KeyHash("MA_BodyNone_CnstWrkr")
u32 chains                    2
chains x 137 bytes:
  u32 0, u32 4, u32 6, u32 4, u32 7
  u32 x3                      KeyHash of upper, middle, end bone (bn_L_upLeg, bn_L_lowLeg, bn_L_foot)
  u32 0
  f32 2, f32 pi, f32 15, f32 30, f32 46.7, f32 0.05, f32 0.5
  u32 root bone hash
  4 names\0 padded to 4       bn_L_upLeg, bn_L_upLeg, bn_L_lowLeg, bn_L_foot
  u32 0, f32 1, u32 0, u32 0, u32 0, u8 0
```

The second chain repeats this for the right leg.
