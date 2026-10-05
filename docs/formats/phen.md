# Physics mesh (`PHEN`)

Part of the [file format reference](../REFERENCE.md): [4.34 Island level chunks and entities](../REFERENCE.md#434-island-level-chunks-and-entities).

**Physics mesh (`PHEN`, islands, one per island, version 10).** All four parse to the end.

```
u32 10, u32 0, u32 faces, u32 vertices
faces x 20 bytes:
  u16 v0, v1, v2, v3     v3 = 0xffff for a triangle
  u8 0, 0, 0, u8 0xff for a triangle, 0 for a quad
  u32 flags              low byte 0x04/0x0a/0x2c/0xac/0xae, plus one of 0x20000-0x100000
  u32 material hash      matches a hash in the island's MARE chunk (0x002eaeec most often)
faces x u8               mostly 0; 49-255 on a few faces
vertices x f32 x, y, z
u32 2, u32 nodes, u32 internal nodes
nodes x 128 bytes        the culling-tree node of INST (4.23): 6 x 4 f32 child boxes, 4 refs index<<8|kind, 16 zero bytes
```

| Island | Faces | Quads | Vertices | Tree nodes |
|---|---|---|---|---|
| Caine Key (tropical 01) | 11,951 | 2,678 | 8,532 | 3,244 |
| tropical 02 | 7,807 | 3,132 | 6,279 | 1,365 |
| tropical 03 | 7,360 | 2,375 | 5,575 | 1,365 |
| arctic | 2,221 | 1,481 | 2,426 | 427 |

Ref kinds 0-3 occur; kind-1 indexes stay below the face count.

**Winding and flags.** Faces wind clockwise seen from their walkable side: of the faces whose normal is
within 45 degrees of vertical, 19,924 point down by the (v1 - v0) x (v2 - v0) rule and 233 point up. The low flag byte
takes 13 values: 0x04, 0x2c, 0xac and 0xae (bit 2 set) or 0x02, 0x0a, 0x12, 0x2a, 0x42, 0x52, 0x72, 0x7a (bit 1 set), plus
0x06. Only bit-1 faces have a non-zero per-face byte (49-255). The high part of the flags (bits 17-22) is 0 or 2-64, mostly
one bit.
