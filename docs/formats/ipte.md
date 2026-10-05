# Rebindable actions (`IPTE`)

Part of the [file format reference](../REFERENCE.md): [4.37 GUI, fonts, input and stats](../REFERENCE.md#437-gui-fonts-input-and-stats).

**Rebindable actions (`IPTE`, `misc\input_bindings.asr`, 1).** The list the controls menu shows.

```
u32 0, u32 0
u32 1, u32 0x7dece088, u32 4
prop 1 { u32 2, EG2_GUI_KBM, EG2_KBM }                    the editable profiles
prop 1 { u32 2, EG2_KBM_Current, EG2_GUI_KBM_Current }    the profiles that hold the player's bindings
u32 62, 62 x 41-byte record
u32 0, text ref INPUT_BINDINGS_CLASHQUERY ("Key … is already bound"), text ref CONFIRM_BAD_ACTION, u32 1, u32 2,
text ref CANCEL_BAD_ACTION
record: [u32 2][u32 id][text ref label][u8 0][u32 1][u32 action set][u32 action][u32 direction]
```

Labels are `inputs` texts (`INPUT_ACTIONLABEL_CAMERAUP` "Camera Up"); the last four records use `menu` texts (Follow,
Photo Mode, Hide UI, Skip). Direction uses the `IPTB` convention: `CameraPan` and `CameraRotate` 0 right, 1 left,
2 up, 3 down; `CameraZoom` 0 in, 1 out. The records cover camera pan, rotate, zoom and snap, floors 1-6 and vanity view,
pause and fast forward, build menu, rotate item, undo and redo, tabs, minion quantity, tagging, special abilities,
power toggle, move and sell item, the GUI toggles, characters 1-6, event log, copy, focus, follow, photo mode, hide UI
and skip.
