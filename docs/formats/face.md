# Facial poses (`FACE`)

Part of the [file format reference](../REFERENCE.md): [4.34 Island level chunks and entities](../REFERENCE.md#434-island-level-chunks-and-entities).

**Facial poses (`FACE`, 27: `misc\common.asr` 4, `misc\packages\required\characters_content.asr` 19, one per island).**

```
u32 2, u32 0, u32 head      KeyHash of the head: male_a/b/c, female_a/b, OceansEG, RedIvan, Maximilian...
u32 8, u32 poses (189)
poses x 13 bytes: [u32 pose hash][u16 count][u16 count2][u16 first][u16 end][u8 0]    end - first = count
u32 bones (23), bones x u32 KeyHash of the bone name (bn_jaw, bn_Eye_L, bn_MouthU_M, bn_EyeLidU_R, bn_Cheek_L...)
u32 keys (497-523)
keys x 32 bytes: [f32 x, y, z, w rotation][f32 x, y, z translation][u32 bone index]
45-byte tail, the same in all 27 (below)
```

A pose's `first`..`end` is a range of keys. The four island chunks are identical (head `OceansEG`).

**Tail and pose hashes.** The 45 bytes are 9 f32 angles in radians, all whole degrees: 31, 40, 24, 28, 12, 0, 40, 28, 0.
Then `[i32 -1][u32 0][u8 0]`. They are probably eye or head look limits. The 189 pose hashes match no string or name of
up to 5 characters. Some run in sequences (`0x08fee1ad`-`af`, `0x08fee1eb`-`ed`), which fits names that differ only in a
last character.
