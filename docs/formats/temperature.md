# Temperature

Part of the [file format reference](../REFERENCE.md): [4.14 Temperature](../REFERENCE.md#414-temperature).

The lair temperature system covers every island. Its data sits in `dlc_oceans` and `common`. A tile's temperature
is the sum of nearby furniture outputs and any story offset, and its band decides the effects.

**Bands (`rtlv`, dlc_oceans, 7 objects):**

| Offset | Field |
|---|---|
| +35 | i32 lowest temperature |
| +39 | i32 highest temperature |
| +43 | f32 1 in the mild bands, 1.5 Cold/Hot, 3 Freezing/Melting (probably the furniture wear multiplier) |
| +68 | u32 band key (furniture output entries refer to it) |
| +72 | f32 x4 overlay colour RGBA |

Ranges: Freezing -128..-13, Cold -12..-7, Chilly -6..-1, Neutral (`0x6d9c05c1`, no name text) 0..0, Warm 1..6,
Hot 7..12, Melting 13..127.

**Furniture output.** A furniture record that gives off heat or cold carries a list prop `[u32 n]` + n entries
whose first i32 holds the output. The list always ends at `00000000 00000080 3F010001`.

| Entry | Layout | Examples |
|---|---|---|
| 24 bytes | `[i32 output][u32 0][1][2][1][2]` | Generator 4, Fusion Generator 8, Super Computer 8, icy caves -8 |
| 40 bytes | `[i32 output][u32 band key][text "High"/"Medium"/"Low"][text description]`, one per player setting | Furnace 8/6/4, Industrial Air Conditioner -8/-4/-2 |

**Story offsets.** Flowgraph node type `0xc3376493` shifts the whole lair's temperature by its `Offset` pin, fed
by one i32 `Value` setting. The Polar campaign's `OceanGenius_Obj*_ObjectiveEnd_TemperatureDrop_Activate` steps use
-1, -2 and -40, and two doomsday steps use it too.

**Effects.** 20 `rtrt` traits in `common` each pair "Enter a X Tile" and "Exit a X Tile" components (kinds 0x20 and
0x21) with a band id. They cover stat drain, slowed movement, agent immunity ("Thermal Underwear", "Lightweight
Clothing") and "Comfortable" (`0xb616e40c` fires on Neutral and Warm, `0xfb1b54b5` on Chilly). The genius packages
unlock them in `Unlock Feature - Starting TemperatureTraits - Oceans_Activate`.
