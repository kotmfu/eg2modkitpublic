# Map header (`bsnf`)

Part of the [file format reference](../REFERENCE.md): [4.35 Map and save state, more data objects](../REFERENCE.md#435-map-and-save-state-more-data-objects).

**Map header (`bsnf`, first chunk of every `.base`, `.scenario` and save, 27).** `[u32 0][u32 0]`, then one prop whose key is the version (`0xf`; `0xb` in `frontend.base`) and whose payload runs to the end of the chunk:

| Field | Layout |
|---|---|
| Lair id | u32, KeyHash of the lair stem ([4.22](../REFERENCE.md#422-lair-maps-envsbasedefinitionslairbase)). The frontend and benchmark files use Lair Tropical 01's. |
| Game mode | u32 (v15 only): 0 in `.base` files, KeyHash `standard` (`0x4e3d1ebd`) or `sandbox` (`0x6f2fbec7`) |
| Packages | `[u32 n]` + n `rpkg` object ids, the packages in play (`0xd7b5d5fe` common, `0x6ce6ab89` furniture, `0x4b303440` genius_max, ...) |
| Entitlements | prop 1: `[u32 n]` + n `rpkg` entitlement hashes (`0xc30c6735` Oceans, `0xc8d2e349` Robots, ...). Arctic's map lists Oceans; current saves list 11. |
| Unknown list | prop 1 (v15 only): `[u32 n]` + n u32; empty in every file |
| Genius | u32, KeyHash `genius_<name>` (`0xebe4489c` genius_max); `genius_none` in `frontend.scenario`, 0 in `.base` files |
| Region | u32, the lair's world-map region (`rmlr` id); 0 in `.base` files |
| Difficulty | u32, KeyHash `easy` / `medium` / `hard` |
| Date | `[u16 year][u8 month 0-11][u8 day 0-30][u8 0][u8 hour][u8 minute][u8 second]`: the save time, or the export time of a map |
| Gold | u32, the current gold (999,999,999 in sandbox saves). Equals stat `0x5298c142` in `stsy`. |
| Minions | u32, probably the minion count (it tracks the number of `0x8003` `ENTI`s, which run a few higher). Equals stat `0xbd89de8f`. |
| Difficulties | prop 2: `[u32 7]` + 7 difficulty hashes (all `easy` in saves, 0 in `.base` files) |
| Play time | u32 seconds (saves 10 minutes apart differ by 600) |
| Cell counts | prop 1: `[u32 n]` + n `[u32 cell type][u32 count]` over the whole grid ([4.22](../REFERENCE.md#422-lair-maps-envsbasedefinitionslairbase) types); empty in saves and scenarios |
| Faction values | prop 1: v15 `[u32 n]` + n `[u32 rmlr id]` prop 1 { `[u32 m]` + m `[u32 faction hash][f32]` }; v11 holds one faction list with no region id. 5 factions per region, 6 with J.A.W.S. (`0xada25400`) on the Arctic map. The faction hashes are the ones `rdfl` uses ([4.29](../REFERENCE.md#429-other-data-object-classes)). Values run from about 37 to 330. Empty in saves and scenarios. |
| Version | `[u32 1][u32 v]` (v15 only), probably the content version the file was written with: 1 in April 2021 saves, 2, 4, 8, 10, 11, 12, then 13 in current saves and maps |
