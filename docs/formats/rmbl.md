# Rumble (`RMBL`)

Part of the [file format reference](../REFERENCE.md): [4.37 GUI, fonts, input and stats](../REFERENCE.md#437-gui-fonts-input-and-stats).

**Rumble (`RMBL`, `misc\common.asr`, 1, 100 bytes).** `[u32 0][u32 0][u32 11]` + 11 `[u32 effect][u32 hash]` pairs.
Each first hash is an object id in `misc\common.asr` `BLUE` #53 (chunk 55, 6,314 bytes): 8 objects of class
`0xca24922d` and 3 of class `0xf742ae4e`. The objects hold 8 to 19 members with float values (1, 3, 5.56); they are
probably the rumble effect definitions. Ten pairs share the second hash `0x49cd6f8b`; the last pair has `0x61ca9bf8`.
Neither second hash occurs elsewhere.
