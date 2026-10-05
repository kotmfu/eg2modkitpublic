# Dynamic music (`DYMC`)

Part of the [file format reference](../REFERENCE.md): [4.32 Sound and dialogue](../REFERENCE.md#432-sound-and-dialogue).

**Dynamic music (`DYMC`, `sounds\gmsndmeta.asr`; 1).** Version 3, 46277 bytes.

```
[u32 3][u32 0][u32 4]["Layer 1"]["Layer 2"]["Layer 3"]["Layer 4"]
[u32 53] tracks:
  [u32 18][name][u32 4][4 x path]                   one WAV; the other three paths are empty
  [f32 loop_start][f32 loop_end][f32 1][f32 bpm][u32 beats_per_bar][u32 0][f32 bar][f32 length]
  [4 x u32 layer flag 0-2][u8 1][u32 KeyHash(bus)]
  4 x [f32 volume][f32 0/1][u8 0][f32 fade][f32][f32 0.08][f32 0.08]
  [f32][u8 1]
[u32 57] themes: [u32 10][u32 type][u32 KeyHash(name)][name "DMT_<track>"] + body (below)
music logic graphs and variables (below)
```

- Every path sits in both `sounds\streamingsounds.asr.pc.streamsounds` and the `.ssm` copy: 21 `Sounds\Cutscenes\…`,
  31 `Sounds\Music\Gameplay\…` and `Sounds\Music\Benchmark\…`, two `Sounds\Music\Menu\…`.
- The bus hash is KeyHash(`Music`) on 32 tracks and KeyHash(`Cutscene_Music`) on 21 (cutscene intros and the
  benchmark loop).
- Cutscene tracks store BPM 60, loop 0 to length. Music tracks store their real tempo (`MUS_MenuTrack2_123BPM_3-4`:
  123, 3 beats). `loop_start` is a whole number of bars, and `bar` is one or two bars (1.46 s at 123 BPM in 3/4).

**Themes and music logic.** A theme starts `[u32 10][u32 type][u32 KeyHash(name)][name "DMT_<track>"]`. Bodies by type:

```
type 1 (24: cutscene intros, stings), 32 bytes:
  [u32 1][u32 0][u32 0][u8 0][u8 flag][u32 3][u32 KeyHash(track)][u32 4][u32 0][u8 1][u8 0]
type 0 (32: gameplay, menu and credits music), 147 bytes:
  [u32 1][u32 0][u32 0][u8 0][u8 flag][u32 7][u32 KeyHash(track)]
  [f32 10][f32 20][u32 0][f32 lead][u32 0][u32 3] ...
  +63 [f32 0.19-3.2]  +110 [f32 0.1]  +132 [u8 1]  +141 [u8 0/1]  other bytes fixed
type 2 (DMT_Stop_1SecFade), 84 bytes: [u32 1][u32 0][u32 0][u8 0][u8 0][u32 4][u32 0][f32 5] ...
```

- `flag` is 1 on 18 type-0 themes and 3 type-1 themes (`DMT_MUS_Sting01` to `03`).
- Type-0 themes vary in only 21 bytes. `lead` equals the track's `loop_start` minus its `bar` field in every
  theme (`MUS_Hi_130BPM_4-4`: 7.38 - 6.46 = 0.92). The 10 and 20 are fixed, probably the minimum and maximum play time
  in seconds before a change.
- `+110` is 0.1 in 26 themes, 0.05 to 0.64 in the rest. `+132` is 0 on `DMT_TEST_FG3` and the benchmark theme.
  `+141` is 1 on both `EvilGenius2Theme` versions, `DMT_TEST_FG3` and the benchmark theme.
- `DMT_MUS_Credits_EndGame_01` to `04` reuse the genius tracks (Emma, Red Ivan, Zalika, Maximillian).
- `DMT_TEST_FG3` names a hash that matches no track.

The music logic follows the last theme, from byte 0x56e9 to the end. It uses the flowgraph form of [4.20](../REFERENCE.md#420-scripts-flowgraphs): node names
and size-prefixed blocks `[u8 kind][u16 0][u8 0x80][u8 0][u32 size]`. Node and value names include `Start`, `End`,
`Pick A Track`, `Test Track 1` to `14`, `Time for new track?`, `Time At Which New Track Was Picked`, `Game Time`,
`Is Music Playing?`, `Time Scale`, `Activate Trigger`, `Multi-Branch`, `Mixer Preset Queue/Unqueue`,
`DynamicMusic_High Alert - Started`, `Is High Alert ? `, `START HIGH ALERT`, `STOP HIGH ALERT`, `PAUSED`,
`FASTFORWARD` and `NORMAL SPEED`. Six `Start` nodes mark six graphs.

The section ends with a variable table, `[u32 0x67f83a3f][u32 11]` + 11 variables:

```
[u32 id][u32 type][u8 2][u16 0][u8 0x80][u8 0][u32 size][name][u32 1][u32 0][u16 0][u8 0xc0][u32 kind] ...
```

| Type | Kind | Variables |
|---|---|---|
| `0xf7af1971` | 4 | `TALKING HEADS`, `HIGH ALERT`, `GAMEOVER`, `DEFAULT BASE`, `FRONTEND`, `GAMEOVER CREDITS` `MUSIC SHUFFLER` |
| `0x22c84ed5` | 1 | `EndCreditsMusicPlaying?`, `Benchmark Music playing?`, `Cache_IsTalkingHeadsMusicPlaying`, `DoneInitialGameplayUpdate`, `Cache_IsHighAlert` |

The id is not KeyHash(name). Shuffler variables are probably track pools and kind-1 variables booleans.
