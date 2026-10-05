# Input prompt icons (`IPTP`)

Part of the [file format reference](../REFERENCE.md): [4.37 GUI, fonts, input and stats](../REFERENCE.md#437-gui-fonts-input-and-stats).

**Input prompt icons (`IPTP`, `misc\common.asr`, 1).** `[u32 0][u32 0]` prop 1 { prop 1 { `[u32 273]` + 273 x 24-byte
entries } `[u8 0][u8 1][i32 -1]` }.

```
entry: [u8 input][i32 code][char[3] device][u8 2][7 bytes 0][u32 icon hash][f32 width]
```

The input is the `IPTB` slot number (keyboard virtual-key code; gamepad slot 0-23), except that `KBM` inputs 0-4
with code -3 are the mouse buttons. Device: `KBM` 57, `STM` 42, `DS4`
36, `DSS` 36, `VIT` 36, `XB1` 33, `NDO` 33 (Steam controller, DualShock 4, DualSense, Vita, Xbox, Nintendo). The width
is the icon's width over its height (1 for square keys, 2.565 for Space). 

**Icons and codes.** The icon hash is
`KeyHash` of the icon texture path without extension:
`data/graphics/gui/icons/inputs/<folder>/tools_icon_<kind>_<folder>_<input>`. All 273 entries resolve.

| Device | Folder | Names |
|---|---|---|
| `KBM` | `kbm` | `button_kbm_008` … by virtual-key code; mouse inputs 0-4 → `button_kbm_256` … `260`; Shift, Ctrl, Alt (16-18) → `160`, `162`, `164`; numpad → `button_kbm_numpad0` … `numpadslash` |
| `XB1` | `xb1` | `button_xb1_000` …, `axis1d_xb1_004_005`, `axis2d_xb1_004_005_006_007` |
| `DS4`, `VIT` | `ds4` | as Xbox, `ds4` (the Vita reuses the DualShock 4 icons) |
| `DSS` | `ds5` | `button_ds5_cross`, `circle`, `square`, `triangle`, `options`, `touchpad`, `leftbumper`; `axis_ds5_dpadupdown`, `leftstick` |
| `NDO` | `nsp` | `button_nsp_b` (slot 0), `a`, `y`, `x`, `plus`, `minus`; `axis_nsp_dpadup` … |
| `STM` | `stm` | `button_stm_a` …, `axis_stm_touchpadplus`, `gyrox_stm_up`, `gyro_stm_all`, back triggers (slots 24-30) |

Code layout: bits 0-1 are 0 (keys, gamepad) or 1 (mouse); bits 2-7, 12-17 and 22-27 hold the further inputs of a
combined icon, 63 = none, with the bits above an unused field set to 1. A single input therefore reads -4 (-3 for the
mouse). Example: `0x01c06014` is inputs 4, 5, 6, 7 (`axis2d_…_004_005_006_007`).
