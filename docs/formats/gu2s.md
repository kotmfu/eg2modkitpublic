# GUI settings (`GU2S`)

Part of the [file format reference](../REFERENCE.md): [4.37 GUI, fonts, input and stats](../REFERENCE.md#437-gui-fonts-input-and-stats).

**GUI settings (`GU2S`, `gui\gui.asr`, 1, 168 bytes).**

```
u32 5, u32 0
f32 3840, f32 2160                 the layout reference resolution
u32 15, 15 x u32                   every GUIF state machine name (3 gui.asr, 1 splash.asr, 11 main.asr)
7 x u32                            gui.asr components: the option widgets (slider 0x905c7548, tickbox 0xbef7a030,
                                   dropdown 0x9b17986a, button 0x83d95634, 0x7872eb70, 0x17c5636f, 0xe91dd944)
u32 7, 7 x [u32 message box type][u32 component]
```

The message box pairs map to the gui.asr popup components: `0x1c1cc25e` → `F_Popup_Info`, `0x45d133ec` → Warning,
`0x67452a58` → Error, `0x677bb01a` → Input, `0x1af9c7dd` → Progress, `0x1c22ef25` → Wait, `0x90209285` → CrashReport.
