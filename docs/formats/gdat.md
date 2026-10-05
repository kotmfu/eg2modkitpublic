# Game data (`gdat`)

Part of the [file format reference](../REFERENCE.md): [4.37 GUI, fonts, input and stats](../REFERENCE.md#437-gui-fonts-input-and-stats).

**Game data (`gdat`, `misc\common.asr`, 1, 382 bytes).** `[u32 1][u32 0]` prop 1 { prop `0x24` { 356 raw bytes } }:

| Offset | Content |
|---|---|
| `0x1a` | 30 job ids (`rjob`) the code uses by role: Building (twice), Constructing, Deconstructing, Burning Body, Escorting, Interrogating a Prisoner, Brainwashing, Patrolling the Lair, Responding to Intruder, Repairing Item, Rearming Trap, Extinguishing fire, Acquiring a Weapon, Returning an Item, Travelling to world, Deserting, Mocking a Prisoner, Fuelling, Delivering Ammo, Returning Ammo, the test job |
| `0x92` | A tutorial-package consumable (`rcns` `0xb9a7eeb6`), consumable `0x14ffb3a5`, two tags (`rtag`), Gold, Intel and Tech (`rcns`), the Mess Hall job category (`rtmj`) |
| `0xb2` | `KeyHash("MEDIUM")` (default difficulty), `KeyHash("genius_max")` (default genius), region "Crown Gold" (`rmlr`) |
| `0xbe` | `[u32 3]` + 3 RGBA float colours: green (0.12, 0.56, 0), orange (0.91, 0.58, 0.02), red (0.84, 0.17, 0.05), alpha 0.78 each; then f32 0.925, 0.298, 1, 0.784, 0.08 |
| `0x106` | 5 `gui\gui.asr` component names: `F_MinionEntryExpanded` (`0xdfd9ae01`), `0xab0020e8`, `0x4e1cb528`, `F_OffscreenIndicator` (`0xf3cb6bd2`), `F_Popup_SchemeStart` (`0xd0661bf8`) |
| `0x11a` | f32 5, u32 5, f32 5, f32 5, f32 5 |
| `0x12e` | 8 hashes: furniture `0x505dd80d` (benchmark package), `0x0a1d1b6f`, `0x8e8d7696`, then gui.asr graph components `0xf19a0dcd`, `0xe5debce1`, `0x743f9700`, `0xa9862b10` and the Sticker Book scroller `0xc88a4d46` |
| `0x14e` | `[u32 5]` + 5 pairs of gui.asr components: four `[0x6cb527a7][0x4b771ce6]` (locked item, `F_Stats_StickerBook_GenericUnlocked`) and one `[0xd1dfd995][0xd1dfd995]` |
| `0x17a` | gui.asr component `0xfdb8690d` (`F_Reward_Item_NoIcon`) |
