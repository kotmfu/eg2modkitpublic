# Lair maps (`envs\basedefinitions\<lair>.base`)

Part of the [file format reference](../REFERENCE.md): [4.22 Lair maps (`envs\basedefinitions\<lair>.base`)](../REFERENCE.md#422-lair-maps-envsbasedefinitionslairbase).

An uncompressed container: `bsnf`, one 10-14 MB `ENTI` (the grid, id `0x989680`), `ARNM`, `dtvs`. The ENTI holds a
property tree whose block keys differ per lair (`1d/44/6` in Crown Gold, `1e/45/7-8` elsewhere). The keys are
serializer versions ([4.34](../REFERENCE.md#434-island-level-chunks-and-entities)).

- `bsnf` +17 = the lair id = KeyHash(file stem), followed by the world-map regions (`rmlr`) with 5 (key, f32) pairs
  each.
- Floor: raw `u32 floor, u32 width, u32 height`, then width x height cells, row-major.
- Cell (prop `0x33` in Crown Gold, `0x34` elsewhere; the key is the serializer version): `u32 flags @0, u32 type @4,
  u32 row @8, u32 floor @12, u32 column @16`, then a variable tail.
- Cell type names the room or rock. Rock tiers 1-4: `0x497004c7`, `0x48f78ca2`, `0x48f25963`, `0xd55a674f`. Gold
  seam `0x48f218d8`, edge rock `0x0cf2f3d8`, lift `0x080b0101`, outside `0x430bd860`. Rooms: Corridor `2a5b912a`,
  Power Station `ecacefcf`, Barracks `dddf7f89`, Mess Hall `29eb7171`, Vault `e58aa2f2`, Control Room `a3275078`,
  Armoury `731de7c9`, Prison `c276dccb`, Laboratory `6f00f6f3`, Training Room `627743f5`, Archive `728da182`,
  Infirmary `a92c5365`, Staff Room `d4db647b`, Casino/Hotel `e4cbb274`, Inner Sanctum `6b9b4ad3`, Workshop `d36ac811`,
  Test Chamber `bac20f8c`.
- A dug cell has a room type, flags bit 9 off and bit 4 on, and the tail
  `[u32 region][03000000 810014 0A000000 C7047049 D0101D7E 00000000 01]`.
- Pre-placed objects: a list in the grid block (key `0x64` = version 100 in Crown Gold, `0x68` = 104 elsewhere):
  `[u32 count][u8 1][u32 id]`, then `[u8 1][u32 id]` before each object. Payload: `u32 flags, u32 fnas, row, floor,
  column, i32 facing x, y`, covered cells, footprint, rotation. A record of the wrong version crashes the game.
- AI block (grid `/1e/1b`): `[u8 has groups]`, then agent squads (`[01][u32 id][u32 1]` + prop `0x3eb`) and civilian
  groups. A squad's `[6]` block lists the area it has searched; a fully searched area makes the squad leave.
- Characters are `ENTI` `[u32 1][u32 0][u32 id][u32 kind]` (0x8003 minions, 0x8004 other characters, 0x8007 their
  companion, 0x800b island vehicles), position @80 (x, height, z). Grid row = x, column = z. Floor heights: 0 = 20,
  2 = 0, 3 = -14, 4 = -28, 5 = -52.

The game loads maps through its save loader, and only new games read the map.
