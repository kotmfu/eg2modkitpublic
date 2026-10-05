# Data objects and properties

Part of the [file format reference](../REFERENCE.md): [4.3 Data objects and properties](../REFERENCE.md#43-data-objects-and-properties).

Each lowercase chunk holds one data object, and the tag names its class.

```
u32 version     10
u32 pad0
u32 object_id   sdbm of the object's GUID (below); what other objects and manifests use to refer to it
u32 pad1        usually 0
u32 package_id  the owning package
u32 aux_id      KeyHash of the object's source name (below)
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

**Object id.** The object id is the sdbm hash of the object's GUID. The tools export each data object as
`LevelExportTemp0\ToolsEG_DataObject_Data_<Class>_<Name>_{AAAAAAAA-BBBBBBBB-CCCCCCCC-DDDDDDDD}.asr`, and these names
survive as strings in the content archives. Take the four hex groups as u32 A, B, C, D and write them little-endian in
the order B, A, D, C (16 bytes). The id is sdbm over those bytes:

```
h = 0
for each byte c: h = c + (h << 6) + (h << 16) - h     (mod 2^32)
```

Example: `TalkingHeads_Scene_Emma_01_Beginning_{BEC76C2B-59D32F53-11EA57EF-B7C8435B}` hashes the bytes
`53 2f d3 59 2b 6c c7 be 5b 43 c8 b7 ef 57 ea 11` to `0xc0568f80`, the id of that `rtsc`. The same hash gives the
`rpkg` package ids from the `DataPackage_{...}` names (29 of 29) and the `fegd`/`felr` ids from the `EG2 FrontEnd ..._{...}`
names. The strings name 3,399 GUIDs; 3,271 of the data-object ones hash to an object id in the install.

A new object needs a new GUID; its id is then sdbm of the GUID. The game never sees the GUID, so any unused u32 also
loads, as the existing rule (new ids collide with no game id) already requires.

**Aux id.** The aux id is KeyHash of `<Name>`, the part of the export name after the class prefix. For `rtag` the prefix
is `Tag_`, and the names keep their spaces and punctuation: `Tag_Training - Mercenary` gives `Training - Mercenary`,
whose KeyHash is the aux id. Taking the prefix as one word, the rule holds on every export-named `rtag` (290), `rscm`
(800), `rcns` (395), `rtrt` (381), `rjob` (154), `rspl` (119), `robj` (120 of 386), `rsvs` (85), `rsdv` (46), `rsei`
(40), `rbar` (40), `rrtl` (21), `rant` (20), `room`, `rctt`, `rttr` and `rtlv`. The other classes (`rtsc`, `rcan`,
`rmcb`, `rmcc`, `rmch`, `rmlr`, `rsbs`, `rtbg`, `rtrp`, `rctr`, `reqa`, `rmpv`) use a class prefix of more than one word
(`TalkingHeads_`), so the split point differs; their aux ids still resolve from the trailing name where tested
(`reqa` `Guard`).
