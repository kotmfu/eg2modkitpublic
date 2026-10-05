# Fixed save chunks (`DYMG`, `DLIG`, `ATIG`)

Part of the [file format reference](../REFERENCE.md): [4.35 Map and save state, more data objects](../REFERENCE.md#435-map-and-save-state-more-data-objects).

**Fixed save chunks (`DYMG`, `DLIG`, `ATIG`, scenarios and saves, 22 each).** The same bytes in every file. `DYMG` (17 bytes): `00000000 00000000 06 01 00 00 00 00 00 00 00`. `DLIG` (16): `[u32 2][u32 0][u32 0][u32 0]`. `ATIG` (16): zeros.

**Purpose.** `DYMG`, `DLIG` and `SMXG` probably hold the runtime state of the dynamic music (`DYMC`), dialogue (`DL*`) and mixer
(`SDMX`) systems: `SMXG`'s values are `SDMX` snapshot names, and the exe lists the save tags together as `AITG`, `ADAG`,
`DLIG`, `ATIG`, `DYMG`, `SMXG`, `ENTI`. Their fixed bytes stay unread.
