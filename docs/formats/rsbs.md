# Sticker Book (`rsbs`)

Part of the [file format reference](../REFERENCE.md): [4.29 Other data object classes](../REFERENCE.md#429-other-data-object-classes).

**Sticker Book (`rsbs`, 29).** One collection per faction or minion branch ("H.A.M.M.E.R.", "Science", "Muscle"):
`[name text ref]`, then `[u32 n]` + n prop 8 items `[name text ref][description text ref][icon hash]`, the `fnas` the
item unlocks, `[1][2][1][2]` and an unlock-criteria text ref.
