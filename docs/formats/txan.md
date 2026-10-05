# Texture animation (`TXAN`)

Part of the [file format reference](../REFERENCE.md): [4.33 Effects and cutscenes](../REFERENCE.md#433-effects-and-cutscenes).

**Texture animation (`TXAN`, 32: `misc\common.asr` 23, islands 4, packages 5).**

```
u32 3, u32 0
name\0           Treadmill_scroller, Animated_Blinkey_Console_anim
group\0          usually empty; vault, messhall, Armoury, IC_eggpod
u32 1
f32 duration     seconds per loop (0.1 to 30)
u32 type         1 flipbook (stepped), 3 scroll, 10 (laser_glow_pulse)
u32 n
n x [f32 u][f32 v][f32 time (0-1)][f32 0][f32 0]
```

A scroller goes from (0, 0) to (±1, 0) or (0, 1) over the loop. A flipbook steps through atlas cells (u in steps of
0.25 or 0.0625).
