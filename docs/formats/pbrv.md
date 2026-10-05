# Reflection probes (`PBRV`)

Part of the [file format reference](../REFERENCE.md): [4.34 Island level chunks and entities](../REFERENCE.md#434-island-level-chunks-and-entities).

**Reflection probes (`PBRV`, islands, one per island, 8,389,780 bytes).** The four are byte-identical.

```
u32 2 (version), u32 0, u32 2 (probe count)
2 x 156-byte probe: f32 x16 matrix (scale 2000), f32 x16 inverse (scale 0.0005), 24 zero bytes, f32 12,000,000
u32 256 (face size), u32 9 (mip count), u32 108 (12 faces x 9 mips)
108 x [u32 row pitch][u32 mip size]    2048/524288, 1024/131072 ... 8/8
u32 8,388,576 (data size)
data: 12 faces (2 cube maps), each 9 mips from 256 x 256, RGBA half floats (8 bytes a texel), 699,048 bytes a face
```

Alpha is 1.0 throughout. Face 2 (up) is the brightest, with values above 1.
