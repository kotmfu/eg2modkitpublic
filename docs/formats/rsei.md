# Text icons (`rsei`)

Part of the [file format reference](../REFERENCE.md): [4.35 Map and save state, more data objects](../REFERENCE.md#435-map-and-save-state-more-data-objects).

**Text icons (`rsei`, 76).** Icons that text strings embed. Prop 2 (18 bytes): `[u32 name hash = aux id][u32 GUI icon key][f32 scale 1 or 1.161][u32 colour ARGB][u8 tint][u8 0]`. White (`0xffffffff`, tint 0) on 66; gold `0xffebc139` with tint 1 on `Money`, `Power`, `GoldCapIncrease`, `MinionCapIncrease`, `BroadcastStrength`. Names: `genius_max`, `henchman_iris`, `roomtype_vault`, `minionclass_muscle`, `ActorStat_Health`, `Money`. A text string places one with 4 characters U+0100 + byte, most significant byte first, encoding the name hash: `ǏĞƘƛ` style runs before stat names in tooltips. 68 distinct runs in the English text resolve to `rsei` names (`Money` 31 times, `ActorStat_Health` 23).
