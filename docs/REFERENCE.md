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
| `shadercache.bin` | Compiled shader cache the game writes at run time (13 MB); the install does not ship it |
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
`deferred = 1` ([4.4](#44-packages-rpkg)) and then loads at game start after the base content. It can hold:

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

It reads `bin\eg2modkit.cfg` ([4.28](#428-runtime-config-bineg2modkitcfg)) and logs to `bin\eg2modkit.log`.

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
([4.3](#43-data-objects-and-properties)), so a reference to a new text points at `KeyHash("eg2modkit")`.

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
| `runtime` | Runtime entry ([4.28](#428-runtime-config-bineg2modkitcfg)) | `bin\eg2modkit.cfg` |
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
| `mapEdits` | `file, floor, x0, y0, x1, y1, action, tier, room?, item?, facing?, template?, templateKey?, anyVersion, squad?, wave?, class?, tree?, patrol?, patrolState?, vehicle?` (`class`: the agent's character class, hex, in place of the template's at entity @56 and in its squad member records; `tree`: the behaviour tree it runs, hex, at entity @240 after a u32 999; 0xb37c4130 or 0x7448b8e2, a single `Idle` step, keeps it in place); `patrol`: grid object ids, hex, space-separated, written into its squad as the member's target furniture types and the squad's target list, with `patrolState` the member's state byte | `.base` patch |
| `sceneryEdits` | `file, group, groupName?, x0/z0/x1/z1?, action hide/move/copy/mesh, source?, dx, dy, dz` | `.pc` patch |
| `newLairs` | `stem, from, region?, keepId, islandEdits[FieldEdit]` | New `envs\` files, plus a `common.asr` block when `keepId` is false |
| `treeEdits` | `tree, step, setting, field?, value, expect, note?`: a behaviour tree value ([4.31](#431-behaviour-trees-axbt)) by tree hash and step index. `setting` picks a typed setting by 0-based position; `field` picks a plain field instead: `mode` (Serial 1-3, Parallel 0-3), `count` and `time` (Loop), `watch` and `compare` (conditions, 3 or 4) | Changed blocks of `misc\common.asr`, swapped in by the runtime DLL |
| `newClips` | `name, package, source, note?`: an animation clip (`HCAN`, [4.36](#436-animation-and-skeletons)) under its own name; the build sets the clip's name and name hash | Added to `<package>_content.asr`; the game finds clips in any loaded package by `KeyHash` of the name |
| `clipSwaps` | `from, to, note?`: every `KeyHash(from)` in `misc\common.asr` `BLUE`, `CPAN`, `RFLX` and `AALG` chunks becomes `KeyHash(to)` | Changed blocks of `misc\common.asr`, swapped in by the runtime DLL |
| `classTrees` | `class, tree, note?` (hex ids): the class's `DefaultBT` (0xa6775653) in the `misc\common.asr` `BLUE` ActorBase tree. A class without its own `DefaultBT` takes over a 4-byte override member equal to its parent's value, so the chunk keeps its size. Characters store the tree they run (entity @240), so the class's tree reaches only characters that spawn after the change | Changed blocks of `misc\common.asr`, swapped in by the runtime DLL |
| `henchmanBarSlots` | Number, 6-100 (a value, not a list) | `gui\main.asr` patch: more squares in the HUD bar, plate widened 115 per extra slot |

`MapEdit.action` takes `tier`, `dig`, `room`, `gold`, `wall`, `rock`, `remove`, `place`, `character` or `agent`.
X counts columns and Y counts rows.

A `.eg2mod` is a zip holding `<id>.json` and a `<id>.assets/` folder.

## 4. File formats

Every chunk tag in the install, and the file that describes it:

| Tag | Format |
|---|---|
| `AALG` | [aalg](formats/aalg.md) |
| `AAUT` | [4.30](#430-other-engine-chunks) (empty) |
| `ADSP` | [4.30](#430-other-engine-chunks) (empty) |
| `AFSO` | [4.30](#430-other-engine-chunks) (empty) |
| `AMRO` | [4.30](#430-other-engine-chunks) (empty) |
| `APFO` | [4.30](#430-other-engine-chunks) (empty) |
| `ARNM` | [arnm](formats/arnm.md) |
| `ASET` | [aset](formats/aset.md) |
| `ASSD` | [assd](formats/assd.md) |
| `ASTS` | [streamed-sounds](formats/streamed-sounds.md) |
| `ATIG` | [dymg-dlig-atig](formats/dymg-dlig-atig.md) |
| `AUDA` | [auda](formats/auda.md) |
| `audo` | [4.29](#429-other-data-object-classes) |
| `AXBB` | [4.30](#430-other-engine-chunks) (empty) |
| `AXBT` | [behaviour-trees](formats/behaviour-trees.md) |
| `BLUE` | [lair-settings](formats/lair-settings.md) |
| `bsnf` | [bsnf](formats/bsnf.md) |
| `COMA` | [chunks](formats/chunks.md) |
| `CPAN` | [cpan](formats/cpan.md) |
| `CRED` | [cred](formats/cred.md) |
| `CRNA` | [crna](formats/crna.md) |
| `CTAC` | [ctac](formats/ctac.md) |
| `CTAT` | [ctat](formats/ctat.md) |
| `CTEV` | [ctev](formats/ctev.md) |
| `CTTR` | [cttr](formats/cttr.md) |
| `CUTS` | [cuts](formats/cuts.md) |
| `DFG2` | [4.30](#430-other-engine-chunks) (empty) |
| `DLET` | [dlet](formats/dlet.md) |
| `DLEV` | [dlev](formats/dlev.md) |
| `DLIG` | [dymg-dlig-atig](formats/dymg-dlig-atig.md) |
| `DLLN` | [dlln](formats/dlln.md) |
| `DLLT` | [dllt](formats/dllt.md) |
| `dtvs` | [dtvs](formats/dtvs.md) |
| `DYIN` | [dyin](formats/dyin.md) |
| `DYMC` | [dymc](formats/dymc.md) |
| `DYMG` | [dymg-dlig-atig](formats/dymg-dlig-atig.md) |
| `EMOD` | [emod](formats/emod.md) |
| `ENTI` | [enti](formats/enti.md) |
| `FAAN` | [faan](formats/faan.md) |
| `FACE` | [face](formats/face.md) |
| `fegd` | [island-select](formats/island-select.md) |
| `felr` | [island-select](formats/island-select.md) |
| `fnas` | [furniture](formats/furniture.md) |
| `FNFO` | [packages](formats/packages.md) |
| `FNTK` | [fntk](formats/fntk.md) |
| `fntr` | [furniture](formats/furniture.md) |
| `FOG ` | [islands](formats/islands.md) |
| `FONT` | [font](formats/font.md) |
| `FSX2` | [fsx2](formats/fsx2.md) |
| `FXET` | [fxet](formats/fxet.md) |
| `FXPT` | [fxpt](formats/fxpt.md) |
| `FXST` | [fxst](formats/fxst.md) |
| `FXTT` | [fxtt](formats/fxtt.md) |
| `gdat` | [gdat](formats/gdat.md) |
| `GISN` | [gisn](formats/gisn.md) |
| `GU2S` | [gu2s](formats/gu2s.md) |
| `GUAP` | [guap](formats/guap.md) |
| `GUAT` | [gui-layouts](formats/gui-layouts.md) |
| `GUIF` | [guif](formats/guif.md) |
| `HCAN` | [hcan](formats/hcan.md) |
| `HMPT` | [hmpt](formats/hmpt.md) |
| `HRTF` | [hrtf](formats/hrtf.md) |
| `HSBB` | [models-and-meshes](formats/models-and-meshes.md) |
| `HSKE` | [models-and-meshes](formats/models-and-meshes.md) |
| `HSKL` | [models-and-meshes](formats/models-and-meshes.md) |
| `HSKN` | [hskn](formats/hskn.md) |
| `HSND` | [hsnd](formats/hsnd.md) |
| `HTPR` | [chunks](formats/chunks.md) |
| `HTXT` | [text](formats/text.md) |
| `IKTM` | [iktm](formats/iktm.md) |
| `INST` | [islands](formats/islands.md) |
| `IPTB` | [iptb](formats/iptb.md) |
| `IPTE` | [ipte](formats/ipte.md) |
| `IPTP` | [iptp](formats/iptp.md) |
| `IRTX` | [4.34](#434-island-level-chunks-and-entities) |
| `lght` | [lght](formats/lght.md) |
| `MARE` | [materials](formats/materials.md) |
| `META` | [4.30](#430-other-engine-chunks) (empty) |
| `mtex` | [jobs-and-traits](formats/jobs-and-traits.md) |
| `NAV1` | [chunks](formats/chunks.md) |
| `OCMH` | [chunks](formats/chunks.md) |
| `PBRV` | [pbrv](formats/pbrv.md) |
| `PHEN` | [phen](formats/phen.md) |
| `PLUT` | [plut](formats/plut.md) |
| `PSKY` | [psky](formats/psky.md) |
| `RAGD` | [ragd](formats/ragd.md) |
| `rant` | [rant](formats/rant.md) |
| `rbar` | [rbar](formats/rbar.md) |
| `rcan` | [rmcc-rmcb-rmch-rmca](formats/rmcc-rmcb-rmch-rmca.md) |
| `rcml` | [rcml](formats/rcml.md) |
| `rcns` | [resources](formats/resources.md) |
| `rctr` | [research-and-engineering](formats/research-and-engineering.md) |
| `rctt` | [research-and-engineering](formats/research-and-engineering.md) |
| `rdfl` | [rdfl](formats/rdfl.md) |
| `REND` | [4.34](#434-island-level-chunks-and-entities) |
| `reqa` | [reqa](formats/reqa.md) |
| `REWA` | [rewa](formats/rewa.md) |
| `RFLX` | [rflx](formats/rflx.md) |
| `rjob` | [jobs-and-traits](formats/jobs-and-traits.md) |
| `RMBL` | [rmbl](formats/rmbl.md) |
| `rmca` | [rmcc-rmcb-rmch-rmca](formats/rmcc-rmcb-rmch-rmca.md) |
| `rmcb` | [rmcc-rmcb-rmch-rmca](formats/rmcc-rmcb-rmch-rmca.md) |
| `rmcc` | [rmcc-rmcb-rmch-rmca](formats/rmcc-rmcb-rmch-rmca.md) |
| `rmch` | [rmcc-rmcb-rmch-rmca](formats/rmcc-rmcb-rmch-rmca.md) |
| `rmlr` | [regions](formats/regions.md) |
| `rmpv` | [rmpv](formats/rmpv.md) |
| `robj` | [objectives](formats/objectives.md) |
| `room` | [scripts](formats/scripts.md) |
| `rpkg` | [packages](formats/packages.md) |
| `rrtl` | [regions](formats/regions.md) |
| `rsbs` | [rsbs](formats/rsbs.md) |
| `RSCF` | [embedded-files](formats/embedded-files.md) |
| `rscm` | [schemes](formats/schemes.md) |
| `rsdv` | [schemes](formats/schemes.md) |
| `rsei` | [rsei](formats/rsei.md) |
| `RSFL` | [packages](formats/packages.md) |
| `rspl` | [schemes](formats/schemes.md) |
| `rsvs` | [rsvs](formats/rsvs.md) |
| `rtag` | [rtag](formats/rtag.md) |
| `rtbg` | [rtsc](formats/rtsc.md) |
| `rtdt` | [rtdt](formats/rtdt.md) |
| `rtlv` | [temperature](formats/temperature.md) |
| `rtmj` | [rtmj](formats/rtmj.md) |
| `rtrp` | [research-and-engineering](formats/research-and-engineering.md) |
| `rtrt` | [jobs-and-traits](formats/jobs-and-traits.md) |
| `rtsc` | [rtsc](formats/rtsc.md) |
| `rtsu` | [rtsc](formats/rtsc.md) |
| `rttr` | [research-and-engineering](formats/research-and-engineering.md) |
| `RVBP` | [rvbp](formats/rvbp.md) |
| `SDDC` | [sddc](formats/sddc.md) |
| `SDEV` | [sdev](formats/sdev.md) |
| `SDGS` | [4.30](#430-other-engine-chunks) (empty) |
| `SDMX` | [sdmx](formats/sdmx.md) |
| `SDPH` | [sdph](formats/sdph.md) |
| `SDSM` | [sdsm](formats/sdsm.md) |
| `SMXG` | [smxg](formats/smxg.md) |
| `STRC` | [strc](formats/strc.md) |
| `STSC` | [stsc](formats/stsc.md) |
| `STSM` | [stsm](formats/stsm.md) |
| `stsy` | [stsy](formats/stsy.md) |
| `SUBS` | [subs](formats/subs.md) |
| `SUBT` | [4.30](#430-other-engine-chunks) (empty) |
| `TEXT` | [text-effect-textures](formats/text-effect-textures.md) |
| `trpa` | [trpa](formats/trpa.md) |
| `ttsy` | [ttsy](formats/ttsy.md) |
| `TXAN` | [txan](formats/txan.md) |
| `TXST` | [texture-streaming](formats/texture-streaming.md) |
| `vhcl` | [vhcl](formats/vhcl.md) |
| `VTEX` | [vtex](formats/vtex.md) |
| `WOFX` | [wofx](formats/wofx.md) |
| `WPSG` | [wpsg](formats/wpsg.md) |

### 4.1 Container (`.asr` and others)

- [Container (`.asr` and others)](formats/container.md)

### 4.2 Chunks

- [Chunks](formats/chunks.md)

### 4.3 Data objects and properties

- [Data objects and properties](formats/data-objects-and-properties.md)

### 4.4 Packages (`rpkg`)

- [Packages (`rpkg`)](formats/packages.md)

### 4.5 Text (`HTXT`)

- [Text (`HTXT`)](formats/text.md)

### 4.6 Embedded files (`RSCF`)

- [Embedded files (`RSCF`)](formats/embedded-files.md)

### 4.7 Models and meshes

- [Models and meshes](formats/models-and-meshes.md)

### 4.8 Materials (`MARE`)

- [Materials (`MARE`)](formats/materials.md)

### 4.9 Texture streaming (`.ts` / `TXST`, `.pc_textures`)

- [Texture streaming (`.ts` / `TXST`, `.pc_textures`)](formats/texture-streaming.md)

### 4.10 Sound banks (`*.pc.sounds`)

- [Sound banks (`*.pc.sounds`)](formats/sound-banks.md)

### 4.11 Streamed sounds (`*.pc.streamsounds`)

- [Streamed sounds (`*.pc.streamsounds`)](formats/streamed-sounds.md)

### 4.12 Furniture (`fntr`, `fnas`)

- [Furniture (`fntr`, `fnas`)](formats/furniture.md)

### 4.13 Jobs (`rjob`) and traits (`rtrt`)

- [Jobs (`rjob`) and traits (`rtrt`)](formats/jobs-and-traits.md)

### 4.14 Temperature

- [Temperature](formats/temperature.md)

### 4.15 Research (`rtrp`, `rttr`) and engineering (`rctr`, `rctt`)

- [Research (`rtrp`, `rttr`) and engineering (`rctr`, `rctt`)](formats/research-and-engineering.md)

### 4.16 Resources (`rcns`)

- [Resources (`rcns`)](formats/resources.md)

### 4.17 Objectives (`robj`)

- [Objectives (`robj`)](formats/objectives.md)

### 4.18 Schemes (`rscm`, `rspl`, `rsdv`)

- [Schemes (`rscm`, `rspl`, `rsdv`)](formats/schemes.md)

### 4.19 Regions (`rmlr`, `rrtl`)

- [Regions (`rmlr`, `rrtl`)](formats/regions.md)

### 4.20 Scripts (flowgraphs)

- [Scripts (flowgraphs)](formats/scripts.md)

### 4.21 GUI layouts (`GUAT`)

- [GUI layouts (`GUAT`)](formats/gui-layouts.md)
- [Mods screen](formats/mods-screen.md)

### 4.22 Lair maps (`envs\basedefinitions\<lair>.base`)

- [Lair maps (`envs\basedefinitions\<lair>.base`)](formats/lair-maps.md)

### 4.23 Islands (`envs\<lair>.pc`)

- [Islands (`envs\<lair>.pc`)](formats/islands.md)

### 4.24 Island select

- [Island select](formats/island-select.md)

### 4.25 Lair settings (`misc\common.asr` `BLUE` #25, chunk 27)

- [Lair settings (`misc\common.asr` `BLUE` #25, chunk 27)](formats/lair-settings.md)

### 4.26 Saves (`slotN.sav`)

- [Saves (`slotN.sav`)](formats/saves.md)

### 4.27 Exe facts

- [Exe facts](formats/exe-facts.md)

### 4.28 Runtime config (`bin\eg2modkit.cfg`)

- [Runtime config (`bin\eg2modkit.cfg`)](formats/runtime-config.md)

### 4.29 Other data object classes

- [Talking-head scenes (`rtsc`)](formats/rtsc.md)
- [Doomsday firing levels (`rdfl`)](formats/rdfl.md)
- [Research tiers (`rtdt`)](formats/rtdt.md)
- [Minion Manager priorities (`rtmj`)](formats/rtmj.md)
- [Sticker Book (`rsbs`)](formats/rsbs.md)
- [Scheme casualty labels (`rcml`)](formats/rcml.md)
- [Antagonist events (`rant`)](formats/rant.md)
- [Animation state machine (`rmcc`, `rmcb`, `rmch`, `rmca`)](formats/rmcc-rmcb-rmch-rmca.md)
- [Lighting presets (`lght`)](formats/lght.md)
- [Vehicle routes (`vhcl`)](formats/vhcl.md)

Offsets count from the start of the object body. A text ref is `[u32 1][u32 0][u32 table hash][u32 key hash]` ([4.3](#43-data-objects-and-properties)).

**Others.** `gdat` (1, `misc\common.asr`): [4.37](#437-gui-fonts-input-and-stats). `audo` (1, `sounds\gmsnd.asr`): `[1][0]`, prop 1 with one hash.

**More classes.** `rsvs`, `trpa`, `reqa`, `rbar`, `rsei`, `rmpv` and `rtag`: [4.35](#435-map-and-save-state-more-data-objects).

### 4.30 Other engine chunks

- [Achievements (`REWA`)](formats/rewa.md)
- [Sound mixer (`SDMX`)](formats/sdmx.md)

These chunks start `[u32 version][u32 0]` and most continue with a `[u32 count]`. The table gives what each holds and
where its layout is.

| Chunk | File | Holds |
|---|---|---|
| `CRED`, `GUIF`, `GU2S`, `IPTB`, `IPTE`, `IPTP`, `STSM`, `STRC`, `STSC`, `RMBL`, `SUBS` | `gui\*.asr`, `misc\input_bindings.asr`, `misc\common.asr`, `stat\stat.asr` | Credits, GUI state machines and settings, input bindings and prompts, stats, rumble, subtitles ([4.37](#437-gui-fonts-input-and-stats)) |
| `REWA` | `rewards\rewards.asr` | 93 achievement entries keyed by `EG2_ACH_0NN` |
| `AXBT` | `misc\common.asr` | AI behaviour trees ([4.31](#431-behaviour-trees-axbt)) |
| `AALG`, `ASET`, `ASSD`, `RFLX`, `RAGD`, `IKTM`, `CPAN`, `DYIN` | `misc\common.asr` | Animation logic, sets, reflexes, ragdoll, IK, parametric animation, dynamic instances ([4.36](#436-animation-and-skeletons)) |
| `VTEX` | `misc\common.asr` | Photo-mode colour filters as 3D lookup tables ([vtex](formats/vtex.md)) |
| `AMRO`, `APFO`, `AFSO`, `SUBT`, `META`, `DFG2`, `AXBB` | `misc\common.asr` | Empty: version, 0 and a zero count |
| `SDMX`, `DYMC`, `HRTF` | `sounds\gmsndmeta.asr` | Mixer (below), dynamic music, 3D filter tables ([4.32](#432-sound-and-dialogue)) |
| `SDSM`, `SDEV`, `HSND`, `SDPH`, `SDDC`, `AUDA`, `RVBP` | islands, `misc\common.asr`, packages | Sound samples, events, sound sets, emitters, decals, ambience zones, reverb ([4.32](#432-sound-and-dialogue)) |
| `AAUT`, `ADSP`, `SDGS` | `sounds\gmsndmeta.asr` | Empty |
| `CRNA`, `TEXT`, `TXAN`, `FSX2` | islands, `misc\common.asr` | Coronas, effect textures, texture animation, full-screen effects ([4.33](#433-effects-and-cutscenes)) |
| `FACE`, `EMOD`, `GISN`, `WPSG`, `WOFX`, `REND`, `IRTX` | islands, `misc\common.asr` | Facial poses, environment name, small settings records ([4.34](#434-island-level-chunks-and-entities)) |

### 4.31 Behaviour trees (`AXBT`)

- [Behaviour trees (`AXBT`)](formats/behaviour-trees.md)

### 4.32 Sound and dialogue

- [Sound samples (`SDSM`)](formats/sdsm.md)
- [Sound events (`SDEV`)](formats/sdev.md)
- [Sound sets (`HSND`)](formats/hsnd.md)
- [Sound emitters (`SDPH`)](formats/sdph.md)
- [Ambience zones (`AUDA`)](formats/auda.md)
- [Reverb presets (`RVBP`)](formats/rvbp.md)
- [Decals (`SDDC`)](formats/sddc.md)
- [Dynamic music (`DYMC`)](formats/dymc.md)
- [HRTF tables (`HRTF`)](formats/hrtf.md)
- [Dialogue lines (`DLLN`)](formats/dlln.md)
- [Dialogue events (`DLEV`)](formats/dlev.md)
- [Dialogue playback templates (`DLET`)](formats/dlet.md)
- [Dialogue line templates (`DLLT`)](formats/dllt.md)

Rules for this section:

- A string is `name\0` padded to 4 bytes from its own start, counting the terminator. The padding bytes are not always
  zero: an empty string often reads `00 ff ff 00`.
- Most fields sit at odd offsets. The chunks mix `u8` fields with 4-byte fields and never realign.
- "KeyHash" is the lower-cased `h*31` hash ([4.5](#45-text-htxt)). A path hash uses `/` separators and keeps the extension.

How the pieces link:

| From | Field | To |
|---|---|---|
| `SDEV` entry (kind 0) | KeyHash(sample name) | `SDSM` |
| `SDEV` entry (kind 1) | event id | another `SDEV` |
| `SDEV` | bus name (string) | `SDMX` bus |
| `SDEV` | snapshot hash | KeyHash(`SDMX` snapshot name) |
| `SDSM` | WAV path | `RSCF` in `<file>.pc.sounds`, else `sounds\gmsnd.asr.pc.sounds` |
| `HSND` record, `SDPH` emitter | event id | `SDEV` |
| `AUDA` zone | path hash, bus hash, preset id | streamed WAV, `SDMX` bus, `RVBP` |
| `DYMC` track | WAV path, KeyHash(bus) | `sounds\streamingsounds.asr.pc.streamsounds`, `SDMX` bus |
| `DLEV` entry | line id | `DLLN` |
| `DLLN` | path hash | `ASTS` entry in the sibling `.pc.streamsounds` |
| `DLLN` | table hash + key hash | `HTXT` subtitle |
| `DLLN`, `DLEV` | template hash | `DLLT`, `DLET` |

### 4.33 Effects and cutscenes

- [Effect (`FXET`)](formats/fxet.md)
- [Emitter spawn (`FXST`)](formats/fxst.md)
- [Emitter particles (`FXPT`)](formats/fxpt.md)
- [Trails (`FXTT`)](formats/fxtt.md)
- [Cutscene (`CUTS`)](formats/cuts.md)
- [Actor (`CTAC`)](formats/ctac.md)
- [Track (`CTTR`)](formats/cttr.md)
- [Attachment (`CTAT`)](formats/ctat.md)
- [Event (`CTEV`)](formats/ctev.md)
- [Full-screen effects (`FSX2`)](formats/fsx2.md)
- [Coronas (`CRNA`)](formats/crna.md)
- [Effect textures (`TEXT`)](formats/text-effect-textures.md)
- [Texture animation (`TXAN`)](formats/txan.md)

**Particle effects (`FXET`, `FXPT`, `FXST`, `FXTT`).**

The four chunks sit in `misc\common.asr`, the `misc\packages\*\*_content.asr` packages and the island files. An
effect is written as a run of chunks: one `FXPT` and one `FXST` per emitter, in the same order, then one `FXTT` per
trail, then the `FXET` that ties them together. All four start the same way:

```
u32 version   FXET 22, FXPT 37, FXST 15, FXTT 18
u32 0
u32 hash      KeyHash of the name with \ turned into /
name\0        padded to 4
```

`FXET` names the effect (`Island\PFX_Helicopter_Lights`). `FXPT`, `FXST` and `FXTT` name a part of it as
`<effect>:<part>` (`Island\PFX_Helicopter_Lights:Photon`). The `FXPT` and `FXST` of one emitter share the name and
hash. 1129 effect names exist; some are defined in more than one file.

Most fields are curves:

```
u32 10007   curve marker
u32 1
u32 n       number of samples (1 = constant; 2, 16, 33, 65, 129 are common)
n values    each 1, 2, 3 or 4 f32, fixed per field
```

**Links.** A cutscene attachment (`CTAT` type 5) names its effect by `FXET` hash (`0x5a3afd4e` =
`Island\PFX_Helicopter_Lights`).

**Cutscenes (`CUTS`, `CTAC`, `CTTR`, `CTAT`, `CTEV`).**

Cutscenes sit in the island files (5 or 6 per island: one intro per genius and `CableCars`), in `gui\gui.asr` (5
`GeniusSelection_<genius>` scenes) and in `gui\main.asr` (`Callout`, `MinionTraining`, `TalkingHeads_Left`,
`TalkingHeads_Right`). A cutscene is a contiguous run of chunks: `CUTS`, then per actor a `CTAC`, its `CTTR` and the
`CTAT`s attached to it, then all `CTEV`s.

The chunks are byte packed. Strings are null-terminated and padded to 4 from their own start; padding bytes are not
always zero (an empty string is often `00 ff ff 00`). Data sits in nested blocks:

```
u32 0x80000000 | class version
u8  0
u32 length of the block body
```

This is the property header of [4.3](#43-data-objects-and-properties) with the class version as the key. A derived class's block holds its base
class's block first.

**Other effect chunks.**

The effect chunks store no field names and the exe holds none, so the names in their files come from value ranges,
sample counts and comparison across effect families (fire, smoke, sparks, embers, water, dust, steam). "Probably"
marks every name that rests on ranges alone. A curve with 2 samples is almost always `[0, x]`, a ramp over the
particle's or the effect's life; 16, 32, 33, 65 and 129 samples are drawn curves.

### 4.34 Island level chunks and entities

- [Entities (`ENTI`)](formats/enti.md)
- [Sky (`PSKY`)](formats/psky.md)
- [Projected-texture samples (`PLUT`)](formats/plut.md)
- [Reflection probes (`PBRV`)](formats/pbrv.md)
- [Physics mesh (`PHEN`)](formats/phen.md)
- [Facial poses (`FACE`)](formats/face.md)
- [Environment name (`EMOD`)](formats/emod.md)
- [Vignette (`WOFX`)](formats/wofx.md)
- [`WPSG`](formats/wpsg.md)
- [`GISN`](formats/gisn.md)

Offsets are from the start of the chunk body. `KeyHash` is the h*31 hash from [4.3](#43-data-objects-and-properties).

**Empty stubs (islands).** The same bytes on every island:

| Chunk | Bytes |
|---|---|
| `NAV1` | `[u32 21][u32 0 x3][u32 1001][u8 0][u32 1][u32 0]` (29) |
| `OCMH` | `[u32 2]` + 24 zero bytes |
| `REND`, `IRTX` | 12 zero bytes |

### 4.35 Map and save state, more data objects

- [Map header (`bsnf`)](formats/bsnf.md)
- [Navigation mesh (`ARNM`)](formats/arnm.md)
- [Placed furniture states (`dtvs`)](formats/dtvs.md)
- [Stats (`stsy`)](formats/stsy.md)
- [Pending region (`ttsy`)](formats/ttsy.md)
- [Sound mix (`SMXG`)](formats/smxg.md)
- [Fixed save chunks (`DYMG`, `DLIG`, `ATIG`)](formats/dymg-dlig-atig.md)
- [Scheme visuals (`rsvs`)](formats/rsvs.md)
- [Trap animations (`trpa`)](formats/trpa.md)
- [Character attachments (`reqa`)](formats/reqa.md)
- [Barks (`rbar`)](formats/rbar.md)
- [Text icons (`rsei`)](formats/rsei.md)
- [World-map scene (`rmpv`)](formats/rmpv.md)
- [Tags (`rtag`)](formats/rtag.md)

Offsets in data objects count from the start of the object body. "prop N" is a property `[u32 0x8000000N][u8 0][u32 length]` + payload ([4.3](#43-data-objects-and-properties)). Map and save chunks come from `envs\basedefinitions\*.base` / `*.scenario` and from saves unwrapped from `AsuraZlb` ([4.26](#426-saves-slotnsav)).

**Object ids.** The object id and aux id schemes are in [data objects](formats/data-objects-and-properties.md).

### 4.36 Animation and skeletons

- [Animation (`HCAN`)](formats/hcan.md)
- [Facial animation (`FAAN`)](formats/faan.md)
- [Mount points (`HMPT`)](formats/hmpt.md)
- [Skeleton (`HSKN`)](formats/hskn.md)
- [IK template (`IKTM`)](formats/iktm.md)
- [Ragdoll (`RAGD`)](formats/ragd.md)
- [Animation logic (`AALG`)](formats/aalg.md)
- [Animation set (`ASET`)](formats/aset.md)
- [Animation set default (`ASSD`)](formats/assd.md)
- [Parametric animation (`CPAN`)](formats/cpan.md)
- [Combat reflexes (`RFLX`)](formats/rflx.md)
- [Dynamic instances (`DYIN`)](formats/dyin.md)

**Cross-references.**

- `rcan` (all 38) holds `HCAN` name hashes.
- `rmcb` and `rmch` hold no `HCAN` name, `CPAN` id, `ASET` id or `AALG` node hash.
- `AXBT` holds no `HCAN` name hash or `CPAN` id.
- `BLUE` holds `HCAN` names (1362 hits), `CPAN` ids and `ASET` set ids.

### 4.37 GUI, fonts, input and stats

- [GUI parameters (`GUAP`)](formats/guap.md)
- [Fonts (`FONT`)](formats/font.md)
- [Kerning (`FNTK`)](formats/fntk.md)
- [Credits (`CRED`)](formats/cred.md)
- [GUI global flowgraph (`GUIF`)](formats/guif.md)
- [GUI settings (`GU2S`)](formats/gu2s.md)
- [GUI layouts (`GUAT`)](formats/gui-layouts.md)
- [Input bindings (`IPTB`)](formats/iptb.md)
- [Rebindable actions (`IPTE`)](formats/ipte.md)
- [Input prompt icons (`IPTP`)](formats/iptp.md)
- [Stat counters (`STSM`)](formats/stsm.md)
- [Stat reactions (`STRC`)](formats/strc.md)
- [Stat groups (`STSC`)](formats/stsc.md)
- [Rumble (`RMBL`)](formats/rmbl.md)
- [Subtitles (`SUBS`)](formats/subs.md)
- [Game data (`gdat`)](formats/gdat.md)

Offsets count from the start of the chunk body. A text ref is `[u32 1][u32 0][u32 table hash][u32 key hash]` ([4.3](#43-data-objects-and-properties)).
`inputs` = `0xb96fa1e9`, `menu` = `0x0033155f`.

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

- The names of 18 of 311 `rtag`s (cosmetic-unlock and objective groups), and most raw words in each object type.
- Animation: in `AALG`, the condition compare and negate bytes, the transition cost, sync and flag bits, four action
  payloads, and the names of 6 parameters and 14 action types. In `HCAN`, the rule behind the bounds box, the sound
  event flag bits and the tail of `d032ec05` objects ([4.36](#436-animation-and-skeletons)).
- Effects: the source string of the `CUTS` hash on GUI scenes, the 7 `CTAT` base floats, `CRNA` flags, and the
  unnamed `FXST` and `FXPT` words. The `FX*` curve names are readings from value ranges ([4.33](#433-effects-and-cutscenes)).
- Entities: the `ENTI` component type hashes, classes `0x6a`, `0x74`, `0x42` and `0x8009`, character data after the
  quaternion, node types and pins of the `0x8014` floor flowgraphs, `FACE` pose names, the purpose of most `PSKY`
  rotations, and `PHEN` flag meanings ([4.34](#434-island-level-chunks-and-entities)).
- Sound: the `DYMC` music graph nodes and pins and variable tails, the 16-byte value on `AUDA` INT zones and the
  35-byte layer tail, the `SDEV` flag bits. Most `SDEV` and `SDSM` field names are readings from usage ([4.32](#432-sound-and-dialogue)).
- GUI and stats: flowgraph node types and most pin names in FG3 graphs, the members of the rumble objects in
  `BLUE` #53, `SUBS` field meanings. The ids behind `GUAP` and component parameters, `IPTB` entries, `STSC` groups,
  the `IPTE` header and `GU2S` message box types match no string and are probably generated ([4.37](#437-gui-fonts-input-and-stats)).
- Map and saves: `stsy` stat names, the fixed bytes of `DYMG`, `DLIG` and `ATIG`, `reqa` entry hashes, two `rbar`
  hashes, the third hash in `rsvs` views, the effect hashes on `rmpv` water nodes ([4.35](#435-map-and-save-state-more-data-objects)).
- 25 of 28 behaviour tree variable names ([4.31](#431-behaviour-trees-axbt)), the names of the 5 `lght` channels ([4.29](#429-other-data-object-classes)).
