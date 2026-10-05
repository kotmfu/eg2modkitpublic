# Dialogue lines (`DLLN`)

Part of the [file format reference](../REFERENCE.md): [4.32 Sound and dialogue](../REFERENCE.md#432-sound-and-dialogue).

**Dialogue lines (`DLLN`; 7642).** One spoken line per chunk, version 5. `misc\common.asr_en` holds 2572 and
`sounds\gmsndmeta.asr_en` 110. The rest sit in content packages: `objectives_crimelords_content.asr` 1807,
`objectives_superagents` 825, `objectives_loot` 617, `common_content` 493, the genius and DLC packages 19 to 156 each.

```
[u32 5][u32 0][name]                              EG2_BARK_HENCHMEN_CAPTURE_SEQ01_ELI_VAR01_L01
[u32 line id][u32 speaker][u32 template hash][u32 path hash][u32 0][u32 0]
[u8 0][f32 duration][u32 table hash][u32 key hash]
[u32 units][u16 text[units]]                      inline UTF-16 text, NUL included
```

- `DLEV` entries refer to a line by its line id. The line id equals KeyHash(name) only in `gmsndmeta.asr_en`.
- The template hash is KeyHash(`DefaultLineTemplate`) (the `DLLT` in each file), or KeyHash(`Default`) in
  `gmsndmeta.asr_en`.
- The path hash is KeyHash of the line's `ASTS` name in the `.pc.streamsounds` beside the file
  (`Data/Dialogue/Sounds/en/<speaker>/<line>.wav`). `misc\common.asr_en` pairs with `misc\common.asr_wav_en.pc.streamsounds`.
  A content package `X.asr` pairs with `X.asr.pc.streamsounds`. All 7532 hashes outside `gmsndmeta.asr_en` resolve.
  The WAV name matches the line name in 7473 lines; 59 differ in case.
- Duration runs 1 to 21.5 seconds.
- 7372 lines name their subtitle by table hash and key hash. The table is `talkingheads` (4533), `character` (1984),
  `dlc108_oceans` (493), `dlc103_valkyrie` (122), `tutorial` (112), `dlcv1_tft` (62), `dlcv2_ptl` (26),
  `dlc105_robots` (21), `objective` (18) or `eventlog` (1). Every pair resolves to an `HTXT` entry. The key equals
  the line id in 4270 lines. In 3102 lines the key is another line's (`…_VAR07_L01` uses `…_VAR04_L01`;
  `EG2_BARK_GENIUS_…_POLAR_…` uses `EG2_BARK_POLAR_…`).
- The other 270 lines store table and key 0 and carry their text inline (`units` above 1). They include all 110 event
  log lines in `gmsndmeta.asr_en`. Lines with a table reference store `units` 1 and a single NUL.

**Speaker.** The second word after the name is the speaker: KeyHash of a speaker id. 103 distinct
values; 154 lines store 0. The most common resolve to `genius_max` (675), `genius_emma` (664), `genius_zalika` (663),
`genius_ivan` (581), `IRIS` (262), `WORKER` (225), `POLAR` (220), `Genius` (143), `genius_redivan` (87), `Deep Six`,
`Agent X`, `Eli`, `Espectro` and the other henchmen and super agents. The hashes of 4741 lines match a known string.
