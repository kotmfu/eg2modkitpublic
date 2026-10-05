# GUI global flowgraph (`GUIF`)

Part of the [file format reference](../REFERENCE.md): [4.37 GUI, fonts, input and stats](../REFERENCE.md#437-gui-fonts-input-and-stats).

**GUI global flowgraph (`GUIF`, one per `gui\*.asr`).** Each chunk holds the archive's GUI state machines.

```
u32 1, u32 0, u32 n
n x prop 2 machine {
  u32 name hash, u32 0, u32 state count
  state count x [u32 2][u32 0xe291e897][u32 size] + state body
}
u32 n, n x [u32 machine name hash][u32 0]       the same names again, in the same order
```

A state body is
`[u32 5][u32 1][u32 1][u8 has flowgraph]`; when the flag is 1, an FG3 graph ([scripts](scripts.md)) follows (36 states). The body ends
`[u8 1][state name\0]`, padding and an f32. The f32 is 0, or 0.15 to 0.7 on five states (probably a blend time). The graphs are mostly named `SF_<machine>_<state>` (`SF_MessageBox_Active`, `SF_InGame_DoomsdayFiring`); the other 15 states (73 to 149 bytes) hold none.

| File | Machines | States |
|---|---|---|
| `gui\gui.asr` | `0xf2d1430a`, `0x45327219`, `0x109895f6` | Platform current input; message box Inactive, Active; `F_GUIFlow_Input` (tooltips) |
| `gui\main.asr` | 11 | Open/Closed and Show/Hide pairs; `SF_Cancel_*` checks; 17 in-game states (`SF_InGame_ObjectSelection`, `IntroCutscene`, `Minions`, `TechTree`); main flow (`InGame`, `SF_MainFlow_Frontend`, `Loading`, `Galleries`); doomsday firing Level, Target, Confirm; galleries Furniture, Character; pause menu |
| `gui\splash.asr` | `0xdb8521db` | Disabled, Splash |
