# Tags (`rtag`)

Part of the [file format reference](../REFERENCE.md): [4.35 Map and save state, more data objects](../REFERENCE.md#435-map-and-save-state-more-data-objects).

**Tags (`rtag`, 311).** After the header, prop 3 { `[u32 parent rtag or 0]` prop 1 { `[u32 n]` + n child `rtag` ids } }. Three parents hold 5 children each, and each child names its parent; the parents are used by traits (training boosts for Muscle, Science and Technician training rooms). 308 objects are 50 bytes, the 3 parents 70. Header +12 is nonzero on 47.

**Names.** 293 of 311 tags have names: 290 from export names, 2 from aux-id hash matches, and 1 (`Cos_Unlock_Generic_Restore_Vitality`,
id `0x1cba40d6`) from a pattern search over the naming scheme of its siblings (probably right; the tag gates a Quantum
Chemist completion step). Name families: `Training - <minion type>` (training-room tags), `Trains <Social, Military,
Science>`, `Tier 1`-`Tier 3`, `RoomType_<room>_Starter`, `FurnitureType_<kind>`, `Cos_Unlock_<room>_<set>` and
`Deco_Unlock_...` (cosmetic unlocks), `Tutorial_<item>`, scheme groups (`Sabre05`, `HAMMER03`, `ANVIL01`,
`Patriot04`), `MM_<room>` (Minion Manager priorities).

18 tags stay unnamed: 12 cosmetic-unlock groups referenced by furniture and objective steps (Coffee Machine and Gas
Cylinders; Coat Rack and Brain Hologram; Communication Array and Radio Repeater; Dumbbells and Towel Rack; Drinking
Fountain and Air Con Unit; Tape Drive and Dot Matrix Printer; Food Disposal and Condiments; Snack and Drinks Machines;
Heart Rate Monitor and Brain Hologram; Data Processor and Reel Computer; and those gating `ScientistCompletion`,
`MercenaryCompletion`, `HitmanCompletion`, `BiologistCompletion` and `QuantumChemistCompletion`), one trait tag (Trait
Earn and Loss) and `Workshop_Gastrolley`'s tag (`dlc_oceans`).
