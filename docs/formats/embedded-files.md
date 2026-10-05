# Embedded files (`RSCF`)

Part of the [file format reference](../REFERENCE.md): [4.6 Embedded files (`RSCF`)](../REFERENCE.md#46-embedded-files-rscf).

```
u32 f0, u32 f1, u32 version, u32 flags, u32 size
path\0, filler up to a 4-byte boundary of (20 + path + NUL)
data[size]   at the end
```

| `f0` | Holds |
|---|---|
| 0 | Texture (DDS, even when the path ends `.tga`) or sound (RIFF WAV) |
| 8 | Mesh geometry ([4.7](../REFERENCE.md#47-models-and-meshes)) |

Texture headers: `f1` 2 or 0, version 2, flags 0 or `0x2004020`. GUI textures use flags `0x2004420`.

GUI textures are DX10 DDS, BC7 (DXGI 98), no mips. Model textures carry full mip chains: colour maps BC7 sRGB
(99), masks BC4, normal maps BC5, effects BC7.

Sound banks hold MS-ADPCM WAV at about 48 kHz. The game also plays 16-bit PCM WAV.
