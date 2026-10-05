# HRTF tables (`HRTF`)

Part of the [file format reference](../REFERENCE.md): [4.32 Sound and dialogue](../REFERENCE.md#432-sound-and-dialogue).

**HRTF tables (`HRTF`, `sounds\gmsndmeta.asr`; 1).** Four head-related filter sets from the CIPIC
database. Each set holds horizontal-plane impulse responses only, 16 directions in 32 slots.

```
[u32 2][u32 0][u32 4]
4 x set:
  32 x slot: [128 f32 left ear][128 f32 right ear]      slots 0-15 used, 16-31 zero
  [f32 0][f32 0]
  [32 f32 azimuth, radians]                             16 used, the rest 0
  [u32 16 directions][u32 taps][u32 taps][f32 1][name]
```

- A response keeps its first two samples at 0, then `taps` non-zero samples: 105 for `KEMAR Large Pinna (CPIC
  Subject 021)`, 111 for `KEMAR Small Pinna (CPIC Subject 164)`, `Male1 (CPIC Subject 163)` and `Female1 (CPIC Subject
  015)`. Samples 107 or 113 to 127 are 0.
- The azimuths are 0, 15, 25, 35, 55, 80, 115, 135, 180, 225, 245, 280, 305, 325, 335 and 345 degrees. Positive angles
  turn to the right: at 80 degrees the right-ear response peaks at sample 8 to 17 and the left at 34 to 47. 115 to 245
  degrees are CIPIC's rear positions (elevation 180).
