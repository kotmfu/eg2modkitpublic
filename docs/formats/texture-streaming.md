# Texture streaming (`.ts` / `TXST`, `.pc_textures`)

Part of the [file format reference](../REFERENCE.md): [4.9 Texture streaming (`.ts` / `TXST`, `.pc_textures`)](../REFERENCE.md#49-texture-streaming-ts--txst-pc_textures).

`.pc_textures` stores are uncompressed containers of `RSCF` DDS. A `.ts` table locates each streamed texture a
package uses:

```
u32 version, u32 0, u32 store count, u32 entry count
store names (NUL, padded to 4)
entries: [u32 KeyHash(texture path)][u32 data offset in store][u32 data size][u32 flag][u32 store index][u32 hash2][u32 flags]
```

The offset points at the DDS data inside the store's `RSCF`, not at the chunk header. The engine opens any store
the table names and ignores `hash`/`hash2`, so a table can point a texture at a new store.
