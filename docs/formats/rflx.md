# Combat reflexes (`RFLX`)

Part of the [file format reference](../REFERENCE.md): [4.36 Animation and skeletons](../REFERENCE.md#436-animation-and-skeletons).

**Combat reflexes (`RFLX`, `misc\common.asr`, 31).** One per attacker type (`Hunter`, `Soldier`, `Rig A`, `Emma`).

```
u32 0, u32 0
name\0 padded to 4, u32 KeyHash(name)
prop 1 {
  u32 groups
  groups x prop 0 {
    weapon name\0 padded to 4, u32 KeyHash   Unarmed, Gun, Rifle, Single Handed, Two Handed, Rocket Launcher…
    u32 entries
    entries x { name\0 padded to 4, u32 KeyHash, prop 1 { 7 x [u32 ref][u32 type] } }
  }
  u32 KeyHash("Reflexes")
}
```

- Props use the [4.3](../REFERENCE.md#43-data-objects-and-properties) encoding (`[u32 0x8000NNNN][u8 0][u32 length]`), so fields sit on any byte.
- 65 groups and 1947 entries. Entries are named for the victim (`Magician Ranged Kill`, `Jubei Melee Capture`,
  `Gun Capture`).
- In most entries only the first pair is set: an `HCAN` name hash (`TwoHanded_A_Capture_A_Flinch_Unarmed_01`) with
  type 0, or a `CPAN` id.
- Each group has one `Torso` entry that fills all 7 pairs. Pairs 2 and 3 hold `ASET` slot ids with the `ASET` type id;
  pairs 4 to 7 hold refs with types `a191305a` and `11ea37a4`.
