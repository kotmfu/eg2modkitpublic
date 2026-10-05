# Placed furniture states (`dtvs`)

Part of the [file format reference](../REFERENCE.md): [4.35 Map and save state, more data objects](../REFERENCE.md#435-map-and-save-state-more-data-objects).

**Placed furniture states (`dtvs`, `.base`, `.scenario` and saves, 27).** Version 2: `[u32 2][u32 0]` prop 0 { prop 1 {
`[u32 n]` + n `[u32 key]` prop 5 {entry} } }, n 328 to 478. 96% of keys are `fnas` ids. A version 2 entry is
`[u32 hash]` prop 1 { `[u32 k]` + k hashes } `[u16][u16]`, three props and 5 bytes ending in a flag byte; all 11,003
version 2 entries follow this. The hash lists are asset dependencies: most open with the same 9 hashes, 72% of the rest
appear in the key's own `fnas` asset list, and resolved ones are animation paths
(`\RigC_Shared\Furniture\Staff_Room\LifeDrawing_User_Loop_C_02`, KeyHash with backslashes and no extension) and state
words (`Idle`, `Active`, `Sabotaged`, `Unset`).

Version 1 (`frontend.base` only): `[u32 1][u32 0][u32 n]` + n `[u8 1][u32 fnas][u32 count]` (17), then the same prop 0
tree, whose 321 entries are prop 0 { `[u32 hash]` prop 1 { `[u32 k]` + k hashes } } with nothing after the list.
