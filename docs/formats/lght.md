# Lighting presets (`lght`)

Part of the [file format reference](../REFERENCE.md): [4.29 Other data object classes](../REFERENCE.md#429-other-data-object-classes).

**Lighting presets (`lght`, 12, `misc\common.asr`).** These chunks have no 24-byte object header: `[u32 2][u32 0]`, then
one prop `0xd` holding:

- The preset name, null-terminated and padded to 4 bytes (`Lighting\Island2.EGLighting`).
- 5 prop 3 channels, each `[u32 1]` A-colour A-scalar `[u32 1]` B-colour B-scalar. A colour track is `[u32 n]` + n
  `[f32 time][f32 r][f32 g][f32 b][f32 a]`; a scalar track is `[u32 n]` + n `[f32 time][f32 value]`. Times run 0 to 1.
  Channel 2's B-colour climbs from grey (0) through red (0.24) and white (0.48) to orange (0.73) and back to grey (1),
  a day cycle.
- `[f32][f32]` (1.5 or 2, 2), three `[f32 x][f32 y][f32 z]` angles in radians, an RGBA colour, then 3 f32.
- Prop 1: `[u32 n]` + n cubemaps `[u32 hash]` prop 0 `[12 f32 lighting coefficients][path\0]`
  (`\cubemaps\cubemap_Island2_SunTint.dds`).
- The hash of the cubemap in use.

| Preset | Used by |
|---|---|
| `Default` | JustAPlane test levels |
| `Island2`, `Island3` | Lair Tropical 02, 03 |
| `DLC_Oceans` | Lair Arctic 01 |
| `Map` | World map |
| `Outro_Max`, `_Emma`, `_Ivan`, `_Zalika`, `_Oceans` | Ending cutscenes |
| `SHQ_Light_Normal`, `SHQ_Light_LowPower` | Lighting test levels; LowPower drops channels 1-3 to one blue (0.12, 0.147, 0.2) |

A lair settings entry ([4.25](../REFERENCE.md#425-lair-settings-misccommonasr-blue-25-chunk-27)) names its preset in member `0x3b20430c`: `[3][0x23fda43e][0][3][u32 KeyHash of
"lighting/<name>.eglighting"]`. Lair Tropical 01's entry has no such member.

**Channels.** No channel names are recoverable. The exe holds only `Lighting` and `Default.EGLighting` near the lighting code, and
neither the exe nor the data strings hold a lighting parameter name. In the data: channel 2's B colour is the
8-point day cycle in every preset; channels 1 to 3 are the ones `SHQ_Light_LowPower` replaces with one blue, so they
are probably the lit (powered) light sources; `Outro_Oceans` stores all 5 channels empty.
