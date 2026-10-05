# Mount points (`HMPT`)

Part of the [file format reference](../REFERENCE.md): [4.36 Animation and skeletons](../REFERENCE.md#436-animation-and-skeletons).

**Mount points (`HMPT`, model runs, 585).** Sits in a model run after `HSKL` ([4.7](../REFERENCE.md#47-models-and-meshes)).

```
u32 1, u32 0, u32 points
model name\0 padded to 4
points x { f32 position x3, f32 rotation 3x3, i32 bone, name\0 padded to 4 }
```

- The bone index points into the run's `HSKN` (-1 = model origin). 3429 points; 714 sit on the origin.
- The rotation rows are unit length in every point.
- Common names: `Hair`, `weapon_L`, `weapon_R`, `Plasma_PFX`, `L_toe`, `R_toe`, `compass`, `lairBuilder_R`,
  `overhead`, `Glove_Wall_hit`, `MuzzleFlash`, `callout_camera`, `scheme_000`…`scheme_005`.
