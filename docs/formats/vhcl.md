# Vehicle routes (`vhcl`)

Part of the [file format reference](../REFERENCE.md): [4.29 Other data object classes](../REFERENCE.md#429-other-data-object-classes).

**Vehicle routes (`vhcl`, 1 per lair island, `envs\lair_*.pc`).** No object header: `[u32 1][u32 0]`, then prop 1 with
`[u32 n]` (8 on all four islands) + n nodes. A node is `[u32 ENTI entity id]` prop 2 holding:

| Part | Layout |
|---|---|
| 2 link lists | prop 1 each: `[u32 n]` + n `[u32 other node id]` prop 1 { prop 1 `[u32 k]` + k prop 1 `[f32 from][f32 to]`; the same again }. The ranges are distances along this node's routes and the other node's, probably where the two cross. |
| Short routes | Two prop 2 splines, 3 to 44 m long, starting or ending at the stop. Tail: 28 bytes, `[f32][f32 heading, radians][f32 0, 1 or 10][f32 from][f32 to][0][0]`. |
| Long routes | Two prop 1 splines, 2 to 906 m long, leading away from the stop. Tail: 9 bytes, `[f32][f32 angle][u8]`. |
| Transform | 21 f32: position, then two 3 x 3 rotations |

A spline is `[u32 2][u32 n][u32 1][u32 n]` + n 45-byte points `[u32 0][f32 x, y, z][f32 x3 in tangent][f32 x3 out tangent][f32 distance
along the spline][u8 flag 0-9]`. Most stops sit at y 9 to 12; one per island sits at sea level (y 0), and one below it
(y -8.5 to -65, its long routes at y -102 to -158).

**Spline points.** The two vectors are Hermite tangents. In a point
`[u32 0][f32 x, y, z][f32 x3 in][f32 x3 out][f32 distance][u8 flag]`, the segment from point i to point i+1 is the cubic
with Bezier controls `p(i) + out(i)/3` and `p(i+1) - in(i+1)/3`. The stored distance is that curve's arc length
(298 of 310 segments within 1 cm, median error 0.00001). The flag sets how the tangents were made:

| Flag | Tangents |
|---|---|
| 0 | Smooth: in = out = (next - previous) / 2. First point: in 0, out = next - p. Last: in = p - previous, out 0 |
| 1 | Corner: in = p - previous, out = next - p |
| 2, 3 | Set by hand: in = out, free |
| 4-9 | Rare; 6 has unrelated in and out |

Spline effects (`ENTI` `0x21`) use the same point format; 205 of their 226 segments match the arc length within 1 cm.
