# Furniture (`fntr`, `fnas`)

Part of the [file format reference](../REFERENCE.md): [4.12 Furniture (`fntr`, `fnas`)](../REFERENCE.md#412-furniture-fntr-fnas).

```
fntr: u32 7, u32 0, u32 count, u8 1, count x { u8 1, property 0x8000009b (record) }
```

| Field | Layout |
|---|---|
| Name | `name\0` padded to a multiple of 4. The rest lays out from that boundary, and a rename without re-padding hangs the loader. |
| Texts | 3 text refs: display name, description, plural |
| Footprint | `[u32 n]` + n cells, prop `0x10` (44 bytes: `[u32 (y<<16)\|x][u32 2][flags]`), a row-major W x H rectangle from (0,0) |
| Cell bytes | [0] kind (2 solid, 0 open, 1 door); [36] = 2 keep clear; [20]/[24]/[28]/[32] back/right/front/left edge |
| Gold cost | The u32 after a pi float (`db 0f 49 40`), about 110 bytes in |
| Icon key | The u32 before the first `0x80000001` header after the text block = KeyHash(`data/graphics/gui/icons/furniture/<icon name>`). An unknown key shows a white square. |
| `fnas` id | 1:1 with the item, followed by the owning package's id |
| Job slots | prop 1/5/1: `[u32 n]` + n prop `0x11` (job link, stand points, slot name such as `Locker_Tier_1`) |
| Temperature output | [4.14](../REFERENCE.md#414-temperature) |
| Rooms | Room types the item may stand in (none = anywhere) |
| Links | `rjob`, `rtag` and `trpa` ids |

A slot payload is `[0][job id][0 x4][1][n points]`, each point prop `0x80000004` `[2][2][1][2][0][x][height][z]` in
tile units. A locker's capacity equals its slot count.

`fnas` (art binding): `u32 0, u32 0, u32 fnas_id` + asset refs. The art chain runs record → `fnas` id → `COMA` (key
= fnas id) → model name hash → `HSKN`.
