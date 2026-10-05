# Reverb presets (`RVBP`)

Part of the [file format reference](../REFERENCE.md): [4.32 Sound and dialogue](../REFERENCE.md#432-sound-and-dialogue).

**Reverb presets (`RVBP`, `envs\*.pc.pc.sounds`; 4, identical).** One preset, `Lair_Default`, id `0x1d60f206`.

```
[u32 2][u32 0][u32 1]
[u32 id][u8 4][u16 0][u8 0x80][u8 0][u32 120] + 120 bytes, then [u8 0]
120 bytes: ["Lair_Default"]
  [i32 -1200][i32 -300][i32 0][f32 4.24][f32 0.51][i32 -1500][f32 0.039][i32 100]
  [f32 75][f32 100][f32 3762.6][f32 1][u32 3][f32 1][u32 0][u32 0][f32 1][3 x u32 0][f32 1000][f32 1][u32 1][f32 1]
  [00 ff ff 00][f32 1]
```

The first eight values read as I3DL2 reverb settings: room -1200 mB, room HF -300 mB, decay 4.24 s, HF ratio 0.51,
reflections -1500 mB at 0.039 s, reverb +100 mB. The next three are probably diffusion 75 %, density 100 % and
HF reference 3762.6 Hz.
