# Animation state machine (`rmcc`, `rmcb`, `rmch`, `rmca`)

Part of the [file format reference](../REFERENCE.md): [4.29 Other data object classes](../REFERENCE.md#429-other-data-object-classes).

**Animation state machine (`rmcc`, `rmcb`, `rmch`, `rmca`).** `rmcc` (layer, 130) lists states: `[u32 n]` + `rmcb`
ids, then two subsets. `rmcb` (state, 219) holds a blend value or hash, its transitions (`rmch`), an animation hash and
flags. `rmch` (transition, 158) holds two hashes, a condition list `[u32 n][1][rmca id]…` and a target hash. `rmca`
(condition, 106) holds `[1][f32 threshold or hash]`. `rcan` (38) holds animation sets: prop 3 groups of prop 8 entries
`[animation hash][0][0][set hash][0][u32 1 or 2]`.
