# Materials (`MARE`)

Part of the [file format reference](../REFERENCE.md): [4.8 Materials (`MARE`)](../REFERENCE.md#48-materials-mare).

```
u32 0x33, u32 0, u32 count, u32 3, u32 n
n x 1028-byte blocks (unknown, look like lookup tables)
count x [u32 material hash][u32 len][len bytes]
```

Furniture material records take 342 bytes, with texture hashes at +12/+16/+20 (colour, normal, metal). The material
hash matches the h31 of no known name. Packages share materials, and `misc\common.asr` holds 962. A `MARE` with
`n = 0` is valid.
