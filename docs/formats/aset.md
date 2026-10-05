# Animation set (`ASET`)

Part of the [file format reference](../REFERENCE.md): [4.36 Animation and skeletons](../REFERENCE.md#436-animation-and-skeletons).

**Animation set (`ASET`, `misc\common.asr`, 28).**

```
u32 0, u32 0
u32 type id                   6a56c46f in all 28
type name\0 padded to 4       AnimSetType - Reflexes
u32 set id, u32 1
set name\0 padded to 4        AnimSet - Reflexes - Rig A (Rig B, Pyro, OceansEG, Emma…)
u32 slots                     2
slots x {
  u32 slot id, u32 1
  slot name\0 padded to 4     Reflex - Melee - Full Body (538bd9ac), Reflex - Melee - Secondary (52898f57)
  u32 default                 a CPAN id or 0
  u32 n, n x [u32 key 0-11][u32 0][u32 CPAN id or 0]
}
u32 1, u32 hash, u32 0
```

The ids are not `KeyHash` of the names. `BLUE` refers to the set ids (character classes pick a set).
