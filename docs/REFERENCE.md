# Evil Genius 2 file reference

File locations and formats for Evil Genius 2 (Rebellion, Asura engine), plus the files ModKit reads and writes.
Offsets are bytes and integers are little-endian. `u32`, `i32` and `f32` take 4 bytes.

## 1. Locations

### 1.1 Game folder

Steam installs the game to `steamapps\common\Evil Genius 2` inside a Steam library. `libraryfolders.vdf` in the
Steam folder lists the libraries. On Linux, Steam keeps them under `~/.steam/steam`, `~/.local/share/Steam` or
`~/.var/app/com.valvesoftware.Steam/.local/share/Steam`, and a Wine program sees those paths on drive `Z:`.

| Path | Contents |
|---|---|
| `bin\evilgenius_dx12.exe`, `bin\evilgenius_vulkan.exe` | The game. A protector packs both exes. |
| `misc\packages\required\<name>.asr` | Package manifest (`rpkg`) |
| `misc\packages\required\<name>_Content.asr` | Package content: data objects, furniture, models, textures, materials |
| `misc\packages\required\<name>_Content.ts` | Texture streaming table (`TXST`) |
| `misc\packages\required\<name>_Content.asr.pc.sounds` | Package sound bank (optional) |
| `misc\packages\managed\…` | Same layout for DLC, genius, frontend and sandbox packages |
| `misc\packages\development\` | Four package slots the engine probes and ships empty |
| `misc\common.asr` | 255 MB shared archive: materials (`MARE`), lair settings (`BLUE`), dialogue lines. The game memory-maps it. |
| `misc\Common.asr_en` | Dialogue line table (`DLLN`) |
| `text\pc\localisation.asr` | Text tables to load (`HTPR`) |
| `text\pc\<table>\<table>.asr_<lang>` | One UI text table (`HTXT`), e.g. `text\pc\menu\menu.asr_en` |
| `gui\gui.asr`, `gui\main.asr`, `gui\splash.asr` | GUI layouts (`GUAT`) and GUI textures |
| `<file>.pc.sounds` | Sound bank beside an archive: `sounds\gmsnd.asr.pc.sounds` (UI), `misc\common.asr.pc.sounds`, packages, islands |
| `<file>.pc.streamsounds` | Streamed dialogue and music (`ASTS`) |
| `textures\*.pc_textures` | Streamed texture stores, up to 4.29 GB each: `theblob`, `theblob1`, one per DLC, one for the arctic island |
| `envs\basedefinitions\<lair>.base` | Lair map: diggable grid, pre-placed furniture, AI block |
| `envs\basedefinitions\*.scenario` | Save-format levels (only `benchmark.scenario` holds a level) |
| `envs\<lair>.pc` | The island outside the lair: scenery, meshes, textures, sky, fog, physics, effects |
| `envs\<lair>.ts`, `envs\<lair>.pc.pc.sounds` | The island's texture table and sounds |
| `fmv\*.webm` | Videos |
| `parameters\parameters.asr` | GUI parameters (`GUAP`) |
| `stat\stat.asr` | Steam achievements |
| `dlcdata\<app id>_<name>.txt` | One per DLC; the file holds only the DLC's Steam app id as text, e.g. `1531608` (oceans) |
| `misc\common.asr_wav_en`, `sounds\gmsnd.asr_wav_en`, `sounds\streamingsounds.ssm` | 36-byte stubs (`FNFO` and nothing else). The data sits in the `.pc.streamsounds` / `.pc.sounds` beside each. |

Lairs: `lair_tropical_01_default` (Crown Gold), `_02_` (Montañas Gemelas), `_03_` (Caine Key),
`lair_arctic_01_default` (Icicle Point, Oceans DLC).

### 1.2 Saves

`%LOCALAPPDATA%\Evil Genius 2\PC_ProfileSaves\<steam id>\slot<N>.sav`. `slot0` holds the profile, slots 1-10 the
manual saves and 11-20 the autosaves. The exe caps the count at 20.

Under Proton the game runs in its own prefix, so its saves sit in
`steamapps\compatdata\700600\pfx\drive_c\users\steamuser\AppData\Local\Evil Genius 2\PC_ProfileSaves` in the same
library as the game. 700600 is the game's Steam app id.

### 1.3 ModKit's files

| Path | Holds |
|---|---|
| `%APPDATA%\Eg2ModKit\settings.json` | `gamePath, modsFolder, language, classicLook, advancedTools, order[], enabled[]` |
| `Documents\Eg2ModKit\Mods\<id>.json` | One mod definition (section 3) |
| `Documents\Eg2ModKit\Mods\<id>.assets\` | That mod's replacement files: WAV, DDS, `.mesh`, OBJ, pictures |
| `Documents\Eg2ModKit\Stamps\*.json` | Saved map rectangles as relative `MapEdits` |
| `<app folder>\runtime\xinput1_4.dll` | The runtime proxy the builder installs |
| `<installer folder>\Mods\*.json` + `.assets` | The player installer's mods |
| `<installer folder>\Eg2ModInstaller.json` | The player installer's game path, language and ticked ids |
| `<game>\eg2modkit.installed.json` | Install record (section 2.3) |

### 1.4 What an install writes into the game folder

| Path | Contents |
|---|---|
| `misc\packages\development\e3data.asr`, `e3data_Content.asr`, `e3data_Content.ts` | New furniture, objects, textures and overrides |
| `<game file>.asrpatch` | Whole-file replacement of that file |
| `text\pc\eg2modkit\eg2modkit.asr_<lang>` | Texts for new furniture and objects |
| `textures\eg2modkit.pc_textures` | Replaced streamed textures |
| `envs\lair_<stem>.*`, `envs\basedefinitions\lair_<stem>.base` | New lairs |
| `bin\xinput1_4.dll`, `bin\eg2modkit.cfg` | Runtime proxy and its config |
| `bin\eg2modkit_blocks\*.bin` | Replacement compressed blocks for memory-mapped files |
| `bin\eg2modkit.log` | The proxy's log, written while the game runs |

## 2. Ways into the game

### 2.1 Development package slots

At startup the engine opens `misc\packages\development\{datafordevelopmentfeatures, e3data,
placeholder_objectives, temp_package}.asr` and skips missing ones. A package in a slot needs `entitlement = 0` and
`deferred = 1` (4.4) and then loads at game start after the base content. It can hold:

- new furniture (an `fntr` record with its `fnas` and `COMA`, plus models, materials and textures),
- new data objects of any class under new ids,
- overrides: an edited copy of a game object or furniture record under the same id. The later copy wins,
- new textures.

### 2.2 `.asrpatch`

For each file it opens, the engine looks for `<full name>.asrpatch` beside it and reads that file instead. A patch
replaces the whole file and nothing merges, so a text patch with one entry blanks the rest of that table.

The frontend package loads before the development slots, so changes to it need a patch. The map loader (`.base`,
`.scenario`), island loader (`envs\*.pc`) and video player never look for patches.

Never patch `misc\common.asr`. The game memory-maps it, and a patch breaks the menu art.

### 2.3 Runtime proxy (`bin\xinput1_4.dll`)

The game imports `xinput1_4.dll` (ordinals 2, 3 and 4: `XInputGetState`, `XInputSetState`,
`XInputGetCapabilities`), so a DLL with that name in `bin` loads first. The proxy forwards its 14 exports to the
system copy and leaves game code unchanged. It:

- finds data globals by byte pattern and rewrites them each second,
- hooks `CreateFileA/W`, `GetFileAttributes(Ex)W` and CRT `fopen_s` to send `.base`, `.scenario`, `envs\*.pc` and
  video opens to their `.asrpatch` (a zero-byte video patch reads as "file not found", which skips the video),
- opens a new lair's island files in place of its source lair's,
- swaps replacement blocks into reads and mapped views of a file.

It reads `bin\eg2modkit.cfg` (4.28) and logs to `bin\eg2modkit.log`.

Wine and Proton load their own `xinput1_4` unless the game's Steam launch options say
`WINEDLLOVERRIDES="xinput1_4=n,b" %command%`.

### 2.4 Install record

`<game>\eg2modkit.installed.json`:

```json
{
  "installedAt": "2026-09-27T12:00:00",
  "mods": ["mod-a"],
  "installed": [{ "id": "mod-a", "name": "Mod A", "version": "1.0.3", "fingerprint": "A1B2C3D4E5F60718" }],
  "files": [{ "path": "misc\\packages\\development\\e3data.asr", "size": 1234, "sha256": "…" }]
}
```

`fingerprint` holds the first 16 hex digits of a SHA-256 over the mod's JSON and its `.assets` files. Uninstall
deletes the listed files whose hash still matches.

### 2.5 New ids and texts

New object ids come from the FNV-1a hash of `"eg2modkit:" + key`, with `#n` appended on a collision, on 0 or on
0xFFFFFFFF.

| What | Key |
|---|---|
| New object | `<mod id>/obj/<Key>` |
| New furniture's `fnas` | `<mod id>/<item id>/fnas` |
| New furniture's icon key | `<mod id>/<item id>/key` |
| Development package ids | `package`, `aux` |

New texts go in table `eg2modkit` under keys such as `MOD_<MODID>_<ITEMID>_NAME`. A text reference names its table
(4.3), so a reference to a new text points at `KeyHash("eg2modkit")`.

## 3. Mod file (`<id>.json`)

camelCase JSON with nulls left out. Each list is optional. `id` takes `[A-Za-z0-9_-]`; new furniture ids and new
object keys take `[A-Za-z0-9_]`. Other edits name a new object as `"@Key"`.

```json
{
  "id": "example", "name": "Example", "version": "1.0.0", "author": "", "description": "",
  "newFurniture": [{ "id": "Big_Bunk", "donor": "Bed_01_Bunk", "cost": 666, "displayName": "Big Bunk" }],
  "furnitureEdits": [{ "name": "Door_01", "cost": 250 }],
  "textEdits": [{ "table": "menu", "key": "FE_NEW_GAME", "text": "Begin" }]
}
```

| List | Entry | Builds into |
|---|---|---|
| `newFurniture` | `id, donor, cost, displayName, description?, ownArt, artFrom?, textures[{texture, source}]` | Development package |
| `newObjects` | `key, package, tag, source "0x…", into?, texts[{offset, text}], edits[FieldEdit]` | Development package, or the frontend patch with `into: "frontend"` |
| `furnitureEdits` | `name, cost` | Override record |
| `fieldEdits` | `package, tag, object, offset, type u8/u32/i32/f32, value, expect, note?` | Override object. `object` = record name (`fntr`) or `0x` id; `expect` = the game's bytes as hex. |
| `textEdits` | `table, key, text` | Whole-table patch |
| `runtime` | Runtime entry (4.28) | `bin\eg2modkit.cfg` |
| `assets` | `file, tag, name, occurrence, source, island?` | Patch of the file holding the asset, or ModKit's texture store |
| `newAssets` | `name, source, package?` | New texture |
| `skipVideos` | `"fmv/rebellion.webm"` | Zero-byte patch |
| `researchTrees`, `engineeringTrees` | `tree, nodes[{research, column, row, dx, dy}], links[{from, to}], background?` | Override tree |
| `scriptSwaps` | `object, script, from, fromScript, values[FieldEdit]` | A script block replaced by a copy of another object's |
| `graphEdits` | `object, graph?, op remove-node/copy-node/add-link/remove-link, node, link, from, fromPin, to, toPin` | Script structure changes |
| `requirementEdits` | `object, minions?, furniture?, costs? [{ref, count}], unlocks?` | Research or engineering requirements |
| `taskEdits` | `object, tasks ["0x<objective>:<index>"]` | An objective's task list. A copied task carries the field edits made to its source objective. |
| `schemeEdits` | `object, minions? [[{ref, count}]], heat?, costs?, duration?, expiry?` | Scheme values |
| `poolEdits` | `pool, add[], remove[]` | Scheme pool contents |
| `shapeEdits` | `name, width?, height?, slots?` | Furniture footprint and slot count |
| `jobEdits` | `object, types[]` | Who may do a job |
| `listEdits` | `package, tag, object, at, count, order[]` | Reordered, dropped or repeated list entries |
| `mapEdits` | `file, floor, x0, y0, x1, y1, action, tier, room?, item?, facing?, template?, templateKey?, anyVersion, squad?, vehicle?` | `.base` patch |
| `sceneryEdits` | `file, group, groupName?, x0/z0/x1/z1?, action hide/move/copy/mesh, source?, dx, dy, dz` | `.pc` patch |
| `newLairs` | `stem, from, region?, keepId, islandEdits[FieldEdit]` | New `envs\` files, plus a `common.asr` block when `keepId` is false |
| `henchmanBarSlots` | Number, 6-100 (a value, not a list) | `gui\main.asr` patch: more squares in the HUD bar, plate widened 115 per extra slot |

`MapEdit.action` takes `tier`, `dig`, `room`, `gold`, `wall`, `rock`, `remove`, `place`, `character` or `agent`.
X counts columns and Y counts rows.

A `.eg2mod` is a zip holding `<id>.json` and a `<id>.assets/` folder.

## 4. File formats

### 4.1 Container (`.asr` and others)

```
AsuraZbb (compressed)
  char[8] "AsuraZbb"
  u32     comp_total      file size - 16
  u32     raw_total       decompressed size
  repeat: u32 comp_n, u32 raw_n, zlib stream     one block per 2 MiB of payload

"Asura   " (uncompressed; three trailing spaces)
  char[8] "Asura   "
  repeat: char[4] tag, u32 size (including these 8 bytes), body
  a zero tag ends the list; trailing bytes follow
```

- The zlib streams use a 4 KiB window (header `48 89`).
- A one-call inflate returns only the first block, so a reader has to walk the blocks.
- The game reads the decompressed payload, so a rebuilt file can compress differently.
- To change a memory-mapped file, recompress only the changed blocks and pad each to its old compressed size.

### 4.2 Chunks

Uppercase tags mark engine chunks and lowercase tags mark game data. Chunks sit back to back with no padding.

| Tag | Contents |
|---|---|
| `FNFO` | First chunk. `u32 1, u32 flags (0/4/11/15), u32 payload_size (= payload length - 4), u32 8` |
| `RSFL` | Resource list: `u32 ver, u32 0, u32 count`, entries `name\0` padded to 4 + `u32 hash, u32 size, u32 count`. In a manifest, entry 0's size equals the `rpkg` chunk size. In `misc\common.asr` the entries hold payload offsets, so nothing before a resource may change size. |
| `RSCF` | Embedded file (4.6) |
| `HTXT` | Text table (4.5) |
| `HTPR` | One text table name in `localisation.asr`: `[u32 1][u32 0][NAME\0 padded to 4]` |
| `COMA` | Per-item art binding keyed by the u32 at +12. Keep the list sorted by that key; an unsorted list hangs the loader. |
| `HSKN`, `HSKL`, `HMPT`, `HSBB`, `HSKE` | Model, LOD names, unknown, bounds, end of a model run (4.7) |
| `HCAN`, `FAAN` | Animations |
| `MARE` | Materials (4.8) |
| `TXST` | Texture streaming table, in `.ts` files (4.9) |
| `ASTS` | Streamed sound store (4.11) |
| `ENTI` | Entity: lair grid, characters, island entities (4.22) |
| `INST` | Island scenery instances (4.23) |
| `BLUE` | Class-tree data, including lair settings in `common.asr` (4.25) |
| `GUAT` | GUI layout (4.21) |
| `GUAP`, `FONT` | GUI parameters, fonts |
| `DLLN`, `DLEV`, `DLET`, `DLLT` | Dialogue lines, events, small tables |
| `PSKY`, `FOG `, `PLUT`, `PHEN`, `NAV1`, `PBRV`, `OCMH`, `CT*`, `FX*` | Island sky, fog, colour grade, physics mesh, navmesh stub, probe volume, occlusion, cutscenes, effects |
| `bsnf`, `ARNM`, `dtvs`, `stsy`, `ttsy`, `DYMG`, `DLIG`, `SMXG`, `ATIG` | Map and save state |
| `rpkg` | Package manifest (4.4) |
| other lowercase | Data objects (4.3) |

### 4.3 Data objects and properties

Each lowercase chunk holds one data object, and the tag names its class.

```
u32 version     10
u32 pad0
u32 object_id   what other objects and manifests use to refer to it
u32 pad1        usually 0
u32 package_id  the owning package
u32 aux_id      a second, near-unique id
property stream
```

A property is `u32 key (0x8000NNNN), u8 kind, u32 length, payload`. A payload mixes nested properties with raw
scalars, strings and arrays, with no schema. A parser that recurses into anything shaped like a property header and
keeps the rest as raw bytes rebuilds every object byte for byte. The GUI layout chunk (`GUAT`) uses the same encoding.

| Form | Meaning |
|---|---|
| `[u32 1][u32 0][u32 table hash][u32 key hash]` | Text reference. `0xdd6757f2` = the FURNITURE table. |
| `[u32 id][u32 flag][u32 0]` | Asset reference in `fnas` |
| `name\0` padded to 4 | String |

The object id hash is unknown (CRC32, FNV, djb2, sdbm and h31 over names and GUIDs all fail).

`KeyHash` (`h = h*31 + c` over the lower-cased string) produces these hashes:

| Input | Used in |
|---|---|
| Text key, e.g. `FE_NEW_GAME` | `HTXT` entry hash. A hash with no matching text crashes the game before the title screen. |
| Table name, e.g. `ROOM` | `HTXT` header +12 |
| Texture path after `graphics`, `/` separators, no extension, e.g. `/objects/base/barracks/bunks/bunk_tier_1_colour` | `.ts` entries and `MARE` texture slots |
| `data/graphics/gui/...` path, no extension | GUI icon keys in `fntr`, `rtrp`, `rctt`, `felr` |
| Model name | `COMA` references, manifest model group, `HSKE` |
| `<stem>.base`, `<stem>` | Lair region (`rmlr` +83), lair id (`bsnf` +17) |

### 4.4 Packages (`rpkg`)

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

### 4.5 Text (`HTXT`)

`text\pc\<table>\<table>.asr_<lang>`, 32 tables in the base game.

```
u32 version (4), u32 0, u32 count, u32 KeyHash(table name), u32 text_bytes, u32 0
count x { u32 KeyHash(key), u32 units (UTF-16 units incl. NUL), u16 text[units] }
key table: name\0 padded so the field is a multiple of 4, u32 size, keys\0... in entry order
```

- Entries are unsorted.
- U+E003…U+E004 sequences mark inline icons, value slots and colour spans.
- The game loads only the tables `text\pc\localisation.asr` lists. It ignores text inside a package.
- A `DLLN` line id in `misc\Common.asr_en` doubles as the key of its subtitle, mostly in table `character`.

### 4.6 Embedded files (`RSCF`)

```
u32 f0, u32 f1, u32 version, u32 flags, u32 size
path\0, filler up to a 4-byte boundary of (20 + path + NUL)
data[size]   at the end
```

| `f0` | Holds |
|---|---|
| 0 | Texture (DDS, even when the path ends `.tga`) or sound (RIFF WAV) |
| 8 | Mesh geometry (4.7) |

Texture headers: `f1` 2 or 0, version 2, flags 0 or `0x2004020`. GUI textures use flags `0x2004420`.

GUI textures are DX10 DDS, BC7 (DXGI 98), no mips. Model textures carry full mip chains: colour maps BC7 sRGB
(99), masks BC4, normal maps BC5, effects BC7.

Sound banks hold MS-ADPCM WAV at about 48 kHz. The game also plays 16-bit PCM WAV.

### 4.7 Models and meshes

A model is a run of chunks: `HSKN` (hierarchy, one material hash per face), `HSKL` x5 (`L1#name`…), `HMPT`, `HSBB`
(bounds), `RSCF` x6 geometry (`name`, `l1#name` … `l5#name`, `f0 = 8`), and `HSKE` `[1][0][name hash]` to close it.
Manifest group `18.0.1.2.1.6` lists model name hashes.

Geometry `RSCF` payload:

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

Up is -Y. `HSBB` is `[1][0][name][u32 n][n x (xmin xmax ymin ymax zmin zmax)][u32]`.

### 4.8 Materials (`MARE`)

```
u32 0x33, u32 0, u32 count, u32 3, u32 n
n x 1028-byte blocks (unknown, look like lookup tables)
count x [u32 material hash][u32 len][len bytes]
```

Furniture material records take 342 bytes, with texture hashes at +12/+16/+20 (colour, normal, metal). The material
hash matches the h31 of no known name. Packages share materials, and `misc\common.asr` holds 962. A `MARE` with
`n = 0` is valid.

### 4.9 Texture streaming (`.ts` / `TXST`, `.pc_textures`)

`.pc_textures` stores are uncompressed containers of `RSCF` DDS. A `.ts` table locates each streamed texture a
package uses:

```
u32 version, u32 0, u32 store count, u32 entry count
store names (NUL, padded to 4)
entries: [u32 KeyHash(texture path)][u32 data offset in store][u32 data size][u32 flag][u32 store index][u32 hash2][u32 flags]
```

The offset points at the DDS data inside the store's `RSCF`, not at the chunk header. The engine opens any store
the table names and ignores `hash`/`hash2`, so a table can point a texture at a new store.

### 4.10 Sound banks (`*.pc.sounds`)

An uncompressed container of `RSCF` chunks, each a RIFF WAV.

### 4.11 Streamed sounds (`*.pc.streamsounds`)

```
"Asura   " "ASTS" u32 (file length - 12), u32 2, u32 0, u32 n, u8 0
n x [name NUL-padded to 4 from its start][u8 flag][u32 size][u32 absolute offset]
WAVs back to back, then 4 zero bytes
```

`sounds\streamingsounds.ssm.pc.sounds` holds only an `ASTS` table (no data) of 6 music and ambience streams, with offsets into `sounds\streamingsounds.asr.pc.streamsounds`.
`sounds\streamingsounds.ssm.pc.streamsounds` copies the table of `streamingsounds.asr.pc.streamsounds` (byte
+28 = 1, offsets into the store). 531 names appear twice with identical data. Music is stereo MS-ADPCM (block 76),
dialogue mono (block 22).

### 4.12 Furniture (`fntr`, `fnas`)

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
| Temperature output | 4.14 |
| Rooms | Room types the item may stand in (none = anywhere) |
| Links | `rjob`, `rtag` and `trpa` ids |

A slot payload is `[0][job id][0 x4][1][n points]`, each point prop `0x80000004` `[2][2][1][2][0][x][height][z]` in
tile units. A locker's capacity equals its slot count.

`fnas` (art binding): `u32 0, u32 0, u32 fnas_id` + asset refs. The art chain runs record → `fnas` id → `COMA` (key
= fnas id) → model name hash → `HSKN`.

### 4.13 Jobs (`rjob`) and traits (`rtrt`)

- `rjob` prop `0x12` (219 bytes) opens with 3 groups of 10 floats: Smarts, Vitality, Morale. Float 0 of each holds
  the rate (positive restores, negative drains).
- Who may do a job: prop 1 = `[u32 n][u32 key]` prop `0xE`…, keyed by minion type or character kind. Settings byte
  77 = 1 allows it. No list allows anyone.
- A trait (`rtrt`, prop `0x13`) lists components `[u32 1]["name" padded][u32 kind][settings]`. Kinds: 0 condition,
  3 stat adjust, 4 damage, 6 salary mod, 7 on spawn/max stat, 8 movement/disguise, 12 armour, 19 on attack, 22
  ignore tag, 0x20 enter a temperature tile, 0x21 exit one (4.14).

`mtex` objects list the 17 minion type hashes, robots included. Worker is `0xd162537e`.

### 4.14 Temperature

The lair temperature system covers every island. Its data sits in `dlc_oceans` and `common`. A tile's temperature
is the sum of nearby furniture outputs and any story offset, and its band decides the effects.

**Bands (`rtlv`, dlc_oceans, 7 objects):**

| Offset | Field |
|---|---|
| +35 | i32 lowest temperature |
| +39 | i32 highest temperature |
| +43 | f32 1 in the mild bands, 1.5 Cold/Hot, 3 Freezing/Melting (probably the furniture wear multiplier) |
| +68 | u32 band key (furniture output entries refer to it) |
| +72 | f32 x4 overlay colour RGBA |

Ranges: Freezing -128..-13, Cold -12..-7, Chilly -6..-1, Neutral (`0x6d9c05c1`, no name text) 0..0, Warm 1..6,
Hot 7..12, Melting 13..127.

**Furniture output.** A furniture record that gives off heat or cold carries a list prop `[u32 n]` + n entries
whose first i32 holds the output. The list always ends at `00000000 00000080 3F010001`.

| Entry | Layout | Examples |
|---|---|---|
| 24 bytes | `[i32 output][u32 0][1][2][1][2]` | Generator 4, Fusion Generator 8, Super Computer 8, icy caves -8 |
| 40 bytes | `[i32 output][u32 band key][text "High"/"Medium"/"Low"][text description]`, one per player setting | Furnace 8/6/4, Industrial Air Conditioner -8/-4/-2 |

**Story offsets.** Flowgraph node type `0xc3376493` shifts the whole lair's temperature by its `Offset` pin, fed
by one i32 `Value` setting. The Polar campaign's `OceanGenius_Obj*_ObjectiveEnd_TemperatureDrop_Activate` steps use
-1, -2 and -40, and two doomsday steps use it too.

**Effects.** 20 `rtrt` traits in `common` each pair "Enter a X Tile" and "Exit a X Tile" components (kinds 0x20 and
0x21) with a band id. They cover stat drain, slowed movement, agent immunity ("Thermal Underwear", "Lightweight
Clothing") and "Comfortable" (`0xb616e40c` fires on Neutral and Warm, `0xfb1b54b5` on Chilly). The genius packages
unlock them in `Unlock Feature - Starting TemperatureTraits - Oceans_Activate`.

### 4.15 Research (`rtrp`, `rttr`) and engineering (`rctr`, `rctt`)

- `rtrp` starts with prop `0xf`: name text ref @+33, description @+49, f32 research time @+77, node icon key. Scripts
  give the rewards (4.20).
- Requirement lists sit inside the first prop: minion entries prop4 `[type][count][3][u8 1]`, furniture prop1
  `[fnas][count]`, costs prop1 `[rcns][amount]`, unlocks `[n][fnas…]`. A project with an empty furniture list never
  progresses.
- `rttr` (research tree), prop `0xa` → `0x4`: links `n + (from, to, 0)`, the same links reversed, nodes `n +
  (index, out links, in links, rtrp id, u32 column (1-based), u8 available at start, u32 row)`, `u32 cols, u32
  rows`, grid (node index per cell, row-major, -1 empty), colours. Game trees use at most 5 tiers and 7 rows, links
  run forward within a row, and a node has at most one prerequisite. A tree outside those limits crashes the game.
- `rctt` (engineering tree): fixed header to +130 (+45 name, +49 selector icon key, +53 background key), then links,
  nodes with free placement (`[item or 0 = junction][u8 root][u32 col][u32 row][f32 dx][f32 dy]`), and a grid.

### 4.16 Resources (`rcns`)

`rcns` objects hold the consumables. A u32 at +41 caps how much you can hold: Intel `0x1cb32d8d` and Tech
`0x17a283c0` at 99, Henchman at 10 (three copies: `common`, `dlc_henchman_valkyrie`, `objectives_tutorial`), Gold 0
(vaults set it instead). The cap seeds new games; each save keeps its own.

### 4.17 Objectives (`robj`)

The first prop (`0x20`) starts with a u32 type at +33: 0 main or tutorial, 1 side story (crime lords, loot, DLC and
sandbox recruits), 2 optional. Its child `0x1` holds `[u32 n]` + n task props (key `0x8000002d`). The title text
ref sits at +37 (key at +49).

No object lists the objectives. The game evaluates each loaded objective's condition script. The `sandbox`
package (managed) holds the Sandbox mode objectives.

A task holds its texts, condition blocks and `[1][room id]` (its `_Activate` step), so a task copies cleanly into
another objective. The game runs tasks one at a time and starts a task's modifier when the task becomes current.

**Task condition block** (prop `0xd`, 166 bytes): `[u32 kind][u32 amount][u8 flag]`, two prop5 type references,
then prop1 `[hash]` entries.

| Kind | Condition | Example |
|---|---|---|
| 3 | N minions of a type | "Hire 25 more minions" |
| 4 | N furniture items | "Construct Lockers in the Barracks", 25 |
| 6 | N tiles of a room type (hash in the first prop1) | `MediumTierGold` `0xf6245d9a` task 0: 64 Barracks tiles, amount at body +92 |
| 10 | N schemes completed | "Complete 10 Schemes" |
| 0x24 | Objective state | |

Reward scripts start other objectives (node type `0xd4e0f0bc` = "start objective X").

**Henchman limit.** No single value caps henchmen. The 20 `Recruit X (Sandbox_Henchmen)` objectives, "Unholy Diver",
the four "One Of Us" objectives and each crime-lord story start count henchmen in the lair (script node type `0x10e28e6e`,
`{MinionType} InLair`, which game scripts use only for henchmen) against an i32 setting, the first one after the node type:

- 5: the objective needs fewer than 5 henchmen, so recruit offers stop at 5,
- 4 (story starts): it fails at 4 henchmen while a recruit is already on the way.

`Has5Henchmen` (objectives_optional) checks the same count for an optional objective. The "Henchman Improvement
Program" research raises henchman stats, not the cap. The HUD's genius and henchman bar shows 6 fixed spaces and
fills them in order, so further henchmen draw past its end.

### 4.18 Schemes (`rscm`, `rspl`, `rsdv`)

`rscm` first prop `0x38`:

| Part | Meaning |
|---|---|
| raw6 f32 | Duration in seconds |
| raw16[1] | Payout interval of "while running" schemes |
| raw16[3] | Heat gain to region (negative lowers heat) |
| minion list | Groups of alternatives `[n] + prop4 [type][count][category][u8 1]`; the game picks one group per offer |
| raw8 f32 | Offer expiry (-1 = stays) |
| child [9] | Launch cost `[rcns][amount]` pairs |

Minion categories: Worker 0, Guard types 1, Valet types 2, Technician types 3.

`rspl` (scheme pool): after the 24-byte header, `[u32 0x80000002][u8 0][u32 8 + 8n][u32 n][n x (u32 rscm, u32
weight 1)][u32 ?]`. Region upgrade rules (`rrtl`) and objective steps refer to pools. `rsdv` names world-map schemes
by matching `rtag`s.

### 4.19 Regions (`rmlr`, `rrtl`)

`rmlr` holds a world-map region: +45 name key, +83 KeyHash(`<stem>.base`) (the lair map it picks), +100 globe slot,
heat upgrades and scheme tags. `rrtl` holds region upgrades; `UpgradeHeatScheme_0N_<ANVIL|PATRIOT|OCEAN>` spawn the
heat-lowering schemes.

### 4.20 Scripts (flowgraphs)

`rscm`, `robj`, `room`, `rtrp`, `rrtl`, `rant` and `rctr` hold scripts.

```
name\0 (pad 4)  "FlowGraph/<template>\0"  [u32 1][u32 node count]  nodes...
node:  [type hash][version words / size blocks][00 ff ff 00][u32 3][u32 node id] ... settings, pins
link:  [link id][from node][from pin][to node][to pin][00 ff ff 00] + 25 bytes   (49 bytes, stored under its output pin as [u32 count][u32 ?] + links)
pin:   [pin hash][u32 1][u8 0][name\0 pad 4]
value: "Value\0" + 31 bytes -> [u8 1][u32 4][u32 type][value]   type 2 int, 3 float, 4 bool (u8)
```

Node 0 is Start and node 1 is End. Nodes sit inside size-prefixed property blocks, so a structural edit splices bytes
and fixes the size of each enclosing block. Version words before the marker hold 1 or 2.

**Names.** A pin hash is `KeyHash` of the pin's internal name, which the files don't store. The name stored next to a
pin is its display name and can differ: pin `0x054328a2` is `InputAmount` and shows as "Count". Hashing the game's
strings and word pairs recovers 61 of the 102 pin hashes that links use, including:

| Hash | Name | Role |
|---|---|---|
| `0x65c663f8` | `InputFlow` | Trigger input of every action step |
| `0xb41ab1af` | `OutputFlow` | Fires when an action step finishes |
| `0x79062fbc`, `0xa6f2a4b5` | `FlowTrue`, `FlowFalse` | Branch outputs |
| `0x2969894e` | `DataConstant` | Output of a constant value |
| `0xd017f110`, `0x2c4e5c5e`, `0x080e372a` | `OutputValue`, `OutputResult`, `OutputDataObject` | Data outputs |

Node type hashes don't come from any name in the game's strings, apart from `Branch` (`0xadaf25a2`) and
`ConstantValue` (`0x5cd5544d`). The game's scripts use 114 node types in 17,128 nodes. The most used:

| Type | Uses | Inputs, outputs | Does |
|---|---|---|---|
| `0x5cd5544d` | 4,343 | `DataConstant` | Constant value |
| `0x5ff81097` | 2,311 | `OutputDataObject` | Object reference (resource, room, tag) |
| `0xe2767556` | 1,791 | `InputFlow`, `InputAmount`, `InputConsumableType` | Change a consumable (resource) by an amount |
| `0x687b2d0e`, `0xb7032762`, `0x667797cc` | 617, 591, 118 | Operand1, Operand2, Result | Compare or combine two values |
| `0x7c81ffb9` | 605 | `InputAmount`, `InputDuration`, `MoneyTransactionReason` | Gold transaction |
| `0xc53cb767` | 504 | Consumable, `OutputCount` | Read a consumable's amount |
| `0x514ad5d4` | 305 | `InputObjective`, `OutputResult` | Read an objective's state |
| `0x10e28e6e` | 70 | `InputMinionType` | Count henchmen in the lair |
| `0xd4e0f0bc` | 5 | `InputFlow` | Start an objective |

### 4.21 GUI layouts (`GUAT`)

One `GUAT` chunk per GUI archive (`gui\main.asr` 7.4 MB, 347 components; `gui\gui.asr` 816 KB, 76). It uses the
data object property encoding (4.3) and round-trips byte for byte.

```
u32 0, u32 0, u32 component count
component count x prop 0x8 (component)
```

**Component** (prop `0x8`): a raw header, then widgets (prop `0x9`) in tree order, parameters (prop `0x2`, bindings
named `Internal_*`, `External_*` and the like), and flowgraphs named `F_*`. The header holds the widget tree:

```
u32 name hash, u32 1, u32 widget count
widget count x [u8 0][u32 child count][u32 parent index (0xffffffff = root)]
```

Entries follow the widget order, which is pre-order: each widget comes after its parent, and its children follow it
before the next sibling. Widgets refer to each other by name hash, not by index. The first widget (the root) carries
the component's logic flowgraph.

**Widget** (prop `0x9`):

| Part | Layout |
|---|---|
| Transform | prop `0x7`: raw `[u32 name hash][u32 2][…]`, f32 x and y in its last 12 bytes (e.g. 270, -70); then prop `0x3f` and an f32 opacity |
| Image | raw `[u32 1][u32 0x7fa2ad4a][u32 5][u32 RGBA tint][texture path\0 padded]…` |
| Text | raw `[u32 1][u32 0x5f6527ef][u32 9]…` |
| Container | raw `[u32 1 or 2][u32 0x7933751b][u32 3]…` |
| Size | after prop `0x0` kind 63: raw with f32 width, f32 height, e.g. `[11 zero bytes][f32 40][f32 112]` |
| Flags | raw 13 bytes `00 01 00 00 00 …` |

A widget without the type block is a group with its own size raw: `[5 bytes][f32 width][f32 height]…`. Widget names
are hashed. Texture paths are stored as full `Data\Graphics\GUI\…\name.tga` strings.

**Genius and henchman bar** (`gui\main.asr`, component `0x85b32cbb`, flowgraph `F_characterSelect`):

| Widget | Index | Role |
|---|---|---|
| `0xcee990c5` | 1 | Positioner, 826 x 112, at (270, -70) |
| `0xdfa5aacd` | 2 | Shadow plate, 750 x 112, opacity 0.35: end, end, mid (746) |
| `0x5d3c049f` | 6 | Plate, 750 x 112: end, end, mid (770), indent (`t_HUDbar_indent_mid`, 730 x 72) |
| `0x18db4456` | 11 | Portrait row, 1060 x 100, gap 20, centred in the indent and scaled 0.65, so its left end sits at (indent width - 0.65 x row width) / 2. Widening the plate by d keeps the first portrait in place only when the row grows by d / 0.65. The game adds the genius and henchman portraits here. |
| `0x02105993` | 12 | Slot row, 730 x 72, gap 90: a 25 x 25 genius placeholder and five 25 x 25 `t_hudBar_button` squares |

Widgets 1, 2 and 6 sit by their centre (x at +28 of the transform), so a wider plate grows both ways. The plate always shows 6 spaces at a 115-unit pitch. Portraits past the sixth run on past the plate's end.

### 4.22 Lair maps (`envs\basedefinitions\<lair>.base`)

An uncompressed container: `bsnf`, one 10-14 MB `ENTI` (the grid, id `0x989680`), `ARNM`, `dtvs`. The ENTI holds a
property tree whose block keys differ per lair (`1d/44/6` in Crown Gold, `1e/45/7-8` elsewhere).

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

### 4.23 Islands (`envs\<lair>.pc`)

A compressed container. Its `RSFL` lists build sections (`.sky`, `.asr` physics, `.lit`, `.snd`, `.nav`, `.cut`,
`.ent`, `.pfx` effects, `VehiclesData.asr`). These offsets don't index the payload, so chunks can change size.

`INST` (version 18):

```
[18][0][n]  n x 64-byte instances: f32 position x3, 3x3 rotation*scale as 12 half floats (pad per row), u16 group @60
u32 group count + 64-byte groups ([instance count][part count][first part]...)
u32 part count + 64-byte parts ([material hash]..., f32 min @40, f32 extent @52)
culling trees: u32 tree count; tree 0 = u32 nodes, u32 internal-node count, 128-byte nodes
  (6 x 4 f32 child boxes, 4 refs index<<8|kind (0 node, 1 instance, 0 = empty), 16 zero bytes)
```

Each instance is one leaf. Instanced geometry lives in the embedded file `inst (static)`: `[u32 verts][u32
indices][u32 instances]`, 24-byte vertices (u16 x3 position = part min + q/65535 x extent, half2 UV @8, UV2 @12,
normal 10:10:10 @16), u16 indices.

The sky uses ordinary textures (`specialfx\skybox\skybox_island_1.tga`, `specialfx\fog\fog_island_1.tga`). `FOG `
(183 bytes) holds f32 tint RGBA @21, start @37, end @41, haze RGB @126 (the blue over the island) and scatter RGB
@150. `PHEN` is a standalone physics mesh for ragdolls; minions walk on the lair grid.

### 4.24 Island select

- At startup the exe registers each `*.base` in `envs\basedefinitions\`, plus `JustAPlane.base` as a fallback.
- A `felr` object in `frontend` describes an island-select entry: picture keys, point-of-interest texts, name and
  description keys `LAIR_<STEM>_NAME/_DESC`, and a ref to the island's `rmlr` (+280 on Caine Key, +284 on the
  others).
- The region (`rmlr`) picks the map through +83 = KeyHash(`<stem>.base`).
- The game then fetches per-lair settings by lair id from `misc\common.asr` `BLUE` (4.25).
- `fegd` (frontend) describes a genius on the new-game screen: +65 actor-type id, +73/+77 GUI keys of
  `t_frontend_geniusselect_photo_X` and `t_icon_geniusicon_X`, +332/+340 the two `t_geniusability_X_00N` pictures.

### 4.25 Lair settings (`misc\common.asr` `BLUE` #25, chunk 27)

```
u32 1, u32 0, u32 1, u32 root class 0x614510c0, u32 count (24)
24 objects back to back, no byte lengths: [id][0x0D][id][class][member count] + members
string member: [key][3][0x55f89b99][0][4][len][chars]   (no terminator)
```

22 entries are class `0x6431F764` settings objects. Each lair's entry holds its island path, e.g.
`Lair\Lair_Tropical_03_Default\Lair_Tropical_03_Default`. No entry may move or change size; moving one breaks the
menu art. Entries 19 and 20 (dev test levels) take 4,853 bytes together.

### 4.26 Saves (`slotN.sav`)

```
"Asura   " "AsuraZlb"  u32 compressed length  u32 (raw length - 16)  zlib stream -> an ordinary Asura archive
```

A save holds `bsnf` (with the lair's region), `DYMG`, `DLIG`, `SMXG`, `ATIG`, `ARNM`, `dtvs`, `stsy`, `ttsy` and
about 500 `ENTI`, all hashes and numbers. The grid uses the `.base` format, and current saves store each placed
object as version 104. `slot0` is a raw 8 KB property block zero-padded to 2 MB.

### 4.27 Exe facts

| Fact | Where |
|---|---|
| Save path built from base dir + `PC_ProfileSaves` + steam id | RVA `0x4f03a0` (DX12 exe) |
| Minion hard cap and its HUD copy | Data globals; the HUD copies the cap once at startup and shows "Max" when the count reaches the copy |
| Videos | CRT `fopen_s("fmv/%s.webm")` |

### 4.28 Runtime config (`bin\eg2modkit.cfg`)

INI-like: one `[section]` per entry, `#` comments.

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
| `deref`, `offset` | Follow a pointer, then add an offset |
| `skip_if` | Skip the write while the current value equals this |
| `kind = hashmap_record` | `hit, id_at, mirror_at, offset`: a per-save record in the game's resource hash map, found by its HUD mirror [current][max] |
| `kind = table_lookup` | `id_at, alt_at, alt_if, call_at, offset`: a record the game looks up by id |
| `kind = island_alias` | `value = source stem, stem = new stem, region = 0x…`: open the new lair's island files in place of the source's |
| `kind = file_block` | `value = game file, offset, source`: bytes from `bin\eg2modkit_blocks\<source>` go into reads and mapped views of that file |
| `kind = trace_lair` | Logs the code that reads a lair's record |

The protector unpacks the exe after start, so the proxy retries unresolved patterns for 2 minutes and then writes
each value every second.

## 5. Rules for generated files

- `FNFO` comes first, with its payload size in sync.
- A renamed `fntr` record gets re-padded to 4.
- Text keys come from `KeyHash` of a real key.
- `COMA` chunks stay sorted by key.
- New ids collide with no game id.
- A development package manifest has `entitlement 0`, `deferred 1`, an `RSFL` entry size equal to the `rpkg` size,
  new objects listed in `18.0.1.0` and new `fnas` in `18.0.1.1`.
- An `.asrpatch` replaces the whole file it patches.
- In an archive whose `RSFL` holds offsets (`misc\common.asr`), nothing before a resource changes size.
- Each research or engineering project keeps at least one minion and one furniture requirement.
- Research trees keep to 5 tiers, 7 rows, forward links and one prerequisite per node.
- A placed map object copies a template of the map's serializer version.

## 6. Unknown

- The object id hash, and the real names of `rtag`s (the game ships none).
- Most raw words in each object type.
- Animations (`HCAN`, `FAAN`) and character skeletons.
- Chunks with no decoded layout. `misc\common.asr`: `AALG`, `AFSO`, `AMRO`, `APFO`, `ASET`, `ASSD`, `AXBB`, `AXBT`, `CPAN`, `DFG2`, `DYIN`, `IKTM`, `IPTP`, `META`, `RAGD`, `RFLX`, `RMBL`, `SUBS`, `SUBT`, `VTEX`. Sound (`sounds\gmsndmeta.asr`, islands): `AAUT`, `ADSP`, `DYMC`, `HRTF`, `SDGS`, `SDMX`, `HSND`, `SDEV`, `SDSM`, `SDPH`, `SDDC`, `RVBP`, `AUDA`. Islands: `CRNA`, `EMOD`, `FACE`, `FSX2`, `GISN`, `IRTX`, `REND`, `TEXT`, `TXAN`, `WOFX`, `WPSG`, cutscenes (`CUTS`, `CTAC`, `CTAT`, `CTEV`, `CTTR`) and effects (`FXET`, `FXPT`, `FXST`, `FXTT`). GUI and other files: `GUIF`, `GU2S`, `CRED`, `FNTK`, `FONT`, `GUAP`, `IPTB`, `IPTE` (`misc\input_bindings.asr`), `REWA` (`rewards\rewards.asr`), `STRC`, `STSC`, `STSM` (`stat\stat.asr`).
- Data object classes with no decoded fields: `audo`, `gdat`, `lght`, `rant`, `rbar`, `rcan`, `rcml`, `rdfl`, `reqa`, `rmca`, `rmcb`, `rmcc`, `rmch`, `rmpv`, `rsbs`, `rsei`, `rsvs`, `rtbg`, `rtdt`, `rtmj`, `rtsc`, `rtsu`, `trpa`, `vhcl`.
- `ENTI` component layout, including island effect placement.
- `GUAT` widget type ids beyond image, text and container, and the header fields of components.
- How the code hides the Mods menu (`FE_MODS`, `FE_MOD_CONFLICT` in `menu.asr_en`).
