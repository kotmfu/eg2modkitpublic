# Lair settings (`misc\common.asr` `BLUE` #25, chunk 27)

Part of the [file format reference](../REFERENCE.md): [4.25 Lair settings (`misc\common.asr` `BLUE` #25, chunk 27)](../REFERENCE.md#425-lair-settings-misccommonasr-blue-25-chunk-27).

```
u32 1, u32 0, u32 1, u32 root class 0x614510c0, u32 count (24)
24 objects back to back, no byte lengths: [id][0x0D][id][class][member count] + members
string member: [key][3][0x55f89b99][0][4][len][chars]   (no terminator)
```

22 entries are class `0x6431F764` settings objects. Each lair's entry holds its island path, e.g.
`Lair\Lair_Tropical_03_Default\Lair_Tropical_03_Default`. No entry may move or change size; moving one breaks the
menu art. Entries 19 and 20 (dev test levels) take 4,853 bytes together.
