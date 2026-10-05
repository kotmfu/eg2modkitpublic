# Jobs (`rjob`) and traits (`rtrt`)

Part of the [file format reference](../REFERENCE.md): [4.13 Jobs (`rjob`) and traits (`rtrt`)](../REFERENCE.md#413-jobs-rjob-and-traits-rtrt).

- `rjob` prop `0x12` (219 bytes) opens with 3 groups of 10 floats: Smarts, Vitality, Morale. Float 0 of each holds
  the rate (positive restores, negative drains).
- Who may do a job: prop 1 = `[u32 n][u32 key]` prop `0xE`…, keyed by minion type or character kind. Settings byte
  77 = 1 allows it. No list allows anyone.
- A trait (`rtrt`, prop `0x13`) lists components `[u32 1]["name" padded][u32 kind][settings]`. Kinds: 0 condition,
  3 stat adjust, 4 damage, 6 salary mod, 7 on spawn/max stat, 8 movement/disguise, 12 armour, 19 on attack, 22
  ignore tag, 0x20 enter a temperature tile, 0x21 exit one ([4.14](../REFERENCE.md#414-temperature)).

`mtex` objects list the 17 minion type hashes, robots included. Worker is `0xd162537e`.
