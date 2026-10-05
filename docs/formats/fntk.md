# Kerning (`FNTK`)

Part of the [file format reference](../REFERENCE.md): [4.37 GUI, fonts, input and stats](../REFERENCE.md#437-gui-fonts-input-and-stats).

**Kerning (`FNTK`, `fonts\fonts.asr` 2, `fonts\dfont.asr` 1).** Follows its `FONT` chunk. `[u32 0][u32 0][u32 n]`,
then n u16 words. The words hold one group per kerned left character: `[u16 k]` + k `[u16 right char][i16 adjust]`. A
glyph's kerning offset is the word index of its group. One trailing zero word pads the chunk to 4 bytes when the groups
fill an odd count.

| Chunk | Words | Groups (kerned glyphs) | Pairs | Adjust range |
|---|---|---|---|---|
| `fonts.asr` BodyText | 29,262 | 191 | 14,535 | -8 to 2 |
| `fonts.asr` TitleText | 7,612 | 120 | 3,746 | -13 to 3 |
| `dfont.asr` Arial | 158 | 15 | 71 | -2 to -1 |

`primitiverenderfonts.asr` has no `FNTK`; its glyphs all carry `0xffff`.
