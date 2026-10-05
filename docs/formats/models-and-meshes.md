# Models and meshes

Part of the [file format reference](../REFERENCE.md): [4.7 Models and meshes](../REFERENCE.md#47-models-and-meshes).

A model is a run of chunks: `HSKN` (hierarchy, one material hash per face), `HSKL` x5 (`L1#name`…), `HMPT`, `HSBB`
(bounds), `RSCF` x6 geometry (`name`, `l1#name` … `l5#name`, `f0 = 8`), and `HSKE` `[1][0][name hash]` to close it.
Manifest group `18.0.1.2.1.6` lists model name hashes.

Geometry `RSCF` payload:

```
u32 submeshes, u32 verts, u32 indices, u32 triangles
per submesh 20 bytes: [material hash][u32 0][index count][u32 0][u32 group 1..5]
f32 scale x3, f32 min x3
verts x 48 bytes:
  +0  u16 x3 position = min + q/65536 * scale   (+6 = 0xffff)
  +8  tangent, +12 bitangent, +16 normal: unorm 10:10:10:2, (v - 512) / 511
  +20 0x20080200, +24 half2 UV0, +28 half2 UV1, +32 0xff, +36 12 zero bytes
u16 indices
```

Up is -Y. `HSBB` is `[1][0][name][u32 n][n x (xmin xmax ymin ymax zmin zmax)][u32]`.
