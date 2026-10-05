# GUI parameters (`GUAP`)

Part of the [file format reference](../REFERENCE.md): [4.37 GUI, fonts, input and stats](../REFERENCE.md#437-gui-fonts-input-and-stats).

**GUI parameters (`GUAP`, `parameters\parameters.asr`, 1).** The global GUI variables that layouts and GUI flowgraphs
read and write. `[u32 7][u32 0][u32 298]`, then 298 records:

```
u32 param hash      not KeyHash of the name; GUAT and GUIF refer to parameters by it
u32 type hash
prop 2 {
  name\0 padded to 4    "Design.GUIParams_<group>.<path>", e.g. Design.GUIParams_BuildMenu.BuildMenu.Tab.Count
  u32 1
  u32 flags             0xc0000000; 0xc0000001 on the 3 data provider ids and Tooltip.Texture
  u8  0
  u32 size, value       the default value
}
```

Records are ordered by the low byte of the hash. 33 groups, the largest `Frontend` (39), `HUD` (24), `Pause` (23),
`RenderOrder` (21), `PlatformInput` (20). One record, `Music_Automation_Parameters`, sits outside the `GUIParams_`
naming.

| Type | Uses | Size | Value |
|---|---|---|---|
| `0x22c84ed5` | 194 | 1 | Bool |
| `0xf7af1971` | 68 | 4 | Int (`Tab.Count` 4; `RenderOrder_*` layer numbers such as 7000) |
| `0x95648571` | 11 | 8 | 2D vector (`OptionTemplate_Dropdown_List_Dimensions` 600 x 552) |
| `0x72301071` | 7 | 8 or 16 | Text: `[1][2]` when empty, or a text ref |
| `0x2bc798d5` | 7 | 4 | Float (`Options.SliderDelta` 10) |
| `0xd9c24551` | 4 | 16 | RGBA float colour (`StandardColour_ButtonPositive_Default`) |
| `0x0dc3fe35` | 3 | 4 | Data provider id |
| `0xed974765` | 2 | 12 | Three words, all 0 (`Dropdown_ChangeID`, `Dropdown_ParameterID`) |
| `0x2a0536f9` | 1 | 4 | Texture hash (`Tooltip.Texture`) |
| `0x08e9e7ce` | 1 | 4 | Enum (`BuildMenu.Tab.ListFocus.InputType`) |

Of the 298 parameter hashes, 233 occur in `gui\main.asr` `GUAT`, 49 in `gui\gui.asr` `GUAT`, and 118 and 33 in the
`GUIF` chunks of those files.
