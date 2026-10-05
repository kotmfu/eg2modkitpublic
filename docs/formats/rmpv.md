# World-map scene (`rmpv`)

Part of the [file format reference](../REFERENCE.md): [4.35 Map and save state, more data objects](../REFERENCE.md#435-map-and-save-state-more-data-objects).

**World-map scene (`rmpv`, 6: 5 `mapregions`, 1 `dlc_oceans`).** Prop 5: `[u32 node name][u32 0][u8][u8][u32 0][u32 n]` + n nodes, then `[u32 entitlement]` (Oceans on `Region_Border_Oceans`, otherwise 0). The two u8 are 1 on the `Arctic` and `Antarctic` fills. A node is 22 bytes of name hashes and flags (`[u32 node][u32 parent]...`), an empty prop 1 `[u32 0]`, and f32 10. `Background` (`World_Background_Sea`, 168 nodes) holds boats, caustics and water effects; `border` (`Screen_004`, 21) holds the desk props (`Classified1`, `CupStain1`, `Controls_Dial_1`). The 22-byte records are `Region_Border`, `Region_Border_Oceans`, `Arctic` and `Antarctic`, with no nodes. Nothing refers to `rmpv` by id.

**Node records.** A node is 22 bytes, an empty prop 1 `[u32 0]` and f32 10:

| Offset | Field |
|---|---|
| 0 | Node name (`FX_Water_Wake_A_001`, `Decoration_Boat_B_002`, `CupStain1`) |
| 4 | Model name (`Decoration_Boat_B`, `Decoration_Caustics_Large`, `Screen_004_Cup_Stain`), or 0 on effect nodes |
| 8 | u32 0 |
| 12 | u8 1 on most nodes, 0 on the caustics and `_AO` overlay nodes |
| 13 | u8 1 on the two grid nodes (`Grid_Left`, `Grid_Right`) |
| 14 | A second model name, the same as +4 on boats, caustics and the tape recorder, else 0 |
| 18 | Effect or material hash on the `FX_Water_*`, `VFX_Caustics` and `Decoration_Caustics00NN` nodes (8 values, unresolved), else 0 |
