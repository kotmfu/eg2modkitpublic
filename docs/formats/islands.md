# Islands (`envs\<lair>.pc`)

Part of the [file format reference](../REFERENCE.md): [4.23 Islands (`envs\<lair>.pc`)](../REFERENCE.md#423-islands-envslairpc).

A compressed container. Its `RSFL` lists build sections (`.sky`, `.asr` physics, `.lit`, `.snd`, `.nav`, `.cut`,
`.ent`, `.pfx` effects, `VehiclesData.asr`). These offsets don't index the payload, so chunks can change size.

`INST` (version 18):

```
[18][0][n]  n x 64-byte instances: f32 position x3, 3x3 rotation*scale as 12 half floats (pad per row), u16 group @60
u32 group count + 64-byte groups ([instance count][part count][first part]...)
u32 part count + 64-byte parts ([material hash]..., f32 min @40, f32 extent @52)
culling trees: u32 tree count; tree 0 = u32 nodes, u32 internal-node count, 128-byte nodes
  (6 x 4 f32 child boxes, 4 refs index<<8|kind (0 node, 1 instance, 0 = empty), 16 zero bytes)
```

Each instance is one leaf. Instanced geometry lives in the embedded file `inst (static)`: `[u32 verts][u32
indices][u32 instances]`, 24-byte vertices (u16 x3 position = part min + q/65535 x extent, half2 UV @8, UV2 @12,
normal 10:10:10 @16), u16 indices.

The sky uses ordinary textures (`specialfx\skybox\skybox_island_1.tga`, `specialfx\fog\fog_island_1.tga`). `FOG `
(183 bytes) holds f32 tint RGBA @21, start @37, end @41, haze RGB @126 (the blue over the island) and scatter RGB
@150. `PHEN` is a standalone physics mesh for ragdolls; minions walk on the lair grid.
