# Projected-texture samples (`PLUT`)

Part of the [file format reference](../REFERENCE.md): [4.34 Island level chunks and entities](../REFERENCE.md#434-island-level-chunks-and-entities).

**Projected-texture samples (`PLUT`, islands, one per island).** `[u32 0][u32 0][u32 2]`, then 2 entries of
`[u32 texture hash][8 x 8 RGB bytes]` (196 bytes). The hash is KeyHash of the texture path after `graphics` ([4.3](../REFERENCE.md#43-data-objects-and-properties)).
Tropical islands: `/specialfx/pfx/Test_jb/Caustic1` and `/specialfx/pfx/Test_jb/Blob_test`, both grey. Arctic:
`/specialfx/Coronas/corona_soft`, `/specialfx/Coronas/corona_bright_soft_edge`. The tropical textures are the ones the
island lights project; the 8 x 8 grid is probably a low-resolution copy of each texture. The three tropical chunks are
identical.
