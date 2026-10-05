# Stats (`stsy`)

Part of the [file format reference](../REFERENCE.md): [4.35 Map and save state, more data objects](../REFERENCE.md#435-map-and-save-state-more-data-objects).

**Stats (`stsy`, scenarios and saves, 22).** `[u32 2][u32 0][u32 2]`, then 2 sets, each prop 1 { `[u8 present]` + prop 3 { `[u32 set hash][u32 n]` + n `[u32 stat key]` prop 3 {value list} } }. Set `0xa1220141` (31 stats) is in every file; set `0x38d38dfd` (33 or 34) is missing from sandbox saves and `frontend.scenario`. A value list is `[u32 n]` + n `[u32 id][u32 kind][value]` + `[u8 1]`. Kind 0 = u32, 2 = u8 bool, 3 = u32 hash (a genius hash in one stat). Single values use id `0xc8e624fa`; lists use other hashes. Stat `0x5298c142` is the current gold and `0xc5a6f2d6` probably the highest gold reached; `0xbd89de8f` equals the `bsnf` minion count. The stat names are not in the exe or data strings.
