# Input bindings (`IPTB`)

Part of the [file format reference](../REFERENCE.md): [4.37 GUI, fonts, input and stats](../REFERENCE.md#437-gui-fonts-input-and-stats).

**Input bindings (`IPTB`, `misc\input_bindings.asr`, 1).** The default bindings, one entry per profile and action set.

```
u32 0, u32 0, u32 37
37 x entry:
  u32 entry hash
  u32 2
  u32 profile          EG2_KBM, EG2_GUI_KBM, EG2_Pad, EG2_GUI_Pad (9, 6, 11, 11 entries)
  u32 device           0x25cbf023 "Keyboard and Mouse", 0x8923c912 "Base Gamepad"
  u32 action set
  u32 slot count       272 keyboard and mouse, 24 gamepad
  slot count x slot    in input order
  prop 1 { u32 n, n x [u32 action][u32 0][f32 threshold][f32 -1][f32 -1] }   the entry's analog actions
slot:
  u32 2, u32 direction, u32 analog action (0 = none)
  prop 1 { u32 0 }
  u8 more              1 = a digital binding follows
  digital binding:     [u32 2][u32 action][u32 0] prop 1 { u32 n, n x u32 modifier action } [f32 -1][f32 -1][u8 more]
```

The analog list's threshold is -1, except 0.1 on the gamepad trigger zooms. Direction is 0 right (+x), 1 left, 2 up,
3 down; a 1D axis such as the mouse wheel uses 0 and 1. One digital binding carries a modifier: keyboard Y and Z
also bind `0x003559fe` (Redo) and `0x0036d8e4` (Undo) with `CtrlModifier`.

Keyboard slots 0-255 are Windows virtual-key codes. 256 = left mouse button, 257 = right, 258 = middle (by the
actions bound there; the `GUISystem_Passive` action names call 257 `MMB` and 258 `RMB`), 263-264 = wheel, 265-268 =
mouse movement up, down, left, right.

Gamepad slots: 0 A, 1 B, 2 X, 3 Y, 4-7 D-pad up, down, left, right, 8 Start, 9 Back, 16-19 left stick up, down,
left, right, 20-23 right stick up, down, left, right. By their bindings 10 and 13 are the bumpers (floor down/up, tab
left/right), 11 and 14 the triggers (zoom out/in) and 12 and 15 probably the stick clicks.

| Action set | Keyboard and mouse | Gamepad |
|---|---|---|
| `EG_ActionSet` | WASD `CameraPan`; Q/E `CameraRotate`; Space `TimePause`; `.` `TimeFastForward`; Page Up/Down `FloorUp`/`FloorDown`; B `BuildMenu`; G global operations; L event log, I `SelectEventLogMessage`; M minion management; O objectives; R research; T minion training; Y engineering (`ToggleCraftingGUI`); Z security zones; U `UIToggle`; F4 `PhotoMode`; F7 `PauseOrDVS`; Esc `Pause`; Enter `SkipIntroCutscene`; Shift, Ctrl, Alt modifiers; left button `Select`, right `AltSelect`, middle `CameraRotateFromCursor`; wheel `CameraZoomWheel`; mouse `CursorPos` | A `Select` and `SkipIntroCutscene`; Start `PauseOrDVS`; 10/13 `FloorDown`/`FloorUp`; 11/14 `CameraZoomOutAndHighAlert`/`CameraZoomInAndHighAlert`; 12 `TimePause`; 15 `TimeFastForward`; left stick `CameraPan`; right stick `CameraRotate` |
| `EG_ActionSet_Cancel` | Esc `Cancel`; right button `CancelNoFullScreenChange` | B `Cancel` |
| `EG_ActionSet_OrderSelected` | right button | A |
| `EG_ActionSet_PlacingFurniture` | R/T `RotateItemLeft`/`Right` | Y / D-pad down |
| `EG_ActionSet_CursorRel` | mouse `CursorRel` | |
| `EG2_ActionSet_BuildMenu` | C `CopyUnderCursor` | X Undo; Back `ToggleOverlay` |
| `EG2_ActionSet_SidePanel` | C capture, V terminate, X distract; J `FocusOnMinionOrJob`; K/L special abilities 0/1; right button `WorldSelect` | X `WorldSelect` |
| `EG2_ActionSet_CharacterSelect` | right button `AltSelectCharacter` | Y |
| `EG2_ActionSet_Minion` | F `Follow` | Back |
| `EG2_ActionSet_HUD`, `HUDMenu`, `GlobalOperations` | | Y `OpenHUDMenu`, D-pad up `ToolTip`; X `BuildMenuGUI`, Y `CharacterSelect`; X `JumpToAntagonist`, Back `PinScheme` |
| `GUISystem` | Enter `Accept`; arrows `Navigate` | A `Accept`; D-pad `Navigate` |
| `GUISystem_Cursor`, `GUISystem_Passive` | left button `Cursor_LMB`, wheel `Cursor_Scroll`, mouse `Cursor_Pos`; buttons 256-258 | |
| `EG2GUI_Back`, `EG2GUI_Tabbing` | Esc `Back`; Q/E `TabLeft`/`TabRight` | B; 10/13 |
| `EG2GUI_MinionTraining` | `=`/`-` increase/decrease quantity | X `HireNow`; right stick left/right decrease/increase |
| Other GUI sets (pad only) | | Research X `StartOrCancelResearch`; Objectives X `SideStoryOrReward`, Back `ShowObjectivesOrTrack`; PopUp Y `PopUpToggle`; SaveLoad X `SaveLoad`, Y `DeleteSave`; FrontEnd X `FrontEndToggle`, 11 `ActivateBenchmarkMode`; FrontEndDLC Y `OpenShop`; RightStick 20-23 `RightStick` |
