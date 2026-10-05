# Skeleton (`HSKN`)

Part of the [file format reference](../REFERENCE.md): [4.36 Animation and skeletons](../REFERENCE.md#436-animation-and-skeletons).

**Skeleton (`HSKN`, model runs, 2848).** The first chunk of a model run ([4.7](../REFERENCE.md#47-models-and-meshes)).

```
u32 29
u32 version                   201 (2845), 203 (1), 205 (2)
u32 x                         non-zero on skinned characters (3544); 0 otherwise
u32 bones
model name\0 padded to 4
bones x u32                   parent index (bone 0 = 0, otherwise below the bone's own index)
bones x 28                    bind pose: f32 position x3, f32 quaternion x, y, z, w
u8  0 or 1
bones x { name\0 padded to 4, u8 0 or 1, u32 size, size bytes of geometry }
if 203: f32 x 6               bounds, xmin xmax ymin ymax zmin zmax (FA_BodyFull_AllAnimBones)
if 205: u32 size, size bytes  a second geometry block (Liberty_Hand, Loot_Trojan_Horse)
bones x u32                   mirror bone (bn_L_upLeg <-> bn_R_upLeg); high bit set on the last bone in 44 skinned models
u32 1 (9 on 10 models)
u32 rig hash                  KeyHash of the model name on single-bone props; a shared id on character rigs
bones x u32                   KeyHash of each bone name
tail                          91 bytes, then material variant sets (below)
```

- Characters carry their full rig (81 bones on `MA_BodyNone_Forger`) and no geometry in the chunk; their mesh lives in `RSCF`.
- Props carry their geometry inside the bone records (the `size` field). Most props have one bone named like the model.
- The bind-pose quaternions are unit length in every instance. Bone 0 of a character is `bn_pelvis` at height 0.83
  (-Y up).
- Rig hashes match the `HCAN` rig field.

**Tail.** It follows the bone hashes and has 91 fixed bytes. The fields are byte-aligned.

```
+0   f32, f32                 0 on 2724; draw distances on the rest (100, 90, 400; pairs like 800 and 200)
+8   u32 3                    LOD count
+12  3 x [f32 distance][u32 last bone]
+36  u32                      2 to 74, unknown
+40  18 zero bytes
+58  f32 1, f32 20, 12 zero bytes, f32 1
+82  u8                       1 when material sets follow
+83  u8                       0 or 1 (73), unknown
+84  3 zero bytes
+87  u32 sets
sets x {
  u32 k
  set name\0 padded to 4
  k x name\0 padded to 4      e.g. Temperature / Furnace_Off
  u32 KeyHash(set name)
  u32 n
  n x [u32 material hash][u32 replacement material hash]
}
if sets > 0: u32 0 or 1, u32 0
```

- The LOD distances are 100/100/100 on most props and 10/20/30 on characters. The last-bone field never exceeds the
  bone count - 1: `MA_BodyNone_Forger` animates bones 0-80, 0-55 and 0-35 at its three LODs. A 1-bone prop has 0.
  This field is probably the highest bone kept at each LOD.
- 183 models carry material sets, 845 sets in all. Each set swaps materials (the hashes are `MARE` keys) when the
  game selects that set.
  - Room themes: `Archive`, `Armoury`, `Barracks`, `Laboratory`, `Infirmary`, `Prison`, `Vault`, `Power`,
    `Mess_Hall`, `Training_Room`, `Staff_Room`, `Workshop`, `Corridor`, `Control`.
  - Island and lair themes: `Oceans`, `Oceans_inner_Sanctum`, `Snow`.
  - Geniuses: `Emma`, `Ivan`, `Max`, `Zalika`.
  - Skin tones and hair: `Black`, `White`, `Indian`, `Asian`, `Blonde`.
  - Colour-blind modes: `Protanopia`, `Deuteranopia`, `Tritanopia`, `FOJ_Red_Deuteranopia`.
  - Faction colours: `FOJ_Red`, `FOJ_Blue`, `FOJ_Green`, `Rebellion_Goon`.
- Most sets swap one material; up to 3 pairs occur, and 30 sets swap none. Only `Furnace_Vents` uses a qualifier
  (`Temperature` / `Furnace_Off`).
