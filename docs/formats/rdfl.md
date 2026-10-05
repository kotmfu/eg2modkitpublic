# Doomsday firing levels (`rdfl`)

Part of the [file format reference](../REFERENCE.md): [4.29 Other data object classes](../REFERENCE.md#429-other-data-object-classes).

**Doomsday firing levels (`rdfl`, 33).** One per device level. Prop `0x9` holds `[u32 0]`, then a list
`[u32 n]` + n `[faction hash][rspl id]` pairs (a scheme pool per Forces of Justice faction, [4.18](../REFERENCE.md#418-schemes-rscm-rspl-rsdv)), then:

| Word | Field |
|---|---|
| 0 | i32 effect amount. Its scale and sign differ per device: M.I.D.A.S. 10/25/50, V.E.N.O.M. -100/-150/-200, test fires +-10 to +-50, final shots 300. |
| 1 | Flags: byte 0 = 1 on final shots ("will also destroy your Criminal Network"), byte 3 = 1 on Z.E.R.O.'s whole-world level |
| 2 | 6 |
| 3-8 | f32; V.E.N.O.M. sets 3 of them to -20/-60/-90 |
| 9-12 | Name text ref ("Level 1") |
| 13-16 | Description text ref |
| 17 | i32 days the effect lasts (Z.E.R.O. 10/25/50, V.E.N.O.M. 3/6/9, test fires 0) |
| 18 | f32 30, 45 or 60 |
| 19 | f32 per device (7.664, 13.295, 13.895, 15.294) |
| 20 | f32 3; 1275 on the whole-world level (probably the area) |

The description key names the device and level: `DOOMSDAY_DEVICE_<genius>_FIRINGSTRENGTH_[TESTFIRE_]<1-3 or ENDGAME>_DESC`.
Max = M.I.D.A.S., Emma = V.E.N.O.M., Red Ivan = H.A.V.O.C., Zalika = V.O.I.D., OCEANS = Z.E.R.O. Z.E.R.O.'s test fire reuses
Emma's `TESTFIRE_2` description.
