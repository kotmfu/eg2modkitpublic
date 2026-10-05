# Pending region (`ttsy`)

Part of the [file format reference](../REFERENCE.md): [4.35 Map and save state, more data objects](../REFERENCE.md#435-map-and-save-state-more-data-objects).

**Pending region (`ttsy`, scenarios and saves, 22).** `[u32 1][u32 0]` prop 1 { prop 3 or 4 (the version) { `[u8 present]` + payload } }. Version 3 payload: `[u32 rmlr id][u32 0][u32 0]` (one save holds Western S.A.B.R.E. Territories). Version 4 payload: `[u32]`, 0 in all 3 saves that set it.
