# Talking-head scenes (`rtsc`)

Part of the [file format reference](../REFERENCE.md): [4.29 Other data object classes](../REFERENCE.md#429-other-data-object-classes).

**Talking-head scenes (`rtsc`, 437).** The dialogue pop-ups. Scripts start one through a `Scene` pin ([4.20](../REFERENCE.md#420-scripts-flowgraphs)). The
outer prop `0x7` holds:

- `[u32 rtsu][u32 rtbg]`: the scene setup and its background.
- prop 1 with the cast: `[u32 n]` + n prop 3 entries `[u32 actor type][0][1][0][text ref of the speaker's name]`. Actor
  types are minion types (Worker `0xd162537e`) or character hashes.
- prop 1 with the lines: `[u32 n]` + n prop 5 entries, each with the line's text ref (dialogue tables), the speaking cast
  index, and `[1][actor type]` + the animation hash.

The object takes its display name from the first speaker, so many scenes show as "Worker".

`rtsu` (scene setup, 14): prop 6 with two `[hash][u8 2]` camera or slot entries, then the `rtbg` id. `rtbg`
(background, 13): four hashes, then a list of `[hash][hash]` pairs.
