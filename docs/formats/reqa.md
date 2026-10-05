# Character attachments (`reqa`)

Part of the [file format reference](../REFERENCE.md): [4.35 Map and save state, more data objects](../REFERENCE.md#435-map-and-save-state-more-data-objects).

**Character attachments (`reqa`, 32: 29 `characters`, 3 DLC).** Aux id = KeyHash of a character type (`Guard`, `Mercenary`, `Scientist`, `Zalika`, `JohnSteele`, `Worker`). Prop 1 { `[u32 1][u32 n]` + n prop 1 `[u32 hash][u32 slot]` }, n 0 to 7, slots 0 to 8 (no 6). The hashes don't resolve; probably equipment or attachment pieces per slot. No object refers to `reqa`.
