# Cutscene (`CUTS`)

Part of the [file format reference](../REFERENCE.md): [4.33 Effects and cutscenes](../REFERENCE.md#433-effects-and-cutscenes).

**Cutscene (`CUTS`, 34).**

```
u32 36, u32 0
name\0                     IntroCutscene_Maximilian_Island01
u8 has_hash; u32 hash      GUI scenes only
u32 0
block 22 { block 40 {
  u32 actors               = number of CTAC that follow
  u32 events               = number of CTEV that follow
  f32 x3                   position
  u32 0, u8 0
  u32 n; n x path\0        sounds\cutscenes\cut_intro_island_01_max_sfx_01.wav
  f32 duration             seconds (30 to 50.4 for the intros)
  u8 x4                    flags (04 03 10 00 on intros)
  block 2 { u32 12, u32 0, u32 0, u32 999, u32 999 }
  f32 1, u32 0, i32 -1
  u32 mixer snapshot       KeyHash("Cutscene") on islands, 0 in the GUI
  u8 0
  u32 id                   first CTAC id - 1; 999 in the GUI
  u32 has_range; u32 first, u32 last    an id range 100 wide
  22 bytes                 [u32 0][u32 0][u8 0][u8 ff][u8 ff][11 zero bytes]
  u8 has_lights
  if has_lights: u32 n; n x ([f32 r,g,b][f32 x9 rotation][f32][u8 1]); [f32 r,g,b][f32][f32][u8]
}}
```

The light rig (3 lights plus an ambient colour) appears only on the GUI scenes.

**Hash on GUI scenes.** It is the id that GUI data uses to pick the scene: the GUI layout (`GUAT`, chunk
653 of `gui\main.asr`) holds the `Callout`, `MinionTraining`, `TalkingHeads_Left` and `TalkingHeads_Right` hashes,
each after a field keyed `0x6ce4713b`. The five front-end genius objects (`fegd` in
`misc\packages\managed\frontend_content.asr`) each hold one `GeniusSelection_<genius>` hash at offset 133. The hash is not `KeyHash`, CRC32, FNV or djb2 of the
name, with or without common prefixes and suffixes, and matches no known string.
