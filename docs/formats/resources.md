# Resources (`rcns`)

Part of the [file format reference](../REFERENCE.md): [4.16 Resources (`rcns`)](../REFERENCE.md#416-resources-rcns).

`rcns` objects hold the consumables. A u32 at +41 caps how much you can hold: Intel `0x1cb32d8d` and Tech
`0x17a283c0` at 99, Henchman at 10 (three copies: `common`, `dlc_henchman_valkyrie`, `objectives_tutorial`), Gold 0
(vaults set it instead). The cap seeds new games; each save keeps its own.
