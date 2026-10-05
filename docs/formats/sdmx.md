# Sound mixer (`SDMX`)

Part of the [file format reference](../REFERENCE.md): [4.30 Other engine chunks](../REFERENCE.md#430-other-engine-chunks).

**Sound mixer (`SDMX`, `sounds\gmsndmeta.asr`, version 19).** Strings are null-terminated and padded to 4 bytes,
counting the terminator. The chunk holds a 129-byte header of floats and flags, then:

- `[u32 43]` buses: `[name][parent name]` + a 27-byte block.
- `[u32 16]` snapshots: `[name][u32 0][u32 n]` + n `[bus name][u32 bus hash]` + the 27-byte block, then a 32-byte
  trailer.
- 16 bytes: `[hash][0][0][u32]`.

The 27-byte block is `[f32 volume][f32 0.5][f32 1][4 bytes, 0 or 00 00 00 01][f32 0 or 1][u8 x3][f32]`. The bus tree
runs from `Master` through `CODE_PreMaster` to `CODE_SFX`, `CODE_Music`, `CODE_Speech`, `CODE_MenuSFX` and `FMV`, and
from `Master` to `CODE_Shakes`. The leaves include `Music`, `Dialogue`, `Barks`, `Weapons`, `Foley`, `Props`, `HUD` and
the `Env_*` ambience buses.

A snapshot's block sets each listed bus's volume while that snapshot is active. The trailer starts
`[u8 0-6][f32][f32]` (0.01 to 1, then 0.2 to 2), then `[u32 0][u8 1][u8 0 or 1]` and zeros. The snapshots:
`default`, `Front End Options`, `Front End`, `Atmospheric Low`, `Atmospheric Mid`, `Atmospheric High`, `GUI Popup`,
`FastForward`, `Talking Heads`, `Cutscene`, `TimeScale_000`, `FMV`, `World Map Atmospheric Mid`, `World Map
Atmospheric High`, `Fullscreen GUI` and `Pause`. `default` sets `Music` to 0.1 and speech buses to 0.5. `Pause` mutes
music and world sounds. `Cutscene` and `FMV` list 42 buses, including the `CODE_*` ones.
