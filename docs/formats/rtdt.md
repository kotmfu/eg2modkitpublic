# Research tiers (`rtdt`)

Part of the [file format reference](../REFERENCE.md): [4.29 Other data object classes](../REFERENCE.md#429-other-data-object-classes).

**Research tiers (`rtdt`, 1).** Prop 2 → prop 1 `[u32 5]` + 5 prop 2 entries `[text ref "Tier N"][u32 n][hash]`, where n is
0, 3, 6, 6, 6 (probably the projects needed to open the tier), then a list of the 5 research trees (`rttr`).
