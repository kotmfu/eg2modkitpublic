# Stat groups (`STSC`)

Part of the [file format reference](../REFERENCE.md): [4.37 GUI, fonts, input and stats](../REFERENCE.md#437-gui-fonts-input-and-stats).

**Stat groups (`STSC`, `stat\stat.asr`, 3).** `[u32 4][u32 0][u32 group hash][u32 n]` + n `STSM` stat ids `[u32 m]` +
m `STRC` reaction ids. The three groups split the 118 reactions with no overlap; their stat lists cover all 83 stats
(7 stats sit in two groups).

| Group | Stats | Reactions |
|---|---|---|
| `0x70c76d29` | 25 (bodies disposed, objectives complete, regions in lockdown) | 50: minion type recruitment and first-time reactions |
| `0xa1220141` | 31 (days passed, minions dead, capacity, power) | 27: the `*_UpdateCodeParams` graphs |
| `0x38d38dfd` | 34 (loot, brainwashing, core objectives) | 41: achievement rewards |
