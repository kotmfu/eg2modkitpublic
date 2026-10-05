# Scheme visuals (`rsvs`)

Part of the [file format reference](../REFERENCE.md): [4.35 Map and save state, more data objects](../REFERENCE.md#435-map-and-save-state-more-data-objects).

**Scheme visuals (`rsvs`, 225).** How a scheme shows on the world map. Prop `0xf`:

| Offset | Field |
|---|---|
| 0 | GUI key of the scheme image (`data/graphics/gui/scheme_images/t_schemeimage_...`) |
| 4 | u32 0-11, probably a category index (5 mostly with the military backplate, 6 social, 7 science, 4 generic) |
| 8 | Effect, KeyHash `Map_PFX/PFX_Map_Scheme_<Deception, Muscle, Science or Worker>_<L or M>`, or 0 |
| 12, 16 | 0 |
| 20 | `Map_PFX/PFX_Map_Scheme_Complete` |
| 24 | Selector effect `Map_PFX/PFX_Map_Selector_Green_<Large, Medium or Small>` |
| 28 | GUI key of the scheme icon (`data/graphics/gui/icons/schemes/t_icon_scheme_140_...`) |
| 32 | GUI key of the minion backplate (`.../miniontypes/backplates/t_icon_minion_backplate_<social, military, science, generic>_f`) |
| 36 | f32 RGBA, the branch colour: Deception (0.46, 0.29, 0.73), Science (0.58, 0.73, 0.13), Muscle (0.87, 0.4, 0.11), Worker (0.87, 0.66, 0.11), Intel (0.2, 0.49, 0.65) |
| 52 | Pointer effect `Map_PFX/Map_Pointer_<Large, Medium or Small>` |
| 56 | u8 0 or 1 |
| 57 | `[u32 2]`, then 2 views |

A view is `[u32 model][u32 animation][u32 hash]` prop 1 { `[u32 n]` + n prop 1 `[u32 effect][u8 flag]` } `[u32]`. The model is `Activity_Scheme_<Branch>_<S, M or L>` (or `_Threat_`); the animation is `Activity_Scheme_Animation_Rotate_A` or 0. The effect list holds `Map_PFX/Map_Pointer_B` or `Map_PFX/Map_Pointer_Threat`. The second view swaps in `Oceans_Activity_Scheme_*` models and animations on 14 schemes and adds `Map_PFX/Map_Foam_Rings_Scheme_<S, M, L>` with flag 1 on 19 (probably the view at sea). The view's third hash is unresolved; it matches in both views on 222. The u32 after view 1 has 4 values that follow the pointer size (`0x414bba80` Medium, `0x020b82d0` Large, `0x02735c9c` Small, or 0); after view 2 it is 0. Only `rscm` refers to `rsvs`.
