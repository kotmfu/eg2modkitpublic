# Photo-mode colour tables (`VTEX`)

Part of the [file format reference](../REFERENCE.md): [4.30 Other engine chunks](../REFERENCE.md#430-other-engine-chunks).

**Photo-mode colour tables (`VTEX`, `misc\common.asr`, 1).** Photo-mode colour filters as 3D lookup tables: `[0][0][u32 9]` + 9 x (`[u32 KeyHash of the texture path]` `[u32 16][u32 16][u32 16]` + 16 x 16 x 16 RGBA bytes). The path is the filter's `FSX2` colour-table texture with `/` separators and no extension (`/specialfx/fsfx/rgbtables/PhotoMode/RGBTable16x1_EG_PhotoModeSepia`): Chill, BW, Violet, 8mm, 16mm, Sepia, Noir, Heatwave, SoftVintage
