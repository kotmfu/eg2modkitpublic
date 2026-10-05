# Dialogue events (`DLEV`)

Part of the [file format reference](../REFERENCE.md): [4.32 Sound and dialogue](../REFERENCE.md#432-sound-and-dialogue).

**Dialogue events (`DLEV`; 811).** A pool of lines the game picks from, version 12. `misc\common.asr_en` holds 615,
`objectives_tutorial_content.asr` 109 and `sounds\gmsndmeta.asr_en` 87.

```
[u32 12][u32 0][name][u32 hash][u32 0-3][u32 0][u8 100][u8 100][u8 0/1]
[u32 n] n x [u32 line id][u32 0][u32 0][u8 weight][empty string]
[group][u32 template hash][17 bytes 0]
```

- `n` runs 1 to 10. Of 2811 entries, 2799 name a `DLLN` line id; 12 name no line in the install.
- Weight is 100, or 0 in `gmsndmeta.asr_en`.
- The group is `Global` in `gmsndmeta.asr_en` and empty elsewhere.
- The template is `BarkPlaybackTemplate` (627), `EventLogPlaybackTemplate` (97) or `Default` (87).
- The hash equals KeyHash(name) only in `gmsndmeta.asr_en`.
