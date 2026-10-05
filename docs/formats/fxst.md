# Emitter spawn (`FXST`)

Part of the [file format reference](../REFERENCE.md): [4.33 Effects and cutscenes](../REFERENCE.md#433-effects-and-cutscenes).

**Emitter spawn (`FXST`, 4866).** After the header, ten curves with value widths `1, 3, 1, 3, 1, 1, 1, 3, 1, 1`
(the vec3 curves usually carry only x), then 24 words:

| Word | Value |
|---|---|
| 3 | The chunk's own hash |
| 11, 12 | 100, 5 |
| 13 | Small count, 1 to 7 |
| 14 | -1 |
| 16 | Flags (`0x20030`, `0x20010`, `0x20`) |
| 17 | 1 |
| 22 | 1000 |

**Curve slots.** | Slot | Width | Values | Probably |
|---|---|---|---|
| 0 | 1 | constant median 0, up to 412; sparks 1.9, dust 3.7 | initial speed |
| 1 | 3 | x from -1 to 1 (median 0.34), y and z near 0 | emission direction or cone |
| 2 | 1 | 0 to 1 | randomness of slot 1 |
| 3 | 3 | 0 to 1 per axis, usually 0 | random velocity per axis |
| 4 | 1 | constant median 10, up to 1633; drawn curves start near 46 and fall to 0; embers 43, sparks 16 | emission rate, particles per second |
| 5 | 1 | 0.09 to 1, usually 1; drawn curves fall from 1 to 0 | emission multiplier |
| 6 | 1 | constant 9.81 for most; smoke 6.9, steam -0.7, dust -1.2 | gravity (negative rises) |
| 7 | 3 | usually 0; -9.94 to 13.2 | constant acceleration (wind) |
| 8 | 1 | usually 1, up to 668; debris 11.5, embers 5.9 | velocity scale |
| 9 | 1 | usually 0; drawn curves rise from 0 to about 5.6 | probably drag or turbulence |

**The 24 words.** | Word | Values | Probably |
|---|---|---|
| 0 | -1 to 0.06, usually 0 | |
| 1 | -0.5 to 4, usually 1 | |
| 2 | -1 to 1, usually 0 | |
| 3 | the chunk's own hash | |
| 4 | 0.025 to 40 s, median 1 | emitter duration; equals the effect duration in 2747 of 4866 |
| 5 | 0.5 (0 to 1) | |
| 6 | 1 (0 to 10) | |
| 7 | 1.0 (4840) or 2.0 | |
| 8 | 1 or -1 | |
| 9 | 0.001 to 100, median 1 | |
| 10 | 0 | |
| 11 | 100 | |
| 12 | 5 | |
| 13 | 1 to 7 (1 in 1816) | |
| 14 | 0xffffffff | |
| 15 | 1.0 (5 have 0) | |
| 16 | flags: 0x20030, 0x20010, 0x20, 0x2a0, 0x220, 0x20210, 0 | |
| 17 | 1 (20, 10, 3, 38 in 18) | |
| 18-20 | 0, or 0.0087 / 0.0175 in all three (57) | angles in radians (0.5 and 1 degree) |
| 21 | 0 (4701) or 1-5 | |
| 22 | 1000 | |
| 23 | varies, no float pattern | |
