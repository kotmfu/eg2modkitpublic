# Effect textures (`TEXT`)

Part of the [file format reference](../REFERENCE.md): [4.33 Effects and cutscenes](../REFERENCE.md#433-effects-and-cutscenes).

**Effect textures (`TEXT`, islands, 7).** `[u32 3][u32 0][u32 n]`, then n paths, null-terminated and padded to 4
(`\specialfx\Coronas\corona_haze_001.bmp`, `\objects\Map\Map_Corona\Map_Corona_Cloud.tga`). The `TEXT` just before `CRNA`
(3 to 5 paths) holds the corona textures. The three tropical islands have a second `TEXT` (3 paths: `Blob_test.tga`,
`Caustic1.tga`, `proj_water_caustics.tga`), probably projector textures.
