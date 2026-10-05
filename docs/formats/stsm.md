# Stat counters (`STSM`)

Part of the [file format reference](../REFERENCE.md): [4.37 GUI, fonts, input and stats](../REFERENCE.md#437-gui-fonts-input-and-stats).

**Stat counters (`STSM`, `stat\stat.asr`, 1).** `[u32 4][u32 0][u32 83]` + 83 x `[u32 stat id]` prop 7 { flowgraph }.
Each flowgraph is named `<stat>/Flowgraph`: `Total Count - Minions Dead`, `Total Current - Power Available`, `Filtered
Count - Global Schemes Completed`, `Bool - All Core Regions Permanently Destroyed`, `Refresh stats on load`. Prefixes:
`Total Count` (running totals), `Total Current` (live values), `Filtered Count` (totals by a filter such as minion type
or difficulty), `Bool`.
