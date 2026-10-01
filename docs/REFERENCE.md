# ModKit reference

Where every file lives, what reads and writes it, how a mod becomes files in the game folder, and the layout of
every format ModKit handles. [RESEARCH.md](RESEARCH.md) and [HANDOFF.md](../HANDOFF.md) have the experiments
behind these facts. Offsets are bytes, integers are little-endian, and `u32`/`f32` are 4 bytes.

"Confirmed" means seen working in game. The rest comes from parsing and rebuilding every file in a retail
install. Facts that neither check covers say so.

## 1. Where things live

### The game folder

Default `C:\Program Files (x86)\Steam\steamapps\common\Evil Genius 2`. `GameInstall.FindDefault` also checks every
library in Steam's `libraryfolders.vdf`. ModKit accepts a folder that has `bin\evilgenius_dx12.exe` and
`misc\packages`.

| Path | What it is |
|---|---|
| `bin\evilgenius_dx12.exe`, `bin\evilgenius_vulkan.exe` | The game. Packed by a protector, so ModKit never patches either one on disk. |
| `misc\packages\required\<name>.asr` | Package manifest (`rpkg`) |
| `misc\packages\required\<name>_Content.asr` | Package content: data objects, furniture, models, textures, materials |
| `misc\packages\required\<name>_Content.ts` | Texture streaming table (`TXST`) for that package |
| `misc\packages\required\<name>_Content.asr.pc.sounds` | Package sound bank (optional) |
| `misc\packages\managed\…` | Same layout: DLC, genius and frontend packages |
| `misc\packages\development\` | Four empty dev slots the engine probes. ModKit uses `e3data`. |
| `misc\common.asr` | 255 MB shared archive: materials (`MARE`), lair settings (`BLUE`), dialogue lines. Memory-mapped by the game. |
| `misc\Common.asr_en` | Dialogue line table (`DLLN`), not UI text |
| `text\pc\localisation.asr` | List of text tables to load (`HTPR`) |
| `text\pc\<table>\<table>.asr_<lang>` | One UI text table (`HTXT`), e.g. `text\pc\menu\menu.asr_en` |
| `gui\gui.asr`, `gui\main.asr`, `gui\splash.asr` | GUI layouts (`GUAT`) and GUI textures |
| `<file>.pc.sounds` | Sound bank beside an archive: `sounds\gmsnd.asr.pc.sounds` (UI), `misc\common.asr.pc.sounds`, packages, islands |
| `<file>.pc.streamsounds` | Streamed dialogue and music (`ASTS`): `sounds\streamingsounds.asr.pc.streamsounds`, `misc\common.asr_wav_en.pc.streamsounds`, packages |
| `textures\*.pc_textures` | Streamed texture blobs, up to 4.29 GB each (RSCF DDS); `theblob`, `theblob1`, one per DLC. The arctic island has its own. |
| `envs\basedefinitions\<lair>.base` | Lair map: the diggable grid, pre-placed furniture, AI block |
| `envs\basedefinitions\*.scenario` | Save-format levels (only `benchmark.scenario` is real) |
| `envs\<lair>.pc` | The island outside: scenery instances, meshes, textures, sky, fog, physics, effects |
| `envs\<lair>.ts`, `envs\<lair>.pc.pc.sounds` | The island's texture table and sounds |
| `fmv\*.webm` | Videos (intro logos, outros) |
| `parameters\parameters.asr` | GUI parameters (`GUAP`) |
| `stat\stat.asr` | Steam achievements |

The four lairs are `lair_tropical_01_default` (Crown Gold), `_02_` (Montañas Gemelas), `_03_` (Caine Key) and
`lair_arctic_01_default` (Icicle Point, Oceans DLC).

### Saves

`%LOCALAPPDATA%\Evil Genius 2\PC_ProfileSaves\<steam id>\slot<N>.sav`. `slot0` is the profile. Slots 1-10 are
manual saves and 11-20 are autosaves; the exe hard-codes the limit of 20. ModKit only reads saves: it takes
furniture and agent templates from them and never writes one.

### ModKit's own files

| Path | Written by | Read by | Holds |
|---|---|---|---|
| `%APPDATA%\Eg2ModKit\settings.json` | ModManager | ModManager, ColdWarArt | Game path, mods folder, language, ticked mods, look, advanced tools |
| `Documents\Eg2ModKit\Mods\<id>.json` | ModManager, ColdWarArt, any editor | Both apps | One mod definition (section 3) |
| `Documents\Eg2ModKit\Mods\<id>.assets\` | ModManager (Replace…, imports) | Builder | The mod's replacement files: WAV, DDS, `.mesh`, OBJ, pictures |
| `Documents\Eg2ModKit\Mods\_retired\` | You | Nothing | Old probes. The manager lists top-level `.json` only. |
| `Documents\Eg2ModKit\Stamps\*.json` | Lair maps page, ColdWarArt | Lair maps page | Saved map rectangles as relative `MapEdits` |
| `<ModManager dir>\runtime\winmm.dll` | `native\winmm\build.cmd` (copied at build) | Builder | The runtime proxy DLL to install |
| `<installer dir>\Mods\*.json` + `.assets` | Eg2ModInstaller | Eg2ModInstaller | The player's mods |
| `<installer dir>\Eg2ModInstaller.json` | Eg2ModInstaller | Eg2ModInstaller | Game path, language, ticked ids |
| `<game>\eg2modkit.installed.json` | Installer | Both apps | What was installed (section 2.4) |

### What an install writes into the game folder

Nothing else is ever written. `Installer.IsSafeTarget` refuses any other path.

| Path | When |
|---|---|
| `misc\packages\development\e3data.asr`, `e3data_Content.asr`, `e3data_Content.ts` | Any new or changed furniture, object, texture or tree |
| `<any game file>.asrpatch` | Whole-file replacement of that file (see 2.2) |
| `text\pc\eg2modkit\eg2modkit.asr_<lang>` | Texts for new furniture and new objects |
| `textures\eg2modkit.pc_textures` | Replaced streamed textures |
| `envs\lair_<stem>.*`, `envs\basedefinitions\lair_<stem>.base` | New lairs (only when no game file has that name) |
| `bin\winmm.dll`, `bin\eg2modkit.cfg` | Runtime tweaks, lair maps, scenery, new lairs, video skips |
| `bin\eg2modkit_blocks\*.bin` | Replacement compressed blocks for memory-mapped files |
| `bin\eg2modkit.log` | Written by the DLL while the game runs. Uninstall deletes it. |

## 2. How mods work

### 2.1 From mod file to game files

1. **Load.** `GameData.Load` reads every base package, text table, GUI archive and lair level: 517 furniture
   records, about 8,700 data objects and 26,273 texts. It always reads the game's own files, never an installed
   `.asrpatch`.
2. **Build.** `ModBuilder.Build(game, tickedMods)` merges every ticked mod into one set of files and returns them
   with a report and a list of errors. Mod order doesn't matter. Two mods that set the same value differently are
   an error; two that set it the same way are fine. It parses every file it builds again and checks
   it before returning it.
3. **Install.** `Installer.Install` removes the previous install, writes the new files, and records each one's size
   and SHA-256. It refuses to overwrite a file it didn't write unless you confirm, and it never replaces a game lair.
4. **Uninstall** deletes the recorded files whose hash still matches. It also deletes the DLL log and an empty
   `misc\packages\development`.

ModManager and Eg2ModInstaller run the same build and install code, so each can uninstall what the other installed.

### 2.2 The routes into the game

The engine has no mod support. ModKit uses three hooks.

**Dev slot package (preferred).** The engine opens `misc\packages\development\{datafordevelopmentfeatures,
e3data, placeholder_objectives, temp_package}.asr` at startup and skips missing ones silently. ModKit writes one
package in the `e3data` slot with `entitlement = 0` and `deferred = 1`, so it loads at game start after the base
content. Into it go:

- new furniture (a copied `fntr` record, `fnas` and `COMA`, plus copied art if the item has its own),
- new data objects of any type (a copy with a new id),
- **overrides**: an edited copy of a game object or furniture record under the *same* id. The later copy wins
  (confirmed for a furniture price and a research time). Price edits, value edits, tree layouts, requirement,
  task, scheme, pool, job, shape, list and graph edits of game objects all build this way,
- new textures (GUI icons, own-art textures, tree backgrounds).

**`.asrpatch` (whole-file replacement).** For every file it opens, the engine also looks for `<full name>.asrpatch`
next to it and reads that instead. A patch replaces the **whole file**: nothing merges. A patch with one text
entry blanks every other string in that table. So ModKit always rebuilds the complete file from the game's copy
plus every mod's changes. It uses patches for:

- text edits of existing keys (`text\pc\<t>\<t>.asr_<lang>.asrpatch`),
- `text\pc\localisation.asr.asrpatch`, which adds `EG2MODKIT` to the table list,
- the `frontend` package (it loads before the dev slot), and new objects put there with `Into = "frontend"`,
- replaced textures, sounds, meshes and animations inside packages, GUI archives and island levels,
- sound banks and streamed sound stores (whole store, up to 418 MB),
- `.ts` tables repointed at ModKit's texture blob,
- lair maps (`.base.asrpatch`) and islands (`.pc.asrpatch`),
- zero-byte `fmv\<video>.webm.asrpatch` files, which the DLL turns into "file not found" to skip a video.

`misc\common.asr` is the exception: any `.asrpatch` of it breaks the menu art, because the game memory-maps the
file. ModKit swaps changed blocks in through the DLL instead (`file_block`, section 4.24).

**Runtime DLL (`bin\winmm.dll`).** Both exes import `winmm.dll`, so a proxy placed in `bin` loads first and
forwards all 181 exports to `System32\winmm.dll`. It never changes game code. It:

- finds data globals by byte pattern and rewrites them every second (minion cap, gold capacity, developer switches,
  Intel/Tech caps in saves, salary rate),
- hooks the exe's `CreateFileA/W`, `GetFileAttributes(Ex)W` and CRT `fopen_s` imports to send `.base`, `.scenario`,
  `envs\*.pc` and video opens to their `.asrpatch`. The map loader has no `.asrpatch` lookup of its own.
- aliases a new lair's island files (`island_alias`) while its map or one of its saves is loaded,
- swaps replacement blocks into reads and mapped views of a file (`file_block`).

It reads `bin\eg2modkit.cfg` (section 4.24) and logs to `bin\eg2modkit.log`.

### 2.3 Ids and text keys

New objects need ids that no game file uses and that stay the same from one build to the next, so a save made with a
mod keeps working after a rebuild. `IdAllocator` takes the FNV-1a hash of `"eg2modkit:" + key` and adds `#n` on a
collision, on 0 or on 0xFFFFFFFF. Keys:

| What | Key |
|---|---|
| New object | `<mod id>/obj/<Key>` |
| New furniture's `fnas` | `<mod id>/<item id>/fnas` |
| New furniture's icon key | `<mod id>/<item id>/key` |
| Dev slot package ids | `package`, `aux` |

Other edits refer to a new object as `"@Key"`. A `FieldEdit` value of `"@Key"` writes that object's id.

New texts go to ModKit's own table `eg2modkit` under keys such as `MOD_<MODID>_<ITEMID>_NAME` and `_DESC`. Text
references in objects name their table (`[1][0][table hash][key hash]`), so the reference is rewritten to point at
`KeyHash("eg2modkit")`.

### 2.4 Install record and mod versions

`<game>\eg2modkit.installed.json`:

```json
{
  "installedAt": "2026-09-27T12:00:00",
  "mods": ["quick-tweaks", "cold-war-core"],
  "installed": [{ "id": "quick-tweaks", "name": "Quick tweaks", "version": "1.0.3", "fingerprint": "A1B2C3D4E5F60718" }],
  "files": [{ "path": "misc\\packages\\development\\e3data.asr", "size": 1234, "sha256": "…" }]
}
```

`fingerprint` is the first 16 hex digits of a SHA-256 over the mod's JSON and every file in its `.assets` folder.
Both apps compare it with the mod on disk, so they can report "changed since it was applied" even when the version
number didn't change. The installer saves the record after each file it writes, so an install that crashes halfway can still
be uninstalled.

### 2.5 Sharing: `.eg2mod`

A `.eg2mod` is a plain zip holding `<id>.json` and a `<id>.assets/` folder. The same zip renamed `.zip` works too.
Export packs the whole assets folder. Install extracts only those two entries, guards against zip-slip, and
replaces an existing mod with the same id. Install copies a bare `.json` along with any `.assets` folder next to it.

### 2.6 Saves and mods

A save made with mods active can refer to their objects. New lairs using their own id can't be loaded without the
mod. Lair map edits only affect new games, because saves keep their own copy of the grid. Intel and Tech caps
set in data only seed new games; the `*_cap_save` runtime tweaks change a loaded save.

## 3. The mod file (`<id>.json`)

`ModDefinition`, camelCase JSON, nulls left out. Every list is optional.

```json
{
  "id": "evil-bunk", "name": "Evil Bunk", "version": "1.0.0", "author": "", "description": "",
  "newFurniture": [{ "id": "Evil_Bunk", "donor": "Bed_01_Bunk", "cost": 666, "displayName": "Evil Bunk" }],
  "furnitureEdits": [{ "name": "Door_01", "cost": 250 }],
  "textEdits": [{ "table": "menu", "key": "FE_NEW_GAME", "text": "Begin Evil" }]
}
```

`id` is `[A-Za-z0-9_-]`. New furniture ids and new object keys are `[A-Za-z0-9_]`.

| List | Entry | Builds into |
|---|---|---|
| `newFurniture` | `id, donor, cost, displayName, description?, ownArt, artFrom?, textures[{texture, source}]` | Dev slot: copied record, `fnas`, `COMA`, icon; own art copies models, materials and textures under new names |
| `newObjects` | `key, package, tag, source "0x…", into?, texts[{offset, text}], edits[FieldEdit]` | Dev slot (or the frontend patch with `into`): a copy of the source with a new id |
| `furnitureEdits` | `name, cost` | Override record |
| `fieldEdits` | `package, tag, object, offset, type u8/u32/i32/f32, value, expect, note?` | Override object. `object` = record name (`tag` `fntr`) or `0x` id; `expect` = the game's bytes as hex. |
| `textEdits` | `table, key, text` | Whole-table `.asrpatch` |
| `runtime` | `RuntimePatch` (section 4.24) | `bin\eg2modkit.cfg` |
| `assets` | `file, tag, name, occurrence, source, island?` | Patch of the file holding the asset, ModKit's texture blob, or a new lair's copy |
| `newAssets` | `name, source, package?` | New texture in the dev slot or the frontend patch |
| `skipVideos` | `"fmv/rebellion.webm"` | Zero-byte `.asrpatch` + DLL |
| `researchTrees`, `engineeringTrees` | `tree, nodes[{research, column, row, dx, dy}], links[{from, to}], background?` | Override tree |
| `scriptSwaps` | `object, script, from, fromScript, values[FieldEdit]` | One script block replaced by a copy of another object's |
| `graphEdits` | `object, graph?, op remove-node/copy-node/add-link/remove-link, node, link, from, fromPin, to, toPin` | Script structure changes |
| `requirementEdits` | `object, minions?, furniture?, costs? [{ref, count}], unlocks?` | Research or engineering requirement lists |
| `taskEdits` | `object, tasks ["0x<objective>:<index>"]` | An objective's task list |
| `schemeEdits` | `object, minions? [[{ref, count}]], heat?, costs?, duration?, expiry?` | Scheme values |
| `poolEdits` | `pool, add[], remove[]` | Which schemes a pool offers |
| `shapeEdits` | `name, width?, height?, slots?` | Furniture footprint and slot count |
| `jobEdits` | `object, types[]` | Who may do a job |
| `listEdits` | `package, tag, object, at, count, order[]` | Reorder, drop or repeat entries of any list |
| `mapEdits` | `file, floor, x0, y0, x1, y1, action, tier, room?, item?, facing?, template?, templateKey?, anyVersion, squad?, vehicle?` | `.base.asrpatch` |
| `sceneryEdits` | `file, group, groupName?, x0/z0/x1/z1?, action hide/move/copy/mesh, source?, dx, dy, dz` | `.pc.asrpatch` |
| `newLairs` | `stem, from, region?, keepId, islandEdits[FieldEdit]` | New `envs\` files, plus a `common.asr` block when `keepId` is false |

`MapEdit.action` is one of: `tier`, `dig`, `room`, `gold`, `wall`, `rock`, `remove`, `place`, `character`,
`agent`. X is the column and Y the row.

## 4. File formats

### 4.1 Container (`.asr` and friends)

Two forms. The game accepts either for a patch.

```
AsuraZbb (compressed)
  char[8] "AsuraZbb"
  u32     comp_total      file size - 16
  u32     raw_total       decompressed size
  repeat: u32 comp_n, u32 raw_n, zlib stream     one block per 2 MiB of payload

"Asura   " (uncompressed: the payload itself; three trailing spaces)
  char[8] "Asura   "
  repeat: char[4] tag, u32 size (including these 8 bytes), body
  a zero tag ends the list; trailing bytes are kept as they are
```

- The zlib streams use a **4 KiB window** (header `48 89`). .NET only writes 32 KiB windows, so ModKit has its own
  encoder, `Zlib4k`: greedy LZ77 with one fixed-Huffman block. It is a byte-exact port of
  `reference/deflate4k.py`, and the self-test checks it against `reference/golden.json`. `CompressSmall` does
  lazy matching with dynamic Huffman, for blocks that have to fit back into their old space.
- Inflating the whole file in one call returns only the first block. You have to walk the blocks.
- ModKit's rebuilt files are not byte-identical to Rebellion's (the deflate output differs), but the decompressed
  payload is, and that is all the game reads.
- `AsuraArchive.ChangedBlocks` re-compresses only the changed blocks and pads each to its old compressed size.
  `misc\common.asr` needs this.

### 4.2 Chunks

Uppercase tags are engine chunks and lowercase tags are Evil Genius 2's game data. Chunks sit back to back with
no padding.

| Tag | What |
|---|---|
| `FNFO` | Always first. `u32 1, u32 flags (0/4/11/15), u32 payload_size (= payload length - 4), u32 8`. ModKit keeps the size right when it writes. |
| `RSFL` | Resource list: `u32 ver, u32 0, u32 count`, entries `name\0` padded to 4 + `u32 hash, u32 size, u32 count`. In a manifest, entry 0's size must equal the `rpkg` chunk size. In `misc\common.asr` the entries are payload offsets, so nothing may change size before a resource. |
| `RSCF` | Embedded file (4.6) |
| `HTXT` | Text table (4.5) |
| `HTPR` | One text table name in `localisation.asr`: `[u32 1][u32 0][NAME\0 padded to 4]` |
| `COMA` | Per-item art binding keyed by the u32 at +12. **Stored sorted by that key; keep it sorted** (an unsorted list hung the loader). |
| `HSKN`, `HSKL`, `HMPT`, `HSBB`, `HSKE` | Model, LOD names, ?, bounds, end of a model run (4.7) |
| `HCAN`, `FAAN` | Animations (not decoded) |
| `MARE` | Materials (4.8) |
| `TXST` | Texture streaming table, in `.ts` files (4.9) |
| `ASTS` | Streamed sound store (4.11) |
| `ENTI` | Entity: lair grid, characters, island entities (4.19, 4.21) |
| `INST` | Island scenery instances (4.20) |
| `BLUE` | Class-tree data, including lair settings in `common.asr` (4.22) |
| `GUAT`, `GUAP`, `FONT` | GUI layout, GUI parameters, fonts |
| `DLLN`, `DLEV`, `DLET`, `DLLT` | Dialogue lines, events, small tables |
| `PSKY`, `FOG `, `PLUT`, `PHEN`, `NAV1`, `PBRV`, `OCMH`, `CT*`, `FX*` | Island sky, fog, colour grade, physics mesh, navmesh stub, probe volume, occlusion, cutscenes, effects |
| `bsnf`, `ARNM`, `dtvs`, `stsy`, `ttsy`, `DYMG`, `DLIG`, `SMXG`, `ATIG` | Map and save state |
| `rpkg` | Package manifest (4.4) |
| other lowercase | Data objects (4.3) |

### 4.3 Data objects and properties

Every lowercase chunk except a few stubs is one data object. **The tag is the class.**

```
u32 version     10
u32 pad0
u32 object_id   what other objects and manifests use to refer to it
u32 pad1        usually 0
u32 package_id  the owning package
u32 aux_id      a second, near-unique id (not a class id)
property stream
```

A property is `u32 key (0x8000NNNN), u8 kind, u32 length, payload`. Payloads mix nested properties with raw
scalars, strings and arrays, and there is no schema. `PropStream` recurses into anything that looks like a
property header and keeps everything else as raw bytes, so `parse(x).ToBytes() == x` holds for every object in the
game. All editing builds on that.

Common value forms inside objects:

| Form | Meaning |
|---|---|
| `[u32 1][u32 0][u32 table hash][u32 key hash]` | Text reference. `0xdd6757f2` = the FURNITURE table. 15,908 of 16,020 text refs use this form. |
| `[u32 id][u32 flag][u32 0]` | Asset reference in `fnas` |
| `name\0` padded to 4 | Strings are NUL-terminated and padded by their length |

The object id hash is unknown: CRC32, FNV, djb2, sdbm and h31 over names and GUIDs all fail. New objects take free
ids from `IdAllocator`.

**Hashes the game uses (all `KeyHash`: `h = h*31 + c` over the lower-cased string):**

| Input | Where |
|---|---|
| Text key, e.g. `FE_NEW_GAME` | `HTXT` entry hash (53,819 of 53,820 match). An invented hash crashes the game before the title screen. |
| Table name, e.g. `ROOM` | `HTXT` header +12 |
| Texture path after `graphics`, `/` separators, no extension, e.g. `/objects/base/barracks/bunks/bunk_tier_1_colour` | `.ts` entry and `MARE` texture slots |
| `data/graphics/gui/...` path, no extension | GUI icon keys in `fntr`, `rtrp`, `rctt`, `felr` |
| Model name | `COMA` references, manifest model group, `HSKE` |
| `<stem>.base`, `<stem>` | Lair region (`rmlr` +83), lair id (`bsnf` +17) |

### 4.4 Packages (`rpkg`)

A manifest (`<name>.asr`) holds `FNFO`, `RSFL` and one `rpkg` data object. Its outer property `0x12` starts with an
18-byte profile:

```
u32 kind         1 = required, 3 = managed
u32 entitlement  0 = base game; a per-DLC hash on paid DLC
u32 genius       managed genius_* packages only
u8[6] flags
```

Then come length-prefixed `u32` id arrays, addressed by property path:

| Path | Lists |
|---|---|
| `18.0.1.0` | Data objects the package provides |
| `18.0.1.1` | `fnas` ids |
| `18.0.1.2.1.N` | Typed asset groups (`.6` models, `.4` animations, `.7` sound samples in furniture's layout) |

After the arrays comes one byte, **`deferred`**: 0 loads the content at boot, 1 at game start. Then `P2(00)`.

Load order: at boot the engine reads every manifest and loads the content of the six `deferred = 0` packages
(common, characters, furniture, mapregions, frontend, dlc_recruitablesuperagents). At game start it reads the
manifests again and loads every package's content in a fixed order, DLC before the packages it builds on. A
package can refer to another package's objects and assets. **A dev slot package must have `entitlement = 0` and
`deferred = 1`.**

The content file (`<name>_Content.asr`) holds `FNFO`, `RSFL` (an empty one works), and the objects and assets. Each
content package has exactly one `fntr` table, and the game merges the tables of all packages. The `.ts` next to it can be an
empty archive (`"Asura   "` + 4 zero bytes).

### 4.5 Text (`HTXT`)

`text\pc\<table>\<table>.asr_<lang>`, 32 tables in the base game, including the dlc1xx ones.

```
u32 version (4), u32 0, u32 count, u32 KeyHash(table name), u32 text_bytes, u32 0
count x { u32 KeyHash(key), u32 units (UTF-16 units incl. NUL), u16 text[units] }
key table: name\0 padded so the field is a multiple of 4, u32 size, keys\0... in entry order
```

- Entries are not sorted. `TextTable.Add` hashes the key and refuses duplicates and collisions.
- Inline markup (icons, value slots, colour spans) is U+E003…U+E004 sequences. ModKit keeps it as it is, and the
  ColdWarArt sheets write it as `{1}..{n}`.
- The game loads only the tables that `text\pc\localisation.asr` lists. Text inside a package, as a `.asr_en`
  companion or an inline `HTXT`, is **not** read.
- Subtitles are ordinary text: a `DLLN` line id in `misc\Common.asr_en` is the key of its subtitle, mostly in table
  `character`.

### 4.6 Embedded files (`RSCF`)

```
u32 f0, u32 f1, u32 version, u32 flags, u32 size
path\0, filler up to a 4-byte boundary of (20 + path + NUL)
data[size]   at the very end
```

| `f0` | Holds |
|---|---|
| 0 | Texture (DDS, even when the path says `.tga`) or sound (RIFF WAV) |
| 8 | Mesh geometry (4.7) |

Texture headers: `F1` is 2 or 0, version 2, flags 0 or `0x2004020`. GUI textures all use flags `0x2004420`.

**Textures.** GUI textures are DX10 DDS, BC7 (DXGI 98), no mips. Model textures have full mip chains: colour maps
BC7 sRGB (99), masks BC4, normal maps BC5, effects BC7. `Dds.Decode` reads BC1/3/4/5/7. `Dds.EncodeLike(original,
rgba)` writes the original's format, mip count and header. You can replace a texture with PNG or JPG; it is
resized and encoded to match the original.

**Sounds.** Sound banks are MS-ADPCM WAV at about 48 kHz. Replacements can be 16-bit PCM WAV (confirmed).

**Icons.** A furniture item's build-menu icon is `data\graphics\gui\icons\furniture\<icon name>.tga`, found through
the record's icon key (4.12).

### 4.7 Models and meshes

A model is a run of chunks: `HSKN` (hierarchy, one material hash per face), `HSKL` x5 (`L1#name`…), `HMPT`, `HSBB`
(bounds), `RSCF` x6 geometry (`name`, `l1#name` … `l5#name`, `f0 = 8`), and `HSKE` `[1][0][name hash]` to close it.
Manifest group `18.0.1.2.1.6` lists model name hashes.

Geometry `RSCF` payload (`MeshGeometry`):

```
u32 submeshes, u32 verts, u32 indices, u32 triangles
per submesh 20 bytes: [material hash][u32 0][index count][u32 0][u32 group 1..5]
f32 scale x3, f32 min x3
verts x 48 bytes:
  +0  u16 x3 position = min + q/65536 * scale   (+6 = 0xffff)
  +8  tangent, +12 bitangent, +16 normal: unorm 10:10:10:2, (v - 512) / 511
  +20 0x20080200, +24 half2 UV0, +28 half2 UV1, +32 0xff, +36 12 zero bytes
u16 indices
```

The game's up axis is **-Y**. Assets export and import OBJ (V flipped, `usemtl mat_<hash>` per submesh), up to
65,535 vertices. `HSBB` is `[1][0][name][u32 n][n x (xmin xmax ymin ymax zmin zmax)][u32]`; replacing LOD0 updates
a single-box `HSBB` that matched the old mesh. ModKit doesn't update `HSKN` per-face data.

### 4.8 Materials (`MARE`)

```
u32 0x33, u32 0, u32 count, u32 3, u32 n
n x 1028-byte blocks (unknown, look like lookup tables)
count x [u32 material hash][u32 len][len bytes]
```

Furniture records are 342 bytes, with texture hashes at +12/+16/+20 (colour, normal, metal). A material hash is not
the h31 of any name found; ModKit treats it as an arbitrary key. Materials are shared across packages, and
`misc\common.asr` holds 962 of them. A `MARE` with `n = 0` is valid.

### 4.9 Texture streaming (`.ts` / `TXST`, `.pc_textures`)

`.pc_textures` blobs are uncompressed containers of `RSCF` DDS. A `.ts` table says where each streamed texture a
package uses is:

```
u32 version, u32 0, u32 blob count, u32 entry count
blob names (NUL, padded to 4)
entries: [u32 KeyHash(texture path)][u32 data offset in blob][u32 data size][u32 flag][u32 blob index][u32 hash2][u32 flags]
```

The offset points at the DDS data inside the blob's `RSCF`, not at the chunk header, so a blob can't be resized in
place. ModKit writes replacements to its own blob `textures\eg2modkit.pc_textures` and repoints every `.ts` entry
for that texture name: it appends the blob name and rewrites the index, offset and size. The engine opens the new
blob and doesn't check `hash`/`hash2` (confirmed). A texture no `.ts` references can't be replaced this way.

### 4.10 Sound banks (`*.pc.sounds`)

An uncompressed container of `RSCF` chunks, each a RIFF WAV. It gets a whole-file patch, kept uncompressed.

### 4.11 Streamed sounds (`*.pc.streamsounds`)

```
"Asura   " "ASTS" u32 (file length - 12), u32 2, u32 0, u32 n, u8 0
n x [name NUL-padded to 4 from its start][u8 flag][u32 size][u32 absolute offset]
WAVs back to back, then 4 zero bytes
```

`sounds\streamingsounds.ssm.pc.streamsounds` is a table-only copy of `streamingsounds.asr.pc.streamsounds` (byte
+28 = 1, offsets pointing into the store). Replacing a sound rewrites both. 531 names appear twice with identical
data, and a replacement replaces both copies. Music is stereo MS-ADPCM (block 76), dialogue mono (block 22).

### 4.12 Furniture (`fntr`, `fnas`)

```
fntr: u32 7, u32 0, u32 count, u8 1, count x { u8 1, property 0x8000009b (record) }
```

Record payload, from the start:

| Field | Layout |
|---|---|
| Name | `name\0` **padded to a multiple of 4**. Everything after is laid out from that boundary; a rename that doesn't re-pad hangs the loader. `FurnitureRecord.Name` re-pads. |
| Texts | 3 text refs: display name, description, plural (205 of 209 base records) |
| Footprint | `[u32 n]` + n cells, prop `0x10` (44 bytes: `[u32 (y<<16)\|x][u32 2][flags]`), a row-major W x H rectangle from (0,0) |
| Cell bytes | [0] kind (2 solid, 0 open, 1 door); [36] = 2 keep clear; [20]/[24]/[28]/[32] back/right/front/left edge (meaning not confirmed) |
| Gold cost | The u32 right after a pi float (`db 0f 49 40`), about 110 bytes in |
| Icon key | The u32 just before the first `0x80000001` header after the text block = KeyHash(`data/graphics/gui/icons/furniture/<icon name>`). A random value shows a white square. |
| `fnas` id | 1:1 with the item, followed by the owning package's id |
| Job slots | prop 1/5/1: `[u32 n]` + n prop `0x11` (job link, stand points, slot name such as `Locker_Tier_1`) |
| Rooms | The room types the item may stand in (none = anywhere) |
| Links | `rjob`, `rtag` and `trpa` ids |

A slot payload is `[0][job id][0 x4][1][n points]`, where each point is prop `0x80000004` `[2][2][1][2][0][x][height][z]`
in tile units. Locker capacity is its slot count (Lockers: 3, confirmed).

`fnas` (art binding) is `u32 0, u32 0, u32 fnas_id` + asset refs. The art chain is record → `fnas` id → `COMA` (key
= fnas id) → model name hash → `HSKN`.

### 4.13 Jobs (`rjob`) and traits (`rtrt`)

- `rjob` prop `0x12` (219 bytes) opens with 3 groups of 10 floats: **Smarts, Vitality, Morale**. Float 0 of each is
  the rate (positive restores, negative drains).
- Who may do a job: prop 1 = `[u32 n][u32 key]` prop `0xE`… The key is a minion type or another character kind.
  Settings byte 77 = 1 means allowed. With no list, anyone may do it.
- A trait (`rtrt`, prop `0x13`) is a list of components `[u32 1]["name" padded][u32 kind][settings]`. Kinds: 0
  condition, 3 stat adjust, 4 damage, 6 salary mod, 7 on spawn/max stat, 8 movement/disguise, 12 armour, 19 on
  attack, 22 ignore tag.

Minion type hashes come from `mtex` (`Requirements.MinionTypes`, 17 types including the robots). Worker is
`0xd162537e`.

### 4.14 Research (`rtrp`, `rttr`) and engineering (`rctr`, `rctt`)

- `rtrp` starts with prop `0xf`: name text ref @+33, description @+49, **f32 research time @+77**, node icon key
  (+65 in Larger Storage Bays). Rewards are scripts (4.18).
- Requirement lists (`Requirements`), inside the first prop: minion entries prop4 `[type][count][3][u8 1]`, furniture
  prop1 `[fnas][count]`, costs prop1 `[rcns][amount]`, unlocks `[n][fnas…]`. **An empty furniture list makes a
  project never progress**, so the builder refuses one.
- `rttr` (research tree), prop `0xa` → `0x4`: links `n + (from, to, 0)`, the same links reversed, nodes `n +
  (index, out links, in links, rtrp id, u32 column (1-based), u8 available at start, u32 row)`, `u32 cols, u32
  rows`, grid (node index per cell, row-major, -1 empty), colours. Game trees have at most 5 tiers and 7 rows,
  links go forward within a row, and a node has at most one prerequisite. A tree that breaks this crashed the game,
  so `ResearchTree.Check` enforces it.
- `rctt` (engineering tree): fixed header to +130 (+45 name, +49 selector icon key, +53 background key), then links,
  nodes with free placement (`[item or 0 = junction][u8 root][u32 col][u32 row][f32 dx][f32 dy]`), and a grid.
  ModKit routes the connector lines the way the game does.

### 4.15 Resources (`rcns`)

`rcns` objects are the consumables. The max you can hold is a u32 at **+41**: Intel `0x1cb32d8d` and Tech
`0x17a283c0` are 99. It seeds new games only; saves store their own.

### 4.16 Objectives (`robj`)

The first prop (`0x20`) → child `0x1` = `[u32 n]` + n task props (key `0x8000002d`). A task is self-contained: its
texts, condition blocks, and `[1][room id]` (its `_Activate` step), so tasks can be copied between objectives. The
game runs tasks one at a time, and a task's modifier starts when it becomes current. Title text ref @+37 (key @+49).
Other objectives' reward scripts start an objective (node type `0xd4e0f0bc` = "start objective X").

### 4.17 Schemes (`rscm`, `rspl`, `rsdv`)

`rscm` first prop `0x38`:

| Part | Meaning |
|---|---|
| raw6 f32 | Duration in seconds |
| raw16[1] | Payout interval of "while running" schemes |
| raw16[3] | Heat gain to region (negative = heat reduction) |
| minion list | Groups of alternatives `[n] + prop4 [type][count][category][u8 1]`; the game picks one group per offer |
| raw8 f32 | Offer expiry (-1 = stays) |
| child [9] | Launch cost `[rcns][amount]` pairs (the shown Intel price of heat schemes doesn't come from here) |

Minion categories: Worker 0; Guard types 1; Valet types 2; Technician types 3.

`rspl` is a scheme pool: after the 24-byte header, `[u32 0x80000002][u8 0][u32 8 + 8n][u32 n][n x (u32 rscm, u32
weight 1)][u32 ?]`. Region upgrade rules (`rrtl`) and objective steps refer to pools. `rsdv` names world-map schemes
by matching `rtag`s.

### 4.18 Scripts (flowgraphs)

Scripts live inside objects: `rscm`, `robj`, `rtrp`, `rrtl`, `rant` and `rctr` hold 2,854 graphs.

```
name\0 (pad 4)  "FlowGraph/<template>\0"  [u32 1][u32 node count]  nodes...
node:  [type hash][version words / size blocks][00 ff ff 00][u32 3][u32 node id] ... settings, pins
link:  [link id][from node][from pin][to node][to pin][00 ff ff 00] + 25 bytes   (49 bytes, stored under its output pin as [u32 count][u32 ?] + links)
pin:   [pin hash][u32 1][u8 0][name\0 pad 4]
value: "Value\0" + 31 bytes -> [u8 1][u32 4][u32 type][value]   type 2 int, 3 float, 4 bool (u8)
```

Node 0 is Start and node 1 is End in every graph. Node and pin type names aren't stored; `FlowGraph.Names` learns
them from each type's most common strings. Nodes sit inside size-prefixed property blocks, so a structural edit is
a byte splice plus a size fix in every block around it (`FlowGraphEdit`). A script alone in a sized block (prop
`0x1`, payload `[1][5]name…`) can be swapped whole for a copy of another.

### 4.19 Lair maps (`envs\basedefinitions\<lair>.base`)

An uncompressed container: `bsnf`, one 10-14 MB `ENTI` (the grid, id `0x989680`), `ARNM`, `dtvs`. The ENTI is a
property tree; block keys differ per lair (`1d/44/6` in Crown Gold, `1e/45/7-8` elsewhere).

- `bsnf` +17 = the lair id = KeyHash(file stem). After it come the world-map regions (`rmlr`) with 5 (key, f32)
  pairs each.
- **Floor**: raw `u32 floor, u32 width, u32 height`, then width x height cells, row-major.
- **Cell** (prop `0x33` in Crown Gold, `0x34` elsewhere: the prop key is the serializer version): `u32 flags @0, u32
  type @4, u32 row @8, u32 floor @12, u32 column @16`, then a variable tail.
- **Cell type is the room or rock.** Rock tiers 1-4: `0x497004c7`, `0x48f78ca2`, `0x48f25963`, `0xd55a674f`. Gold seam
  `0x48f218d8`, edge rock `0x0cf2f3d8`, lift `0x080b0101`, outside `0x430bd860`. Rooms: Corridor `2a5b912a`, Power
  Station `ecacefcf`, Barracks `dddf7f89`, Mess Hall `29eb7171`, Vault `e58aa2f2`, Control Room `a3275078`, Armoury
  `731de7c9`, Prison `c276dccb`, Laboratory `6f00f6f3`, Training Room `627743f5`, Archive `728da182`, Infirmary
  `a92c5365`, Staff Room `d4db647b`, Casino/Hotel `e4cbb274`, Inner Sanctum `6b9b4ad3`, Workshop `d36ac811`, Test Chamber
  `bac20f8c`.
- A dug cell takes the form the game writes when it excavates: a new type, flags bit 9 off and bit 4 on, and the tail
  `[u32 region][03000000 810014 0A000000 C7047049 D0101D7E 00000000 01]`. Flag bit 3 is not "dug".
- **Pre-placed objects**: a list in the grid block (key `0x64` = version 100 in Crown Gold, `0x68` = 104 elsewhere):
  `[u32 count][u8 1][u32 id]`, then `[u8 1][u32 id]` before each one. Payload: `u32 flags, u32 fnas, row, floor,
  column, i32 facing x, y`, covered cells, footprint, rotation. A copy must use a template of the same version (a
  mismatch crashed the game); `anyVersion` places a v104 record in Crown Gold's v100 list (confirmed).
- **AI block** (grid `/1e/1b`): `[u8 has groups]`, then agent squads (`[01][u32 id][u32 1]` + prop `0x3eb`) and
  civilian groups. A squad's `[6]` block is the area it has already searched: all of it and the squad leaves, none
  of it and its members can't be killed after one hit. `Agents.KnownRadius = 3` works.
- Characters are `ENTI` `[u32 1][u32 0][u32 id][u32 kind]` (0x8003 minions, 0x8004 other characters, 0x8007 their
  companion, 0x800b island vehicles). Position @80 (x, height, z); grid row = x, column = z. Floor heights: 0 = 20,
  2 = 0, 3 = -14, 4 = -28, 5 = -52.

The game loads maps through its save loader, which has no `.asrpatch` lookup, so the DLL redirects the open. Only
new games read the map.

### 4.20 Islands (`envs\<lair>.pc`)

A compressed container. Its `RSFL` is the build manifest of sections (`.sky`, `.asr` physics, `.lit`, `.snd`, `.nav`,
`.cut`, `.ent`, `.pfx` effects, `VehiclesData.asr`); unlike `common.asr`, its offsets don't index the payload, so
resizing chunks is safe.

`INST` (version 18):

```
[18][0][n]  n x 64-byte instances: f32 position x3, 3x3 rotation*scale as 12 half floats (pad per row), u16 group @60
u32 group count + 64-byte groups ([instance count][part count][first part]...)
u32 part count + 64-byte parts ([material hash]..., f32 min @40, f32 extent @52)
culling trees: u32 tree count; tree 0 = u32 nodes, u32 internal-node count, 128-byte nodes
  (6 x 4 f32 child boxes, 4 refs index<<8|kind (0 node, 1 instance, 0 = empty), 16 zero bytes)
```

Every instance is exactly one leaf. Hiding zeroes the matrix. Moving and copying update the leaf and widen the
boxes above it. Instanced geometry is the embedded file `inst (static)`: `[u32 verts][u32 indices][u32 instances]`,
24-byte vertices (u16 x3 position = part min + q/65535 x extent, half2 UV @8, UV2 @12, normal 10:10:10 @16), u16
indices.

The sky is ordinary textures (`specialfx\skybox\skybox_island_1.tga`, `specialfx\fog\fog_island_1.tga`). `FOG `
(183 bytes) holds f32 tint RGBA @21, start @37, end @41, haze RGB @126 and scatter RGB @150; the haze colour is the
blue over the island. `PHEN` is a standalone physics mesh, so hidden scenery still collides with ragdolls. Minions
walk on the lair grid, not on `PHEN`.

### 4.21 New lairs and the island select

- The exe registers every `*.base` in `envs\basedefinitions\` at startup (and `JustAPlane.base` as a fallback).
- The island-select entry is a `felr` object in `frontend`: picture keys, point-of-interest texts, name/desc keys
  `LAIR_<STEM>_NAME/_DESC`, and a ref to the island's `rmlr` (felr +280 on Caine Key, +284 on the others; ModKit
  finds the fields by value).
- The region `rmlr` (mapregions) picks the map: **+83 = KeyHash("<stem>.base")**, +45 = the name key, +100 = globe
  slot. A new lair needs a new `rmlr`, and that region must replace the source's in the copy's `bsnf` list.
- The game then fetches per-lair settings by the lair id from `misc\common.asr` `BLUE` (4.22). Two ways:
  - `keepId: true` keeps the source's id and settings, and the DLL aliases the island files by name while the new
    lair or one of its saves is loaded (it reads the region in the save's `bsnf`).
  - `keepId: false` writes a settings entry under the new id, naming the new island (confirmed). Only one slot is big
    enough, so only one own-id lair is possible for now.

### 4.22 Lair settings (`misc\common.asr` `BLUE` #25, chunk 27)

```
u32 1, u32 0, u32 1, u32 root class 0x614510c0, u32 count (24)
24 objects back to back, no byte lengths: [id][0x0D][id][class][member count] + members
string member: [key][3][0x55f89b99][0][4][len][chars]   (no terminator)
```

22 entries are class `0x6431F764` settings objects. Each lair's entry holds its island path, e.g.
`Lair\Lair_Tropical_03_Default\Lair_Tropical_03_Default`. **No entry may move or change size.** Moving entries
broke the menu art; replacing entries in place worked. A new lair's entry takes the place of two adjacent dev test
level entries (19 + 20, 4,853 bytes), padded to that size. The changed compressed block goes in through the
DLL's `file_block`.

### 4.23 Saves (`slotN.sav`)

```
"Asura   " "AsuraZlb"  u32 compressed length  u32 (raw length - 16)  zlib stream -> an ordinary Asura archive
```

It holds `bsnf` (with the lair's region), `DYMG`, `DLIG`, `SMXG`, `ATIG`, `ARNM`, `dtvs`, `stsy`, `ttsy` and ~500
`ENTI`. It is all hashes and numbers, with no names. The grid is the same format as a `.base`, and current saves
store every object as v104, which is where furniture and agent templates come from. `slot0` is a raw 8 KB property
block zero-padded to 2 MB. `SaveFile` loads and saves round trip.

### 4.24 Runtime config (`bin\eg2modkit.cfg`)

INI-like, one `[section]` per entry, `#` comments. `ModBuilder.BuildRuntime` writes it from every mod's
`runtime` list, plus generated entries.

```ini
[minion_hard_cap]
pattern = 8B 3D ?? ?? ?? ?? 8B 8B ?? ?? ?? ?? 03 0D ?? ?? ?? ?? 03 8B ?? ?? ?? ?? 3B F9 0F 43 F9
rel = 2          ; rel32 at match+at+rel ...
len = 6          ; ... of an instruction this long -> the global's address
type = u32       ; u8 u16 u32 i32 u64 f32 f64
value = 1000
```

| Key | Meaning |
|---|---|
| `pattern` | Bytes to find, `??` = any. It must resolve to one address. |
| `at`, `rel`, `len` | Instruction start from the match, and its rel32 operand |
| `deref`, `offset` | Follow a pointer, then add an offset (struct field) |
| `skip_if` | Don't write while the current value equals this |
| `kind = hashmap_record` | `hit, id_at, mirror_at, offset`: a per-save record found in the game's resource hash map by matching the HUD mirror [current][max] (Intel/Tech caps in saves) |
| `kind = table_lookup` | `id_at, alt_at, alt_if, call_at, offset`: a record the game looks up by id (the game-setup preset; salary rate) |
| `kind = island_alias` | `value = source stem, stem = new stem, region = 0x…`: open the new lair's island files in place of the source's |
| `kind = file_block` | `value = game file, offset, source`: bytes from `bin\eg2modkit_blocks\<source>` go into reads and mapped views of that file |
| `kind = trace_lair` | Research only: logs the code that reads a lair's record |

The DLL retries unresolved patterns for 2 minutes after start (the protector unpacks the exe first), then writes
every value again each second. Presets are in `RuntimePresets.All`, checked against both exes (the salary rate against DX12 only).

### 4.25 Settings and other JSON

- `settings.json`: `gamePath, modsFolder, language, classicLook, advancedTools, order[], enabled[]`.
- Stamps (`MapStamp`): a rectangle saved as `MapEdits` relative to its corner. Placed objects keep their record as the
  template, so Crown Gold stamps only paste into Crown Gold.
- `Eg2.ModKit\data\agents.json` (embedded): bundled agent templates (Soldier, two Investigators, Diver) taken from
  saves.

## 5. Rules every generated file follows

- Parse again and check the edit landed. The self-test and install verifier must pass.
- `FNFO` first, with its payload size in sync.
- A renamed `fntr` record is re-padded to 4.
- New text keys go in through `TextTable.Add`; never invent a hash.
- `COMA` chunks are sorted by key.
- New ids collide with nothing in the game; they come from `IdAllocator`.
- Dev slot manifest: `entitlement 0`, `deferred 1`, the `RSFL` entry size equal to the `rpkg` size, and new objects
  listed in `18.0.1.0` and new `fnas` in `18.0.1.1`.
- An `.asrpatch` is a complete replacement of the file it patches.
- In an archive whose `RSFL` holds offsets (`misc\common.asr`), nothing before a resource changes size.
- Every research or engineering project keeps at least one minion and one furniture requirement.
- Research trees keep to 5 tiers, 7 rows, forward links and one prerequisite per node.
- A map object copies a template of the same serializer version as the map.

## 6. Not known yet

- The object id hash, and the real names of `rtag`s (not shipped).
- Most of each object's raw words, per type (about 90%).
- Animations (`HCAN`, `FAAN`) and character skeletons.
- `ENTI` component layout, including where island effects are placed.
- How the hidden Mods menu (`FE_MODS`, `FE_MOD_CONFLICT` in `menu.asr_en`) is gated.
- The genius banner's "M" logo source.
- Furniture durability in placed objects, and the power-cut modifier outside its story.
- Why placed agents become unkillable when a real raid arrives (probably the missing world-state raid record).
