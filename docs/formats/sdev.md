# Sound events (`SDEV`)

Part of the [file format reference](../REFERENCE.md): [4.32 Sound and dialogue](../REFERENCE.md#432-sound-and-dialogue).

**Sound events (`SDEV`, same files; 3198).** What the game plays, version 34. `misc\common.asr` holds 1007, packages
1751, `gmsndmeta.asr` 310 and each island 21 to 37.

```
[u32 34][u32 0][name][u32 event id]
[f32 vol_min][f32 vol_max][f32 pitch_min][f32 pitch_max][f32 d0][f32 d1][f32 0.5][f32 1]
[u32 m][u32 flags][u8 loop][f32 dist_min][f32 dist_max][f32 10][f32 50]
[f32 0][f32 g][f32 0][f32 0]
[u32 n][n x [u32 hash][u32 kind]]
m = 5 only: [6 f32]                                    (1,10,1,1, 1.0f,1.0f) and similar
[bus][category][subcategory][u8][u8]["Default"][f32 dist_max]
[u32 limit][u32 KeyHash(group)][group][u8 100][u8 100]
[f32][f32][u32 0-3][u32 0][f32 fade?][f32 fade?][u32 0-2][u32 0]
[u32 event A][u32 event B][u32 snapshot]  snapshot != 0: [f32][f32]
[u32 1-2][u32 0][u32 0/1][u32 0][u32 0/1][f32][u32 0-2][f32 1][u32 0]
```

- The event id equals KeyHash(name) in 2653 events. `HSND`, `SDPH` and kind-1 entries refer to an event by this id.
- Volume runs 0.2 to 1.5 and pitch 0.7 to 1.3. `d0`/`d1` are 0 in 3084 events and up to 15 otherwise, probably a
  random start delay.
- `dist_min` is 1 to 250 and `dist_max` 15 to 9999. The second copy of `dist_max` matches in 3190 events.
- `loop` is 1 on 618 of the 656 events named `*Loop*`.
- `m` runs 0 to 5 (4 in 2931 events). `flags` is mostly `0x434`.
- The `n` entries are 1 to 25 variations, picked by the game. Kind 0 holds KeyHash of an `SDSM` name (9382 entries).
  Kind 1 holds another event's id (212 entries).
- The bus string names an `SDMX` bus: `Props` 1119, `Vocalisations_Minions` 535, `Foley` 456, `PFX` 274,
  `MenuSFX_NoPitch` 190, `Weapons` 158, then `Props_WorldMap`, `MenuSFX_Pitch`, `Shakes`, `Env`, `Env_GLOBAL`, `HUD`
  and the other `Env_*` buses. 104 events have an empty category and subcategory.
- `limit` is 1, 2, 3, 4, 5, 6, 10, 20 or 32, probably the instance limit for the group. 196 events name a group
  (`Gym`, `Footsteps`, `SlotMachine`, `Omni_Checkup_Syringe`). The others store hash 0 and an empty name.
- The `[u8 100][u8 100]` pair is 100,100 in 3192 events, probably percentages.
- Event A (94 events) and event B (77) are KeyHash names of other events, such as the `_Start`/`_Stop` partners of a
  loop.
- The snapshot hash is KeyHash(`GUI Popup`) in 9 GUI events and 0 in the rest.

**Field values.** Offsets follow the layout above.

| Field | Values | Reading |
|---|---|---|
| `d0`, `d1` | 0, 0 in 3084; (3, 5) `ENV_Bird_Crane`, (10, 15) `ENV_Island_Arctic_Wolf`, (5, 15) `ENV_Arctic_Walrus`, (0, 4) wave loops | Probably the random gap between repeats, in seconds; set mostly on looping ambience events |
| `m` | 4 in 2931, 5 in 117, 0 in 131, 3 in 11, 2 in 7, 1 in 1 | Probably the play mode. 4: pick one sample (1 to 25 kind-0 entries). 0: holds the kind-1 entries, child events played together (`PRP_Base_Satellite_Loop`, `PRP_Gym_Bike_Loop`). 2 and 3: short sequences (`PRP_SlotMachine_Coin_Insert`, `PRP_Security_Station_Telephone_Ring_02`). 5: all 76 `Shakes` events and 41 damaged-machine props; adds the 6 floats |
| `m` = 5 floats | (1, 10, 1, 1, 1, 1) on the 76 shakes; (0.1, 1, 0.1, 1, 0.1, 1) and (0.1, 5, 0.1, 1, 0.1, 1) on damaged generators; (1, 5, 1, 5, 1, 5) on spark loops | Three min/max pairs, probably modulation ranges |
| `flags` | `0x400` in all; `0x20` 3111, `0x10` 2884, `0x4` 2879; `0x8000` 235 (`Props_WorldMap`, `Env`, `Env_GLOBAL`); `0x1` 112 (ambience); `0x20000` 43; `0x800` 22; `0x8` 9; `0x200` 6 | Bits unnamed. `0x8000` marks world-map and ambience buses |
| first `u8` after the subcategory | 1 on 372: `MenuSFX_NoPitch` 130, `Props_WorldMap` 99, `MenuSFX_Pitch` 92, `HUD` 25 | Probably non-positional (2D) playback |
| 16-byte block `[f32][f32][u32][u32 0]` | (3, 3) on minion coughs, (0.3 to 2, same) on 6 props; `u32` 2 on 81 weapon events, 3 on 6, 1 on 1 | The float pair is probably a minimum gap between plays. The `u32` is probably a voice-stealing mode |
| fade pair | (0, 0.1) in 2436; (0.2, 0.4) shakes; (0, 0.5), (0.5, 0.5), (1, 1), (0, 1) on loops | Probably fade-in and fade-out times in seconds |
| `[u32 0-2]` before event A | 1 in 2535, 2 in 567, 0 in 96 | Unread |

The final nine words, in order:

| Word | Values | Reading |
|---|---|---|
| 0 | 1; 2 on the 76 shakes | Unread; follows word 6 for shakes |
| 1, 3, 8 | 0 | |
| 2 | 1 on 268 atmosphere and ambience loops (`ENV_Mid_Atmos_Waves_Small_Loop`, `ENV_High_Atmos_Wind_Loop`) | Unread |
| 4 | 1 on 22 `GUI_TalkingHeads_Sequence_*_Loop` events | Unread |
| 5 (`f32`) | 0 in 3120; 0.01 to 2 (0.05 `GUI_Furniture_Place_Snap`, 0.2 `PFX_Fire_Character_Burning_Loop`) | Probably a retrigger cooldown in seconds |
| 6 | 0; 2 on the 76 shakes; 1 on 11 `*_Controller` events | Probably the output: 0 speakers, 1 controller speaker, 2 rumble or camera shake |
| 7 (`f32`) | 1 | |
