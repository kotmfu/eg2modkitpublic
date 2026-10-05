# Barks (`rbar`)

Part of the [file format reference](../REFERENCE.md): [4.35 Map and save state, more data objects](../REFERENCE.md#435-map-and-save-state-more-data-objects).

**Barks (`rbar`, 76, `common`, 110 bytes).** Voice-bark triggers named like the `EG2_BARK_*` text keys (`GENIUS_TAUNT`, `HENCHMEN_ATTACK`, `Genius_TagAgent_Kill`, `SUPERAGENT_ARRIVAL`, `CrimeLord_Defeat`). Prop `0xa` (77 bytes):

| Offset | Field |
|---|---|
| 0 | u32 event id, shared by genius and henchman versions (TAUNT `0x59`, CONFIRMMOVE `0x52`, ATTACK `0x8f`, CAPTURE `0x90`, SELECTED `0xa0`, REST `0xae`, CANNOTCOMPLY `0xaf`, tag orders `0xcd`-`0xd9`) |
| 4 | u32 0 |
| 8 | `rjob` id or 0 (the two REST barks name "Restoring stats") |
| 12 | u32 hash, unresolved |
| 16 | u32 1 |
| 20 | f32, probably a cooldown: 0.1, 120 on super agents and crime lords, 5.5, 5 |
| 24 | 3 u8 flags |
| 27 | prop 5: `[u32 speaker group][1][3][0][2][0][u32 1 or 2]`; groups `Geniuses`, `Henchmen`, `Agent`, `CrimeLords`, `Minion` |
| 64 | f32 5, u32 hash (`0xbab74f18`; `0x01d43a91` on super agents and crime lords), u8 1, u32 3 or 0 |

**Hashes.** +12 is one hash per bark event (`GENIUS_TAUNT` `0x5df7fe47`, `Genius_Ability_01`/`_02` `0xcacb11fd`/`0xcacb11fe`), shared by
barks of the same event. It matches no `DLEV`, `DLET`, `DLLN` or `SDEV` hash, no KeyHash of their names or of any
underscore-split part of a bark text key. +68 is `0xbab74f18` or `0x01d43a91` (super agents and crime lords), also
unresolved; it is not KeyHash of a `DLET` template name. The prop 5 speaker group is KeyHash of `Geniuses`, `Henchmen`,
`Agent`, `CrimeLords`, `Minion`, `Demolitioner` or `JohnSteele`.
