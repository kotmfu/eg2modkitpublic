# Parametric animation (`CPAN`)

Part of the [file format reference](../REFERENCE.md): [4.36 Animation and skeletons](../REFERENCE.md#436-animation-and-skeletons).

**Parametric animation (`CPAN`, `misc\common.asr`, 182).** Sizes vary (64 to 322 bytes).

```
u32 1, u32 0
u32 id                        unique; ASET and RFLX refer to it
u32 2, u32 class, u32 size, size bytes
```

| Class | Count | Payload |
|---|---|---|
| `a68e0afa` | 102 | Random pick: `[u32 4][u32 2][u32 1][u32 0][u32 0][f32 0.01-1][u32 0/1][u32 0/1][u32 1][u32 n]` + n clip hashes (`KeyHash` of `HCAN` names; repeats weight the pick: `CabalLeader_Idle_01` x18) |
| `cc998397` | 42 | Directional: `[u32 2][u32 2][u32 1][u32 0][u32 0][u32 parameter hash][u32 1][u32 n]` + n x `[u32 1][f32 min angle][f32 max angle][u32 1][u32 m][m clip hashes]`. Angles in radians split the circle into front, left, back and right (`TwoHanded_J_Flinch_Front_01`) |
| `44f3e7f8` | 38 | Blend space (below) |

**Blend space (class `44f3e7f8`).** Payload after the size field:

```
u32 4, u32 2, u32 1, u32 0, u32 1
u32 rows                                     2
rows x {
  u32 1, f32 min, f32 max, u32 1, u32 n      ranges -3.054..-0.087 and 0.087..3.054 (radians)
  n x prop 2 (kind 0, length 12) { f32 angle, u32 0, u32 clip hash }
}
u32 1, u32 0, f32 0.3, f32 0.3, f32 0.15, u32 0
```

- Each row places 5 clips at angles -pi, -pi/2, 0, pi/2 and pi: step backward, left, forward, right and backward
  again (`Thief_step_backward_02`, `Thief_step_left_01`, `Thief_step_forward_01`, `Thief_step_right_01`).
- The two rows hold the step-off variants `_01` and `_02`.
- All 38 instances carry this exact shape: Thief, Soldier (bazooka), Zalika, Zalika gun and others.
