# Sound mix (`SMXG`)

Part of the [file format reference](../REFERENCE.md): [4.35 Map and save state, more data objects](../REFERENCE.md#435-map-and-save-state-more-data-objects).

**Sound mix (`SMXG`, scenarios and saves, 22).** `[u32 1][u32 0]` prop 1 { `[u32 n]` + n mixer snapshot hashes }, n 6 in 2021 saves and 10 later. Slot 0 is always `Default` (`0x5c13d641`); others hold `FastForward` (`0x675cdae9`), `TimeScale_000` (`0x91aac18e`), `Atmospheric Mid` (`0x8a2b33a5`), `Atmospheric Low` (`0x8a2b30b1`) or 0.
