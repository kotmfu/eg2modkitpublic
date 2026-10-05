# Fonts (`FONT`)

Part of the [file format reference](../REFERENCE.md): [4.37 GUI, fonts, input and stats](../REFERENCE.md#437-gui-fonts-input-and-stats).

**Fonts (`FONT`, `fonts\fonts.asr` 2, `fonts\dfont.asr` 1, `fonts\primitiverenderfonts.asr` 1).** One font per chunk.

```
u32 version            9 (dfont), 13 (primitiverenderfonts), 14 (fonts.asr)
u32 0
char name[100]         "40pt_BodyText.asr", "40pt_TitleText.asr", "Arial_11_bold.asr"; bytes after the terminator are leftover memory
+0x6c f32 size         40, 32, 18, 17
+0x70 u32 page count   12, 21, 1, 1
+0x74 u32              1 in version 14, else 0
+0x78 u8 0, f32 a, f32 line height, f32 c     (-10.65, 56, 1.18) BodyText; (-2.5, 69, 0.625) TitleText; (0, 18, 1) Arial
page count x page:
  char path[108]       "\Fonts\40pt_BodyText_0.dds", "\fonts\Arial_11_bold_0.tga"; leftover memory after the terminator
  u16 first char, u16 last char
  u32 texture width, u32 texture height
  u32 glyph count
  glyph count x glyph
version 13 and 14 only: 18 bytes [u8][f32][f32][f32][f32][u8]
```

A glyph is `[f32 u][f32 v][f32 x 5 (v9, v13) or 6 (v14)][u16 char][u16 kerning offset]`, 32 or 36 bytes. u and v place
the glyph on its page texture (0 to 1). The third float is 0 to 42 (probably the advance in pixels); in versions 9 and
13 the last four floats are 0. Glyphs are sorted by character, and each page's first and last glyph match the page's
character range. The kerning offset indexes the font's `FNTK` table, `0xffff` for none.

| Font | Pages | Glyphs | Range | Page texture |
|---|---|---|---|---|
| `40pt_BodyText` | 12 | 5,125 | U+0020 to U+FFE1 | 1024 x 976 (last 800 x 800) |
| `40pt_TitleText` | 21 | 5,340 | U+0020 to U+FFE1 | 1024 x 992 (last 416 x 496) |
| `Arial_11_bold` (dfont, primitiverenderfonts) | 1 | 256 | 0 to 255 | 256 x 256 `.tga` |

The page textures are not in the chunk; the paths name `.dds` and `.tga` files.
