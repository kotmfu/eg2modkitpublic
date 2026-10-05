# Container (`.asr` and others)

Part of the [file format reference](../REFERENCE.md): [4.1 Container (`.asr` and others)](../REFERENCE.md#41-container-asr-and-others).

```
AsuraZbb (compressed)
  char[8] "AsuraZbb"
  u32     comp_total      file size - 16
  u32     raw_total       decompressed size
  repeat: u32 comp_n, u32 raw_n, zlib stream     one block per 2 MiB of payload

"Asura   " (uncompressed; three trailing spaces)
  char[8] "Asura   "
  repeat: char[4] tag, u32 size (including these 8 bytes), body
  a zero tag ends the list; trailing bytes follow
```

- The zlib streams use a 4 KiB window (header `48 89`).
- A one-call inflate returns only the first block, so a reader has to walk the blocks.
- The game reads the decompressed payload, so a rebuilt file can compress differently.
- To change a memory-mapped file, recompress only the changed blocks and pad each to its old compressed size.
