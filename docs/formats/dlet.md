# Dialogue playback templates (`DLET`)

Part of the [file format reference](../REFERENCE.md): [4.32 Sound and dialogue](../REFERENCE.md#432-sound-and-dialogue).

**Dialogue playback templates (`DLET`; 4).** `TalkingHeads`, `EventLogPlaybackTemplate` and `BarkPlaybackTemplate`
in `misc\common.asr_en`, `Default` in `sounds\gmsndmeta.asr_en`.

```
[u32 6][u32 0][name][u32 hash][bus name][f32 x6][f32][u32][5 x u32 0][u32]
```

| Template | Bus | Values |
|---|---|---|
| `TalkingHeads`, `Default` | `Dialogue` | 1, 5, 100, 5, 100, 10, 0.0, 929, …, 100 |
| `EventLogPlaybackTemplate`, `BarkPlaybackTemplate` | `Barks` | 1, 10, 50, 10, 50, 20, 1.0, 1, …, 0 |

The six floats read as volume and two distance pairs. The bus names an `SDMX` bus.
