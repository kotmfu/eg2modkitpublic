# Packages (`rpkg`)

Part of the [file format reference](../REFERENCE.md): [4.4 Packages (`rpkg`)](../REFERENCE.md#44-packages-rpkg).

A manifest (`<name>.asr`) holds `FNFO`, `RSFL` and one `rpkg` object. Its outer property `0x12` starts with an
18-byte profile:

```
u32 kind         1 = required, 3 = managed
u32 entitlement  0 = base game; a per-DLC hash on paid DLC
u32 genius       genius_* packages only
u8[6] flags
```

Length-prefixed `u32` id arrays follow, by property path:

| Path | Lists |
|---|---|
| `18.0.1.0` | Data objects the package provides |
| `18.0.1.1` | `fnas` ids |
| `18.0.1.2.1.N` | Typed asset groups (`.6` models, `.4` animations, `.7` sound samples) |

One byte, `deferred`, follows the arrays: 0 loads the content at boot, 1 at game start. Then `P2(00)`.

At boot the engine reads each manifest and loads the six `deferred = 0` packages (common, characters, furniture,
mapregions, frontend, dlc_recruitablesuperagents). At game start it reads the manifests again and loads the other
packages' content in a fixed order, DLC before the packages it builds on. Packages refer to each other's objects
and assets freely.

The content file (`<name>_Content.asr`) holds `FNFO`, `RSFL` (empty works) and the objects and assets. Each
content package has one `fntr` table, and the game merges them. The `.ts` beside it can be an empty archive
(`"Asura   "` + 4 zero bytes).
