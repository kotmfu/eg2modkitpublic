# Animation (`HCAN`)

Part of the [file format reference](../REFERENCE.md): [4.36 Animation and skeletons](../REFERENCE.md#436-animation-and-skeletons).

**Animation (`HCAN`, `misc\common.asr` and packages and `envs\*.pc`, 8078).** One clip per chunk. Little-endian,
no padding after the track table, so everything after it can sit on any byte.

```
u32  22                       version
u32  flags                    see below
u32  bones
f32  duration                 seconds
f32  root speed               m/s; equals the last track's start-to-end translation / duration when flag 0x10 is set
u32  mask events              count of the 20-byte records below
name\0 padded to 4            clip name (Rifle_B_Flinch_Front_01)
u32  0, u32 0xffff0000
u32  tracks                   bones, or bones + 1 when flag 0x10 is set
u32  rotation keys            total over all tracks
u32  position keys            total over all tracks
tracks x 12   [u16 rot count][u16 pos count][u32 first rot key][u32 first pos key]
rotation keys x 8             u16 x, y, z, w
rotation key times x 2        u16
position keys x 12            f32 x, y, z
position key times x 2        u16
if flag 0x01: bones x f32     bind-pose bone length: each bone's offset from its parent (pelvis 0.92, thigh 0.44)
mask events x 20              [u64 mask][f32 start][f32 end][u32 type]
if flag 0x08: f32 x 6         bounds: xmin, xmax, ymin, ymax, zmin, zmax
u32 0, u32 n, n x 28          sound events [f32 start][u32 slot][f32 end][u32 flags][u32 point hash or 0][f32 value][u32 0]
u32  rig hash                 shared with the skeleton's HSKN (below)
bones x u32                   KeyHash of each bone name (bn_pelvis, bn_L_upLeg, cBn_hat)
u32 1, u32 0, u32 kind, u32 1  kind: 0 body, 1 facial, 2 reaction
u32 n, n x u32                KeyHash of related clips (Gun_A_SingleShot_A_01 for Gun_A_SingleShot_A_01_F)
u32  source name hash         KeyHash of the clip name, of a sibling clip, or 0
u32 n, n x object             [u32 2][u32 class hash][u32 size][size bytes, property encoding]
```

- The first-key fields chain: each track's first key equals the previous track's first key plus its count, and the
  last sums give the two totals.
- Track i animates bone i. With flag 0x10 the extra track is last and carries root motion (a walk cycle moves it 1 m
  along +Z).
- Rotation: x, y, z = v / 32767 - 1, w = v / 65535 (w is never negative). All 8.3 million keys in `misc\common.asr`
  come out unit length.
- Key times map 0..65535 onto the clip. A track's times rise from 0 to 65535; a single key sits at 0.
- Clips sample at 30 frames per second: the longest track has duration x 30 + 1 keys. Tracks drop redundant keys, and
  a constant track has one key.
- Durations run 0.033 to 100 s. Bones run 1 to 115. 340 rig hashes occur. The rig hash and bone hashes match the
  skeleton's `HSKN`; 54-bone clips such as `Rifle_B_Flinch_Front_01` use `b732dc6d`, 71-bone clips `f562706e`.
- Names ending `_F` are the facial variants; their `FAAN` (below) carries the face.

| Flag bit | Count | Meaning |
|---|---|---|
| `0x01` | 6793 | Per-bone float block present |
| `0x02` | 21 | Additive clip: rotations are offsets from the pose they play over. 70 to 95% of its rotation keys are identity, against 9% in other clips. Set on `*_Delta_*` (`Unarmed_A_Delta_Left_01`, `Flamethrower_Pyro_Delta_Back_01`, `Worker_buildtool_constructFurniture_delta_01`), `Unarmed_I_SecondaryFlinch_*` and 5 Oceans prop clips |
| `0x08` | 7575 | Bounds present |
| `0x10` | 4853 | Extra root-motion track |
| `0x20` | 8078 | Always set |
| bits 8-15 | | `0x10` always set, plus `0x02 + 0x04` (always together) and `0x08`; values 0x1e (4843), 0x16 (1708), 0x10 (881), 0x18 (646) |
| bits 28-31 | | Key reduction level, probably (below) |

Bits 8-15 do not change the layout. Every clip with `0x0600` set also has flag `0x01` (6551 of 6551). Every clip with
`0x0800` set has flag `0x08` except 4. Clips with only `0x10` mix all four combinations. These are probably the
export options that requested the per-bone and bounds blocks. `0x0600` is the common value on the 63- to 115-bone
genius and henchman rigs; `0x0800` alone is the common value on the 71-bone minion rigs.

**Key encoding.**

- Rotation: `x = round((v + 1) * 32767)` for x, y, z and `w = round(v * 65535)`. No stored key has negative w, so a
  writer negates the quaternion when w < 0.
- Times: `t = trunc(frame / frames * 65535)` in 32-bit float, where `frames = duration * 30`. This reproduces every key
  time in 2389 of 2391 clips in `misc\common.asr`; two clips (`Zalika_run_neutral_01`, `Zalika_walk_neutral_01`) differ
  by 1 on 10 keys. Some long clips (`ComputerConsole_User_Loop_B_02`) carry keys between frames. The reader maps
  `t / 65535 * duration`, so rounding to nearest is equally valid.
- Every duration is a whole number of frames at 30 fps.
- Every track has at least one rotation key and one position key (no track has 0 of either). A multi-key run starts at
  0 and ends at 65535. A single key sits at 0 (5 exceptions). Rotation and position times are independent lists.

**Flag bits 28-31: key reduction level (probably).** The nibble tracks how many keys survive reduction.

| Value | Clips | Keys / (tracks x frames) | Tracks kept at every frame | Typical content |
|---|---|---|---|---|
| 0 | 43 | 0.15 | 0% | Fans, seagulls, pendulums, conference-table loops |
| 1 | 129 | 0.15 | 1.5% | Satellite dishes, buoys, Oceans camera shots |
| 2 | 75 | 0.16 | 9% | Oceans DLC props and lair pieces (48 in `dlc_oceans_content`) |
| 3 | 7698 | 0.45 | 31% | Almost all character and furniture clips |
| 4 | 133 | 0.49 | 32% | Genius cutscene clips, reactions, some furniture |

- On one rig the density rises with the value: `3163162b` 0.05 / 0.29 / 0.48 / 0.59 for values 0 / 1 / 3 / 4,
  `204cc5a6` 0.05 / 0.35 / 0.47 / 0.65, `536e942a` 0.26 / 0.54 for 3 / 4.
- At value 0 every kept interior key deviates at least 0.011 (quaternion component) or 8 mm from the line through its
  neighbours, so the tolerance is coarse. Values 2 to 4 keep keys that sit exactly on that line.
- The value does not follow the file, the rig family, events, objects or mask events. Clip families split by
  value only because props carry 0 to 2.

**Trailer** `[u32 1][u32 0][u32 kind][u32 1]` after the bone hashes. The third word is the clip kind.

| Kind | Clips | Meaning |
|---|---|---|
| 0 | 5711 | Body clip |
| 1 | 2306 | Facial overlay: 2305 are `*_F` clips |
| 2 | 61 | Reaction overlay: all 61 `*_R` clips (`Emma_Scared_E_01_R`) |

- The related-clip list holds the clips the overlay plays over. Kind 1: the body clip with the `_F` removed (1772 +
  521) and its minion-variant siblings (`ClipBoard_Operator_Mount_C_01` for `ClipBoard_Operator_Mount_A_01_F`, 503).
  Kind 2: the clip with the `_R` removed (61 of 61). Kind 0 clips have no list except 14 `Evolve_*_F` clips.
- The source name hash after the list is `KeyHash` of the clip name (5431), of the `_A_` sibling when the clip is a
  `_B_` or `_C_` variant (120, `SlotMachineC_User_Loop_B_02` -> `SlotMachineC_User_Loop_A_02`), or 0 (2147).

**Mask events** `[u64 mask][f32 start][f32 end][u32 type]`. The mask bit is a gameplay event tag, not a bone. One bit
is set in 1679 of 1702 records; 23 have mask 0. The same bit lands on unrelated bones across rigs (bit 35 is
`bn_L_thumb_end` on `f3f47eb6` and `bn_L_lowArm_twistA` on `f562706e`), while its clips share an action:

| Bit | Records | Clips | Probable meaning |
|---|---|---|---|
| 3, 4 | 83, 106 | walk, run, jog cycles; ranges (0.25-0.4, 0.8-0.9 on `Emma_walk_neutral_01`) | Left and right foot support phases |
| 5 | 427 | `*_Flinch_*`, kills, captures | Hit moment |
| 6 | 136 | mount, unstow, pick-up | Item hand-over |
| 7, 35 | 14, 418 | punches, kicks, single attacks | Melee impact |
| 8, 36 | 199, 41 | `*_SingleShot_*`, `*_DoubleShot_*` | Shot fired |
| 9, 10, 15 | 7, 5, 5 | `Gun_Z_*`, `Gun_M_*` | Genius gun extra shots |
| 19 | 8 | `*_disguise_equip_01` | Disguise swap |
| 20 | 30 | `Gun_Z_Kill_*_Flinch_*` at 1.0 | Death at clip end |
| 23 | 4 | `*_ThrowBomb_*` | Throw release |
| 24 | 16 | `GenericRemote_*`, `PlaceBug` | Device use |
| 46 | 37 | `ExplosiveFlinch_*_Loop`, 0 to 1 | Whole-clip state |
| 0-2, 21-22, 27-30, 37-50 | 1 to 31 each | one furniture or ability each (SharkTank, Cloning, RevolvingDoor) | Object-specific |

- Type is 6 on 1678 records and 2 on 24 (Unarmed kick and punch impacts, `superInvestigator_walk_neutral_01_F`). The
  same 0/2/6 field appears in every object's base block below.
- 1471 records are instants (start = end); ranges occur on locomotion, loops and the cable car.

**Sound events (28 bytes)** `[f32 start][u32 slot][f32 end][u32 flags][u32 point hash][f32 value][u32 0]`.

- The slot is a slot of the `HSND` sound set of the model that plays the clip (`HSND` slots also run 0 to 111). Where
  that set is known (the `HSND` named after a model with the clip's rig), it holds every slot the clip uses in 316 of
  422 clips, against 116 of 422 for random slots of the same count. The `HSND` record maps the slot to an `SDEV` event.
- The point hash (`Foot_L`, `Hand_R`, `Sound C`) overrides the attach point; it differs from the `HSND` record's bone
  hash in 135 of 139 cases.
- Value is 1 on 18169 records, otherwise 0.2 to 0.8: probably volume.
- Flags: 7 on 21300, 15 on 250, others on 17 (3, 6, 10, 11, 14, 30). Bit `0x8` is common on ranged events
  (93 of 250 records with flags 15 have start < end, against 136 of 21300 with flags 7). The bits are unnamed.
- Slot by family: 0 (6663) on most clips, 28-31 on furniture loops and console use, 14-25 on Emma, 44-56 on flinches
  and kills, 100-104 on minion emotion clips.

**Objects** `[u32 2][u32 class][u32 size][size bytes]`. The body is a property (key = class version) holding the
event base block as property 5, then the class payload. Class `cc22de92` has the base block only.

Base block (property 5, 32 to 52 bytes):

```
f32 start, f32 end             normalised clip time
u32 type                       0, 2 or 6
u32 KeyHash(name) or 0
u32 1 when named, else 0
name\0 padded to 4             4 zero bytes when unnamed
u32 n, n bytes                 n = 0 except cc22de92 (1 byte, 0x24 or 0x25)
u32 type hash                  fixed per class (table)
```

| Class | Version | Count | Type hash | Payload after the base | Meaning |
|---|---|---|---|---|---|
| `d032ec05` | 1 | 1473 | `69d865af` | property 8: `[u32 FXET id][u32 KeyHash(point)][u32][u32 flags][point name\0]`, then 53-72 bytes holding u8/u16 fields and sometimes a bone name (`bn_spineA`) | Effect on a point (`PFX_Diver_DiverDrips` on `PFX_Drips`) |
| `45803af7` | 4 | 294 | `cfe120a1` | `[u32 item id][u8 on]` + 20 bytes, or 56 bytes carrying a named point (`FurnitureAttachProjectile_Attachment`) | Show or attach an item: the id is an object in `misc\common.asr` `BLUE` #22 (46 of 47 ids); on is 1 on mounts (58) and 0 on dismounts (43) |
| `a966812b` | 1 | 180 | `45deb441` | property 1 (20 bytes): `[u32 effect id][12 zero bytes][u32 0x00ffff00]` | Skin effect: every id is an object in `BLUE` #50 (`skineffect`); flinch and kill clips |
| `998715d4` | 4 | 74 | `d87366dd` or `f05c69fc` | `[u32 0]`; the base carries the name | Align the character: `d87366dd` on move names (`MoveToTrapMount`, `EscapeFling_Across`), `f05c69fc` on facing names (`FaceEscapePosition`, `RotateTowardsJobProp`) |
| `2d6bfade` | 1 | 53 | `2a360d08` | property 2: `[u32 bac30d03][u32 KeyHash(name)][name\0]` + flags | Named sound event resolved by the owner's blueprint (`SoundEvent_FirePistol`, `AnimEvent_CombatSound6`; the hashes occur in `BLUE` #4 and #22) |
| `2939715c` | 0 | 49 | `744d6b5e` | `[u32 id]` | Trap event: every id is an object in `BLUE` #65 (11 objects); SharkTank, WallTrap clips |
| `cc22de92` | - | 22 | `8d151a7c` | none | Weapon fire tick on Oceans weapons and turrets |
| `fd83df1a` | 2 | 5 | `fc45dfc4` | `[u32 item id][u8 1]` | Item event on `Gun_Z_*` and `RocketLauncher_I_SingleShot_A_01`; one id is in `BLUE` #22 |
| `7c54d995` | 0 | 1 | `fab6017f` | 4 bytes `01 01 01 01` | `Oceans_Polar_Doomday_Device_Twist_Loop_01` |

- `[u32 bac30d03][u32 KeyHash][name\0]` is the encoding of a named reference wherever it appears.
- The `BLUE` ids are not `KeyHash` of any known string.

**Per-bone block (flag `0x01`).** Each float is the length of the bone's bind-pose offset from its parent:
`|position|` of that bone in an `HSKN` skeleton, matched by bone hash. 5583 of 5601 clips that have a skeleton
holding all their bones match within 2 mm on every bone. The 18 others differ on a few bones (jaw and mouth on
`Abomination_run_start_01_F`, or a minion body with longer legs). 1192 clips have no `HSKN` in the install that holds all
their bones; 1285 clips lack the block.

**Rig hash.** A single-bone `HSKN` uses `KeyHash` of its bone name (2204 of 2204). Multi-bone rig hashes are opaque ids
(not `KeyHash` of the joined names, a fold or `CRC32` of the bone hashes). Of the 340 clip rig hashes, 95 never occur in any
`HSKN` (245 do). Among them, the 748 `204cc5a6` clips and the 648 `f562706e` clips
use only bones of `FA_BodyFull_AllAnimBones` (the `HSKN` 203 skeleton), which carries another rig hash; the 517
`3163162b` clips match no single `HSKN`. Where a skeleton holds all of a clip's bones, the clip lists them in another
order in 47 of 190 distinct bone sets. Clips therefore probably bind to
the skeleton by bone hash, and the rig hash is a label.

**Root motion (flag `0x10`).** The extra last track is the root. Rule over all 4853 clips with the flag: root speed =
distance from the first to the last root position key / duration (2302 clips), and root speed = 0 exactly when the root
does not move (2551). The root track keeps 1 or more rotation keys (585 rotate). 126 clips without the flag carry a
non-zero speed that matches no track (camera shots, a few `Gun_Z_Kill_*` flinches).

**Writing a clip.** Every `HCAN` in the install (8078) decodes to float tracks and encodes back byte for byte. The
encoder takes quaternions, positions and times in seconds, rounds as above, and derives the counts, the first-key
chain and the totals. Feeding it 32-bit float quaternions and times gives the same bytes (2391 of 2391 in
`misc\common.asr`). A clip resampled to one key per frame round-trips with a quaternion error of 1.5e-5 (half a step).

Minimal rules for a new clip on an existing rig:

- Rig: copy the rig hash, bone count and bone hash list from an existing clip of that rig; track i animates the bone
  with hash i. Keep the per-bone lengths of that clip (flag `0x01`), or clear the flag and the block.
- Tracks: one per bone, plus a root track last when flag `0x10` is set. Each track needs at least one rotation and one
  position key; positions are local offsets (a constant bind-pose offset needs one key). Times rise strictly from 0 to
  65535; a constant track has one key at 0. Counts are u16 per track.
- Duration: frames / 30. Root speed: root distance / duration, or 0.
- Bounds (flag `0x08`): a box around the posed body in the interleaved order; copying a similar clip's box works for
  a similar pose. The flag and block can be cleared.
- Flags: `0x30001e29` (no root track) or `0x30001e39` (root track) match the common character clips. `0x20` and
  `0x1000` are set on every clip.
- Empty sections: mask events 0, sound events `[u32 0][u32 0]`, related clips `[u32 0]`, objects `[u32 0]`. Trailer
  `[1][0][0][1]` for a body clip. Source name hash: `KeyHash(name)`.

**Clip lookup.** Everything that names a clip uses `KeyHash` of the clip name; no chunk carries an `HCAN` index.

- `CPAN` (random, directional and blend-space nodes), `BLUE` character and furniture blueprints (1043 of the 2081
  `characters_content` clips), `rcan`, `RFLX`, `FAAN` (its clip hash), furniture `fnas`, `COMA` and `trpa`, and the
  related-clip and source fields of other `HCAN` chunks.
- The package manifest `rpkg` lists the clips that have a `FAAN`: in `characters` 1510 hashes, exactly the 1510 clips
  with a `FAAN` (of 2081); in `dlc_oceans` 170, the 162 with a `FAAN` and 8 without. The other clips are not listed; the
  content file only has to contain the chunk.
- A new clip therefore needs a unique name and a reference by `KeyHash(name)` from whatever plays it (a `CPAN`, a
  blueprint in `BLUE`, a furniture `fnas`), or it can replace an existing clip under the same name.
