# Sky (`PSKY`)

Part of the [file format reference](../REFERENCE.md): [4.34 Island level chunks and entities](../REFERENCE.md#434-island-level-chunks-and-entities).

**Sky (`PSKY`, islands, one per island, version 22).** Fields run back to back with no alignment. Strings are
null-terminated and unpadded. All four parse to the end.

```
u32 22, u32 0
f32 x9             rotation R1 (tropical 02 and 03: equal to R2)
f32 x9             rotation R2, the isometric camera angle: 45 degrees yaw, 35.26 pitch, on all four
f32 x9             rotation R3, a yaw of 0 (Caine Key), +90 (tropical 02, 03) or -90 degrees (arctic)
f32 x3 x3          three points 4 units apart in height
f32 2
u8 1, f32 0.145-0.155, u8 0
f32 x6             1.0 (arctic: 1, 1, 1, 0.979, 1, 0.647), probably two RGB tints
f32 x3             RGB 0.235, 0.271, 0.549 in all four
u32 1, f32 x4      0-16
u8 1, skybox path  \specialfx\Skybox\skybox_Island_1.tga, \specialfx\Skybox\skybox_DLC_Arctic_1.tga
u8 0, u8 0
f32                sky rotation in degrees (296.2 Caine Key, 91.6 arctic)
f32 x6             -0.5 or 0.5, 2 or 1.5, 8 or 6, 0.3, 6, 2
u8 1, u8 1, u32 1, f32 1, u8 0, u8 1
cloud path         \environments\Island\Island_Cloud_Alpha.tga
f32 2, f32 2, f32 500
u8 0, u8 255, u8 255, 17 zero bytes
u32 1, f32 0 or 1
f32 x9             rotation (orthonormal)
9 zero bytes
```

The arctic sky is 432 bytes and the others 428; the difference is the skybox path.

**Rotations.** The closing `f32 x9` rotation is the same on all three tropical islands, and the arctic one holds the same rows in another
order. The sky rotation in degrees is 296.2 (Caine Key), 53.6 (tropical 02), 50.6 (tropical 03) and 91.6 (arctic).
